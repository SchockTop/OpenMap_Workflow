using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Germany.Common;
using OpenMapUnifier.Germany.Niedersachsen;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Niedersachsen (LGLN OpenGeoData) as an <see cref="IGermanState"/> — thin
/// adapter over the STAC machinery in
/// <see cref="OpenMapUnifier.Germany.Niedersachsen"/>. Tile URLs live on S3
/// with acquisition batch + flight date in the path, so every lookup goes
/// through LGLN's STAC APIs (newest epoch per tile wins).
/// </summary>
public sealed class NiedersachsenState : StateBase
{
    public override string Code => "ni";
    public override string Name => "Niedersachsen";
    public override int UtmZone => 32;
    public override string License => "CC BY 4.0";
    public override string Attribution => NiedersachsenCatalog.Attribution;

    public override IReadOnlyDictionary<string, string> Datasets { get; } =
        NiedersachsenCatalog.Datasets.Values.ToDictionary(d => d.Id, d => d.Label);

    public override async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        using var source = new NiedersachsenTileSource();
        return await source.JobsForAsync(datasetId, area, ct).ConfigureAwait(false);
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        return new NiedersachsenElevation.StacTileResolver(datasetId, proxy);
    }
}
