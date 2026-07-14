namespace OpenMapUnifier.Core.Geodesy;

/// <summary>One ranked interpretation of a raw coordinate pair.</summary>
public sealed record CoordinateGuess(
    int Epsg,
    string CrsName,
    GeoPoint Geo,
    double Confidence,
    string Reason,
    bool AxesSwapped = false)
{
    public override string ToString() =>
        FormattableString.Invariant(
            $"EPSG:{Epsg} ({CrsName}) -> {Geo}  [{Confidence:P0}{(AxesSwapped ? ", axes swapped" : "")}] {Reason}");
}

/// <summary>
/// The geographic region detection scores against: results landing inside get
/// full confidence, fading to zero within ~3° outside. Use a custom region
/// when working with non-German data.
/// </summary>
public sealed record DetectionRegion(
    double MinLat, double MaxLat, double MinLon, double MaxLon, string Name)
{
    /// <summary>Germany with margin — the default.</summary>
    public static readonly DetectionRegion Germany = new(46.5, 55.8, 4.5, 16.5, "Germany");

    /// <summary>Central Europe — for data straddling borders.</summary>
    public static readonly DetectionRegion CentralEurope = new(43.0, 58.0, -1.0, 25.0, "Central Europe");
}

/// <summary>
/// Heuristic CRS detection for raw number pairs — the "no clue what CRS this
/// is" debugging tool. Given (a, b) it tests every registered CRS (both axis
/// orders where plausible), converts, and scores by whether the result lands
/// in/near the expected region. Returns interpretations ranked by confidence;
/// genuinely ambiguous inputs (a bare UTM easting fits zone 32 AND 33) yield
/// multiple candidates on purpose.
/// </summary>
public static class CoordinateDetector
{
    public static IReadOnlyList<CoordinateGuess> Detect(double a, double b,
        DetectionRegion? region = null)
    {
        var bounds = region ?? DetectionRegion.Germany;
        var guesses = new List<CoordinateGuess>();

        // --- geographic ------------------------------------------------------
        if (IsLat(a) && IsLon(b))
            AddGeo(guesses, bounds, new GeoPoint(a, b), swapped: false, "looks like lat/lon in degrees");
        if (IsLat(b) && IsLon(a) && Math.Abs(a - b) > 1e-9)
            AddGeo(guesses, bounds, new GeoPoint(b, a), swapped: true, "looks like lon/lat in degrees (GeoJSON order)");

        // --- plain UTM (both German zones + WGS84 variants) -------------------
        if (a is > 100_000 and < 1_000_000 && b is > 5_100_000 and < 6_200_000)
        {
            TryPlanar(guesses, bounds, 25832, a, b, "6-digit easting + 7-digit northing (UTM range)");
            TryPlanar(guesses, bounds, 25833, a, b, "6-digit easting + 7-digit northing (UTM range)");
        }
        if (b is > 100_000 and < 1_000_000 && a is > 5_100_000 and < 6_200_000)
        {
            TryPlanar(guesses, bounds, 25832, b, a, "northing/easting given in reverse order", swapped: true);
            TryPlanar(guesses, bounds, 25833, b, a, "northing/easting given in reverse order", swapped: true);
        }

        // --- zone-prefixed UTM (zE-N): 8-digit easting starting 32/33 ----------
        if (a is > 32_000_000 and < 34_000_000 && b is > 5_100_000 and < 6_200_000)
        {
            var zone = (int)(a / 1_000_000);
            TryPlanar(guesses, bounds, zone == 32 ? 4647 : 5650, a, b,
                $"8-digit easting with UTM zone prefix {zone} (zE-N)");
        }

        // --- Gauß-Krüger: 7-digit Rechtswert starting with zone digit 2-5 -----
        if (a is > 2_000_000 and < 6_000_000 && b is > 5_100_000 and < 6_200_000)
        {
            var zone = (int)(a / 1_000_000);
            if (zone is >= 2 and <= 5)
                TryPlanar(guesses, bounds, 31464 + zone, a, b,
                    $"7-digit Rechtswert with Gauß-Krüger zone digit {zone} (legacy DHDN)");
        }

        // --- Web Mercator ------------------------------------------------------
        if (Math.Abs(a) is > 400_000 and < 20_100_000 && Math.Abs(b) is > 4_000_000 and < 20_100_000 &&
            a is < 2_000_000 && b is > 5_000_000 and < 8_000_000)
        {
            TryPlanar(guesses, bounds, 3857, a, b, "meters in Web-Mercator range (web map export)");
        }

        return guesses.OrderByDescending(g => g.Confidence).ToList();

        static bool IsLat(double v) => v is >= -90 and <= 90;
        static bool IsLon(double v) => v is >= -180 and <= 180;
    }

    /// <summary>Best single interpretation, or null when nothing fits.</summary>
    public static CoordinateGuess? DetectBest(double a, double b, DetectionRegion? region = null) =>
        Detect(a, b, region).FirstOrDefault();

    /// <summary>
    /// Interpret a pair as a KNOWN CRS (no guessing), scored only by whether
    /// the result lands in the region. Used by the importer's AssumeEpsg mode.
    /// </summary>
    public static CoordinateGuess? Interpret(double x, double y, int epsg, DetectionRegion? region = null)
    {
        var bounds = region ?? DetectionRegion.Germany;
        GeoPoint geo;
        try
        {
            geo = CrsRegistry.ToGeo(epsg, x, y);
        }
        catch (Exception)
        {
            return null;
        }
        var fit = RegionFit(geo, bounds);
        return fit <= 0
            ? null
            : new CoordinateGuess(epsg, CrsRegistry.Get(epsg).Name, geo, 0.5 + 0.45 * fit,
                $"interpreted as configured EPSG:{epsg}");
    }

    private static void AddGeo(List<CoordinateGuess> guesses, DetectionRegion bounds,
        GeoPoint geo, bool swapped, string reason)
    {
        var confidence = 0.55 * RegionFit(geo, bounds) + (swapped ? 0.25 : 0.4);
        guesses.Add(new CoordinateGuess(4326, CrsRegistry.Get(4326).Name, geo, confidence, reason, swapped));
    }

    private static void TryPlanar(List<CoordinateGuess> guesses, DetectionRegion bounds,
        int epsg, double x, double y, string reason, bool swapped = false)
    {
        GeoPoint geo;
        try
        {
            geo = CrsRegistry.ToGeo(epsg, x, y);
        }
        catch (Exception)
        {
            return; // numerically implausible for this CRS
        }
        var fit = RegionFit(geo, bounds);
        if (fit <= 0) return;
        var confidence = 0.55 * fit + 0.4 - (swapped ? 0.15 : 0);
        guesses.Add(new CoordinateGuess(epsg, CrsRegistry.Get(epsg).Name, geo, confidence, reason, swapped));
    }

    /// <summary>1.0 inside the region, fading to 0 within a few degrees outside.</summary>
    private static double RegionFit(GeoPoint geo, DetectionRegion bounds)
    {
        if (double.IsNaN(geo.Latitude) || double.IsNaN(geo.Longitude)) return 0;
        var dLat = Distance(geo.Latitude, bounds.MinLat, bounds.MaxLat);
        var dLon = Distance(geo.Longitude, bounds.MinLon, bounds.MaxLon);
        var d = Math.Max(dLat, dLon);
        return d <= 0 ? 1.0 : Math.Max(0.0, 1.0 - d / 3.0);

        static double Distance(double v, double min, double max) =>
            v < min ? min - v : v > max ? v - max : 0;
    }
}
