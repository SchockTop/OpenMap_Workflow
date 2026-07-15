using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Grid;
using OpenMapUnifier.Raster;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// The scene's terrain: one contiguous height grid covering the region,
/// merged from provider tiles (or loaded from a file) so that sampling and
/// ray casts are fast, synchronous array lookups. A surface-model layer
/// (DOM) can sit alongside a terrain layer (DGM) — their difference is the
/// object height above ground (nDSM), which is how "is a tree/building"
/// conditions work without any vector data.
/// </summary>
public sealed class TerrainLayer
{
    public HeightGrid Grid { get; }
    public BoundingBox Bounds => Grid.Bounds;
    public double Resolution => Grid.PixelSize;

    public TerrainLayer(HeightGrid grid)
    {
        Grid = grid;
    }

    /// <summary>Load from a GeoTIFF or XYZ file on disk.</summary>
    public static TerrainLayer FromFile(string path)
    {
        var grid = path.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase)
            ? XyzGridReader.Read(File.ReadAllBytes(path))
            : path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? XyzGridReader.ReadZip(path)
                : GeoTiffReader.Read(path);
        return new TerrainLayer(grid);
    }

    /// <summary>
    /// Build the merged region grid from an elevation provider (tiles are
    /// downloaded on demand and cached by the provider). <paramref name="resolution"/>
    /// is the output cell size in meters — coarser than the source is fine and
    /// keeps memory bounded (a 10x10 km region at 1 m is 400 MB; at 2 m, 100 MB).
    /// </summary>
    public static async Task<TerrainLayer> LoadAsync(TiledElevationProvider provider,
        BoundingBox region, double resolution = 1.0, CancellationToken ct = default)
    {
        var width = (int)Math.Ceiling(region.Width / resolution);
        var height = (int)Math.Ceiling(region.Height / resolution);
        var data = new float[width * height];
        Array.Fill(data, -9999f);
        var originN = region.MaxNorthing;

        // Blit tile by tile: fetch each covering source tile once, then fill
        // every output cell whose center falls inside it.
        foreach (var tile in TileGrid.TilesFor(region, gridKm: 1))
        {
            ct.ThrowIfCancellationRequested();
            var source = await provider.GetGridAsync(
                TileGrid.TileFor(new UtmPoint(tile.MinEasting + 1, tile.MinNorthing + 1)), ct)
                .ConfigureAwait(false);
            if (source is null) continue; // no coverage here — cells stay NoData

            for (var row = 0; row < height; row++)
            {
                var northing = originN - (row + 0.5) * resolution;
                if (northing < tile.MinNorthing || northing >= tile.MaxNorthing) continue;
                for (var col = 0; col < width; col++)
                {
                    var easting = region.MinEasting + (col + 0.5) * resolution;
                    if (easting < tile.MinEasting || easting >= tile.MaxEasting) continue;
                    var sample = source.Sample(new UtmPoint(easting, northing));
                    if (sample is { } v) data[row * width + col] = (float)v;
                }
            }
        }

        return new TerrainLayer(new HeightGrid(data, width, height,
            region.MinEasting, originN, resolution));
    }

    /// <summary>Terrain height at a position, or null outside coverage.</summary>
    public double? HeightAt(UtmPoint p) => Grid.Sample(p);

    /// <summary>Highest terrain value in the layer (useful as a ray-march ceiling).</summary>
    public double MaxHeight()
    {
        var max = double.MinValue;
        foreach (var v in Grid.Data)
            if (v != Grid.NoDataValue && v > max) max = v;
        return max;
    }
}
