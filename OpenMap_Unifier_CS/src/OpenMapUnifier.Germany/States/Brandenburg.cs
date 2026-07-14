using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Brandenburg (LGB) — plain HTTPS directory tree at data.geobasis-bb.de,
/// EPSG:25833, 1 km tiles named "&lt;prod&gt;_33&lt;EEE&gt;-&lt;NNNN&gt;.zip"
/// (zone glued to the easting, dash before the northing). Verified live.
/// </summary>
public sealed class Brandenburg : StateBase
{
    private const string Base = "https://data.geobasis-bb.de/geobasis/daten";

    public override string Code => "bb";
    public override string Name => "Brandenburg";
    public override int UtmZone => 33;
    public override string License => "DL-DE/BY-2.0";
    public override string Attribution => "© GeoBasis-DE/LGB, dl-de/by-2-0";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF in zip (NoData -9999)",
        ["dgm1_xyz"] = "Terrain 1 m, ASCII-XYZ in zip",
        ["bdom"] = "Image-based surface model, GeoTIFF in zip",
        ["dop20rgbi"] = "Orthophoto 20 cm RGBI, GeoTIFF in zip (~90 MB/tile)",
        ["lod2"] = "3D buildings CityGML in zip",
    };

    private static (string Path, string Prod, string Ext) Route(string datasetId) => datasetId.ToLowerInvariant() switch
    {
        "dgm1" => ("dgm/tif", "dgm", ".zip"),
        "dgm1_xyz" => ("dgm/xyz", "dgm", ".zip"),
        "bdom" => ("bdom/tif", "bdom", ".zip"),
        "dop20rgbi" => ("dop/rgbi_tif", "dop", ".zip"),
        "lod2" => ("3d_gebaeude/lod2_gml", "lod2", ".zip"),
        _ => throw new KeyNotFoundException($"Unknown Brandenburg dataset '{datasetId}'."),
    };

    internal static DownloadJob JobFor(string datasetId, TileId tile)
    {
        var (path, prod, ext) = Route(datasetId);
        var fileName = FormattableString.Invariant($"{prod}_33{tile.EastKm:D3}-{tile.NorthKm:D4}{ext}");
        return new DownloadJob(fileName, $"{Base}/{path}/{fileName}");
    }

    public override Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DownloadJob>>(
            TileGrid.TilesFor(area).Select(t => JobFor(datasetId, t)).ToList());

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "bdom");
        return new DelegateTileResolver(1,
            (tile, _) => Task.FromResult<DownloadJob?>(JobFor(datasetId, tile)),
            (path, _) => ZipHelpers.ReadGeoTiffEntry(path,
                n => n.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)));
    }
}
