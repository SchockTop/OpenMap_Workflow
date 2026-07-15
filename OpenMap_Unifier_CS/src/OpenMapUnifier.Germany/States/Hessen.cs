using System.Text.Json;
using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Hessen (gds.hessen.de download center) — EPSG:25832, license DL-DE Zero
/// 2.0. Packages are per municipality (no per-tile URLs and no spatial index),
/// resolved through the portal's JSON REST API; download URLs embed the
/// CURRENT date, so they must be minted fresh via the API. The municipality
/// zips hold standard 1 km tiles ("dgm1_32_&lt;E&gt;_&lt;N&gt;_1_he.tif") and
/// the server supports ranges, so tiles are range-extracted. First height
/// query in a new region can be slow: without a spatial index, archives'
/// central directories are scanned until the tile is found (then cached).
/// </summary>
public sealed class Hessen : StateBase
{
    private const string Api =
        "https://gds.hessen.de/INTERSHOP/rest/WFS/HLBG-Geodaten-Site/-/downloadcenter";

    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, IReadOnlyList<string>> _archives = new(StringComparer.OrdinalIgnoreCase);

    public override string Code => "he";
    public override string Name => "Hessen";
    public override int UtmZone => 32;
    public override string License => "DL-DE Zero 2.0";
    public override string Attribution => "Hessische Verwaltung für Bodenmanagement und Geoinformation (dl-zero-de/2.0)";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF tiles range-extracted from per-municipality zips",
        ["dom1"] = "Surface model 1 m, GeoTIFF tiles from per-municipality zips",
        ["dop20"] = "Orthophoto 20 cm RGB, JPEG+worldfile in per-municipality zips",
        ["lod2"] = "3D buildings CityGML, one GML per municipality",
    };

    private static string CategoryFor(string datasetId) => datasetId.ToLowerInvariant() switch
    {
        "dgm1" => "3D-Daten/Digitales Geländemodell (DGM1)",
        "dom1" => "3D-Daten/Digitales Oberflächenmodell (DOM1)",
        "dop20" => "Luftbildinformationen/Digitale Orthophotos DOP20",
        "lod2" => "3D-Daten/3D-Gebäudemodelle/3D-Gebäudemodelle LoD2",
        _ => throw new KeyNotFoundException($"Unknown Hessen dataset '{datasetId}'."),
    };

    /// <summary>Walk the download-center tree and collect every zip URL for a category.</summary>
    private async Task<IReadOnlyList<string>> ArchivesForAsync(HttpClient http, string datasetId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_archives.TryGetValue(datasetId, out var cached)) return cached;

            var urls = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(CategoryFor(datasetId));
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var path = queue.Dequeue();
                var json = await http.GetStringAsync(
                    $"{Api}?path={Uri.EscapeDataString(path)}", ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("navigation", out var navigation))
                {
                    foreach (var nav in navigation.EnumerateArray())
                    {
                        var name = nav.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (!string.IsNullOrEmpty(name))
                            queue.Enqueue($"{path}/{name}");
                    }
                }
                if (root.TryGetProperty("searchresult", out var sr) &&
                    sr.TryGetProperty("downloads", out var downloads))
                {
                    foreach (var dl in downloads.EnumerateArray())
                    {
                        var ext = dl.TryGetProperty("fileExtension", out var e) ? e.GetString() : null;
                        if (!string.Equals(ext, "zip", StringComparison.OrdinalIgnoreCase)) continue;
                        if (dl.TryGetProperty("downloadLink", out var link) &&
                            link.TryGetProperty("uri", out var uri) &&
                            uri.GetString() is { Length: > 0 } href)
                            urls.Add(href.StartsWith("http") ? href : "https://gds.hessen.de" + href);
                    }
                }
            }
            if (urls.Count == 0)
                throw new InvalidDataException($"Hessen download center returned no archives for {datasetId}.");
            return _archives[datasetId] = urls;
        }
        finally
        {
            _gate.Release();
        }
    }

    public override async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        // No spatial index exists — jobs are all municipality archives of the
        // dataset; pick the ones you need by name.
        var http = CreateHttp(null, 300);
        var archives = await ArchivesForAsync(http, datasetId, ct).ConfigureAwait(false);
        return archives
            .Select(u => new DownloadJob(Uri.UnescapeDataString(u.Split('/')[^1]).Replace(' ', '_'), u))
            .ToList();
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        var http = CreateHttp(proxy, 600);
        var fetcher = new RemoteArchiveFetcher(http,
            ct => ArchivesForAsync(http, datasetId, ct));

        var prod = datasetId.ToLowerInvariant();
        return new ArchiveTileResolver(1, fetcher,
            tile => name => name.Contains(FormattableString.Invariant(
                                $"{prod}_32_{tile.EastKm}_{tile.NorthKm}_1_he")) &&
                            name.EndsWith(".tif", StringComparison.OrdinalIgnoreCase),
            tile => FormattableString.Invariant($"{prod}_32_{tile.EastKm}_{tile.NorthKm}_1_he.tif"),
            (path, _) => GeoTiffReader.Read(path));
    }
}
