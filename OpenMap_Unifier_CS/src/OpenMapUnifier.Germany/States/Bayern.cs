using OpenMapUnifier.Germany.Catalog;
using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Germany.Bayern;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Bayern (LDBV) as an <see cref="IGermanState"/> — thin adapter over the
/// Bayern-specific machinery in <see cref="OpenMapUnifier.Germany.Bayern"/>
/// (static bayernwolke tile URLs, WMS renders, metalink parsing), which
/// remains directly usable for Bayern-only tooling.
/// </summary>
public sealed class Bayern : StateBase
{
    private readonly BayernTileSource _rawSource = new();

    public override string Code => "by";
    public override string Name => "Bayern";
    public override int UtmZone => 32;
    public override string License => "CC BY 4.0";
    public override string Attribution => BayernCatalog.Attribution;

    public override IReadOnlyDictionary<string, string> Datasets { get; } =
        BayernCatalog.Instance.Datasets.Values.ToDictionary(d => d.Id, d => d.Label);

    public override Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        var info = BayernCatalog.Instance[datasetId];
        IReadOnlyList<DownloadJob> jobs = (info.Kind == DatasetKind.Wms
            ? new BayernWmsSource().JobsFor(datasetId, area)
            : _rawSource.JobsFor(datasetId, area)).ToList();
        return Task.FromResult(jobs);
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dgm5", "dom20");
        return datasetId.ToLowerInvariant() switch
        {
            "dgm5" => new BayernElevation.Dgm5XyzResolver(_rawSource),
            var id => new BayernElevation.GeoTiffTileResolver(id, _rawSource),
        };
    }
}
