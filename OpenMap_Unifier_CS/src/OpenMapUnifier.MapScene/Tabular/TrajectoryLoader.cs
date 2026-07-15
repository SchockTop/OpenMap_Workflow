using System.Globalization;
using System.Text.Json;
using OpenMapUnifier.Geodesy;

namespace OpenMapUnifier.MapScene.Tabular;

/// <summary>
/// Loads trajectories from CSV or JSON using a <see cref="TabularMapping"/>.
/// CSV: delimiter is sniffed (comma/semicolon/tab), decimal commas survive
/// when the delimiter is a semicolon. JSON: an array of flat objects (or an
/// object containing exactly one such array). <see cref="SuggestMapping"/>
/// proposes a mapping from the headers and a data sample — show it to the
/// user, let them correct it, done.
/// </summary>
public static class TrajectoryLoader
{
    // ---- loading ---------------------------------------------------------------

    public static Trajectory LoadCsv(string path, TabularMapping mapping, SceneAnchor? anchor = null) =>
        Build(ParseCsv(File.ReadAllLines(path)), mapping, anchor);

    public static Trajectory LoadJson(string path, TabularMapping mapping, SceneAnchor? anchor = null) =>
        Build(ParseJson(File.ReadAllText(path)), mapping, anchor);

    public static Trajectory Load(string path, TabularMapping mapping, SceneAnchor? anchor = null) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? LoadJson(path, mapping, anchor)
            : LoadCsv(path, mapping, anchor);

    // ---- mapping suggestion -------------------------------------------------------

    /// <summary>
    /// Propose a mapping from column names (and, for position columns without
    /// telltale names, from where sample values land via the coordinate
    /// detector). Always review the result — it is a suggestion, not an oracle.
    /// </summary>
    public static TabularMapping SuggestMapping(string path)
    {
        var table = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? ParseJson(File.ReadAllText(path))
            : ParseCsv(File.ReadAllLines(path));

        var mapping = new TabularMapping();
        foreach (var name in table.Columns)
        {
            var role = GuessRole(name);
            if (role != FieldRole.Ignore)
                mapping.Fields[name] = role;
        }

        // Unnamed position columns: try the coordinate detector on the first row.
        if (mapping.ColumnFor(FieldRole.X) is null && mapping.ColumnFor(FieldRole.Latitude) is null &&
            table.Rows.Count > 0)
        {
            var numeric = table.Columns
                .Where(c => !mapping.Fields.ContainsKey(c) && table.Rows[0].TryGetValue(c, out _))
                .ToArray();
            for (var i = 0; i + 1 < numeric.Length; i++)
            {
                var guess = CoordinateDetector.DetectBest(
                    table.Rows[0][numeric[i]], table.Rows[0][numeric[i + 1]]);
                if (guess is { Confidence: >= 0.8 })
                {
                    if (guess.Epsg == 4326)
                    {
                        mapping.Fields[numeric[i]] = guess.AxesSwapped ? FieldRole.Longitude : FieldRole.Latitude;
                        mapping.Fields[numeric[i + 1]] = guess.AxesSwapped ? FieldRole.Latitude : FieldRole.Longitude;
                    }
                    else
                    {
                        mapping.Fields[numeric[i]] = FieldRole.X;
                        mapping.Fields[numeric[i + 1]] = FieldRole.Y;
                        mapping.PositionEpsg = guess.Epsg;
                    }
                    break;
                }
            }
        }
        return mapping;
    }

    private static FieldRole GuessRole(string name) => name.Trim().ToLowerInvariant() switch
    {
        "t" or "time" or "timestamp" or "zeit" or "sec" or "seconds" => FieldRole.Time,
        "lat" or "latitude" or "breite" => FieldRole.Latitude,
        "lon" or "lng" or "long" or "longitude" or "laenge" => FieldRole.Longitude,
        "x" or "e" or "east" or "easting" or "utm_x" or "rechtswert" => FieldRole.X,
        "y" or "n" or "north" or "northing" or "utm_y" or "hochwert" => FieldRole.Y,
        "z" or "alt" or "altitude" or "height" or "hoehe" or "ele" or "elevation" or "agl" or "msl" => FieldRole.Z,
        "yaw" or "heading" or "hdg" or "psi" or "course" => FieldRole.Yaw,
        "pitch" or "theta" or "nick" => FieldRole.Pitch,
        "roll" or "phi" or "bank" => FieldRole.Roll,
        _ => FieldRole.Ignore,
    };

    // ---- table model ------------------------------------------------------------------

    internal sealed record Table(IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, double>> Rows);

