namespace OpenMapUnifier.Core.Geodesy;

/// <summary>
/// A planar position in EPSG:25832 (ETRS89 / UTM zone 32N), meters.
/// This is the native CRS of all Bayern OpenData products.
/// </summary>
public readonly record struct UtmPoint(double Easting, double Northing)
{
    public double DistanceTo(UtmPoint other)
    {
        var de = other.Easting - Easting;
        var dn = other.Northing - Northing;
        return Math.Sqrt(de * de + dn * dn);
    }

    public override string ToString() =>
        FormattableString.Invariant($"E={Easting:F2} N={Northing:F2}");
}
