using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Sachsen (GeoSN) — public Nextcloud shares on
/// geocloud.landesvermessung.sachsen.de, EPSG:25833, 2 km tiles with the zone
/// glued to the easting ("dgm1_33410_5654_2_sn_tiff.zip"). Fully constructible
/// from coordinates; a 404 means the tile isn't offered. Share ids come from
/// the portal's batch-download page (verified July 2026). DL-DE/BY-2.0.
/// </summary>
public sealed class Sachsen : StateBase
{
    private const string Base = "https://geocloud.landesvermessung.sachsen.de/public.php/dav/files";

    public override string Code => "sn";
    public override string Name => "Sachsen";
    public override int UtmZone => 33;
    public override string License => "DL-DE/BY-2.0";
    public override string Attribution => "© GeoSN, dl-de/by-2-0";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF in zip (2 km tiles, NoData -9999)",
        ["dom1"] = "Surface model 1 m, GeoTIFF in zip (2 km tiles)",
        ["dop20rgb"] = "Orthophoto 20 cm RGB, GeoTIFF in zip (2 km tiles)",
        ["dop20rgbi"] = "Orthophoto 20 cm RGBI, GeoTIFF in zip (~365 MB/tile)",
        ["lod2"] = "3D buildings CityGML in zip (2 km tiles)",
    };

    private static (string Share, string Prod, string Fmt) Route(string datasetId) => datasetId.ToLowerInvariant() switch
    {
        "dgm1" => ("JCcXyifaNdLDnxZ", "dgm1", "tiff"),
        "dom1" => ("S6wwnFwX7882sZm", "dom1", "tiff"),
        "dop20rgb" => ("QQFLq6nkoSnqB5g", "dop20rgb", "tiff"),
        "dop20rgbi" => ("sX3GPcdBMGrfXT9", "dop20rgbi", "tiff"),
        "lod2" => ("AyJqXpJAZJXomCb", "lod2", "citygml"),
        _ => throw new KeyNotFoundException($"Unknown Sachsen dataset '{datasetId}'."),
    };

    internal static DownloadJob JobFor(string datasetId, TileId tile)
    {
        var (share, prod, fmt) = Route(datasetId);
        var fileName = FormattableString.Invariant(
            $"{prod}_33{tile.EastKm:D3}_{tile.NorthKm:D4}_2_sn_{fmt}.zip");
        return new DownloadJob(fileName, $"{Base}/{share}/{fileName}");
    }

    public override Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DownloadJob>>(
            TileGrid.TilesFor(area, 2).Select(t => JobFor(datasetId, t)).ToList());

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        return new DelegateTileResolver(2,
            (tile, _) => Task.FromResult<DownloadJob?>(JobFor(datasetId, tile)),
            (path, _) => ZipHelpers.ReadGeoTiffEntry(path,
                n => n.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)));
    }
}
