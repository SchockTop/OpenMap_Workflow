using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Geometry;

namespace OpenMapUnifier.Grid;

/// <summary>
/// Kilometer-grid math for EPSG:25832. Tiles snap to multiples of the grid
/// size so we never emit tile IDs that don't exist on the server (LoD2 lives
/// on the even 2 km grid, everything else on the 1 km grid).
/// </summary>
public static class TileGrid
{
    /// <summary>The tile containing a position.</summary>
    public static TileId TileFor(UtmPoint p, int gridKm = 1)
    {
        var step = gridKm * 1000.0;
        var eastKm = (int)(Math.Floor(p.Easting / step) * gridKm);
        var northKm = (int)(Math.Floor(p.Northing / step) * gridKm);
        return new TileId(eastKm, northKm, gridKm);
    }

    /// <summary>All tiles intersecting a bounding box, row-major from the SW corner.</summary>
    public static IEnumerable<TileId> TilesFor(BoundingBox box, int gridKm = 1)
    {
        var step = gridKm * 1000.0;
        var startE = (int)Math.Floor(box.MinEasting / step);
        var startN = (int)Math.Floor(box.MinNorthing / step);
        var endE = (int)Math.Ceiling(box.MaxEasting / step);
        var endN = (int)Math.Ceiling(box.MaxNorthing / step);

        for (var e = startE; e < endE; e++)
            for (var n = startN; n < endN; n++)
                yield return new TileId(e * gridKm, n * gridKm, gridKm);
    }

    /// <summary>All tiles intersecting a polygon (the Unifier's KML-region selection).</summary>
    public static IEnumerable<TileId> TilesFor(Polygon2D polygon, int gridKm = 1)
    {
        foreach (var tile in TilesFor(polygon.Bounds, gridKm))
            if (polygon.Intersects(tile.Bounds))
                yield return tile;
    }
}
