using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Geometry;
using OpenMapUnifier.Raster;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// A boolean area on the ground, cell-aligned with the scene terrain so that
/// combining masks is exact and cheap. Build them by hand (polygons), by
/// condition (any predicate over position — classification class, nDSM
/// height, ...), or as analysis output (sensor coverage); then combine with
/// Union / Intersect / Subtract / Dilate. This is the "areas on the ground"
/// concept: one mask = one area definition.
/// </summary>
public sealed class GroundMask
{
    private readonly bool[] _cells;

    public TerrainLayer Terrain { get; }
    public int Width { get; }
    public int Height { get; }
    public double CellSize => Terrain.Resolution;

    public GroundMask(TerrainLayer terrain, bool[]? cells = null)
    {
        Terrain = terrain;
        Width = terrain.Grid.Width;
        Height = terrain.Grid.Height;
        _cells = cells ?? new bool[Width * Height];
        if (_cells.Length != Width * Height)
            throw new ArgumentException("Cell array does not match the terrain grid.", nameof(cells));
    }

    public bool this[int row, int col]
    {
        get => _cells[row * Width + col];
        set => _cells[row * Width + col] = value;
    }

    // ---- building ------------------------------------------------------------

    /// <summary>Mark every cell whose center satisfies a condition.</summary>
    public static GroundMask FromCondition(TerrainLayer terrain, Func<UtmPoint, bool> condition)
    {
        var mask = new GroundMask(terrain);
        for (var row = 0; row < mask.Height; row++)
            for (var col = 0; col < mask.Width; col++)
                if (condition(mask.CellCenter(row, col)))
                    mask[row, col] = true;
        return mask;
    }

    /// <summary>Mark every cell inside a hand-drawn polygon.</summary>
    public static GroundMask FromPolygon(TerrainLayer terrain, Polygon2D polygon) =>
        FromCondition(terrain, polygon.Contains);

    /// <summary>Cells whose classification equals a class id.</summary>
    public static GroundMask FromClass(TerrainLayer terrain, RasterLayer classification, int classId) =>
        FromCondition(terrain, p => classification.ClassAt(p) == classId);

    /// <summary>
    /// Cells where objects stand taller than a threshold above ground —
    /// surface minus terrain (nDSM). With DOM/bDOM as the surface layer this
    /// is the practical "is trees / is buildings" test without vector data.
    /// </summary>
    public static GroundMask FromObjectHeight(TerrainLayer terrain, TerrainLayer surface,
        double minHeightMeters)
    {
        return FromCondition(terrain, p =>
            terrain.HeightAt(p) is { } ground &&
            surface.HeightAt(p) is { } top &&
            top - ground >= minHeightMeters);
    }

    // ---- set algebra ------------------------------------------------------------

    public GroundMask Union(GroundMask other) => Combine(other, (a, b) => a || b);
    public GroundMask Intersect(GroundMask other) => Combine(other, (a, b) => a && b);
    public GroundMask Subtract(GroundMask other) => Combine(other, (a, b) => a && !b);

    public GroundMask Invert()
    {
        var result = new bool[_cells.Length];
        for (var i = 0; i < _cells.Length; i++) result[i] = !_cells[i];
        return new GroundMask(Terrain, result);
    }

    /// <summary>
    /// Grow the area by a distance — "within 5 m of trees" is
    /// treeMask.Dilate(5). Breadth-first over cells (chamfer metric, exact to
    /// one cell).
    /// </summary>
    public GroundMask Dilate(double meters)
    {
        var maxSteps = (int)Math.Ceiling(meters / CellSize);
        var distance = new int[_cells.Length];
        Array.Fill(distance, int.MaxValue);
        var queue = new Queue<int>();
        for (var i = 0; i < _cells.Length; i++)
        {
            if (!_cells[i]) continue;
            distance[i] = 0;
            queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            if (distance[index] >= maxSteps) continue;
            var row = index / Width;
            var col = index % Width;
            foreach (var (dr, dc) in Neighbors)
            {
                int r = row + dr, c = col + dc;
                if (r < 0 || r >= Height || c < 0 || c >= Width) continue;
                var n = r * Width + c;
                if (distance[n] <= distance[index] + 1) continue;
                distance[n] = distance[index] + 1;
                queue.Enqueue(n);
            }
        }
        var result = new bool[_cells.Length];
        for (var i = 0; i < _cells.Length; i++) result[i] = distance[i] <= maxSteps;
        return new GroundMask(Terrain, result);
    }

    private static readonly (int, int)[] Neighbors = { (-1, 0), (1, 0), (0, -1), (0, 1) };

    private GroundMask Combine(GroundMask other, Func<bool, bool, bool> op)
    {
        if (other.Terrain != Terrain)
            throw new ArgumentException("Masks must share the same terrain grid.", nameof(other));
        var result = new bool[_cells.Length];
        for (var i = 0; i < _cells.Length; i++) result[i] = op(_cells[i], other._cells[i]);
        return new GroundMask(Terrain, result);
    }

    // ---- queries & export -----------------------------------------------------------

    public bool Contains(UtmPoint p)
    {
        var col = (int)((p.Easting - Terrain.Grid.OriginEasting) / CellSize);
        var row = (int)((Terrain.Grid.OriginNorthing - p.Northing) / CellSize);
        return row >= 0 && row < Height && col >= 0 && col < Width && this[row, col];
    }

    public UtmPoint CellCenter(int row, int col) => new(
        Terrain.Grid.OriginEasting + (col + 0.5) * CellSize,
        Terrain.Grid.OriginNorthing - (row + 0.5) * CellSize);

    public int CellCount()
    {
        var count = 0;
        foreach (var cell in _cells)
            if (cell) count++;
        return count;
    }

    /// <summary>Covered area in square meters.</summary>
    public double AreaSquareMeters() => CellCount() * CellSize * CellSize;

    /// <summary>
    /// Write as a georeferenced GeoTIFF (1 = inside, 0 = outside) — opens in
    /// QGIS/Blender-GIS on top of the ortho for visual checking.
    /// </summary>
    public void SaveGeoTiff(string path)
    {
        var data = new float[_cells.Length];
        for (var i = 0; i < _cells.Length; i++) data[i] = _cells[i] ? 1f : 0f;
        GeoTiffWriter.Write(path, new HeightGrid(data, Width, Height,
            Terrain.Grid.OriginEasting, Terrain.Grid.OriginNorthing, CellSize, noDataValue: -1f));
    }
}
