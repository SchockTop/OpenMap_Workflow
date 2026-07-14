using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Core.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Berlin (gdi.berlin.de ATOM downloads) — EPSG:25833. DGM1/DOM1 come as
/// 2 km zips of ASCII-XYZ, DOP20 as per-2km-tile JPEG2000, LoD2 as 1 km
/// CityGML zips. License DL-DE Zero 2.0 (no attribution required). Verified live.
/// </summary>
public sealed class Berlin : StateBase
{
    private const string Base = "https://gdi.berlin.de/data";

    public override string Code => "be";
    public override string Name => "Berlin";
    public override int UtmZone => 33;
    public override string License => "DL-DE Zero 2.0";
    public override string Attribution => "Geoportal Berlin (dl-zero-de/2.0)";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, ASCII-XYZ in 2 km zips",
        ["dom1"] = "Surface model 1 m, zipped (2 km)",
        ["dop20rgbi"] = "Orthophoto 20 cm RGBI, JPEG2000 per 2 km tile (~80 MB)",
        ["lod2"] = "3D buildings CityGML, 1 km zips",
    };

    internal static DownloadJob JobFor(string datasetId, TileId tile) => datasetId.ToLowerInvariant() switch
    {
        "dgm1" => Job($"DGM1_{tile.EastKm}_{tile.NorthKm}.zip", $"{Base}/dgm1/atom"),
        "dom1" => Job($"DOM1_{tile.EastKm}_{tile.NorthKm}.zip", $"{Base}/dom/atom"),
        "dop20rgbi" => Job(
            FormattableString.Invariant($"dop20rgbi_33_{tile.EastKm}_{tile.NorthKm}_2_be_2026.jp2"),
            $"{Base}/oi_dop2026/atom"),
        "lod2" => Job($"LoD2_{tile.EastKm}_{tile.NorthKm}.zip", $"{Base}/a_lod2/atom"),
        _ => throw new KeyNotFoundException($"Unknown Berlin dataset '{datasetId}'."),
    };

    private static DownloadJob Job(string fileName, string dir) => new(fileName, $"{dir}/{fileName}");

    private static int GridFor(string datasetId) => datasetId.ToLowerInvariant() == "lod2" ? 1 : 2;

    public override Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DownloadJob>>(
            TileGrid.TilesFor(area, GridFor(datasetId)).Select(t => JobFor(datasetId, t)).ToList());

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        return new DelegateTileResolver(2,
            (tile, _) => Task.FromResult<DownloadJob?>(JobFor(datasetId, tile)),
            (path, _) => XyzGridReader.ReadZip(path));
    }
}