    private static Trajectory Build(Table table, TabularMapping mapping, SceneAnchor? anchor)
    {
        mapping.Validate();
        var timeCol = mapping.ColumnFor(FieldRole.Time);
        var xCol = mapping.ColumnFor(FieldRole.X);
        var yCol = mapping.ColumnFor(FieldRole.Y);
        var latCol = mapping.ColumnFor(FieldRole.Latitude);
        var lonCol = mapping.ColumnFor(FieldRole.Longitude);
        var zCol = mapping.ColumnFor(FieldRole.Z);
        var yawCol = mapping.ColumnFor(FieldRole.Yaw);
        var pitchCol = mapping.ColumnFor(FieldRole.Pitch);
        var rollCol = mapping.ColumnFor(FieldRole.Roll);
        var extraCols = mapping.Fields.Where(kv => kv.Value == FieldRole.Extra).Select(kv => kv.Key).ToArray();

        // First pass: positions in UTM to place the anchor at the path center.
        var positions = new List<(double E, double N, double Z)>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            double e, n;
            if (xCol is not null && yCol is not null)
            {
                (e, n) = CrsRegistry.Convert(row[xCol], row[yCol], mapping.PositionEpsg, 25832);
            }
            else
            {
                var utm = Etrs89UtmTransform.Zone32.ToUtm(new GeoPoint(row[latCol!], row[lonCol!]));
                (e, n) = (utm.Easting, utm.Northing);
            }
            positions.Add((e, n, zCol is not null && row.TryGetValue(zCol, out var z) ? z : 0));
        }

        anchor ??= new SceneAnchor(new UtmPoint(
            positions.Average(p => p.E), positions.Average(p => p.N)));

        var samples = new List<TrajectorySample>(table.Rows.Count);
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var time = timeCol is not null && row.TryGetValue(timeCol, out var raw)
                ? raw / mapping.TimeDivisor
                : i * mapping.FallbackSampleInterval;

            Dictionary<string, double>? extra = null;
            foreach (var c in extraCols)
                if (row.TryGetValue(c, out var v))
                    (extra ??= new Dictionary<string, double>())[c] = v;

            samples.Add(new TrajectorySample(
                time,
                anchor.ToLocal(new UtmPoint(positions[i].E, positions[i].N), positions[i].Z),
                Angle(row, yawCol, mapping),
                Angle(row, pitchCol, mapping),
                Angle(row, rollCol, mapping),
                extra));
        }
        return new Trajectory(anchor, samples);

        static float Angle(Dictionary<string, double> row, string? col, TabularMapping m) =>
            col is not null && row.TryGetValue(col, out var v) ? (float)(v * m.AngleToDegrees) : 0f;
    }

    // ---- parsers ------------------------------------------------------------------------

    internal static Table ParseCsv(IReadOnlyList<string> lines)
    {
        var content = lines.Where(l => l.Trim().Length > 0).ToArray();
        if (content.Length < 2)
            throw new InvalidDataException("CSV needs a header line and at least one data line.");

        var delimiter = Sniff(content[0]);
        var columns = content[0].Split(delimiter).Select(c => c.Trim().Trim('"')).ToArray();

        var rows = new List<Dictionary<string, double>>(content.Length - 1);
        foreach (var line in content.Skip(1))
        {
            var cells = line.Split(delimiter);
            var row = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length && i < cells.Length; i++)
            {
                var cell = cells[i].Trim().Trim('"');
                // Decimal commas only make sense when they can't be delimiters.
                if (delimiter != ',' && cell.Count(ch => ch == ',') == 1 && !cell.Contains('.'))
                    cell = cell.Replace(',', '.');
                if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    row[columns[i]] = v;
            }
            if (row.Count > 0) rows.Add(row);
        }
        return new Table(columns, rows);

        static char Sniff(string header)
        {
            foreach (var d in new[] { ';', '\t', ',' })
                if (header.Contains(d)) return d;
            return ',';
        }
    }

    internal static Table ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.EnumerateObject()
                .Select(p => p.Value)
                .FirstOrDefault(v => v.ValueKind == JsonValueKind.Array);
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("JSON trajectory must be an array of objects (or contain one).");

        var columns = new List<string>();
        var rows = new List<Dictionary<string, double>>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var row = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in item.EnumerateObject())
            {
                double? v = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.String when double.TryParse(prop.Value.GetString(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                    _ => null,
                };
                if (v is null) continue;
                row[prop.Name] = v.Value;
                if (!columns.Contains(prop.Name)) columns.Add(prop.Name);
            }
            if (row.Count > 0) rows.Add(row);
        }
        return new Table(columns, rows);
    }
}
