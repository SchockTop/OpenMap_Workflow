using System.Text.Json;
using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Grid;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Schleswig-Holstein (LVermGeo SH "dladownload" app) — EPSG:25832, true
/// per-1km-tile downloads. Tile filenames carry rolling per-tile years, so
/// each tile is resolved via the overview.php GeoJSON (bbox query) and then
/// fetched through massen.php with the product id and 10 km block. DGM1 is
/// ASCII-XYZ (pixel centers at x.50); DOP20/bDOM GeoTIFF; LoD2 CityGML 1.0.
/// CC BY 4.0, © GeoBasis-DE/LVermGeo SH. Verified live.
/// </summary>
public sealed class SchleswigHolstein : StateBase
{
    private const string App = "https://geodaten.schleswig-holstein.de/gaialight-sh/_apps/dladownload";

    public override string Code => "sh";
    public override string Name => "Schleswig-Holstein";
    public override int UtmZone => 32;
    public override string License => "CC BY 4.0";
    public override string Attribution => "© GeoBasis-DE/LVermGeo SH (CC BY 4.0)";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, ASCII-XYZ per 1 km tile (~28 MB)",
        ["dop20"] = "Orthophoto 20 cm RGBI, GeoTIFF per 1 km tile",
        ["lod2"] = "3D buildings CityGML 1.0 per 1 km tile",
    };

    private static (string Type, int ProdId, string Ext, bool Stack) Route(string datasetId) =>
        datasetId.ToLowerInvariant() switch
        {
            "dgm1" => ("dgm1", 2, ".xyz", false),
            "dop20" => ("dop20", 7, ".tif", true),
            "lod2" => ("lod2", 4, ".xml", false),
            _ => throw new KeyNotFoundException($"Unknown Schleswig-Holstein dataset '{datasetId}'."),
        };

    public override async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        using var http = CreateHttp(null, 120);
        return await QueryJobsAsync(http, datasetId, area, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<DownloadJob>> QueryJobsAsync(HttpClient http, string datasetId,
        BoundingBox area, CancellationToken ct)
    {
        var (type, prodId, ext, stack) = Route(datasetId);
        var url = FormattableString.Invariant(
            $"{App}/_ajax/overview.php?type[]={type}&crs=EPSG:25832&bbox={area.MinEasting:F0},{area.MinNorthing:F0},{area.MaxEasting:F0},{area.MaxNorthing:F0}");
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.TryGetProperty("result", out var r) ? r : doc.RootElement;
        if (!root.TryGetProperty("features", out var features))
            return Array.Empty<DownloadJob>();

        var jobs = new List<DownloadJob>();
        foreach (var f in features.EnumerateArray())
        {
            var props = f.GetProperty("properties");
            var name = Prop(props, "kachel_n") ?? Prop(props, "kaname");
            if (name is null) continue;
            var year = Prop(props, "jahr") ?? "";
            var block = Prop(props, "kach10km") ?? Prop(props, "filepath");
            if (block is null)
            {
                // Derive the 10 km block from the tile key in the name: 32<EEE>_<NNNN>.
                var key = NiTileKey(name);
                if (key is null) continue;
                block = FormattableString.Invariant(
                    $"32{key.Value.E / 10 * 10:D3}_{key.Value.N / 10 * 10:D4}");
            }

            var fileName = name + ext;
            var dl = $"{App}/massen.php?file={fileName}&id={prodId}&live={year}&km={block}";
            if (stack)
            {
                var key = NiTileKey(name);
                if (key is { } k)
                    dl += FormattableString.Invariant($"&stack=32{k.E:D3}_{k.N:D4}");
            }
            jobs.Add(new DownloadJob(fileName, dl));
        }
        return jobs
            .DistinctBy(j => j.FileName)
            .OrderBy(j => j.FileName, StringComparer.Ordinal)
            .ToList();

        static string? Prop(JsonElement props, string name) =>
            props.TryGetProperty(name, out var v)
                ? v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString()
                : null;
    }

    /// <summary>Extract (E,N) km from names like "dgm1_32_572_6020_1_sh_2023" or "LoD2_32_573_6020_1_SH".</summary>
    private static (int E, int N)? NiTileKey(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"_32_(\d{3})_(\d{4})_");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1");
        var http = CreateHttp(proxy, 300);
        return new DelegateTileResolver(1,
            async (tile, ct) =>
            {
                // Inset the tile bounds slightly so neighbors don't match too.
                var area = new BoundingBox(
                    tile.MinEasting + 10, tile.MinNorthing + 10,
                    tile.MaxEasting - 10, tile.MaxNorthing - 10);
                var jobs = await QueryJobsAsync(http, "dgm1", area, ct).ConfigureAwait(false);
                var marker = FormattableString.Invariant($"_32_{tile.EastKm:D3}_{tile.NorthKm:D4}_");
                return jobs.FirstOrDefault(j => j.FileName.Contains(marker));
            },
            (path, _) =>
            {
                using var stream = File.OpenRead(path);
                return XyzGridReader.Read(stream);
            });
    }
}
