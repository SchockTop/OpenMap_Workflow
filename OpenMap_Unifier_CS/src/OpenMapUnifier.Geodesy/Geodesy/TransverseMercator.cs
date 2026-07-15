namespace OpenMapUnifier.Geodesy;

/// <summary>
/// Ellipsoid-generic transverse Mercator projection using the Karney/Krüger
/// 6th-order series (the method proj uses; sub-millimeter accuracy within a
/// zone). Powers UTM (GRS80/WGS84) and the legacy Gauß-Krüger (Bessel 1841)
/// projections. Input/output geographic coordinates are on THIS projection's
/// datum — datum shifts happen outside (see <see cref="HelmertTransform"/>).
/// </summary>
public sealed class TransverseMercator
{
    private readonly double _lon0Deg;
    private readonly double _k0;
    private readonly double _falseEasting;
    private readonly double _falseNorthing;
    private readonly double _n;
    private readonly double _aa;
    private readonly double[] _alpha;
    private readonly double[] _beta;
    private readonly double[] _delta;

    public Ellipsoid Ellipsoid { get; }

    public TransverseMercator(Ellipsoid ellipsoid, double centralMeridianDeg,
        double scale = 0.9996, double falseEasting = 500_000.0, double falseNorthing = 0.0)
    {
        Ellipsoid = ellipsoid;
        _lon0Deg = centralMeridianDeg;
        _k0 = scale;
        _falseEasting = falseEasting;
        _falseNorthing = falseNorthing;

        var f = ellipsoid.Flattening;
        var n = _n = f / (2.0 - f);
        var n2 = n * n;
        _aa = ellipsoid.SemiMajorAxis / (1 + n) * (1 + n2 / 4 + n2 * n2 / 64 + n2 * n2 * n2 / 256);
        _alpha = ComputeAlpha(n);
        _beta = ComputeBeta(n);
        _delta = ComputeDelta(n);
    }

    /// <summary>Geographic (on this datum) → projected easting/northing.</summary>
    public UtmPoint Forward(GeoPoint geo)
    {
        var phi = geo.Latitude * Math.PI / 180.0;
        var lam = (geo.Longitude - _lon0Deg) * Math.PI / 180.0;

        var e2n = 2 * Math.Sqrt(_n) / (1 + _n);
        var sinPhi = Math.Sin(phi);
        var t = Math.Sinh(Atanh(sinPhi) - e2n * Atanh(e2n * sinPhi));
        var xiP = Math.Atan2(t, Math.Cos(lam));
        var etaP = Asinh(Math.Sin(lam) / Math.Sqrt(t * t + Math.Cos(lam) * Math.Cos(lam)));

        double xi = xiP, eta = etaP;
        for (var j = 1; j <= 6; j++)
        {
            xi += _alpha[j] * Math.Sin(2 * j * xiP) * Math.Cosh(2 * j * etaP);
            eta += _alpha[j] * Math.Cos(2 * j * xiP) * Math.Sinh(2 * j * etaP);
        }

        return new UtmPoint(_falseEasting + _k0 * _aa * eta, _falseNorthing + _k0 * _aa * xi);
    }

    /// <summary>Projected easting/northing → geographic (on this datum).</summary>
    public GeoPoint Inverse(UtmPoint p)
    {
        var xi = (p.Northing - _falseNorthing) / (_k0 * _aa);
        var eta = (p.Easting - _falseEasting) / (_k0 * _aa);

        double xiP = xi, etaP = eta;
        for (var j = 1; j <= 6; j++)
        {
            xiP -= _beta[j] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            etaP -= _beta[j] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }

        var chi = Math.Asin(Math.Sin(xiP) / Math.Cosh(etaP));
        var phi = chi;
        for (var j = 1; j <= 6; j++)
            phi += _delta[j] * Math.Sin(2 * j * chi);

        var lam = Math.Atan2(Math.Sinh(etaP), Math.Cos(xiP));
        return new GeoPoint(phi * 180.0 / Math.PI, _lon0Deg + lam * 180.0 / Math.PI);
    }

    private static double Atanh(double x) => 0.5 * Math.Log((1 + x) / (1 - x));
    private static double Asinh(double x) => Math.Log(x + Math.Sqrt(x * x + 1));

    private static double[] ComputeAlpha(double n)
    {
        double n2 = n * n, n3 = n2 * n, n4 = n3 * n, n5 = n4 * n, n6 = n5 * n;
        return new[]
        {
            0.0,
            n / 2 - 2 * n2 / 3 + 5 * n3 / 16 + 41 * n4 / 180 - 127 * n5 / 288 + 7891 * n6 / 37800,
            13 * n2 / 48 - 3 * n3 / 5 + 557 * n4 / 1440 + 281 * n5 / 630 - 1983433 * n6 / 1935360,
            61 * n3 / 240 - 103 * n4 / 140 + 15061 * n5 / 26880 + 167603 * n6 / 181440,
            49561 * n4 / 161280 - 179 * n5 / 168 + 6601661 * n6 / 7257600,
            34729 * n5 / 80640 - 3418889 * n6 / 1995840,
            212378941 * n6 / 319334400,
        };
    }

    private static double[] ComputeBeta(double n)
    {
        double n2 = n * n, n3 = n2 * n, n4 = n3 * n, n5 = n4 * n, n6 = n5 * n;
        return new[]
        {
            0.0,
            n / 2 - 2 * n2 / 3 + 37 * n3 / 96 - n4 / 360 - 81 * n5 / 512 + 96199 * n6 / 604800,
            n2 / 48 + n3 / 15 - 437 * n4 / 1440 + 46 * n5 / 105 - 1118711 * n6 / 3870720,
            17 * n3 / 480 - 37 * n4 / 840 - 209 * n5 / 4480 + 5569 * n6 / 90720,
            4397 * n4 / 161280 - 11 * n5 / 504 - 830251 * n6 / 7257600,
            4583 * n5 / 161280 - 108847 * n6 / 3991680,
            20648693 * n6 / 638668800,
        };
    }

    private static double[] ComputeDelta(double n)
    {
        double n2 = n * n, n3 = n2 * n, n4 = n3 * n, n5 = n4 * n, n6 = n5 * n;
        return new[]
        {
            0.0,
            2 * n - 2 * n2 / 3 - 2 * n3 + 116 * n4 / 45 + 26 * n5 / 45 - 2854 * n6 / 675,
            7 * n2 / 3 - 8 * n3 / 5 - 227 * n4 / 45 + 2704 * n5 / 315 + 2323 * n6 / 945,
            56 * n3 / 15 - 136 * n4 / 35 - 1262 * n5 / 105 + 73814 * n6 / 2835,
            4279 * n4 / 630 - 332 * n5 / 35 - 399572 * n6 / 14175,
            4174 * n5 / 315 - 144838 * n6 / 6237,
            601676 * n6 / 22275,
        };
    }
}
