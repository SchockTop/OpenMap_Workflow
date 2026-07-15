using OpenMapUnifier.Germany.Bayern;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using Xunit;

namespace OpenMapUnifier.Tests;

/// <summary>
/// Tile filenames and URL shapes, pinned against the patterns verified live
/// in the Python Unifier (backend/downloader.py).
/// </summary>
public class BayernNamingTests
{
    private readonly BayernTileSource _source = new();

    [Theory]
    [InlineData("dgm1", 729, 5433, "729_5433.tif")]        // DGM: no "32" prefix
    [InlineData("dgm5", 729, 5433, "729_5433.zip")]
    [InlineData("dop20", 672, 5424, "32672_5424.tif")]     // DOP: "32" zone prefix
    [InlineData("dop40", 672, 5424, "32672_5424.tif")]
    [InlineData("dom20", 689, 5333, "32689_5333_20_DOM.tif")]
    [InlineData("laser", 672, 5424, "32672_5424.laz")]
    public void FileName_MatchesLiveServerPattern(string dataset, int eastKm, int northKm, string expected)
    {
        Assert.Equal(expected, _source.FileNameFor(dataset, new TileId(eastKm, northKm)));
    }

    [Fact]
    public void Dgm1_Urls_UseDgmGroupPathOnBothMirrors()
    {
        var job = _source.JobFor("dgm1", new TileId(729, 5433));
        Assert.Equal(new[]
        {
            "https://download1.bayernwolke.de/a/dgm/dgm1/729_5433.tif",
            "https://download2.bayernwolke.de/a/dgm/dgm1/729_5433.tif",
        }, job.Mirrors);
    }

    [Fact]
    public void Dgm5_Url_UsesDgm5xyzPath()
    {
        var job = _source.JobFor("dgm5", new TileId(729, 5433));
        Assert.Equal("https://download1.bayernwolke.de/a/dgm/dgm5xyz/729_5433.zip", job.Mirrors[0]);
    }

    [Fact]
    public void Dop20_Url_UsesDataPath()
    {
        var job = _source.JobFor("dop20", new TileId(672, 5424));
        Assert.Equal("https://download1.bayernwolke.de/a/dop20/data/32672_5424.tif", job.Mirrors[0]);
    }

    [Fact]
    public void Lod2_Marienplatz_SnapsToEven2kmTile()
    {
        // The exact case documented in the Python catalog: Marienplatz
        // (UTM ~691, 5334) -> 690_5334.gml under /a/lod2/citygml/.
        var tile = TileGrid.TileFor(new UtmPoint(691_607.86, 5_334_760.39),
            BayernCatalog.Instance["lod2"].GridKm);
        var job = _source.JobFor("lod2", tile);
        Assert.Equal("690_5334.gml", job.FileName);
        Assert.Equal("https://download1.bayernwolke.de/a/lod2/citygml/690_5334.gml", job.Mirrors[0]);
    }

    [Fact]
    public void WmsUrl_HasVerifiedRequestShape()
    {
        var wms = new BayernWmsSource();
        var url = wms.UrlFor("relief_wms", new TileId(668, 5424));
        Assert.StartsWith("https://geoservices.bayern.de/pro/wms/dgm/v1/relief?", url);
        Assert.Contains("service=wms&version=1.1.1&request=GetMap", url);
        Assert.Contains("layers=by_relief_schraeglicht", url);
        Assert.Contains("srs=EPSG:25832", url);
        Assert.Contains("BBOX=668000,5424000,669000,5425000", url);
    }

    [Fact]
    public void WmsHighRes_Uses300DpiRender()
    {
        var wms = new BayernWmsSource { HighRes = true };
        var url = wms.UrlFor("dop20cir_wms", new TileId(691, 5334));
        Assert.Contains("WIDTH=5906", url);
        Assert.Contains("DPI=300", url);
        Assert.Contains("layers=by_dop20cir", url);
    }

    [Fact]
    public void EstimateSizeMb_ScalesWithTileCount()
    {
        var box = new BoundingBox(691_200, 5_334_100, 693_800, 5_335_900); // 6 tiles
        Assert.Equal(6 * 4.0, _source.EstimateSizeMb("dgm1", box));
    }

    [Fact]
    public void UnknownDataset_ThrowsWithKnownList()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => BayernCatalog.Instance["nope"]);
        Assert.Contains("dgm1", ex.Message);
    }
}
