using OpenMapUnifier.Core.Catalog;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Geometry;
using OpenMapUnifier.Core.Grid;

namespace OpenMapUnifier.Germany.Bayern;

/// <summary>
/// Turns an area of interest into download jobs for a Bayern raw dataset —
/// the C# counterpart of the Python Unifier's generate_1km_grid_files().
/// Every job carries one URL per bayernwolke mirror so the downloader can
/// fall through when a mirror flaps.
/// </summary>
public sealed class BayernTileSource
{
    private readonly BayernCatalog _catalog;
    private readonly IReadOnlyList<string> _mirrors;

    public BayernTileSource(BayernCatalog? catalog = null, IReadOnlyList<string>? mirrors = null)
    {
        _catalog = catalog ?? BayernCatalog.Instance;
        _mirrors = mirrors ?? BayernCatalog.RawMirrors;
    }

    /// <summary>Tile filename for a dataset, e.g. "729_5433.tif" (DGM1), "32672_5424.tif" (DOP20).</summary>
    public string FileNameFor(string datasetId, TileId tile)
    {
        var d = RequireRaw(datasetId);
        return $"{d.TilePrefix}{tile.Key}{d.TileSuffix}{d.Extension}";
    }

    public DownloadJob JobFor(string datasetId, TileId tile)
    {
        var d = RequireRaw(datasetId);
        var fileName = FileNameFor(datasetId, tile);
        var urls = _mirrors.Select(m => $"{m}/a/{d.UrlPath}/{fileName}").ToArray();
        return new DownloadJob(fileName, urls);
    }

    public IEnumerable<DownloadJob> JobsFor(string datasetId, BoundingBox area)
    {
        var d = RequireRaw(datasetId);
        return TileGrid.TilesFor(area, d.GridKm).Select(t => JobFor(datasetId, t));
    }

    public IEnumerable<DownloadJob> JobsFor(string datasetId, Polygon2D area)
    {
        var d = RequireRaw(datasetId);
        return TileGrid.TilesFor(area, d.GridKm).Select(t => JobFor(datasetId, t));
    }

    /// <summary>Rough download size in MB, from the catalog's per-tile averages.</summary>
    public double EstimateSizeMb(string datasetId, BoundingBox area)
    {
        var d = RequireRaw(datasetId);
        return (d.AverageTileMb ?? 0) * TileGrid.TilesFor(area, d.GridKm).Count();
    }

    private DatasetInfo RequireRaw(string datasetId)
    {
        var d = _catalog[datasetId];
        if (d.Kind != DatasetKind.Raw || d.UrlPath is null)
            throw new ArgumentException(
                $"Dataset '{datasetId}' is not a raw tile dataset — use {nameof(BayernWmsSource)} for WMS layers.",
                nameof(datasetId));
        return d;
    }
}
