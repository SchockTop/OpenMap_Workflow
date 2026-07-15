namespace OpenMapUnifier.Geodesy;

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
