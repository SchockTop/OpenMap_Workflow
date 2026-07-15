namespace OpenMapUnifier.Geodesy;

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
