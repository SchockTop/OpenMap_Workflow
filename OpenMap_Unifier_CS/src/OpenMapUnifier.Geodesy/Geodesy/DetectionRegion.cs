namespace OpenMapUnifier.Geodesy;

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
