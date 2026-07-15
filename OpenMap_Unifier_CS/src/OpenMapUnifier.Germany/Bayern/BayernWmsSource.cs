using System.Globalization;
using OpenMapUnifier.Germany.Catalog;
using OpenMapUnifier.Networking;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Geometry;
using OpenMapUnifier.Grid;

namespace OpenMapUnifier.Germany.Bayern;

/// <summary>
/// WMS GetMap job builder for Bayern's rendered layers (hillshade relief,
/// CIR infrared, DOP previews) — the counterpart of generate_wms_tiles().
/// One request per 1 km grid cell, WMS 1.1.1 with srs=EPSG:25832, which is
/// the combination verified against Bavaria's services.
/// </summary>
public sealed class BayernWmsSource
{
    private readonly BayernCatalog _catalog;

    /// <summary>2000 px/km by default; 5906 px/km (300 DPI) in high-res mode.</summary>
    public bool HighRes { get; init; }

    public BayernWmsSource(BayernCatalog? catalog = null)
    {
        _catalog = catalog ?? BayernCatalog.Instance;
    }

    public string UrlFor(string datasetId, TileId tile)
    {
        var d = RequireWms(datasetId);
        var (size, dpiParams) = HighRes
            ? (5906, "&DPI=300&MAP_RESOLUTION=300&FORMAT_OPTIONS=dpi:300")
            : (2000, "");
        var b = tile.Bounds;
        return string.Create(CultureInfo.InvariantCulture,
            $"{d.WmsBaseUrl}?service=wms&version=1.1.1&request=GetMap" +
            $"&format={d.WmsMime}&transparent=true&layers={d.WmsLayer}" +
            $"&srs=EPSG:25832&STYLES=&WIDTH={size}&HEIGHT={size}{dpiParams}" +
            $"&BBOX={(long)b.MinEasting},{(long)b.MinNorthing},{(long)b.MaxEasting},{(long)b.MaxNorthing}");
    }

    public DownloadJob JobFor(string datasetId, TileId tile)
    {
        var d = RequireWms(datasetId);
        var ext = d.Extension.TrimStart('.');
        var fileName = FormattableString.Invariant(
            $"{d.WmsLayer}_{(long)tile.MinEasting}_{(long)tile.MinNorthing}.{ext}");
        return new DownloadJob(fileName, UrlFor(datasetId, tile));
    }

    public IEnumerable<DownloadJob> JobsFor(string datasetId, BoundingBox area) =>
        TileGrid.TilesFor(area).Select(t => JobFor(datasetId, t));

    public IEnumerable<DownloadJob> JobsFor(string datasetId, Polygon2D area) =>
        TileGrid.TilesFor(area).Select(t => JobFor(datasetId, t));

    private DatasetInfo RequireWms(string datasetId)
    {
        var d = _catalog[datasetId];
        if (d.Kind != DatasetKind.Wms || d.WmsBaseUrl is null || d.WmsLayer is null)
            throw new ArgumentException(
                $"Dataset '{datasetId}' is not a WMS dataset — use {nameof(BayernTileSource)} for raw tiles.",
                nameof(datasetId));
        return d;
    }
}
