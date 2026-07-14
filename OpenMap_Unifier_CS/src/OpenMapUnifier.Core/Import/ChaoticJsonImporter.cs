using System.Globalization;
using System.Text.Json;
using OpenMapUnifier.Core.Geodesy;

namespace OpenMapUnifier.Core.Import;

/// <summary>One coordinate recovered from a messy JSON document.</summary>
public sealed record FoundCoordinate(
    string Path,
    string Raw,
    CoordinateGuess Guess,
    double? Z)
{
    public GeoPoint Geo => Guess.Geo;

    /// <summary>The coordinate expressed in any registered CRS.</summary>
    public (double X, double Y) In(int epsg) => CrsRegistry.FromGeo(epsg, Guess.Geo);
}

/// <summary>
/// Recovers coordinates from arbitrary, inconsistent JSON ("chaotic JSON"):
/// walks the whole document and recognizes
/// <list type="bullet">
/// <item>named pairs under any common spelling — lat/latitude/breite,
/// lon/lng/long/longitude/laenge, x/y, e/n, easting/northing,
/// rechtswert/hochwert, ostwert/nordwert, utm_x/utm_y (values may be numbers
/// or numeric strings, dot or comma decimals),</item>
/// <item>2/3-element numeric arrays (GeoJSON-style [lon, lat] or [x, y, z]),</item>
/// <item>coordinate strings, including DMS ("48°8'14"N 11°34'32"E") and
/// "lat, lon" text,</item>
/// <item>z/alt/height/hoehe/ele siblings as elevation.</item>
/// </list>
/// Planar values run through <see cref="CoordinateDetector"/>, so UTM32/33,
/// Gauß-Krüger, zone-prefixed and Web-Mercator inputs are told apart by where
/// they land. Every find reports its JSON path, what was matched, the ranked
/// best interpretation and its confidence — check low-confidence rows by hand.
/// </summary>
public static class ChaoticJsonImporter
{
    private static readonly string[] LatKeys = { "lat", "latitude", "breite", "phi" };
    private static readonly string[] LonKeys = { "lon", "lng", "long", "longitude", "laenge", "länge", "lambda" };
    private static readonly string[] XKeys = { "x", "e", "east", "easting", "rechtswert", "rw", "ostwert", "utm_x", "utm_e" };
    private static readonly string[] YKeys = { "y", "n", "north", "northing", "hochwert", "hw", "nordwert", "utm_y", "utm_n" };
    private static readonly string[] ZKeys = { "z", "alt", "altitude", "height", "hoehe", "höhe", "ele", "elevation" };

    public static IReadOnlyList<FoundCoordinate> ScanFile(string path) =>
        Scan(File.ReadAllText(path));

    public static IReadOnlyList<FoundCoordinate> Scan(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        var found = new List<FoundCoordinate>();
        Walk(doc.RootElement, "$", found);
        return found;
    }

    private static void Walk(JsonElement element, string path, List<FoundCoordinate> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryNamedPair(element, path, found, out var consumedKeys))
                {
                    foreach (var prop in element.EnumerateObject())
                        if (!consumedKeys.Contains(prop.Name))
                            Walk(prop.Value, $"{path}.{prop.Name}", found);
                }
                else
                {
                    foreach (var prop in element.EnumerateObject())
                        Walk(prop.Value, $"{path}.{prop.Name}", found);
                }
                break;

            case JsonValueKind.Array:
                if (!TryNumericArray(element, path, found))
                {
                    var i = 0;
                    foreach (var item in element.EnumerateArray())
                        Walk(item, $"{path}[{i++}]", found);
                }
                break;

