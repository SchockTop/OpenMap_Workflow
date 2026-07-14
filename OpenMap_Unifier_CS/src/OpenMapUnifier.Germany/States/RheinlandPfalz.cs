using System.Text.RegularExpressions;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Core.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Rheinland-Pfalz (geobasis-rlp.de) — EPSG:25832, flat per-product HTTPS
/// directories. Raster filenames embed a per-tile flight year, resolved via
/// the product's atomfeed-links.xml (one link per tile; fetched once). DGM1 is
/// 1 km GeoTIFF (NoData -9999) exactly like Bayern; DOP20 is 2 km JPEG2000;
/// LoD2 is deterministic on the even 2 km grid. DL-DE/BY-2.0. Verified live.
/// </summary>
public sealed class RheinlandPfalz : StateBase
{
    private const string Base = "https://geobasis-rlp.de/data";

    private readonly Dictionary<string, UrlIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public override string Code => "rp";
    public override string Name => "Rheinland-Pfalz";
    public override int UtmZone => 32;
    public override string License => "DL-DE/BY-2.0";
    public override string Attribution => "©GeoBasis-DE / LVermGeoRP, dl-de/by-2-0";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF LZW (NoData -9999, index-resolved year)",
        ["dom1"] = "Surface model 1 m, GeoTIFF (index-resolved year)",
        ["dop20rgb"] = "Orthophoto 20 cm RGB, JPEG2000 (2 km tiles, ~131 MB)",
        ["lod2"] = "3D buildings CityGML 1.0, plain .gml (2 km tiles, deterministic)",
    };

    private UrlIndex IndexFor(string datasetId, ProxyManager? proxy)
    {
        lock (_lock)
        {
            if (_indexes.TryGetValue(datasetId, out var cached)) return cached;
            var (product, fmt, ext, gridToken) = datasetId.ToLowerInvariant() switch
            {
                "dgm1" => ("dgm1", "tif", "tif", "1"),
                "dom1" => ("dom1", "tif", "tif", "1"),
                "dop20rgb" => ("dop20rgb", "jp2", "jp2", "2"),
                _ => throw new KeyNotFoundException($"Unknown RLP indexed dataset '{datasetId}'."),
            };
            var indexUrl = $"{Base}/{product}/current/{fmt}/atomfeed-links/atomfeed-links.xml";
            var pattern = new Regex(
                $@"href=""(?<url>[^""]*{product}_32_(?<e>\d{{3}})_(?<n>\d{{4}})_{gridToken}_rp_\d{{4}}\.{ext})""",
                RegexOptions.Compiled);
            var index = new UrlIndex(CreateHttp(proxy), indexUrl, pattern, m => m.Groups["url"].Value);
            _indexes[datasetId] = index;
            return index;
        }
    }

    internal static DownloadJob Lod2JobFor(TileId tile)
    {
        var fileName = FormattableString.Invariant($"LoD2_32_{tile.EastKm}_{tile.NorthKm}_2_RP.gml");
        return new DownloadJob(fileName, $"{Base}/geb3dlo/current/gml/{fileName}");
    }

    public override async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        if (datasetId.Equals("lod2", StringComparison.OrdinalIgnoreCase))
            return TileGrid.TilesFor(area, 2).Select(Lod2JobFor).ToList();

        var grid = datasetId.StartsWith("dop", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        var index = IndexFor(datasetId, null);
        var jobs = new List<DownloadJob>();
        foreach (var tile in TileGrid.TilesFor(area, grid))
        {
            if (await index.UrlForAsync(tile.EastKm, tile.NorthKm, ct).ConfigureAwait(false) is { } url)
                jobs.Add(new DownloadJob(url.Split('/')[^1], url));
        }
        return jobs;
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        var index = IndexFor(datasetId, proxy);
        return new DelegateTileResolver(1,
            async (tile, ct) =>
                await index.UrlForAsync(tile.EastKm, tile.NorthKm, ct).ConfigureAwait(false) is { } url
                    ? new DownloadJob(url.Split('/')[^1], url)
                    : null,
            (path, _) => GeoTiffReader.Read(path));
    }
}
