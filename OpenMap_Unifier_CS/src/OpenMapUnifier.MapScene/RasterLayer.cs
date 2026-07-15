using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Raster;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// A classification / label raster aligned to the map (ground-texture
/// classes, land cover, any per-cell integer). Values sample
/// nearest-neighbor — classes must never be interpolated.
/// </summary>
public sealed class RasterLayer
{
    public HeightGrid Grid { get; }

    public RasterLayer(HeightGrid grid)
    {
        Grid = grid;
    }

    public static RasterLayer FromFile(string path) =>
        new(GeoTiffReader.Read(path));

    /// <summary>Class id at a position, or null outside / NoData.</summary>
    public int? ClassAt(UtmPoint p)
    {
        var v = Grid.SampleNearest(p);
        return v is { } value ? (int)Math.Round(value) : null;
    }
}
