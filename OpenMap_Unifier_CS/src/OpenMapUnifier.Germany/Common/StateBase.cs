using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Grid;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Raster;

namespace OpenMapUnifier.Germany.Common;

/// <summary>
/// Shared plumbing for state implementations: HTTP client creation (proxy-
/// aware), grid enumeration, and elevation-provider wiring.
/// </summary>
public abstract class StateBase : IGermanState
{
    public abstract string Code { get; }
    public abstract string Name { get; }
    public abstract int UtmZone { get; }
    public abstract string License { get; }
    public abstract string Attribution { get; }
    public abstract IReadOnlyDictionary<string, string> Datasets { get; }

    public Etrs89UtmTransform Transform => Etrs89UtmTransform.ForZone(UtmZone);

    public abstract Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default);

    public TiledElevationProvider CreateElevationProvider(string datasetId, string cacheDirectory,
        IDownloader? downloader = null, ProxyManager? proxy = null)
    {
        var resolver = CreateHeightResolver(datasetId, proxy);
        downloader ??= new HttpTileDownloader(new DownloaderOptions { Proxy = proxy });
        return new TiledElevationProvider(resolver, cacheDirectory, downloader,
            maxCachedGrids: 16, transform: Transform);
    }

    /// <summary>Height-tile resolver for one of the state's elevation datasets.</summary>
    protected abstract IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy);

    protected static HttpClient CreateHttp(ProxyManager? proxy, int timeoutSeconds = 300) =>
        (proxy ?? new ProxyManager()).CreateClient(TimeSpan.FromSeconds(timeoutSeconds));

    protected KeyNotFoundException UnknownDataset(string datasetId) => new(
        $"Unknown dataset '{datasetId}' for {Name}. Available: {string.Join(", ", Datasets.Keys)}.");

    /// <summary>Elevation datasets must be one of the state's height products.</summary>
    protected void RequireHeightDataset(string datasetId, params string[] allowed)
    {
        if (!allowed.Contains(datasetId, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"'{datasetId}' is not an elevation dataset for {Name} " +
                $"(use {string.Join(" or ", allowed)}).", nameof(datasetId));
    }
}

/// <summary>
/// Resolver built from lambdas — most states differ only in "URL for tile" and
/// "how to parse the downloaded file", so the tile math and caching stay here.
/// </summary>
public sealed class DelegateTileResolver : IHeightTileResolver
{
    private readonly Func<TileId, CancellationToken, Task<DownloadJob?>> _jobFor;
    private readonly Func<string, TileId, HeightGrid> _parse;

    public int GridKm { get; }

    public DelegateTileResolver(int gridKm,
        Func<TileId, CancellationToken, Task<DownloadJob?>> jobFor,
        Func<string, TileId, HeightGrid> parse)
    {
        GridKm = gridKm;
        _jobFor = jobFor;
        _parse = parse;
    }

    public TileId TileFor(UtmPoint position) => TileGrid.TileFor(position, GridKm);
    public Task<DownloadJob?> JobForAsync(TileId tile, CancellationToken ct = default) => _jobFor(tile, ct);
    public HeightGrid Parse(string localPath, TileId tile) => _parse(localPath, tile);
}

/// <summary>
/// Resolver for archive-only states (Hamburg, Bremen, Saarland, Hessen):
/// tiles are range-extracted from remote zips via <see cref="RemoteArchiveFetcher"/>
/// instead of downloaded per URL.
/// </summary>
public sealed class ArchiveTileResolver : IHeightTileResolver, ITileFetcher
{
    private readonly RemoteArchiveFetcher _fetcher;
    private readonly Func<TileId, Func<string, bool>> _entryFilterFor;
    private readonly Func<TileId, string> _cacheNameFor;
    private readonly Func<string, TileId, HeightGrid> _parse;

    public int GridKm { get; }

    public ArchiveTileResolver(int gridKm, RemoteArchiveFetcher fetcher,
        Func<TileId, Func<string, bool>> entryFilterFor,
        Func<TileId, string> cacheNameFor,
        Func<string, TileId, HeightGrid> parse)
    {
        GridKm = gridKm;
        _fetcher = fetcher;
        _entryFilterFor = entryFilterFor;
        _cacheNameFor = cacheNameFor;
        _parse = parse;
    }

    public TileId TileFor(UtmPoint position) => TileGrid.TileFor(position, GridKm);

    public Task<DownloadJob?> JobForAsync(TileId tile, CancellationToken ct = default) =>
        Task.FromResult<DownloadJob?>(null); // unused — FetchAsync drives this resolver

    public Task<string?> FetchAsync(TileId tile, string cacheDirectory, CancellationToken ct = default) =>
        _fetcher.FetchAsync(_entryFilterFor(tile), _cacheNameFor(tile), cacheDirectory, ct);

    public HeightGrid Parse(string localPath, TileId tile) => _parse(localPath, tile);
}
