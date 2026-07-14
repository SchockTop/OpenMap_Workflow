using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Core.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Saarland (LVGL) — EPSG:25832. Open data lives on a public Nextcloud share
/// as per-Landkreis zips (0.3-37 GB); the WebDAV endpoint supports HTTP
/// ranges, so 1 km tiles are extracted remotely. The share token below is the
/// one published in the official INSPIRE ATOM feed (July 2026) — if LVGL
/// rotates it, re-resolve it from the DGM1 dataset feed. DL-DE/BY-2.0.
/// </summary>
public sealed class Saarland : StateBase
{
    private const string ShareBase =
        "https://www.shop.lvgl.saarland.de/cloud/public.php/dav/files/NK8ndP55qAqGEZD";

    // Raster products use these Landkreis codes (TrueDOP uses SBR/NKN instead
    // of SB/NK). Saarbrücken first — most queries land there.
    private static readonly string[] RasterKreise = { "SB", "MZG", "NK", "SLS", "SPK", "WND" };

    public override string Code => "sl";
    public override string Name => "Saarland";
    public override int UtmZone => 32;
    public override string License => "DL-DE/BY-2.0";
    public override string Attribution => "© GeoBasis DE/LVGL-SL, dl-de/by-2-0";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF tiles range-extracted from per-Landkreis zips",
        ["dom1"] = "Surface model 1 m, GeoTIFF tiles from per-Landkreis zips",
        ["dop20rgbi"] = "TrueDOP 20 cm RGBI — per-Landkreis zips (up to 37 GB!)",
        ["lod2"] = "3D buildings CityGML — per-Landkreis zips",
    };

    private static IReadOnlyList<string> ArchivesFor(string datasetId) => datasetId.ToLowerInvariant() switch
    {
        "dgm1" => RasterKreise.Select(lk =>
            $"{ShareBase}/OD_DGM1_2025_tif_LK/DGM1_tif_{lk}_EPSG-25832_Entstehung-2025.zip").ToArray(),
        "dom1" => RasterKreise.Select(lk =>
            $"{ShareBase}/OD_DOM1_2025_tif_LK/DOM1_tif_{lk}_EPSG-25832_Entstehung-2025.zip").ToArray(),
        "dop20rgbi" => new[] { "SBR", "MZG", "NKN", "SLS", "SPK", "WND" }.Select(lk =>
            $"{ShareBase}/OD_TrueDOP20_2025_tif_LK/tDOP-{lk}_EPSG-25832_Entstehung-2025.zip").ToArray(),
        "lod2" => RasterKreise.Select(lk =>
            $"{ShareBase}/OD_Geb%c3%a4udemodelle_LoD2_gml_LK/{lk}_LOD2BWK_gml.zip").ToArray(),
        _ => throw new KeyNotFoundException($"Unknown Saarland dataset '{datasetId}'."),
    };

    public override Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DownloadJob>>(
            ArchivesFor(datasetId).Select(u => new DownloadJob(Uri.UnescapeDataString(u.Split('/')[^1]), u)).ToList());

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        var http = CreateHttp(proxy, 600);
        var archives = ArchivesFor(datasetId);
        var fetcher = new RemoteArchiveFetcher(http,
            _ => Task.FromResult(archives));

        var prod = datasetId.ToLowerInvariant();
        return new ArchiveTileResolver(1, fetcher,
            tile => name => name.Contains(FormattableString.Invariant(
                                $"{prod}_32_{tile.EastKm}_{tile.NorthKm}_1_SL")) &&
                            name.EndsWith(".tif", StringComparison.OrdinalIgnoreCase),
            tile => FormattableString.Invariant($"{prod}_32_{tile.EastKm}_{tile.NorthKm}_1_SL.tif"),
            (path, _) => GeoTiffReader.Read(path));
    }
}
