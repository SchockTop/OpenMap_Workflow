using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Germany;
using OpenMapUnifier.Germany.States;
using Xunit;

namespace OpenMapUnifier.Tests;

/// <summary>
/// Pins each state's tile naming / URL shape to the patterns verified live
/// against the portals (July 2026), so regressions in the builders are caught
/// offline.
/// </summary>
public class GermanStatesTests
{
    [Fact]
    public void Registry_ContainsAllSixteenStates()
    {
        Assert.Equal(16, GermanStates.All.Count);
        Assert.All(new[]
            {
                "by", "ni", "nw", "he", "th", "sn", "be", "bb",
                "st", "mv", "hh", "hb", "sh", "bw", "rp", "sl",
            },
            code => Assert.True(GermanStates.All.ContainsKey(code), $"missing state '{code}'"));
    }

    [Fact]
    public void BayernAndNiedersachsen_AreRegularRegistryStates()
    {
        var by = GermanStates.Get("by");
        Assert.Equal(32, by.UtmZone);
        Assert.Contains("dgm1", by.Datasets.Keys);
        Assert.Contains("dgm5", by.Datasets.Keys);

        var ni = GermanStates.Get("ni");
        Assert.Contains("dom1", ni.Datasets.Keys);
    }

    [Fact]
    public async Task Bayern_JobsThroughRegistry_MatchBayernTileSource()
    {
        var jobs = await GermanStates.Get("by").JobsForAsync("dgm1",
            new BoundingBox(729_000, 5_433_000, 730_000, 5_434_000));
        var job = Assert.Single(jobs);
        Assert.Equal("729_5433.tif", job.FileName);
        Assert.Equal(2, job.Mirrors.Count); // both bayernwolke mirrors
    }

    [Theory]
    [InlineData("be", 33)]
    [InlineData("bb", 33)]
    [InlineData("sn", 33)]
    [InlineData("mv", 33)]
    [InlineData("nw", 32)]
    [InlineData("bw", 32)]
    public void States_DeclareCorrectUtmZone(string code, int zone)
    {
        Assert.Equal(zone, GermanStates.Get(code).UtmZone);
    }

    [Fact]
    public void Brandenburg_UrlGluesZoneToEastingWithDash()
    {
        var job = Brandenburg.JobFor("dgm1", new TileId(367, 5807));
        Assert.Equal("dgm_33367-5807.zip", job.FileName);
        Assert.Equal("https://data.geobasis-bb.de/geobasis/daten/dgm/tif/dgm_33367-5807.zip", job.Mirrors[0]);
    }

    [Fact]
    public void Berlin_Dgm1_Uses2kmZipNaming()
    {
        var job = Berlin.JobFor("dgm1", new TileId(390, 5820, 2));
        Assert.Equal("DGM1_390_5820.zip", job.FileName);
        Assert.Equal("https://gdi.berlin.de/data/dgm1/atom/DGM1_390_5820.zip", job.Mirrors[0]);
    }

    [Fact]
    public void Sachsen_UrlConcatenatesZoneAndUsesNextcloudShare()
    {
        var job = Sachsen.JobFor("dgm1", new TileId(410, 5654, 2));
        Assert.Equal("dgm1_33410_5654_2_sn_tiff.zip", job.FileName);
        Assert.Equal(
            "https://geocloud.landesvermessung.sachsen.de/public.php/dav/files/JCcXyifaNdLDnxZ/dgm1_33410_5654_2_sn_tiff.zip",
            job.Mirrors[0]);
    }

    [Fact]
    public void MecklenburgVorpommern_BuildsAtomDownloadUrl()
    {
        var job = MecklenburgVorpommern.JobFor("dgm1", new TileId(288, 5946, 2));
        Assert.Equal("dgm1_33_288_5946_2_gtiff.tif", job.FileName);
        Assert.Contains("dgm_download?index=4&dataset=", job.Mirrors[0]);
        Assert.EndsWith("&file=dgm1_33_288_5946_2_gtiff.tif", job.Mirrors[0]);
    }

