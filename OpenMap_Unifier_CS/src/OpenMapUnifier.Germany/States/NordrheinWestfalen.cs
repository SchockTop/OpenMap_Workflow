using System.Text.RegularExpressions;
using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Grid;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Nordrhein-Westfalen (opengeodata.nrw.de) — EPSG:25832, 1 km tiles,
/// license DL-DE Zero 2.0. Raster filenames embed a per-tile acquisition year,
/// so DGM1/DOM1/DOP resolve through the folder's XML index (fetched once and
/// cached); LoD2 is deterministic ("LoD2_32_&lt;E&gt;_&lt;N&gt;_1_NW.gml",
/// case-sensitive). DOP is 10 cm RGBI JPEG2000. Verified live.
/// </summary>
public sealed class NordrheinWestfalen : StateBase
{
    private const string Dgm1Dir = "https://www.opengeodata.nrw.de/produkte/geobasis/hm/dgm1_tiff/dgm1_tiff/";
    private const string Dom1Dir = "https://www.opengeodata.nrw.de/produkte/geobasis/hm/dom1_tiff/dom1_tiff/";
    private const string DopDir = "https://www.opengeodata.nrw.de/produkte/geobasis/lusat/akt/dop/dop_jp2_f10/";
    private const string Lod2Dir = "https://www.opengeodata.nrw.de/produkte/geobasis/3dg/lod2_gml/lod2_gml/";

    private readonly Dictionary<string, UrlIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public override string Code => "nw";
    public override string Name => "Nordrhein-Westfalen";
    public override int UtmZone => 32;
    public override string License => "DL-DE Zero 2.0";
    public override string Attribution => "Land NRW (dl-zero-de/2.0)";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF (NoData -9999; index-resolved year suffix)",
        ["dom1"] = "Surface model 1 m, GeoTIFF (index-resolved)",
        ["dop10rgbi"] = "Orthophoto 10 cm RGBI, JPEG2000 (~20-35 MB/tile, index-resolved)",
        ["lod2"] = "3D buildings CityGML 1.0, plain .gml (deterministic URL)",
    };

    private UrlIndex IndexFor(string datasetId, ProxyManager? proxy)
    {
        lock (_lock)
        {
            if (_indexes.TryGetValue(datasetId, out var cached)) return cached;
            var (dir, prod, ext) = datasetId.ToLowerInvariant() switch
            {
                "dgm1" => (Dgm1Dir, "dgm1", "tif"),
                "dom1" => (Dom1Dir, "dom1", "tif"),
                "dop10rgbi" => (DopDir, "dop10rgbi", "jp2"),
                _ => throw new KeyNotFoundException($"Unknown NRW indexed dataset '{datasetId}'."),
            };
            var pattern = new Regex(
                $@"{prod}_32_(?<e>\d{{3}})_(?<n>\d{{4}})_1_nw_\d{{4}}\.{ext}",
                RegexOptions.Compiled);
            var index = new UrlIndex(CreateHttp(proxy), dir, pattern, m => dir + m.Value);
            _indexes[datasetId] = index;
            return index;
        }
    }

    internal static DownloadJob Lod2JobFor(TileId tile)
    {
        var fileName = FormattableString.Invariant($"LoD2_32_{tile.EastKm}_{tile.NorthKm}_1_NW.gml");
        return new DownloadJob(fileName, Lod2Dir + fileName);
    }

    public override async Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        var tiles = TileGrid.TilesFor(area).ToList();
        if (datasetId.Equals("lod2", StringComparison.OrdinalIgnoreCase))
            return tiles.Select(Lod2JobFor).ToList();

        var index = IndexFor(datasetId, null);
        var jobs = new List<DownloadJob>();
        foreach (var tile in tiles)
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
