using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Import;
using Xunit;

namespace OpenMapUnifier.Tests;

public class ConversionSuiteTests
{
    // ---- Gauß-Krüger (DHDN/Bessel + EPSG:1777 Helmert), pyproj reference ----

    // Tolerance: pyproj selects REGIONAL DHDN Helmert variants per point (it
    // picked the all-West-Germany EPSG:1777 for Munich/Köln but a different
    // regional set for Berlin); we always use EPSG:1777. Differences stay
    // within the ~1 m accuracy class of the legacy datum itself.
    [Theory]
    [InlineData(48.137222, 11.575556, 4468517.5446, 5333330.4531, 0.002)] // Munich
    [InlineData(50.9375, 6.9603, 4145948.7012, 5656796.1810, 0.002)]      // Köln
    [InlineData(52.52, 13.405, 4595472.2165, 5821692.7436, 1.0)]          // Berlin (regional variant)
    public void GaussKruegerZone4_MatchesPyproj(double lat, double lon, double r, double h, double tol)
    {
        var gk = GaussKruegerTransform.Zone4.ToUtm(new GeoPoint(lat, lon));
        Assert.Equal(r, gk.Easting, tolerance: tol);
        Assert.Equal(h, gk.Northing, tolerance: tol);

        var back = GaussKruegerTransform.Zone4.ToGeo(new UtmPoint(gk.Easting, gk.Northing));
        Assert.Equal(lat, back.Latitude, 6);
        Assert.Equal(lon, back.Longitude, 6);
    }

    [Theory]
    [InlineData(48.137222, 11.575556, 3691761.0929, 5336456.5594)]
    [InlineData(50.9375, 6.9603, 3356705.2612, 5646673.5362)]
    public void GaussKruegerZone3_MatchesPyproj(double lat, double lon, double r, double h)
    {
        var gk = GaussKruegerTransform.Zone3.ToUtm(new GeoPoint(lat, lon));
        Assert.Equal(r, gk.Easting, tolerance: 2e-3);
        Assert.Equal(h, gk.Northing, tolerance: 2e-3);
    }

    // ---- Web Mercator, zone-prefixed, WGS84 UTM — pyproj reference ----------

    [Theory]
    [InlineData(48.137222, 11.575556, 1288584.9996, 6129714.1232)]
    [InlineData(52.52, 13.405, 1492237.7741, 6894699.8013)]
    public void WebMercator_MatchesPyproj(double lat, double lon, double x, double y)
    {
        var p = WebMercatorTransform.Instance.ToUtm(new GeoPoint(lat, lon));
        Assert.Equal(x, p.Easting, tolerance: 1e-3);
        Assert.Equal(y, p.Northing, tolerance: 1e-3);

        var back = WebMercatorTransform.Instance.ToGeo(new UtmPoint(x, y));
        Assert.Equal(lat, back.Latitude, 7);
        Assert.Equal(lon, back.Longitude, 7);
    }

    [Fact]
    public void ZonePrefixedUtm_AddsZoneMillionOffset()
    {
        var p = ZonePrefixedUtmTransform.Zone32.ToUtm(new GeoPoint(48.137222, 11.575556));
        Assert.Equal(32691607.8605, p.Easting, tolerance: 1e-3);  // pyproj EPSG:4647
        Assert.Equal(5334760.3881, p.Northing, tolerance: 1e-3);
    }

    [Fact]
    public void Wgs84Utm_MatchesPyproj()
    {
        var p = Wgs84UtmTransform.Zone32.ToUtm(new GeoPoint(48.137222, 11.575556));
        Assert.Equal(691607.8605, p.Easting, tolerance: 1e-3);    // pyproj EPSG:32632
        Assert.Equal(5334760.3882, p.Northing, tolerance: 1e-3);
    }

    // ---- Helmert & ECEF -------------------------------------------------------

    [Fact]
    public void Helmert_ApplyInverse_RoundTripsToMicrometers()
    {
        var t = HelmertTransform.DhdnToWgs84;
        var p = new EcefPoint(4_177_000, 855_000, 4_728_000);
        var back = t.ApplyInverse(t.Apply(p));
        Assert.Equal(p.X, back.X, tolerance: 1e-5);
        Assert.Equal(p.Y, back.Y, tolerance: 1e-5);
        Assert.Equal(p.Z, back.Z, tolerance: 1e-5);
    }

    [Fact]
    public void EcefGeodetic_RoundTripsWithHeight()
    {
        var geo = new GeoPoint(48.137222, 11.575556);
        var ecef = HelmertTransform.GeodeticToEcef(geo, Ellipsoid.Wgs84, 520.0);
        var (back, height) = HelmertTransform.EcefToGeodetic(ecef, Ellipsoid.Wgs84);
        Assert.Equal(geo.Latitude, back.Latitude, 9);
        Assert.Equal(geo.Longitude, back.Longitude, 9);
        Assert.Equal(520.0, height, tolerance: 1e-4);
    }

    // ---- Registry ---------------------------------------------------------------

    [Fact]
    public void Registry_ConvertsBetweenArbitraryCrs()
    {
        // GK4 -> UTM32 (the classic legacy-to-modern migration).
        var (x, y) = CrsRegistry.Convert(4468517.5446, 5333330.4531, 31468, 25832);
        Assert.Equal(691607.86, x, tolerance: 0.01);
        Assert.Equal(5334760.39, y, tolerance: 0.01);

        // 4326 planar convention is (lon, lat).
        var (lon, lat) = CrsRegistry.Convert(691607.8605, 5334760.3881, 25832, 4326);
        Assert.Equal(11.575556, lon, 6);
        Assert.Equal(48.137222, lat, 6);
    }

