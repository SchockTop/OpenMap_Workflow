using System.Text.Json;
using System.Text.RegularExpressions;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Sachsen-Anhalt (LVermGeo LSA) — EPSG:25832, 2 km tiles, two-step download:
/// numeric tile ids live in a GeoJSON grid embedded in each product page, and
/// a "prepare" call mints a temporary zip URL. Ids are scraped once per
/// product and cached. DL-DE/BY-2.0, © LVermGeo LSA. Verified live.
/// </summary>
public sealed class SachsenAnhalt : StateBase
{
    private const string Base = "https://www.lvermgeo.sachsen-anhalt.de";

    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, Dictionary<string, long>> _tileIds = new(StringComparer.OrdinalIgnoreCase);

    public override string Code => "st";
    public override string Name => "Sachsen-Anhalt";
    public override int UtmZone => 32;
    public override string License => "DL-DE/BY-2.0";
    public override string Attribution => "© GeoBasis-DE / LVermGeo LSA, dl-de/by-2-0";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF in zip (2 km tiles, 2000x2000 px, NoData -9999)",
        ["dom1"] = "Surface model 1 m, GeoTIFF in zip (2 km tiles)",
        ["dop20rgbi"] = "Orthophoto 20 cm RGBI, GeoTIFF in zip (2 km tiles; prepare can take minutes)",
        ["lod2"] = "3D buildings CityGML in zip (2 km tiles)",
    };

    private static (string Page, string Mod, string Prod) Route(string datasetId) => datasetId.ToLowerInvariant() switch
    {
        "dgm1" => ("/de/gdp-dgm1.html", "2,2913,501", "dgm1"),
        "dom1" => ("/de/gdp-dom1.html", "3,2926,501", "dom1"),
        "dop20rgbi" => ("/de/gdp-dop20-auswahl.html", "4,1962,501", "dop20rgbi"),
        "lod2" => ("/de/gdp-download-lod2.html", "4,1965,501", "lod2"),
        _ => throw new KeyNotFoundException($"Unknown Sachsen-Anhalt dataset '{datasetId}'."),
    };

    /// <summary>Tile label as used in the portal's grid: "32{EEE}{NNNN}".</summary>
    private static string LabelFor(TileId tile) =>
        FormattableString.Invariant($"32{tile.EastKm:D3}{tile.NorthKm:D4}");

    private async Task<Dictionary<string, long>> TileIdsAsync(HttpClient http, string datasetId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tileIds.TryGetValue(datasetId, out var cached)) return cached;

            var (page, _, _) = Route(datasetId);
            var html = await http.GetStringAsync(Base + page, ct).ConfigureAwait(false);

            // The tile grid is inlined as a GeoJSON string argument to
            // MapDownloadSelector — single-quoted, and JSON itself contains no
            // single quotes. Whitespace inside the JSON varies, so locate the
            // FeatureCollection marker and backtrack to its opening brace.
            var marker = html.IndexOf("\"FeatureCollection\"", StringComparison.Ordinal);
            if (marker < 0)
                throw new InvalidDataException($"No tile grid found on {page} — page layout changed?");
            var start = html.LastIndexOf('{', marker);
            var end = html.IndexOf('\'', marker);
            var geojson = end > start ? html[start..end] : html[start..];

            var map = new Dictionary<string, long>();
            using var doc = JsonDocument.Parse(geojson);
            foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                var props = f.GetProperty("properties");
                var label = props.TryGetProperty("label", out var l)
                    ? l.ValueKind == JsonValueKind.Number ? l.GetRawText() : l.GetString()
                    : null;
                if (label is null || !props.TryGetProperty("id", out var idProp)) continue;
                map[label] = idProp.ValueKind == JsonValueKind.Number
                    ? idProp.GetInt64()
                    : long.Parse(idProp.GetString() ?? "0");
            }
            if (map.Count == 0)
                throw new InvalidDataException($"Tile grid on {page} yielded no ids.");
            return _tileIds[datasetId] = map;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DownloadJob?> ResolveJobAsync(HttpClient http, string datasetId, TileId tile,
        CancellationToken ct)
    {
        var ids = await TileIdsAsync(http, datasetId, ct).ConfigureAwait(false);
        if (!ids.TryGetValue(LabelFor(tile), out var id))
            return null;

        var (_, mod, prod) = Route(datasetId);
        var prepareUrl = $"{Base}/de/mod/{mod}/ajax/1/prepare/?items={id}&format=zip";
        var response = await http.GetStringAsync(prepareUrl, ct).ConfigureAwait(false);
        var m = Regex.Match(response, @"https?://\S+?\.zip");
        if (!m.Success)
            throw new InvalidDataException(
                $"Sachsen-Anhalt prepare call returned no download URL (tile {LabelFor(tile)}).");

        var fileName = FormattableString.Invariant(
            $"{prod}_32_{tile.EastKm}_{tile.NorthKm}_2_st.zip");
        return new DownloadJob(fileName, m.Value);
    }

    public override async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        var http = CreateHttp(null, 600);
        var jobs = new List<DownloadJob>();
        foreach (var tile in TileGrid.TilesFor(area, 2))
        {
            if (await ResolveJobAsync(http, datasetId, tile, ct).ConfigureAwait(false) is { } job)
                jobs.Add(job);
        }
        return jobs;
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        var http = CreateHttp(proxy, 600);
        return new DelegateTileResolver(2,
            (tile, ct) => ResolveJobAsync(http, datasetId, tile, ct),
            (path, _) => ZipHelpers.ReadGeoTiffEntry(path,
                n => n.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)));
    }
}
