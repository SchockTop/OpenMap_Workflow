using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Grid;
using OpenMapUnifier.Raster;

namespace OpenMapUnifier.Germany.Bayern;

/// <summary>
/// Height tile resolvers for Bayern's elevation products, and factory helpers
/// for ready-to-use providers. DGM1 (1 m bare earth) is the default; DOM20
/// (20 cm first-return surface: roofs, canopy) and DGM5 (5 m, tiny tiles) are
/// available for surface queries and large areas respectively.
/// </summary>
public static class BayernElevation
{
    /// <summary>Terrain height provider on DGM1 (1 m grid). ~4 MB per queried km².</summary>
    public static TiledElevationProvider CreateDgm1Provider(string cacheDirectory,
        IDownloader? downloader = null, int maxCachedGrids = 16) =>
        new(new GeoTiffTileResolver("dgm1"), cacheDirectory, downloader, maxCachedGrids);

    /// <summary>Terrain height provider on DGM5 (5 m grid). ~0.8 MB per queried km².</summary>
    public static TiledElevationProvider CreateDgm5Provider(string cacheDirectory,
        IDownloader? downloader = null, int maxCachedGrids = 16) =>
        new(new Dgm5XyzResolver(), cacheDirectory, downloader, maxCachedGrids);

    /// <summary>
    /// Surface height provider on DOM20 (20 cm grid, first return — includes
    /// buildings and vegetation). ~44 MB per queried km²; DOM20 − DGM1 gives
    /// object height above ground (nDSM).
    /// </summary>
    public static TiledElevationProvider CreateDom20Provider(string cacheDirectory,
        IDownloader? downloader = null, int maxCachedGrids = 4) =>
        new(new GeoTiffTileResolver("dom20"), cacheDirectory, downloader, maxCachedGrids);

    /// <summary>Resolver for Bayern GeoTIFF elevation products (DGM1, DOM20).</summary>
    public sealed class GeoTiffTileResolver : IHeightTileResolver
    {
        private readonly BayernTileSource _source;
        private readonly string _datasetId;

        public int GridKm { get; }

        public GeoTiffTileResolver(string datasetId, BayernTileSource? source = null)
        {
            _datasetId = datasetId;
            _source = source ?? new BayernTileSource();
            GridKm = BayernCatalog.Instance[datasetId].GridKm;
        }

        public TileId TileFor(UtmPoint position) => TileGrid.TileFor(position, GridKm);
        public Task<DownloadJob?> JobForAsync(TileId tile, CancellationToken ct = default) =>
            Task.FromResult<DownloadJob?>(_source.JobFor(_datasetId, tile));
        public HeightGrid Parse(string localPath, TileId tile) => GeoTiffReader.Read(localPath);
    }

    /// <summary>Resolver for DGM5's zipped-XYZ tiles.</summary>
    public sealed class Dgm5XyzResolver : IHeightTileResolver
    {
        private readonly BayernTileSource _source;

        public int GridKm => 1;

        public Dgm5XyzResolver(BayernTileSource? source = null)
        {
            _source = source ?? new BayernTileSource();
        }

        public TileId TileFor(UtmPoint position) => TileGrid.TileFor(position, GridKm);
        public Task<DownloadJob?> JobForAsync(TileId tile, CancellationToken ct = default) =>
            Task.FromResult<DownloadJob?>(_source.JobFor("dgm5", tile));
        public HeightGrid Parse(string localPath, TileId tile) => XyzGridReader.ReadZip(localPath);
    }
}
