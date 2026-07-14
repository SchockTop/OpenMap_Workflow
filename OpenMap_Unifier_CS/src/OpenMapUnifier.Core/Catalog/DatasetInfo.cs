namespace OpenMapUnifier.Core.Catalog;

public enum DatasetKind
{
    /// <summary>Direct tile file download (GeoTIFF, CityGML, LAZ, zipped XYZ...).</summary>
    Raw,
    /// <summary>Rendered by a WMS GetMap request per grid cell.</summary>
    Wms,
}

/// <summary>
/// Describes one downloadable dataset. Raw datasets follow the pattern
/// <c>https://&lt;mirror&gt;/a/&lt;UrlPath&gt;/&lt;TilePrefix&gt;&lt;east_km&gt;_&lt;north_km&gt;&lt;TileSuffix&gt;&lt;Extension&gt;</c>
/// on the EPSG:25832 kilometer grid; WMS datasets are rendered per cell from
/// <see cref="WmsBaseUrl"/> / <see cref="WmsLayer"/>.
/// </summary>
public sealed record DatasetInfo
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required DatasetKind Kind { get; init; }

    /// <summary>Tile file extension including the dot (".tif", ".gml", ".zip", ...).</summary>
    public required string Extension { get; init; }

    /// <summary>Ground sample distance in meters, if the product is a raster.</summary>
    public double? PixelSizeMeters { get; init; }

    /// <summary>Tile size on the AdV grid in kilometers (1 for most, 2 for LoD2).</summary>
    public int GridKm { get; init; } = 1;

    /// <summary>Rough average tile size in MB, for download estimates.</summary>
    public double? AverageTileMb { get; init; }

    // Raw datasets
    /// <summary>Full path between /a/ and the tile filename (may contain slashes).</summary>
    public string? UrlPath { get; init; }
    /// <summary>Prepended to "&lt;east_km&gt;_&lt;north_km&gt;": "32" for DOP/DOM, "" for DGM/LoD2.</summary>
    public string TilePrefix { get; init; } = "";
    /// <summary>Product marker between grid id and extension (DOM20: "_20_DOM").</summary>
    public string TileSuffix { get; init; } = "";

    // WMS datasets
    public string? WmsBaseUrl { get; init; }
    public string? WmsLayer { get; init; }
    public string WmsMime { get; init; } = "image/tiff";
}

public interface IDatasetCatalog
{
    IReadOnlyDictionary<string, DatasetInfo> Datasets { get; }
    DatasetInfo this[string id] { get; }
}
