using OpenMapUnifier.Core.Geodesy;
using Xunit;

namespace OpenMapUnifier.Tests;

public class TransformTests
{
    // Reference values computed with pyproj (EPSG:4326 -> EPSG:25832).
    public static TheoryData<double, double, double, double> ReferencePoints => new()
    {
        { 48.137222, 11.575556, 691607.8605, 5334760.3881 }, // Munich Marienplatz
        { 48.000000, 9.000000, 500000.0000, 5316300.2243 },  // on the central meridian
        { 49.891667, 10.898333, 636349.2236, 5528313.7197 }, // Bamberg
        { 47.550000, 12.100000, 733239.8692, 5270944.4965 }, // Alps, SE Bavaria
        { 50.300000, 13.400000, 813311.9454, 5581251.3636 }, // far NE corner
    };

    [Theory]
    [MemberData(nameof(ReferencePoints))]
    public void ToUtm32_MatchesPyproj(double lat, double lon, double expectedE, double expectedN)
    {
        var utm = Etrs89Utm32Transform.Instance.ToUtm32(new GeoPoint(lat, lon));
        // Reference values carry 4 decimals, so allow a millimeter.
        Assert.Equal(expectedE, utm.Easting, tolerance: 1e-3);
        Assert.Equal(expectedN, utm.Northing, tolerance: 1e-3);
    }

    [Theory]
    [MemberData(nameof(ReferencePoints))]
    public void ToGeo_MatchesPyproj(double lat, double lon, double e, double n)
    {
        var geo = Etrs89Utm32Transform.Instance.ToGeo(new Utm32Point(e, n));
        Assert.Equal(lat, geo.Latitude, 7);
        Assert.Equal(lon, geo.Longitude, 7);
    }

    [Fact]
    public void RoundTrip_IsStable()
    {
        var t = Etrs89Utm32Transform.Instance;
        for (var lat = 47.3; lat <= 50.5; lat += 0.37)
        {
            for (var lon = 9.0; lon <= 13.8; lon += 0.43)
            {
                var back = t.ToGeo(t.ToUtm32(new GeoPoint(lat, lon)));
                Assert.Equal(lat, back.Latitude, 9);
                Assert.Equal(lon, back.Longitude, 9);
            }
        }
    }
}
