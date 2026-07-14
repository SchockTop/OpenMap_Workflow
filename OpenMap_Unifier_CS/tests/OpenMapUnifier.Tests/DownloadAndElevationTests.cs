using System.Net;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Raster;
using Xunit;

namespace OpenMapUnifier.Tests;

public class DownloadAndElevationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("omu-tests-").FullName;

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Requested { get; } = new();
        public Func<string, (HttpStatusCode Status, byte[] Body)> Respond { get; set; } =
            _ => (HttpStatusCode.OK, "data"u8.ToArray());

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            var (status, body) = Respond(url);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }

    private static HttpTileDownloader Downloader(StubHandler handler, int retries = 0) =>
        new(new DownloaderOptions { RetriesPerMirror = retries, RetryBaseDelay = TimeSpan.Zero },
            new HttpClient(handler));

    [Fact]
    public async Task Download_FallsThroughToSecondMirror()
    {
        var handler = new StubHandler
        {
            Respond = url => url.Contains("download1")
                ? (HttpStatusCode.NotFound, Array.Empty<byte>())
                : (HttpStatusCode.OK, "tile-bytes"u8.ToArray()),
        };
        using var downloader = Downloader(handler);
        var job = new DownloadJob("t.tif", new[]
        {
            "https://download1.example/a/t.tif",
            "https://download2.example/a/t.tif",
        });

        var result = await downloader.DownloadAsync(job, _dir);

        Assert.True(result.Success);
        Assert.Equal("tile-bytes", File.ReadAllText(result.LocalPath!));
        Assert.Equal(2, handler.Requested.Count);
        Assert.False(File.Exists(result.LocalPath + ".part"));
    }

    [Fact]
    public async Task Download_SkipsExistingFile()
    {
        File.WriteAllText(Path.Combine(_dir, "have.tif"), "already here");
        var handler = new StubHandler();
        using var downloader = Downloader(handler);

        var result = await downloader.DownloadAsync(
            new DownloadJob("have.tif", "https://x.example/have.tif"), _dir);

        Assert.True(result.Success);
        Assert.True(result.Skipped);
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task Download_AllMirrorsFail_ReportsLastError()
    {
        var handler = new StubHandler
        {
            Respond = _ => (HttpStatusCode.NotFound, Array.Empty<byte>()),
        };
        using var downloader = Downloader(handler);

        var result = await downloader.DownloadAsync(
            new DownloadJob("gone.tif", new[] { "https://a.example/x", "https://b.example/x" }), _dir);

        Assert.False(result.Success);
        Assert.Contains("404", result.Error);
    }

    private sealed class FakeResolver : IHeightTileResolver
    {
        public int ParseCalls;
        public int GridKm => 1;
        public TileId TileFor(UtmPoint p) => TileGrid.TileFor(p);
        public Task<DownloadJob?> JobForAsync(TileId tile, CancellationToken ct = default) =>
            Task.FromResult<DownloadJob?>(
                new DownloadJob($"{tile.Key}.tif", $"https://tiles.example/{tile.Key}.tif"));

        public HeightGrid Parse(string localPath, TileId tile)
        {
            Interlocked.Increment(ref ParseCalls);
            var data = new float[100];
            Array.Fill(data, 42f);
            return new HeightGrid(data, 10, 10,
                tile.MinEasting, tile.MaxNorthing, 100.0);
        }
    }

    [Fact]
    public async Task ElevationProvider_DownloadsOnceAndSamples()
    {
        var handler = new StubHandler();
        var resolver = new FakeResolver();
        using var provider = new TiledElevationProvider(resolver, _dir, Downloader(handler));

        var p = new UtmPoint(729_500, 5_433_500);
        var first = await provider.GetElevationAsync(p);
        var second = await provider.GetElevationAsync(new UtmPoint(729_100, 5_433_900));

        Assert.Equal(42.0, first);
        Assert.Equal(42.0, second);
        Assert.Single(handler.Requested);      // one tile fetch
        Assert.Equal(1, resolver.ParseCalls);  // parsed once, cached in memory
    }

    [Fact]
    public async Task ElevationProvider_ReturnsNullWhenTileMissing()
    {
        var handler = new StubHandler
        {
            Respond = _ => (HttpStatusCode.NotFound, Array.Empty<byte>()),
        };
        using var provider = new TiledElevationProvider(new FakeResolver(), _dir, Downloader(handler));

        Assert.Null(await provider.GetElevationAsync(new UtmPoint(1_000, 1_000)));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
