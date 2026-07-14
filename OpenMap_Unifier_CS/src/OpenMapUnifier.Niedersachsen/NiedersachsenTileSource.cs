using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Geometry;
using OpenMapUnifier.Core.Grid;

namespace OpenMapUnifier.Niedersachsen;

/// <summary>
/// Turns an area of interest into download jobs for an LGLN dataset by
/// querying the STAC API. Where a tile has been flown multiple times (common
/// for DOP), only the NEWEST item per grid cell is returned.
/// </summary>
public sealed class NiedersachsenTileSource : IDisposable
{
    private readonly StacClient _stac;
    private readonly ICoordinateTransform _transform;
    private readonly bool _ownsClient;

    public NiedersachsenTileSource(StacClient? stacClient = null, ICoordinateTransform? transform = null)
    {
        _ownsClient = stacClient is null;
        _stac = stacClient ?? new StacClient();
        _transform = transform ?? Etrs89UtmTransform.Zone32;
    }

    public async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        var dataset = NiedersachsenCatalog.Get(datasetId);
        var items = await SearchItemsAsync(dataset, area, ct).ConfigureAwait(false);
        var wanted = TileGrid.TilesFor(area, dataset.GridKm)
            .Select(t => (t.EastKm, t.NorthKm))
            .ToHashSet();
        return ToJobs(dataset, items, key => wanted.Contains(key));
    }

    public async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, Polygon2D area,
        CancellationToken ct = default)
    {
        var dataset = NiedersachsenCatalog.Get(datasetId);
        var items = await SearchItemsAsync(dataset, area.Bounds, ct).ConfigureAwait(false);
        var wanted = TileGrid.TilesFor(area, dataset.GridKm)
            .Select(t => (t.EastKm, t.NorthKm))
            .ToHashSet();
        return ToJobs(dataset, items, key => wanted.Contains(key));
    }

    /// <summary>The newest STAC item covering a single grid tile, or null.</summary>
    public async Task<StacItem?> ItemForTileAsync(string datasetId, TileId tile, CancellationToken ct = default)
    {
        var dataset = NiedersachsenCatalog.Get(datasetId);
        var items = await SearchItemsAsync(dataset, tile.Bounds, ct).ConfigureAwait(false);
        return items
            .Where(i => NiedersachsenCatalog.TileKeyFromItemId(i.Id) == (tile.EastKm, tile.NorthKm))
            .MaxBy(i => i.Datetime ?? DateTimeOffset.MinValue);
    }

    public static DownloadJob? JobFromItem(NiDataset dataset, StacItem item)
    {
        var asset = item.Asset(dataset.AssetKey);
        if (asset is null) return null;
        var fileName = asset.Href.Split('/')[^1];
        return new DownloadJob(fileName, asset.Href);
    }

    private async Task<IReadOnlyList<StacItem>> SearchItemsAsync(NiDataset dataset, BoundingBox area,
        CancellationToken ct)
    {
        // STAC wants a WGS84 bbox. UTM->geographic isn't axis-aligned, so take
        // the envelope of all four projected corners (plus a hair of margin so
        // border tiles aren't lost to rounding).
        var corners = new[]
        {
            _transform.ToGeo(new UtmPoint(area.MinEasting, area.MinNorthing)),
            _transform.ToGeo(new UtmPoint(area.MaxEasting, area.MinNorthing)),
            _transform.ToGeo(new UtmPoint(area.MinEasting, area.MaxNorthing)),
            _transform.ToGeo(new UtmPoint(area.MaxEasting, area.MaxNorthing)),
        };
        const double margin = 1e-5;
        var minLon = corners.Min(c => c.Longitude) - margin;
        var maxLon = corners.Max(c => c.Longitude) + margin;
        var minLat = corners.Min(c => c.Latitude) - margin;
        var maxLat = corners.Max(c => c.Latitude) + margin;

        return await _stac.SearchAsync(dataset.StacRoot, dataset.Collection,
            minLon, minLat, maxLon, maxLat, ct: ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<DownloadJob> ToJobs(NiDataset dataset, IReadOnlyList<StacItem> items,
        Func<(int, int), bool> wanted)
    {
        return items
            .Select(item => (Item: item, Key: NiedersachsenCatalog.TileKeyFromItemId(item.Id)))
            .Where(x => x.Key is { } key && wanted(key))
            .GroupBy(x => x.Key!.Value)
            .Select(g => g.MaxBy(x => x.Item.Datetime ?? DateTimeOffset.MinValue).Item)
            .Select(item => JobFromItem(dataset, item))
            .Where(job => job is not null)
            .Select(job => job!)
            .OrderBy(job => job.FileName, StringComparer.Ordinal)
            .ToList();
    }

    public void Dispose()
    {
        if (_ownsClient) _stac.Dispose();
    }
}
