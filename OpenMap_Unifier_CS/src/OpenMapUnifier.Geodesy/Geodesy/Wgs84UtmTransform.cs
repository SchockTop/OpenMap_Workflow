namespace OpenMapUnifier.Geodesy;

/// <summary>
/// UTM on the WGS84 ellipsoid (EPSG:32632/32633) — what most GPS devices and
/// international tooling emit. Differs from ETRS89 UTM by well under a
/// millimeter of projection math (the datums differ by ~0.5 m over decades of
/// plate drift, which is below the accuracy of the data handled here).
/// </summary>
public sealed class Wgs84UtmTransform : ICoordinateTransform
{
    public static readonly Wgs84UtmTransform Zone32 = new(32);
    public static readonly Wgs84UtmTransform Zone33 = new(33);

    private readonly TransverseMercator _projection;

    public int Zone { get; }

    private Wgs84UtmTransform(int zone)
    {
        Zone = zone;
        _projection = new TransverseMercator(Ellipsoid.Wgs84, zone * 6.0 - 183.0);
    }

    public UtmPoint ToUtm(GeoPoint geo) => _projection.Forward(geo);
    public GeoPoint ToGeo(UtmPoint p) => _projection.Inverse(p);
}
