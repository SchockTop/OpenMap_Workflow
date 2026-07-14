namespace OpenMapUnifier.Core.Geodesy;

/// <summary>
/// EPSG:3857 Web/Pseudo Mercator (spherical formulas on the WGS84 semi-major
/// axis) — what slippy web maps, OSM tiles and most JS map libraries emit.
/// NOT a conformal ellipsoidal Mercator; do not use for measurement.
/// </summary>
public sealed class WebMercatorTransform : ICoordinateTransform
{
    public static readonly WebMercatorTransform Instance = new();

    private const double R = 6378137.0;

    public UtmPoint ToUtm(GeoPoint geo)
    {
        var x = R * geo.Longitude * Math.PI / 180.0;
        var phi = geo.Latitude * Math.PI / 180.0;
        var y = R * Math.Log(Math.Tan(Math.PI / 4.0 + phi / 2.0));
        return new UtmPoint(x, y);
    }

    public GeoPoint ToGeo(UtmPoint p)
    {
        var lon = p.Easting / R * 180.0 / Math.PI;
        var lat = (2.0 * Math.Atan(Math.Exp(p.Northing / R)) - Math.PI / 2.0) * 180.0 / Math.PI;
        return new GeoPoint(lat, lon);
    }
}

/// <summary>
/// Zone-prefixed UTM ("zE-N" CRS: EPSG:4647 for zone 32, EPSG:5650 for zone
/// 33): the plain UTM easting plus zone·1,000,000 — Sachsen-Anhalt's tile
/// labels and various INSPIRE deliveries use it.
/// </summary>
public sealed class ZonePrefixedUtmTransform : ICoordinateTransform
{
    /// <summary>EPSG:4647 — ETRS89 / UTM zone 32N (zE-N).</summary>
    public static readonly ZonePrefixedUtmTransform Zone32 = new(32);

    /// <summary>EPSG:5650 — ETRS89 / UTM zone 33N (zE-N).</summary>
    public static readonly ZonePrefixedUtmTransform Zone33 = new(33);

    private readonly Etrs89UtmTransform _baseTransform;
    private readonly double _offset;

    public int Zone { get; }

    private ZonePrefixedUtmTransform(int zone)
    {
        Zone = zone;
        _baseTransform = Etrs89UtmTransform.ForZone(zone);
        _offset = zone * 1_000_000.0;
    }

    public UtmPoint ToUtm(GeoPoint geo)
    {
        var p = _baseTransform.ToUtm(geo);
        return new UtmPoint(p.Easting + _offset, p.Northing);
    }

    public GeoPoint ToGeo(UtmPoint p) =>
        _baseTransform.ToGeo(new UtmPoint(p.Easting - _offset, p.Northing));
}

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
