namespace OpenMapUnifier.Geodesy;

/// <summary>
/// German legacy Gauß-Krüger CRS (DHDN / Potsdam datum on Bessel 1841;
/// EPSG:31466-31469 for zones 2-5). Rechtswert (easting) carries the zone as
/// its leading digit (zone·1,000,000 + 500,000 false easting), 3° meridian
/// strips, scale 1. ToUtm/ToGeo speak WGS84/ETRS89 geographic coordinates on
/// the outside — the DHDN datum shift (EPSG:1777 Helmert, ~1 m like the datum
/// itself) is applied internally.
/// </summary>
public sealed class GaussKruegerTransform : ICoordinateTransform
{
    public static readonly GaussKruegerTransform Zone2 = new(2); // EPSG:31466
    public static readonly GaussKruegerTransform Zone3 = new(3); // EPSG:31467
    public static readonly GaussKruegerTransform Zone4 = new(4); // EPSG:31468
    public static readonly GaussKruegerTransform Zone5 = new(5); // EPSG:31469

    public static GaussKruegerTransform ForZone(int zone) => zone switch
    {
        2 => Zone2,
        3 => Zone3,
        4 => Zone4,
        5 => Zone5,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Gauß-Krüger zones in Germany are 2-5."),
    };

    /// <summary>The GK zone whose 3° strip contains a longitude.</summary>
    public static GaussKruegerTransform ForLongitude(double lonDeg) =>
        ForZone(Math.Clamp((int)Math.Round(lonDeg / 3.0), 2, 5));

    private readonly TransverseMercator _projection;

    public int Zone { get; }
    public int Epsg => 31464 + Zone;

    private GaussKruegerTransform(int zone)
    {
        Zone = zone;
        _projection = new TransverseMercator(Ellipsoid.Bessel1841, zone * 3.0,
            scale: 1.0, falseEasting: zone * 1_000_000.0 + 500_000.0);
    }

    /// <summary>WGS84/ETRS89 lat/lon → GK Rechtswert/Hochwert.</summary>
    public UtmPoint ToUtm(GeoPoint geo)
    {
        var wgsEcef = HelmertTransform.GeodeticToEcef(geo, Ellipsoid.Wgs84);
        var dhdnEcef = HelmertTransform.DhdnToWgs84.ApplyInverse(wgsEcef);
        var (dhdnGeo, _) = HelmertTransform.EcefToGeodetic(dhdnEcef, Ellipsoid.Bessel1841);
        return _projection.Forward(dhdnGeo);
    }

    /// <summary>GK Rechtswert/Hochwert → WGS84/ETRS89 lat/lon.</summary>
    public GeoPoint ToGeo(UtmPoint gk)
    {
        var dhdnGeo = _projection.Inverse(gk);
        var dhdnEcef = HelmertTransform.GeodeticToEcef(dhdnGeo, Ellipsoid.Bessel1841);
        var wgsEcef = HelmertTransform.DhdnToWgs84.Apply(dhdnEcef);
        var (geo, _) = HelmertTransform.EcefToGeodetic(wgsEcef, Ellipsoid.Wgs84);
        return geo;
    }
}
