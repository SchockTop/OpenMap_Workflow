using System.Text.Json;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Thüringen (geoportal-th.de) — EPSG:25832. DGM1/DOM1 as static 1 km zips
/// (TIFF + XYZ + meta inside) with epoch folders; the older-epoch URL rides
/// along as a "mirror" so tiles not yet reflown since 2020 still resolve.
/// DOP20 has no static URLs (GeoJSON index + download CGI). LoD2 on the even
/// 2 km grid. DL-DE/BY-2.0, © GDI-Th. Verified live.
/// </summary>
public sealed class Thueringen : StateBase
{
    private const string Base = "https://geoportal.geoportal-th.de";
    private const string DlApp = Base + "/gaialight-th/_apps/dladownload";

    public override string Code => "th";
    public override string Name => "Thüringen";
    public override int UtmZone => 32;
    public override string License => "DL-DE/BY-2.0";
    public override string Attribution => "© GDI-Th, dl-de/by-2-0";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, zip of GeoTIFF+XYZ (NoData -9999)",
        ["dom1"] = "Surface model 1 m, zip of GeoTIFF+XYZ",
        ["dop20"] = "Orthophoto 20 cm RGBI, zipped GeoTIFF (resolved via tile index)",
        ["lod2"] = "3D buildings CityGML in zip (2 km tiles)",
    };

    internal static DownloadJob JobFor(string datasetId, TileId tile) => datasetId.ToLowerInvariant() switch
    {
        // Current epoch first; the pre-2020 epoch (no zone token in the name)
        // acts as the fallback mirror for tiles not yet reflown.
        "dgm1" => new DownloadJob(
            FormattableString.Invariant($"dgm1_32_{tile.EastKm}_{tile.NorthKm}_1_th_2020-2025.zip"),
            new[]
            {
                FormattableString.Invariant(
                    $"{Base}/hoehendaten/DGM/dgm_2020-2025/dgm1_32_{tile.EastKm}_{tile.NorthKm}_1_th_2020-2025.zip"),
                FormattableString.Invariant(
                    $"{Base}/hoehendaten/DGM/dgm_2014-2019/dgm1_{tile.EastKm}_{tile.NorthKm}_1_th_2014-2019.zip"),
            }),
        "dom1" => new DownloadJob(
            FormattableString.Invariant($"dom1_32_{tile.EastKm}_{tile.NorthKm}_1_th_2020-2025.zip"),
            new[]
            {
                FormattableString.Invariant(
                    $"{Base}/hoehendaten/DOM/dom_2020-2025/dom1_32_{tile.EastKm}_{tile.NorthKm}_1_th_2020-2025.zip"),
                FormattableString.Invariant(
                    $"{Base}/hoehendaten/DOM/dom_2014-2019/dom1_{tile.EastKm}_{tile.NorthKm}_1_th_2014-2019.zip"),
            }),
        "lod2" => new DownloadJob(
            FormattableString.Invariant($"LoD2_32_{tile.EastKm}_{tile.NorthKm}_2_TH.zip"),
            FormattableString.Invariant(
                $"{Base}/3dgebaeude/LoD2/LoD2_32_{tile.EastKm}_{tile.NorthKm}_2_TH.zip")),
        _ => throw new KeyNotFoundException($"Unknown Thüringen dataset '{datasetId}' (dop20 needs the tile index)."),
    };

    public override async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        if (datasetId.Equals("dop20", StringComparison.OrdinalIgnoreCase))
            return await DopJobsAsync(area, ct).ConfigureAwait(false);
        var grid = datasetId.Equals("lod2", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        return TileGrid.TilesFor(area, grid).Select(t => JobFor(datasetId, t)).ToList();
    }

    /// <summary>DOP20 via the dladownload GeoJSON index (newest epoch per tile).</summary>
    private async Task<IReadOnlyList<DownloadJob>> DopJobsAsync(BoundingBox area, CancellationToken ct)
    {
        using var http = CreateHttp(null, 120);
        var url = FormattableString.Invariant(
            $"{DlApp}/_ajax/overview.php?type=op&crs=EPSG:25832&bbox={area.MinEasting:F0},{area.MinNorthing:F0},{area.MaxEasting:F0},{area.MaxNorthing:F0}");
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.TryGetProperty("result", out var r) ? r : doc.RootElement;
        if (!root.TryGetProperty("features", out var features))
            return Array.Empty<DownloadJob>();

        var newest = new Dictionary<string, (string Datum, long Gid)>();
        foreach (var f in features.EnumerateArray())
        {
            var props = f.GetProperty("properties");
            var title = props.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var datum = props.TryGetProperty("datum", out var d) ? d.GetString() ?? "" : "";
            var gid = props.TryGetProperty("gid", out var g) ? g.GetInt64() : 0;
            if (gid == 0 || title.Length == 0) continue;
            if (!newest.TryGetValue(title, out var existing) ||
                string.CompareOrdinal(datum, existing.Datum) > 0)
                newest[title] = (datum, gid);
        }
        return newest
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new DownloadJob(
                $"dop20_th_{kv.Key.Replace(' ', '_')}.zip",
                $"{DlApp}/download.php?type=op&id={kv.Value.Gid}"))
            .ToList();
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        return new DelegateTileResolver(1,
            (tile, _) => Task.FromResult<DownloadJob?>(JobFor(datasetId, tile)),
            (path, _) => ZipHelpers.ReadGeoTiffEntry(path,
                n => n.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)));
    }
}
