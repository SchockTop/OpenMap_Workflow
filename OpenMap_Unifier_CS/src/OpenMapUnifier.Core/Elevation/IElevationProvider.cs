using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Raster;

namespace OpenMapUnifier.Core.Elevation;

/// <summary>Answers "how high is the terrain at this position?".</summary>
public interface IElevationProvider
{
    /// <summary>
    /// Transform between geographic coordinates and this provider's working
    /// CRS. Zone 32 (EPSG:25832) for most states, zone 33 (EPSG:25833) for
    /// Berlin/Brandenburg/Sachsen/MV — lat/lon queries route through this.
    /// </summary>
    ICoordinateTransform Transform { get; }

    /// <summary>
    /// Elevation in meters above sea level at a position in this provider's
    /// working CRS, or null where no data exists (outside coverage or NoData).
    /// </summary>
    Task<double?> GetElevationAsync(UtmPoint position, CancellationToken ct = default);
}

/// <summary>
/// Maps positions to height tiles for one dataset: which grid cell, what to
/// download, and how to parse the file. Implemented per product in the Bayern
/// and Niedersachsen packages; implement it yourself to plug any other
/// elevation source into <see cref="TiledElevationProvider"/>.
/// JobForAsync is async because some providers (Niedersachsen's STAC API)
/// must query a service to resolve a tile's download URL; it may return null
/// when the tile has no coverage.
/// </summary>
public interface IHeightTileResolver
{
    int GridKm { get; }
    TileId TileFor(UtmPoint position);
    Task<DownloadJob?> JobForAsync(TileId tile, CancellationToken ct = default);
    HeightGrid Parse(string localPath, TileId tile);
}
