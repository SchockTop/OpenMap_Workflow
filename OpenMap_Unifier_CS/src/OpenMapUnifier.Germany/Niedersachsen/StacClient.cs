using System.Globalization;
using System.Text.Json;
using OpenMapUnifier.Networking.Proxy;

namespace OpenMapUnifier.Germany.Niedersachsen;

public sealed record StacAsset(string Key, string Href, string? Type);

public sealed record StacItem(
    string Id,
    DateTimeOffset? Datetime,
    IReadOnlyList<StacAsset> Assets)
{
    public StacAsset? Asset(string key) =>
        Assets.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Minimal STAC API client for LGLN's OpenGeoData services
/// (dgm/dom/dop.stac.lgln.niedersachsen.de). Unlike Bayern's static
/// bayernwolke URLs, Niedersachsen tile downloads live on S3 buckets whose
/// paths embed the acquisition batch ("Los") and flight date — they cannot be
/// computed from the grid, only resolved by querying the STAC catalog.
/// </summary>
public sealed class StacClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public StacClient(HttpClient? httpClient = null, ProxyManager? proxy = null)
    {
        _ownsClient = httpClient is null;
        _http = httpClient ?? (proxy ?? new ProxyManager()).CreateClient(TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// All items of a collection intersecting a WGS84 bbox (lon/lat order, as
    /// STAC requires). Follows rel=next pagination links until exhausted.
    /// </summary>
    public async Task<IReadOnlyList<StacItem>> SearchAsync(string stacRoot, string collection,
        double minLon, double minLat, double maxLon, double maxLat,
        int pageSize = 200, CancellationToken ct = default)
    {
        var bbox = string.Create(CultureInfo.InvariantCulture,
            $"{minLon:F7},{minLat:F7},{maxLon:F7},{maxLat:F7}");
        var url = $"{stacRoot.TrimEnd('/')}/collections/{collection}/items?bbox={bbox}&limit={pageSize}";

        var items = new List<StacItem>();
        for (var page = 0; url is not null && page < 100; page++)
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            url = ParsePage(json, items);
        }
        return items;
    }

    /// <summary>Parse one FeatureCollection page; returns the rel=next href, if any.</summary>
    public static string? ParsePage(string json, List<StacItem> into)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("features", out var features))
        {
            foreach (var feature in features.EnumerateArray())
            {
                var id = feature.GetProperty("id").GetString() ?? "";
                DateTimeOffset? datetime = null;
                if (feature.TryGetProperty("properties", out var props) &&
                    props.TryGetProperty("datetime", out var dt) &&
                    dt.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(dt.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var parsed))
                    datetime = parsed;

                var assets = new List<StacAsset>();
                if (feature.TryGetProperty("assets", out var assetsElement))
                {
                    foreach (var asset in assetsElement.EnumerateObject())
                    {
                        var href = asset.Value.TryGetProperty("href", out var h) ? h.GetString() : null;
                        if (href is null) continue;
                        var type = asset.Value.TryGetProperty("type", out var t) ? t.GetString() : null;
                        assets.Add(new StacAsset(asset.Name, href, type));
                    }
                }
                into.Add(new StacItem(id, datetime, assets));
            }
        }

        if (root.TryGetProperty("links", out var links))
        {
            foreach (var link in links.EnumerateArray())
            {
                if (link.TryGetProperty("rel", out var rel) && rel.GetString() == "next" &&
                    link.TryGetProperty("href", out var href))
                    return href.GetString();
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
