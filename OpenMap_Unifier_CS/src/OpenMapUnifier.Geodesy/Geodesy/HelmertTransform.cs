namespace OpenMapUnifier.Geodesy;

/// <summary>
/// 7-parameter Helmert (similarity) datum transformation in the EPSG
/// "position vector" convention — the one EPSG publishes for German datums.
/// Also hosts geodetic ↔ ECEF conversions, since Helmert operates on ECEF.
/// </summary>
public sealed record HelmertTransform(
    double TxMeters, double TyMeters, double TzMeters,
    double RxArcseconds, double RyArcseconds, double RzArcseconds,
    double ScalePpm)
{
    /// <summary>
    /// DHDN (Potsdam, Bessel 1841) → WGS84/ETRS89 — EPSG:1777
    /// ("DHDN to WGS 84 (2)"), ~1 m accuracy, which matches the intrinsic
    /// accuracy of the legacy datum itself. This is what proj falls back to
    /// without the BeTA2007 grid.
    /// </summary>
    public static readonly HelmertTransform DhdnToWgs84 =
        new(598.1, 73.7, 418.2, 0.202, 0.045, -2.455, 6.7);

    private const double ArcsecToRad = Math.PI / (180.0 * 3600.0);

    /// <summary>Apply source→target (position vector convention).</summary>
    public EcefPoint Apply(EcefPoint p)
    {
        var s = 1.0 + ScalePpm * 1e-6;
        var rx = RxArcseconds * ArcsecToRad;
        var ry = RyArcseconds * ArcsecToRad;
        var rz = RzArcseconds * ArcsecToRad;
        return new EcefPoint(
            TxMeters + s * (p.X - rz * p.Y + ry * p.Z),
            TyMeters + s * (rz * p.X + p.Y - rx * p.Z),
            TzMeters + s * (-ry * p.X + rx * p.Y + p.Z));
    }

    /// <summary>Apply target→source (exact inverse of <see cref="Apply"/>).</summary>
    public EcefPoint ApplyInverse(EcefPoint p)
    {
        var s = 1.0 + ScalePpm * 1e-6;
        var rx = RxArcseconds * ArcsecToRad;
        var ry = RyArcseconds * ArcsecToRad;
        var rz = RzArcseconds * ArcsecToRad;
        double x = (p.X - TxMeters) / s, y = (p.Y - TyMeters) / s, z = (p.Z - TzMeters) / s;

        // Exact inverse of the small-angle matrix M = I + R̃ (Cramer's rule) —
        // the transposed-matrix shortcut leaves sub-millimeter residue because
        // M is only orthonormal to first order.
        var det = 1 + rx * rx + ry * ry + rz * rz;
        return new EcefPoint(
            ((1 + rx * rx) * x + (rz + rx * ry) * y + (rx * rz - ry) * z) / det,
            ((rx * ry - rz) * x + (1 + ry * ry) * y + (rx + ry * rz) * z) / det,
            ((ry + rx * rz) * x + (ry * rz - rx) * y + (1 + rz * rz) * z) / det);
    }

    // ---- geodetic <-> ECEF ------------------------------------------------

    public static EcefPoint GeodeticToEcef(GeoPoint geo, Ellipsoid ellipsoid, double heightMeters = 0)
    {
        var phi = geo.Latitude * Math.PI / 180.0;
        var lam = geo.Longitude * Math.PI / 180.0;
        var sinPhi = Math.Sin(phi);
        var n = ellipsoid.SemiMajorAxis / Math.Sqrt(1 - ellipsoid.E2 * sinPhi * sinPhi);
        return new EcefPoint(
            (n + heightMeters) * Math.Cos(phi) * Math.Cos(lam),
            (n + heightMeters) * Math.Cos(phi) * Math.Sin(lam),
            (n * (1 - ellipsoid.E2) + heightMeters) * sinPhi);
    }

    public static (GeoPoint Geo, double Height) EcefToGeodetic(EcefPoint p, Ellipsoid ellipsoid)
    {
        var e2 = ellipsoid.E2;
        var a = ellipsoid.SemiMajorAxis;
        var lam = Math.Atan2(p.Y, p.X);
        var r = Math.Sqrt(p.X * p.X + p.Y * p.Y);

        // Iterative latitude (converges to sub-µm in a handful of rounds).
        var phi = Math.Atan2(p.Z, r * (1 - e2));
        for (var i = 0; i < 8; i++)
        {
            var sinPhi = Math.Sin(phi);
            var n = a / Math.Sqrt(1 - e2 * sinPhi * sinPhi);
            phi = Math.Atan2(p.Z + e2 * n * sinPhi, r);
        }
        var sinFinal = Math.Sin(phi);
        var nFinal = a / Math.Sqrt(1 - e2 * sinFinal * sinFinal);
        var height = r / Math.Cos(phi) - nFinal;
        return (new GeoPoint(phi * 180.0 / Math.PI, lam * 180.0 / Math.PI), height);
    }
}