    [Fact]
    public void Registry_UnknownEpsg_ThrowsWithList()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => CrsRegistry.Get(12345));
        Assert.Contains("25832", ex.Message);
    }

    // ---- DMS ------------------------------------------------------------------------

    [Theory]
    [InlineData("48°08'14.0\"N 11°34'32.0\"E")]
    [InlineData("N48°08'14\" E11°34'32\"")]
    [InlineData("48 08 14 N, 11 34 32 E")]
    public void Dms_ParsesPairVariants(string text)
    {
        var geo = Dms.ParsePair(text);
        Assert.NotNull(geo);
        Assert.Equal(48.1372, geo!.Value.Latitude, 3);
        Assert.Equal(11.5755, geo.Value.Longitude, 3);
    }

    [Fact]
    public void Dms_ParsesDecimalPairAndHemisphereOrder()
    {
        var geo = Dms.ParsePair("48.137222, 11.575556");
        Assert.Equal(48.137222, geo!.Value.Latitude, 6);

        // Lon-first input, fixed by hemisphere letters.
        var swapped = Dms.ParsePair("11°34'32\"E 48°08'14\"N");
        Assert.Equal(48.1372, swapped!.Value.Latitude, 3);

        var south = Dms.ParsePair("33°55'S 18°25'E");
        Assert.True(south!.Value.Latitude < 0);
    }

    [Fact]
    public void Dms_FormatRoundTrips()
    {
        var geo = new GeoPoint(48.137222, 11.575556);
        var text = Dms.Format(geo);
        var back = Dms.ParsePair(text);
        Assert.Equal(geo.Latitude, back!.Value.Latitude, 5);
        Assert.Equal(geo.Longitude, back.Value.Longitude, 5);
    }

    // ---- Detector -------------------------------------------------------------------

    [Theory]
    [InlineData(48.137222, 11.575556, 4326, false)] // lat/lon
    [InlineData(11.575556, 48.137222, 4326, true)]  // lon/lat (GeoJSON order)
    [InlineData(691607.86, 5334760.39, 25832, false)]
    [InlineData(4468517.54, 5333330.45, 31468, false)]
    [InlineData(32691607.86, 5334760.39, 4647, false)]
    [InlineData(1288585.0, 6129714.1, 3857, false)]
    public void Detector_IdentifiesCrs(double a, double b, int expectedEpsg, bool swapped)
    {
        var best = CoordinateDetector.DetectBest(a, b);
        Assert.NotNull(best);
        Assert.Equal(expectedEpsg, best!.Epsg);
        Assert.Equal(swapped, best.AxesSwapped);
        // Whatever the input CRS, the position must resolve near Munich.
        Assert.Equal(48.137, best.Geo.Latitude, 2);
        Assert.Equal(11.576, best.Geo.Longitude, 2);
    }

    [Fact]
    public void Detector_UtmEasting_OffersBothZonesAsCandidates()
    {
        // A bare 6-digit easting is genuinely ambiguous between zones 32/33
        // (Berlin-in-33 vs Münsterland-in-32 both land inside Germany) — the
        // detector must surface BOTH interpretations for the user to pick.
        var guesses = CoordinateDetector.Detect(391779.26, 5820072.16);
        var utmGuesses = guesses.Where(g => g.Epsg is 25832 or 25833).ToList();
        Assert.Equal(2, utmGuesses.Count);
        var zone33 = utmGuesses.Single(g => g.Epsg == 25833);
        Assert.Equal(52.52, zone33.Geo.Latitude, tolerance: 0.01);
        Assert.Equal(13.405, zone33.Geo.Longitude, tolerance: 0.01);
    }

    [Fact]
    public void Detector_RejectsNonCoordinates()
    {
        Assert.Null(CoordinateDetector.DetectBest(1e9, -4));
    }

    // ---- Chaotic JSON importer ----------------------------------------------------------

    [Fact]
    public void ChaoticJson_RecoversEveryFormatAndSkipsJunk()
    {
        const string chaos = """
            {
              "camera": { "lat": 48.137222, "lon": 11.575556, "alt": 520.0 },
              "gk": { "Rechtswert": 4468517.54, "Hochwert": 5333330.45, "hoehe": "521,3" },
              "utm_strings": { "x": "691607,86", "y": "5334760,39" },
              "geojson_style": [11.575556, 48.137222],
              "dms": "48°08'14.0\"N 11°34'32.0\"E",
              "webmercator": { "x": 1288585.0, "y": 6129714.1 },
              "prefixed": { "ostwert": 32691607.9, "nordwert": 5334760.4 },
              "junk": [1, 2],
              "resolution": { "x": 1920, "y": 1080 },
              "label": "not a coordinate"
            }
            """;
        var found = ChaoticJsonImporter.Scan(chaos);

        Assert.Equal(7, found.Count);
        // Every find must resolve to (approximately) the same Munich position.
        Assert.All(found, f =>
        {
            Assert.Equal(48.137, f.Geo.Latitude, 2);
            Assert.Equal(11.576, f.Geo.Longitude, 2);
        });
        // Z values ride along, including German decimal commas.
        Assert.Equal(520.0, found.Single(f => f.Path == "$.camera").Z);
        Assert.Equal(521.3, found.Single(f => f.Path == "$.gk").Z!.Value, 3);
        // Junk stayed out.
        Assert.DoesNotContain(found, f => f.Path.StartsWith("$.junk"));
        Assert.DoesNotContain(found, f => f.Path.StartsWith("$.resolution"));
        // Conversion helper hits the shared position in EPSG:25832.
        var (x, y) = found[0].In(25832);
        Assert.Equal(691607.86, x, tolerance: 0.5);
        Assert.Equal(5334760.39, y, tolerance: 0.5);
    }
}
