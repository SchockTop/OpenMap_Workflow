using System.Text.RegularExpressions;

namespace OpenMapUnifier.Germany.Niedersachsen;

/// <summary>
/// One LGLN OpenGeoData dataset, resolved through its STAC API. Verified live
/// against the services in July 2026 (see stac roots below). All products are
/// EPSG:25832 on the 1 km AdV grid; elevation COGs are float32 LZW GeoTIFFs
/// with NoData -9999 — same conventions as Bayern, different distribution.
/// </summary>
public sealed record NiDataset
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string StacRoot { get; init; }
    public required string Collection { get; init; }
    /// <summary>Asset key of the actual data file within a STAC item.</summary>
    public required string AssetKey { get; init; }
    public double? PixelSizeMeters { get; init; }
    public int GridKm { get; init; } = 1;
    public double? AverageTileMb { get; init; }
}

/// <summary>
/// Niedersachsen OpenGeoData catalog (LGLN). License: CC BY 4.0.
/// </summary>
public static class NiedersachsenCatalog
{
    public const string Attribution =
        "Datenquelle: LGLN — Landesamt für Geoinformation und Landesvermessung Niedersachsen, " +
        "OpenGeoData.NI (CC BY 4.0)";

    // Item IDs look like "dgm1_32_550_5802_1_ni_2016" /
    // "dop20rgbi_32_550_5802_2_ni_2025-03-06": product, UTM zone, east km,
    // north km, resolution marker, state, acquisition date.
    private static readonly Regex TileKeyPattern =
        new(@"_32_(\d{3})_(\d{4})_", RegexOptions.Compiled);

    public static readonly IReadOnlyDictionary<string, NiDataset> Datasets =
        new Dictionary<string, NiDataset>(StringComparer.OrdinalIgnoreCase)
        {
            ["dgm1"] = new()
            {
                Id = "dgm1",
                Label = "DGM1 — Digital Terrain Model (Height, 1 m)",
                Category = "height",
                Description = "Bare-earth elevation from ALS (>=4 pts/m2), 1m COG GeoTIFF, NoData -9999.",
                StacRoot = "https://dgm.stac.lgln.niedersachsen.de",
                Collection = "dgm1",
                AssetKey = "dgm1-tif",
                PixelSizeMeters = 1.0,
                AverageTileMb = 4,
            },
            ["dom1"] = new()
            {
                Id = "dom1",
                Label = "DOM1 — Digital Surface Model (first return, 1 m)",
                Category = "height",
                Description = "Surface elevation incl. buildings/trees, 1m COG GeoTIFF. " +
                              "DOM1 − DGM1 = object height above ground (nDSM).",
                StacRoot = "https://dom.stac.lgln.niedersachsen.de",
                Collection = "dom1",
                AssetKey = "dom1-tif",
                PixelSizeMeters = 1.0,
                AverageTileMb = 5,
            },
            // NOTE: LGLN's "DOP" collection mixes dop20 and (newer) dop10
            // epochs; both carry the SAME asset keys (dop20_rgb/dop20_rgbi),
            // so "newest flight per tile" can hand back 10 cm imagery in
            // regions that have been reflown. Pixel size is therefore
            // per-item (the STAC "bodenpixelgroesse" property), not fixed.
            ["dop20rgb"] = new()
            {
                Id = "dop20rgb",
                Label = "DOP RGB — Orthophoto (newest flight, 20 or 10 cm)",
                Category = "ortho",
                Description = "RGB aerial imagery COG, newest flight per tile (10cm in reflown regions).",
                StacRoot = "https://dop.stac.lgln.niedersachsen.de",
                Collection = "DOP",
                AssetKey = "dop20_rgb",
                AverageTileMb = 200,
            },
            ["dop20rgbi"] = new()
            {
                Id = "dop20rgbi",
                Label = "DOP RGBI — Orthophoto with near-infrared band (newest flight, 20 or 10 cm)",
                Category = "ortho",
                Description = "4-band RGBI imagery COG (NIR for vegetation/material analysis), newest flight per tile.",
                StacRoot = "https://dop.stac.lgln.niedersachsen.de",
                Collection = "DOP",
                AssetKey = "dop20_rgbi",
                AverageTileMb = 250,
            },
        };

    public static NiDataset Get(string id) =>
        Datasets.TryGetValue(id, out var d)
            ? d
            : throw new KeyNotFoundException(
                $"Unknown Niedersachsen dataset '{id}'. Known: {string.Join(", ", Datasets.Keys)}.");

    /// <summary>Extract (eastKm, northKm) from a STAC item id, or null.</summary>
    public static (int EastKm, int NorthKm)? TileKeyFromItemId(string itemId)
    {
        var m = TileKeyPattern.Match(itemId);
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }
}