    [Fact]
    public void Thueringen_FallsBackToOlderEpochAsMirror()
    {
        var job = Thueringen.JobFor("dgm1", new TileId(644, 5647));
        Assert.Equal(2, job.Mirrors.Count);
        Assert.Equal(
            "https://geoportal.geoportal-th.de/hoehendaten/DGM/dgm_2020-2025/dgm1_32_644_5647_1_th_2020-2025.zip",
            job.Mirrors[0]);
        // Pre-2020 epoch names carry no zone token.
        Assert.Equal(
            "https://geoportal.geoportal-th.de/hoehendaten/DGM/dgm_2014-2019/dgm1_644_5647_1_th_2014-2019.zip",
            job.Mirrors[1]);
    }

    [Theory]
    [InlineData(513, 5402, 513, 5402)] // already odd/even
    [InlineData(514, 5402, 513, 5402)] // even easting snaps down to odd
    [InlineData(513, 5403, 513, 5402)] // odd northing snaps down to even
    [InlineData(514, 5403, 513, 5402)]
    public void BadenWuerttemberg_SnapsToOddEastEvenNorthCells(int e, int n, int cellE, int cellN)
    {
        Assert.Equal((cellE, cellN), BadenWuerttemberg.CellFor(new TileId(e, n)));
    }

    [Fact]
    public async Task BadenWuerttemberg_JobsAreDedupedPerCell()
    {
        var bw = new BadenWuerttemberg();
        // 2x2 km box fully inside one BW cell -> four 1 km tiles, ONE zip.
        var jobs = await bw.JobsForAsync("dgm1", new BoundingBox(513_000, 5_402_000, 515_000, 5_404_000));
        Assert.Single(jobs);
        Assert.Equal("dgm1_32_513_5402_2_bw.zip", jobs[0].FileName);
        Assert.Equal("https://opengeodata.lgl-bw.de/data/dgm/dgm1_32_513_5402_2_bw.zip", jobs[0].Mirrors[0]);
    }

    [Fact]
    public void NordrheinWestfalen_Lod2IsDeterministicAndCaseSensitive()
    {
        var job = NordrheinWestfalen.Lod2JobFor(new TileId(355, 5645));
        Assert.Equal("LoD2_32_355_5645_1_NW.gml", job.FileName);
        Assert.Equal(
            "https://www.opengeodata.nrw.de/produkte/geobasis/3dg/lod2_gml/lod2_gml/LoD2_32_355_5645_1_NW.gml",
            job.Mirrors[0]);
    }

    [Fact]
    public void RheinlandPfalz_Lod2Uses2kmGridWithRpSuffix()
    {
        var job = RheinlandPfalz.Lod2JobFor(new TileId(446, 5538, 2));
        Assert.Equal("LoD2_32_446_5538_2_RP.gml", job.FileName);
        Assert.Equal("https://geobasis-rlp.de/data/geb3dlo/current/gml/LoD2_32_446_5538_2_RP.gml",
            job.Mirrors[0]);
    }

    [Fact]
    public async Task Saarland_ArchiveJobsCoverAllLandkreise()
    {
        var sl = new Saarland();
        var jobs = await sl.JobsForAsync("dgm1", new BoundingBox(354_000, 5_455_000, 356_000, 5_457_000));
        Assert.Equal(6, jobs.Count);
        Assert.Contains(jobs, j =>
            j.Mirrors[0].EndsWith("/OD_DGM1_2025_tif_LK/DGM1_tif_SB_EPSG-25832_Entstehung-2025.zip"));
    }

    [Fact]
    public async Task Bremen_ArchiveJobsListBothCities()
    {
        var hb = new Bremen();
        var jobs = await hb.JobsForAsync("dgm1", new BoundingBox(487_000, 5_880_000, 488_000, 5_881_000));
        Assert.Equal(2, jobs.Count);
        Assert.Contains(jobs, j => j.FileName == "Gitternetz_DGM1_2017_HB_ASCII_XYZ.zip");
        Assert.Contains(jobs, j => j.FileName == "Gitternetz_DGM1_2015_BHV_ASCII_XYZ.zip");
    }

    [Fact]
    public void UnknownState_ThrowsWithKnownCodes()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => GermanStates.Get("xx"));
        Assert.Contains("nw", ex.Message);
        Assert.Contains("by", ex.Message);
    }

    [Fact]
    public void ElevationDatasets_RejectNonHeightProducts()
    {
        Assert.ThrowsAny<Exception>(() =>
            GermanStates.Get("bb").CreateElevationProvider("dop20rgbi", Path.GetTempPath()));
    }
}