            case JsonValueKind.String:
                TryCoordinateString(element.GetString()!, path, found);
                break;
        }
    }

    // ---- named object pairs ---------------------------------------------------

    private static bool TryNamedPair(JsonElement obj, string path, List<FoundCoordinate> found,
        out HashSet<string> consumed)
    {
        consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double? lat = null, lon = null, x = null, y = null, z = null;
        string? latKey = null, lonKey = null, xKey = null, yKey = null;

        foreach (var prop in obj.EnumerateObject())
        {
            if (!TryNumber(prop.Value, out var value)) continue;
            var name = prop.Name.Trim().ToLowerInvariant();
            if (LatKeys.Contains(name)) { lat = value; latKey = prop.Name; }
            else if (LonKeys.Contains(name)) { lon = value; lonKey = prop.Name; }
            else if (XKeys.Contains(name)) { x = value; xKey = prop.Name; }
            else if (YKeys.Contains(name)) { y = value; yKey = prop.Name; }
            else if (ZKeys.Contains(name)) z = value;
        }

        if (lat is { } la && lon is { } lo)
        {
            // Named lat/lon that is out of range is usually swapped or planar.
            var guess = Math.Abs(la) <= 90 && Math.Abs(lo) <= 180
                ? new CoordinateGuess(4326, CrsRegistry.Get(4326).Name, new GeoPoint(la, lo), 0.95,
                    $"named fields '{latKey}'/'{lonKey}'")
                : CoordinateDetector.DetectBest(la, lo);
            if (guess is not null)
            {
                found.Add(new FoundCoordinate(path, Raw(la, lo), guess, z));
                consumed.UnionWith(new[] { latKey!, lonKey! });
                return true;
            }
        }

        if (x is { } px && y is { } py)
        {
            var guess = CoordinateDetector.DetectBest(px, py);
            // Named x/y can be anything (local/scene coords too) — only accept
            // interpretations that actually land somewhere plausible.
            if (guess is { Confidence: >= 0.5 })
            {
                found.Add(new FoundCoordinate(path, Raw(px, py),
                    guess with { Reason = $"named fields '{xKey}'/'{yKey}'; {guess.Reason}" }, z));
                consumed.UnionWith(new[] { xKey!, yKey! });
                return true;
            }
        }
        return false;
    }

    // ---- bare numeric arrays ----------------------------------------------------

    private static bool TryNumericArray(JsonElement array, string path, List<FoundCoordinate> found)
    {
        var length = array.GetArrayLength();
        if (length is < 2 or > 3) return false;

        var values = new double[length];
        var i = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (!TryNumber(item, out values[i])) return false;
            i++;
        }

        // GeoJSON arrays are [lon, lat]; the detector tries both orders and
        // planar interpretations, ranking by where the result lands. Bare
        // arrays carry no naming hint, so demand solid confidence — otherwise
        // every [1, 2] in the document would masquerade as a coordinate.
        var guess = CoordinateDetector.DetectBest(values[0], values[1]);
        if (guess is not { Confidence: >= 0.6 }) return false;
        found.Add(new FoundCoordinate(path, Raw(values[0], values[1]), guess,
            length == 3 ? values[2] : null));
        return true;
    }

    // ---- coordinate strings --------------------------------------------------------

    private static void TryCoordinateString(string text, string path, List<FoundCoordinate> found)
    {
        if (text.Length is < 3 or > 200) return;

        // DMS or decimal "lat lon" text.
        if (text.Any(c => c is '°' or '\'' or '"' or 'N' or 'S' or 'E' or 'W' or 'n' or 's' or 'w') &&
            Dms.ParsePair(text) is { } geo)
        {
            found.Add(new FoundCoordinate(path, text.Trim(),
                new CoordinateGuess(4326, CrsRegistry.Get(4326).Name, geo, 0.9, "DMS/degree string"), null));
            return;
        }

        // Two plain numbers in one string ("690123.4, 5335872.1" / "48.14;11.58").
        var parts = text.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 2 or 3 &&
            TryParse(parts[0], out var a) && TryParse(parts[1], out var b))
        {
            var guess = CoordinateDetector.DetectBest(a, b);
            if (guess is { Confidence: >= 0.6 })
            {
                double? z = parts.Length == 3 && TryParse(parts[2], out var pz) ? pz : null;
                found.Add(new FoundCoordinate(path, text.Trim(), guess, z));
            }
        }
    }

    // ---- helpers -------------------------------------------------------------------

    private static bool TryNumber(JsonElement element, out double value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                value = element.GetDouble();
                return true;
            case JsonValueKind.String when TryParse(element.GetString()!, out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryParse(string s, out double value)
    {
        s = s.Trim();
        // Accept German decimal commas, but not thousands separators.
        if (s.Count(c => c == ',') == 1 && !s.Contains('.'))
            s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string Raw(double a, double b) =>
        string.Create(CultureInfo.InvariantCulture, $"{a}, {b}");
}
