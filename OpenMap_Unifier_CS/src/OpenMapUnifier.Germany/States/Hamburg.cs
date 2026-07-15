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
/// Hamburg (LGV Transparenzportal) — EPSG:25832. No per-tile endpoint: whole-
/// city zips on daten-hamburg.de whose URLs rotate per release and are
/// resolved through the CKAN API. The zips hold 1 km tiles and the server
/// supports HTTP ranges, so single tiles are range-extracted. DGM1 NoData is
/// float32 -MAX (-3.4e38), read from the tile's GDAL tag. DL-DE/BY-2.0.
/// </summary>
public sealed class Hamburg : StateBase
{
    private const string CkanSearch =
        "https://suche.transparenz.hamburg.de/api/3/action/package_search";

    // Fallbacks verified live (July 2026) in case CKAN is unreachable.
    private const string Dgm1Fallback =
        "https://daten-hamburg.de/geographie_geologie_geobasisdaten/Digitales_Hoehenmodell/DGM1/dgm1_hh_2022-04-30.zip";
    private const string Dom1Fallback =
        "https://daten-hamburg.de/opendata/Digitales_Hoehenmodell_bDOM/dom1_hh_2022-11-21.zip";

    public override string Code => "hh";
    public override string Name => "Hamburg";
    public override int UtmZone => 32;
    public override string License => "DL-DE/BY-2.0";
    public override string Attribution => "Freie und Hansestadt Hamburg, LGV (dl-de/by-2-0)";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF tiles range-extracted from the ~1.4 GB city zip",
        ["dom1"] = "Surface model (bDOM) 1 m, GeoTIFF tiles from the city zip",
        ["lod2"] = "3D buildings CityGML — whole-city zip (~660 MB), CKAN-resolved",
    };

    private async Task<IReadOnlyList<string>> ResolveArchivesAsync(HttpClient http, string title,
        string urlMustContain, string fallback, CancellationToken ct)
    {
        try
        {
            var url = $"{CkanSearch}?q=title:\"{Uri.EscapeDataString(title)}\"&sort=metadata_modified+desc&rows=10";
            var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var urls = new List<string>();
            foreach (var package in doc.RootElement.GetProperty("result").GetProperty("results").EnumerateArray())
            {
                if (!package.TryGetProperty("resources", out var resources)) continue;
                foreach (var resource in resources.EnumerateArray())
                {
                    var href = resource.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (href is not null &&
                        href.Contains("daten-hamburg.de") &&
                        href.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        href.Contains(urlMustContain, StringComparison.OrdinalIgnoreCase))
                        urls.Add(href);
                }
            }
            if (urls.Count > 0)
                return urls.Distinct().ToList();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // CKAN flaky — fall through to the pinned release.
        }
        return new[] { fallback };
    }

    public override async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        // Delivery unit is the whole city — jobs are the archives themselves.
        using var http = CreateHttp(null, 120);
        var archives = datasetId.ToLowerInvariant() switch
        {
            "dgm1" => await ResolveArchivesAsync(http, "Digitales Höhenmodell Hamburg DGM 1", "dgm1", Dgm1Fallback, ct).ConfigureAwait(false),
            "dom1" => await ResolveArchivesAsync(http, "Digitales Höhenmodell Hamburg - bDOM", "dom1", Dom1Fallback, ct).ConfigureAwait(false),
            "lod2" => await ResolveArchivesAsync(http, "3D-Gebäudemodell LoD2-DE Hamburg", "LoD2", Dgm1Fallback, ct).ConfigureAwait(false),
            _ => throw new KeyNotFoundException($"Unknown Hamburg dataset '{datasetId}'."),
        };
        return archives.Select(u => new DownloadJob(u.Split('/')[^1], u)).ToList();
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        var isDgm = datasetId.Equals("dgm1", StringComparison.OrdinalIgnoreCase);
        var http = CreateHttp(proxy, 600);
        var fetcher = new RemoteArchiveFetcher(http, async ct =>
        {
            var urls = isDgm
                ? await ResolveArchivesAsync(http, "Digitales Höhenmodell Hamburg DGM 1", "dgm1", Dgm1Fallback, ct).ConfigureAwait(false)
                : await ResolveArchivesAsync(http, "Digitales Höhenmodell Hamburg - bDOM", "dom1", Dom1Fallback, ct).ConfigureAwait(false);
            // Skip legacy 2x2 km XYZ releases; the GeoTIFF archives carry "hh_20".
            return urls.Where(u => !u.Contains("2x2", StringComparison.OrdinalIgnoreCase)).ToList();
        });

        var prod = isDgm ? "dgm1" : "dom1";
        return new ArchiveTileResolver(1, fetcher,
            tile => name => name.Contains(FormattableString.Invariant(
                                $"{prod}_32_{tile.EastKm}_{tile.NorthKm}_1_hh")) &&
                            name.EndsWith(".tif", StringComparison.OrdinalIgnoreCase),
            tile => FormattableString.Invariant($"{prod}_32_{tile.EastKm}_{tile.NorthKm}_1_hh.tif"),
            (path, _) => GeoTiffReader.Read(path));
    }
}
