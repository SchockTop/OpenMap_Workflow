namespace OpenMapUnifier.Geodesy;

/// <summary>
/// ETRS89 (GRS80) &lt;-&gt; UTM transform. German states publish in EPSG:25832
/// (<see cref="Zone32"/>, central meridian 9°E — the west/center/south) or
/// EPSG:25833 (<see cref="Zone33"/>, 15°E — Berlin, Brandenburg, Sachsen,
/// Mecklenburg-Vorpommern). WGS84 and ETRS89 differ by well under a meter in
/// Germany, so GPS/Google-Earth lat/lon can be fed in directly. Projection
/// math lives in <see cref="TransverseMercator"/>.
/// </summary>
public sealed class Etrs89UtmTransform : ICoordinateTransform
{
    /// <summary>EPSG:25832 — ETRS89 / UTM zone 32N (central meridian 9°E).</summary>
    public static readonly Etrs89UtmTransform Zone32 = new(32);

    /// <summary>EPSG:25833 — ETRS89 / UTM zone 33N (central meridian 15°E).</summary>
    public static readonly Etrs89UtmTransform Zone33 = new(33);

    public static Etrs89UtmTransform ForZone(int zone) => zone switch
    {
        32 => Zone32,
        33 => Zone33,
        >= 1 and <= 60 => new Etrs89UtmTransform(zone),
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "UTM zone must be 1-60."),
    };

    private readonly TransverseMercator _projection;

    public int Zone { get; }

    private Etrs89UtmTransform(int zone)
    {
        Zone = zone;
        _projection = new TransverseMercator(Ellipsoid.Grs80, zone * 6.0 - 183.0);
    }

    public UtmPoint ToUtm(GeoPoint geo) => _projection.Forward(geo);
    public GeoPoint ToGeo(UtmPoint utm) => _projection.Inverse(utm);
}
