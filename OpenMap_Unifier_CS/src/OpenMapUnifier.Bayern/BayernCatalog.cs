using OpenMapUnifier.Core.Catalog;

namespace OpenMapUnifier.Bayern;

/// <summary>
/// Bayern OpenData dataset catalog — a 1:1 port of BAYERN_DATASETS from the
/// Python Unifier (backend/downloader.py). All raw tile datasets follow
/// <c>https://&lt;mirror&gt;/a/&lt;url_path&gt;/&lt;tile_id&gt;&lt;ext&gt;</c> on the EPSG:25832
/// kilometer grid. URL shapes were verified against Bavaria's live metalinks:
/// DGM lives under dgm/ with NO "32" zone prefix, DOP under .../data/ WITH it,
/// DOM20 adds a "_20_DOM" suffix, LoD2 sits on the even 2 km grid.
///
/// License: Bayerische Vermessungsverwaltung — CC BY 4.0.
/// </summary>
public sealed class BayernCatalog : IDatasetCatalog
{
    public static readonly IReadOnlyList<string> RawMirrors = new[]
    {
        "https://download1.bayernwolke.de",
        "https://download2.bayernwolke.de",
    };

    public const string Attribution =
        "Datenquelle: Bayerische Vermessungsverwaltung – www.geodaten.bayern.de (CC BY 4.0)";

    public const string Lod2MetalinkUrl =
        "https://geodaten.bayern.de/odd/a/lod2/citygml/meta/metalink/09.meta4";

    public static readonly BayernCatalog Instance = new();

    public IReadOnlyDictionary<string, DatasetInfo> Datasets { get; } =
        new Dictionary<string, DatasetInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["dgm1"] = new()
            {
                Id = "dgm1",
                Label = "DGM1 — Digital Terrain Model (Height, 1 m)",
                Category = "height",
                Description = "Bare-earth elevation, 1m grid, GeoTIFF. Real height data for Blender/3D.",
                Kind = DatasetKind.Raw,
                Extension = ".tif",
                PixelSizeMeters = 1.0,
                AverageTileMb = 4,
                UrlPath = "dgm/dgm1",
                TilePrefix = "",
            },
            ["dgm5"] = new()
            {
                Id = "dgm5",
                Label = "DGM5 — Digital Terrain Model (Height, 5 m)",
                Category = "height",
                Description = "Coarser 5m grid, zipped XYZ — for large areas where DGM1 would be too big.",
                Kind = DatasetKind.Raw,
                Extension = ".zip",
                PixelSizeMeters = 5.0,
                AverageTileMb = 0.8,
                UrlPath = "dgm/dgm5xyz",
                TilePrefix = "",
            },
            ["dom20"] = new()
            {
                Id = "dom20",
                Label = "DOM20 — Digital Surface Model (first return, 20 cm)",
                Category = "height",
                Description = "Surface elevation incl. buildings/trees, 20cm grid, GeoTIFF. " +
                              "DOM20 − DGM1 = nDSM (object height above ground).",
                Kind = DatasetKind.Raw,
                Extension = ".tif",
                PixelSizeMeters = 0.2,
                AverageTileMb = 44,
                UrlPath = "dom20/DOM",
                TilePrefix = "32",
                TileSuffix = "_20_DOM",
            },
            ["dop20"] = new()
            {
                Id = "dop20",
                Label = "DOP20 RGB — Orthophoto 20 cm (Highest quality)",
                Category = "ortho",
                Description = "Raw RGB aerial imagery, 20cm/px, GeoTIFF. Large files (~300 MB/tile).",
                Kind = DatasetKind.Raw,
                Extension = ".tif",
                PixelSizeMeters = 0.2,
                AverageTileMb = 300,
                UrlPath = "dop20/data",
                TilePrefix = "32",
            },
            ["dop40"] = new()
            {
                Id = "dop40",
                Label = "DOP40 RGB — Orthophoto 40 cm",
                Category = "ortho",
                Description = "Raw RGB aerial imagery, 40cm/px, GeoTIFF. ~4x smaller than DOP20.",
                Kind = DatasetKind.Raw,
                Extension = ".tif",
                PixelSizeMeters = 0.4,
                AverageTileMb = 75,
                UrlPath = "dop40/data",
                TilePrefix = "32",
            },
            ["lod2"] = new()
            {
                Id = "lod2",
                Label = "LoD2 — 3D building models (CityGML)",
                Category = "buildings",
                Description = "CityGML with building volumes at Level-of-Detail 2 (roof shapes). 2 km tiles.",
                Kind = DatasetKind.Raw,
                Extension = ".gml",
                GridKm = 2,
                AverageTileMb = 50,
                UrlPath = "lod2/citygml",
                TilePrefix = "",
            },
            ["laser"] = new()
            {
                Id = "laser",
                Label = "Laser — Raw LiDAR point cloud (LAZ)",
                Category = "laser",
                Description = "Compressed LAS (LAZ) — the raw point cloud DGM1 is derived from (~800 MB/tile).",
                Kind = DatasetKind.Raw,
                Extension = ".laz",
                AverageTileMb = 800,
                UrlPath = "laser/data",
                TilePrefix = "32",
            },
            ["dop20cir_wms"] = new()
            {
                Id = "dop20cir_wms",
                Label = "DOP20 CIR — Color-Infrared / NIR 20 cm (vegetation, water)",
                Category = "infrared",
                Description = "Near-infrared aerial imagery, NIR rendered as red. WMS-rendered " +
                              "(no raw CIR tile exists).",
                Kind = DatasetKind.Wms,
                Extension = ".tiff",
                WmsBaseUrl = "https://geoservices.bayern.de/od/wms/dop/v1/dop20",
                WmsLayer = "by_dop20cir",
                WmsMime = "image/tiff",
            },
            ["relief_wms"] = new()
            {
                Id = "relief_wms",
                Label = "Relief (hillshade WMS)",
                Category = "wms_render",
                Description = "Stylised shaded-relief rendering. Visual only — not elevation numbers.",
                Kind = DatasetKind.Wms,
                Extension = ".tiff",
                WmsBaseUrl = "https://geoservices.bayern.de/pro/wms/dgm/v1/relief",
                WmsLayer = "by_relief_schraeglicht",
                WmsMime = "image/tiff",
            },
            ["dop40_wms"] = new()
            {
                Id = "dop40_wms",
                Label = "DOP40 (WMS quick preview)",
                Category = "wms_render",
                Description = "WMS-rendered orthophoto preview. Faster but lower fidelity than raw DOP40.",
                Kind = DatasetKind.Wms,
                Extension = ".jpg",
                WmsBaseUrl = "https://geoservices.bayern.de/od/wms/dop/v1/dop40",
                WmsLayer = "by_dop40c",
                WmsMime = "image/jpeg",
            },
        };

    public DatasetInfo this[string id] =>
        Datasets.TryGetValue(id, out var d)
            ? d
            : throw new KeyNotFoundException(
                $"Unknown Bayern dataset '{id}'. Known: {string.Join(", ", Datasets.Keys)}.");
}
