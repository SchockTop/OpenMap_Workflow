using System.Collections.Concurrent;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Raster;

namespace OpenMapUnifier.Core.Elevation;

/// <summary>
/// Elevation provider over any tiled height dataset. Tiles are downloaded on
/// demand into <see cref="CacheDirectory"/> (skipped when already present),
/// parsed once, and kept in a small in-memory LRU so repeated queries in the
/// same area are effectively free.
/// </summary>
public sealed class TiledElevationProvider : IElevationProvider, IDisposable
{
    private readonly IHeightTileResolver _resolver;
    private readonly IDownloader _downloader;
    private readonly bool _ownsDownloader;
    private readonly int _maxCachedGrids;

    private readonly ConcurrentDictionary<TileId, Lazy<Task<HeightGrid?>>> _grids = new();
    private readonly ConcurrentQueue<TileId> _lru = new();

    public string CacheDirectory { get; }
    public ICoordinateTransform Transform { get; }

    public TiledElevationProvider(IHeightTileResolver resolver, string cacheDirectory,
        IDownloader? downloader = null, int maxCachedGrids = 16,
        ICoordinateTransform? transform = null)
    {
        _resolver = resolver;
        CacheDirectory = cacheDirectory;
        _ownsDownloader = downloader is null;
        _downloader = downloader ?? new HttpTileDownloader();
        _maxCachedGrids = Math.Max(1, maxCachedGrids);
        Transform = transform ?? Etrs89UtmTransform.Zone32;
        Directory.CreateDirectory(cacheDirectory);
    }

    public async Task<double?> GetElevationAsync(UtmPoint position, CancellationToken ct = default)
    {
        var grid = await GetGridAsync(_resolver.TileFor(position), ct).ConfigureAwait(false);
        return grid?.Sample(position);
    }

    /// <summary>The parsed height grid for a tile (downloads + caches on first use).</summary>
    public async Task<HeightGrid?> GetGridAsync(TileId tile, CancellationToken ct = default)
    {
        var lazy = _grids.GetOrAdd(tile, t => new Lazy<Task<HeightGrid?>>(() => LoadAsync(t, ct)));
        var grid = await lazy.Value.ConfigureAwait(false);
        if (grid is null)
            _grids.TryRemove(tile, out _); // don't cache failures forever
        return grid;
    }

    private async Task<HeightGrid?> LoadAsync(TileId tile, CancellationToken ct)
    {
        string? localPath;
        if (_resolver is ITileFetcher fetcher)
        {
            localPath = await fetcher.FetchAsync(tile, CacheDirectory, ct).ConfigureAwait(false);
        }
        else
        {
            var job = await _resolver.JobForAsync(tile, ct).ConfigureAwait(false);
            if (job is null)
                return null;
            var result = await _downloader.DownloadAsync(job, CacheDirectory, ct: ct).ConfigureAwait(false);
            localPath = result.Success ? result.LocalPath : null;
        }
        if (localPath is null)
            return null;

        var grid = _resolver.Parse(localPath, tile);
        _lru.Enqueue(tile);
        while (_lru.Count > _maxCachedGrids && _lru.TryDequeue(out var evict))
            if (!evict.Equals(tile))
                _grids.TryRemove(evict, out _);
        return grid;
    }

    public void Dispose()
    {
        if (_ownsDownloader && _downloader is IDisposable d) d.Dispose();
    }
}
