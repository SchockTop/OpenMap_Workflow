namespace OpenMapUnifier.Geodesy;

/// <summary>Reference ellipsoid (semi-major axis in meters).</summary>
public readonly record struct Ellipsoid(string Name, double SemiMajorAxis, double InverseFlattening)
{
    public double Flattening => 1.0 / InverseFlattening;
    /// <summary>First eccentricity squared.</summary>
    public double E2 => Flattening * (2.0 - Flattening);
    public double SemiMinorAxis => SemiMajorAxis * (1.0 - Flattening);

    /// <summary>GRS 1980 — basis of ETRS89 (all EPSG:258xx CRS).</summary>
    public static readonly Ellipsoid Grs80 = new("GRS 1980", 6378137.0, 298.257222101);

    /// <summary>WGS 84 — GPS; differs from GRS80 only in the 12th digit of 1/f.</summary>
    public static readonly Ellipsoid Wgs84 = new("WGS 84", 6378137.0, 298.257223563);

    /// <summary>Bessel 1841 — basis of the German legacy DHDN datum (Gauß-Krüger).</summary>
    public static readonly Ellipsoid Bessel1841 = new("Bessel 1841", 6377397.155, 299.1528128);
}
