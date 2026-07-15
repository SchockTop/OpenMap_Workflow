namespace OpenMapUnifier.Geodesy;

/// <summary>
/// EPSG-keyed registry of every CRS this framework understands, with
/// any-to-any conversion (pivoting through WGS84/ETRS89 geographic — the two
/// differ by well under the data accuracy in Germany). For EPSG:4326 the
/// planar (X, Y) convention is (longitude, latitude).
/// </summary>
public static class CrsRegistry
{
    public sealed record CrsInfo(int Epsg, string Name, ICoordinateTransform? Transform)
    {
        public bool IsGeographic => Transform is null;
    }

    public static readonly IReadOnlyDictionary<int, CrsInfo> Known = new Dictionary<int, CrsInfo>
    {
        [4326] = new(4326, "WGS 84 (geographic lat/lon)", null),
        [25832] = new(25832, "ETRS89 / UTM zone 32N", Etrs89UtmTransform.Zone32),
        [25833] = new(25833, "ETRS89 / UTM zone 33N", Etrs89UtmTransform.Zone33),
        [32632] = new(32632, "WGS 84 / UTM zone 32N", Wgs84UtmTransform.Zone32),
        [32633] = new(32633, "WGS 84 / UTM zone 33N", Wgs84UtmTransform.Zone33),
        [4647] = new(4647, "ETRS89 / UTM zone 32N (zE-N, 32-prefixed easting)", ZonePrefixedUtmTransform.Zone32),
        [5650] = new(5650, "ETRS89 / UTM zone 33N (zE-N, 33-prefixed easting)", ZonePrefixedUtmTransform.Zone33),
        [3857] = new(3857, "WGS 84 / Pseudo-Mercator (web maps)", WebMercatorTransform.Instance),
        [31466] = new(31466, "DHDN / Gauß-Krüger zone 2 (Bessel)", GaussKruegerTransform.Zone2),
        [31467] = new(31467, "DHDN / Gauß-Krüger zone 3 (Bessel)", GaussKruegerTransform.Zone3),
        [31468] = new(31468, "DHDN / Gauß-Krüger zone 4 (Bessel)", GaussKruegerTransform.Zone4),
        [31469] = new(31469, "DHDN / Gauß-Krüger zone 5 (Bessel)", GaussKruegerTransform.Zone5),
    };

    public static CrsInfo Get(int epsg) =>
        Known.TryGetValue(epsg, out var info)
            ? info
            : throw new KeyNotFoundException(
                $"EPSG:{epsg} is not registered. Known: {string.Join(", ", Known.Keys.OrderBy(k => k))}.");

    /// <summary>Planar/geographic (X, Y) in a CRS → WGS84/ETRS89 geographic.</summary>
    public static GeoPoint ToGeo(int epsg, double x, double y)
    {
        var info = Get(epsg);
        return info.Transform is null
            ? new GeoPoint(Latitude: y, Longitude: x)
            : info.Transform.ToGeo(new UtmPoint(x, y));
    }

    /// <summary>WGS84/ETRS89 geographic → planar/geographic (X, Y) in a CRS.</summary>
    public static (double X, double Y) FromGeo(int epsg, GeoPoint geo)
    {
        var info = Get(epsg);
        if (info.Transform is null)
            return (geo.Longitude, geo.Latitude);
        var p = info.Transform.ToUtm(geo);
        return (p.Easting, p.Northing);
    }

    /// <summary>Convert a coordinate between any two registered CRS.</summary>
    public static (double X, double Y) Convert(double x, double y, int fromEpsg, int toEpsg) =>
        FromGeo(toEpsg, ToGeo(fromEpsg, x, y));
}
