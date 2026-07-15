using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Grid;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Baden-Württemberg (LGL, opengeodata.lgl-bw.de) — EPSG:25832, deterministic
/// static URLs. Delivery unit is a 2x2 km zip holding four 1 km tiles; the
/// 2 km cells are anchored at ODD easting km and EVEN northing km (verified
/// against the live grid). DGM1 is ASCII-XYZ, DOM1/DOP GeoTIFF, LoD2 CityGML.
/// DL-DE/BY-2.0.
/// </summary>
public sealed class BadenWuerttemberg : StateBase
{
    private const string Base = "https://opengeodata.lgl-bw.de/data";

    public override string Code => "bw";
    public override string Name => "Baden-Württemberg";
    public override int UtmZone => 32;
    public override string License => "DL-DE/BY-2.0";
    public override string Attribution => "Datenquelle: LGL, www.lgl-bw.de (dl-de/by-2-0)";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, ASCII-XYZ (four 1 km tiles per 2 km zip)",
        ["dom1"] = "Surface model 1 m, GeoTIFF (2 km zip)",
        ["dop20rgb"] = "Orthophoto 20 cm RGB, GeoTIFF (2 km zip, ~253 MB)",
        ["dop20rgbi"] = "Orthophoto 20 cm RGBI, GeoTIFF (2 km zip)",
        ["lod2"] = "3D buildings CityGML 1.0 (2 km zip)",
    };

    /// <summary>Snap a 1 km tile to its BW 2 km delivery cell (odd E, even N).</summary>
    internal static (int E, int N) CellFor(TileId tile)
    {
        var e = tile.EastKm % 2 != 0 ? tile.EastKm : tile.EastKm - 1;
        var n = tile.NorthKm % 2 == 0 ? tile.NorthKm : tile.NorthKm - 1;
        return (e, n);
    }

    private static (string Dir, string Prod, string Case) Route(string datasetId) => datasetId.ToLowerInvariant() switch
    {
        "dgm1" => ("dgm", "dgm1", "dgm1"),
        "dom1" => ("dom1", "dom1", "dom1"),
        "dop20rgb" => ("dop20", "dop20rgb", "dop20rgb"),
        "dop20rgbi" => ("dop20", "dop20rgbi", "dop20rgbi"),
        "lod2" => ("lod2", "LoD2", "LoD2"),
        _ => throw new KeyNotFoundException($"Unknown Baden-Württemberg dataset '{datasetId}'."),
    };

    internal static DownloadJob JobFor(string datasetId, TileId tile)
    {
        var (dir, prod, _) = Route(datasetId);
        var (e, n) = CellFor(tile);
        var fileName = FormattableString.Invariant($"{prod}_32_{e}_{n}_2_bw.zip");
        return new DownloadJob(fileName, $"{Base}/{dir}/{fileName}");
    }

    public override Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        // Enumerate 1 km tiles, snap each to its 2 km cell, dedupe.
        var jobs = TileGrid.TilesFor(area)
            .Select(t => JobFor(datasetId, t))
            .DistinctBy(j => j.FileName)
            .ToList();
        return Task.FromResult<IReadOnlyList<DownloadJob>>(jobs);
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        var isXyz = datasetId.Equals("dgm1", StringComparison.OrdinalIgnoreCase);
        return new DelegateTileResolver(1,
            (tile, _) => Task.FromResult<DownloadJob?>(JobFor(datasetId, tile)),
            (path, tile) =>
            {
                // The 2 km zip holds four 1 km tiles — pick the one requested.
                var marker = FormattableString.Invariant(
                    $"_32_{tile.EastKm}_{tile.NorthKm}_1_bw");
                return isXyz
                    ? XyzGridReader.ReadZip(path, entryFilter: n => n.Contains(marker))
                    : ZipHelpers.ReadGeoTiffEntry(path,
                        n => n.Contains(marker) && n.EndsWith(".tif", StringComparison.OrdinalIgnoreCase));
            });
    }
}
