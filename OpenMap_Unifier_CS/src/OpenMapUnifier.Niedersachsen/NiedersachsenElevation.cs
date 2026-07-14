using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Core.Raster;

namespace OpenMapUnifier.Niedersachsen;

/// <summary>
/// Height providers for Niedersachsen's elevation COGs. Tile URLs are
/// resolved through the STAC API per tile (they embed acquisition batch and
/// year), then downloaded and sampled exactly like Bayern tiles — the COGs
/// are ordinary tiled LZW float32 GeoTIFFs with NoData -9999.
/// </summary>
public static class NiedersachsenElevation
{
    /// <summary>Terrain height provider on DGM1 (1 m grid).</summary>
    public static TiledElevationProvider CreateDgm1Provider(string cacheDirectory,
        IDownloader? downloader = null, ProxyManager? proxy = null, int maxCachedGrids = 16) =>
        Create("dgm1", cacheDirectory, downloader, proxy, maxCachedGrids);

    /// <summary>Surface height provider on DOM1 (1 m grid, includes buildings/trees).</summary>
    public static TiledElevationProvider CreateDom1Provider(string cacheDirectory,
        IDownloader? downloader = null, ProxyManager? proxy = null, int maxCachedGrids = 16) =>
        Create("dom1", cacheDirectory, downloader, proxy, maxCachedGrids);

    private static TiledElevationProvider Create(string datasetId, string cacheDirectory,
        IDownloader? downloader, ProxyManager? proxy, int maxCachedGrids)
    {
        downloader ??= new HttpTileDownloader(new DownloaderOptions { Proxy = proxy });
        return new TiledElevationProvider(
            new StacTileResolver(datasetId, proxy), cacheDirectory, downloader, maxCachedGrids);
    }

    /// <summary>Resolver that answers "which file covers this tile?" via STAC.</summary>
    public sealed class StacTileResolver : IHeightTileResolver
    {
        private readonly string _datasetId;
        private readonly NiedersachsenTileSource _source;

        public int GridKm { get; }

        public StacTileResolver(string datasetId, ProxyManager? proxy = null,
            NiedersachsenTileSource? source = null)
        {
            _datasetId = datasetId;
            GridKm = NiedersachsenCatalog.Get(datasetId).GridKm;
            _source = source ?? new NiedersachsenTileSource(new StacClient(proxy: proxy));
        }

        public TileId TileFor(UtmPoint position) => TileGrid.TileFor(position, GridKm);

        public async Task<DownloadJob?> JobForAsync(TileId tile, CancellationToken ct = default)
        {
            var item = await _source.ItemForTileAsync(_datasetId, tile, ct).ConfigureAwait(false);
            return item is null ? null : NiedersachsenTileSource.JobFromItem(NiedersachsenCatalog.Get(_datasetId), item);
        }

        public HeightGrid Parse(string localPath, TileId tile) => GeoTiffReader.Read(localPath);
    }
}
