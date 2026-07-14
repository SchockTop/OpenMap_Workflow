using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Raster;

namespace OpenMapUnifier.Core.Elevation;

/// <summary>Answers "how high is the terrain at this position?".</summary>
public interface IElevationProvider
{
    /// <summary>
    /// Elevation in meters above sea level, or null where no data exists
    /// (outside coverage or NoData cells).
    /// </summary>
    Task<double?> GetElevationAsync(Utm32Point position, CancellationToken ct = default);
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
    TileId TileFor(Utm32Point position);
    Task<DownloadJob?> JobForAsync(TileId tile, CancellationToken ct = default);
    HeightGrid Parse(string localPath, TileId tile);
}
