using OpenMapUnifier.Germany.Niedersachsen;
using Xunit;

namespace OpenMapUnifier.Tests;

public class NiedersachsenTests
{
    // Trimmed from a real response of
    // https://dgm.stac.lgln.niedersachsen.de/collections/dgm1/items?bbox=... (July 2026),
    // plus a synthetic second epoch for tile 550_5803 to exercise newest-wins.
    private const string StacPage = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "id": "dgm1_32_550_5803_1_ni_2016",
              "properties": { "los": "L1603", "datetime": "2016-04-18T00:00:00Z" },
              "assets": {
                "dgm1-tif": {
                  "href": "https://dgm1.s3.eu-de.cloud-object-storage.appdomain.cloud/L1603/Vollkacheln/dgm1_32_550_5803_1_ni_2016.tif",
                  "type": "image/tiff; application=geotiff; profile=cloud-optimized"
                },
                "dgm1-metadata": {
                  "href": "https://dgm1.s3.eu-de.cloud-object-storage.appdomain.cloud/L1603/Vollkacheln/dgm1_32_550_5803_1_ni_2016.xml",
                  "type": "text/plain"
                }
              }
            },
            {
              "type": "Feature",
              "id": "dgm1_32_550_5803_1_ni_2022",
              "properties": { "los": "L2201", "datetime": "2022-03-01T00:00:00Z" },
              "assets": {
                "dgm1-tif": {
                  "href": "https://dgm1.s3.eu-de.cloud-object-storage.appdomain.cloud/L2201/Vollkacheln/dgm1_32_550_5803_1_ni_2022.tif"
                }
              }
            },
            {
              "type": "Feature",
              "id": "dgm1_32_550_5802_1_ni_2016",
              "properties": { "los": "L1603", "datetime": "2016-04-18T00:00:00Z" },
              "assets": {
                "dgm1-tif": {
                  "href": "https://dgm1.s3.eu-de.cloud-object-storage.appdomain.cloud/L1603/Vollkacheln/dgm1_32_550_5802_1_ni_2016.tif"
                }
              }
            }
          ],
          "links": [
            { "rel": "next", "href": "https://dgm.stac.lgln.niedersachsen.de/collections/dgm1/items?page=2" }
          ]
        }
        """;

    [Fact]
    public void ParsePage_ExtractsItemsAssetsAndNextLink()
    {
        var items = new List<StacItem>();
        var next = StacClient.ParsePage(StacPage, items);

        Assert.Equal(3, items.Count);
        Assert.Equal("dgm1_32_550_5803_1_ni_2016", items[0].Id);
        Assert.Equal(new DateTimeOffset(2016, 4, 18, 0, 0, 0, TimeSpan.Zero), items[0].Datetime);
        Assert.Equal(2, items[0].Assets.Count);
        Assert.EndsWith("dgm1_32_550_5803_1_ni_2016.tif", items[0].Asset("dgm1-tif")!.Href);
        Assert.Contains("page=2", next);
    }

    [Theory]
    [InlineData("dgm1_32_550_5802_1_ni_2016", 550, 5802)]
    [InlineData("dom1_32_550_5803_1_ni_2016", 550, 5803)]
    [InlineData("dop20rgbi_32_550_5802_2_ni_2025-03-06", 550, 5802)]
    public void TileKeyFromItemId_ParsesAllProducts(string id, int east, int north)
    {
        Assert.Equal((east, north), NiedersachsenCatalog.TileKeyFromItemId(id));
    }

    [Fact]
    public void TileKeyFromItemId_RejectsForeignIds()
    {
        Assert.Null(NiedersachsenCatalog.TileKeyFromItemId("not_a_tile"));
    }

    [Fact]
    public void JobFromItem_UsesAssetFileName()
    {
        var items = new List<StacItem>();
        StacClient.ParsePage(StacPage, items);

        var job = NiedersachsenTileSource.JobFromItem(NiedersachsenCatalog.Get("dgm1"), items[0]);

        Assert.NotNull(job);
        Assert.Equal("dgm1_32_550_5803_1_ni_2016.tif", job!.FileName);
        Assert.Single(job.Mirrors);
        Assert.StartsWith("https://dgm1.s3.eu-de.cloud-object-storage.appdomain.cloud/", job.Mirrors[0]);
    }

    [Fact]
    public void JobFromItem_MissingAsset_ReturnsNull()
    {
        var item = new StacItem("dgm1_32_1_1_1_ni_2020", null, Array.Empty<StacAsset>());
        Assert.Null(NiedersachsenTileSource.JobFromItem(NiedersachsenCatalog.Get("dgm1"), item));
    }

    [Fact]
    public void Catalog_UnknownDataset_ThrowsWithKnownList()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => NiedersachsenCatalog.Get("dgm5"));
        Assert.Contains("dom1", ex.Message);
    }

    private sealed class StubStacHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // First page carries the items + a next link; the second page ends
            // pagination with an empty feature list.
            var body = request.RequestUri!.ToString().Contains("page=2")
                ? """{ "type": "FeatureCollection", "features": [], "links": [] }"""
                : StacPage;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }

    [Fact]
    public async Task JobsForAsync_FollowsPaginationAndKeepsNewestPerTile()
    {
        using var source = new NiedersachsenTileSource(
            new StacClient(new HttpClient(new StubStacHandler())));
        var area = new Core.Geodesy.BoundingBox(550_000, 5_802_000, 551_000, 5_804_000);

        var jobs = await source.JobsForAsync("dgm1", area);

        Assert.Equal(2, jobs.Count); // two tiles, not three items
        Assert.Contains(jobs, j => j.FileName == "dgm1_32_550_5802_1_ni_2016.tif");
        // Tile 550_5803 was flown in 2016 and 2022 — the 2022 epoch must win.
        Assert.Contains(jobs, j => j.FileName == "dgm1_32_550_5803_1_ni_2022.tif");
    }
}
