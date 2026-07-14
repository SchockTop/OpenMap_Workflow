using OpenMapUnifier.Core.Geodesy;

namespace OpenMapUnifier.Core.Raster;

/// <summary>
/// A georeferenced single-band elevation raster in EPSG:25832. Row 0 is the
/// northernmost row (GeoTIFF convention); pixel values are meters above sea
/// level with <see cref="NoDataValue"/> marking gaps (-9999 for Bayern DGM).
/// </summary>
public sealed class HeightGrid
{
    public float[] Data { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>Easting of the top-left CORNER (not pixel center).</summary>
    public double OriginEasting { get; }
    /// <summary>Northing of the top-left CORNER (not pixel center).</summary>
    public double OriginNorthing { get; }
    public double PixelSize { get; }
    public float NoDataValue { get; }

    public BoundingBox Bounds => new(
        OriginEasting, OriginNorthing - Height * PixelSize,
        OriginEasting + Width * PixelSize, OriginNorthing);

    public HeightGrid(float[] data, int width, int height,
        double originEasting, double originNorthing, double pixelSize, float noDataValue = -9999f)
    {
        if (data.Length != width * height)
            throw new ArgumentException($"Data length {data.Length} != {width}x{height}.", nameof(data));
        Data = data;
        Width = width;
        Height = height;
        OriginEasting = originEasting;
        OriginNorthing = originNorthing;
        PixelSize = pixelSize;
        NoDataValue = noDataValue;
    }

    public float ValueAt(int row, int col) => Data[row * Width + col];

    public bool Contains(Utm32Point p) =>
        p.Easting >= OriginEasting && p.Easting <= OriginEasting + Width * PixelSize &&
        p.Northing <= OriginNorthing && p.Northing >= OriginNorthing - Height * PixelSize;

    /// <summary>
    /// Bilinearly interpolated elevation, or null outside the grid / on NoData.
    /// Pixel centers sit at corner + (i + 0.5) * pixel; sampling clamps to the
    /// outermost centers, so the outer half-pixel ring is nearest-neighbor.
    /// </summary>
    public double? Sample(Utm32Point p)
    {
        if (!Contains(p)) return null;

        var x = (p.Easting - OriginEasting) / PixelSize - 0.5;
        var y = (OriginNorthing - p.Northing) / PixelSize - 0.5;
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);

        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = Math.Min(x0 + 1, Width - 1);
        var y1 = Math.Min(y0 + 1, Height - 1);
        var fx = x - x0;
        var fy = y - y0;

        double v00 = ValueAt(y0, x0), v10 = ValueAt(y0, x1);
        double v01 = ValueAt(y1, x0), v11 = ValueAt(y1, x1);
        if (v00 == NoDataValue || v10 == NoDataValue || v01 == NoDataValue || v11 == NoDataValue)
        {
            // Fall back to the nearest pixel so a single NoData neighbor
            // doesn't poison an otherwise valid position.
            var nearest = ValueAt((int)Math.Round(y), (int)Math.Round(x));
            return nearest == NoDataValue ? null : nearest;
        }

        return v00 * (1 - fx) * (1 - fy) + v10 * fx * (1 - fy)
             + v01 * (1 - fx) * fy + v11 * fx * fy;
    }

    /// <summary>Nearest-neighbor elevation, or null outside the grid / on NoData.</summary>
    public double? SampleNearest(Utm32Point p)
    {
        if (!Contains(p)) return null;
        var col = Math.Clamp((int)((p.Easting - OriginEasting) / PixelSize), 0, Width - 1);
        var row = Math.Clamp((int)((OriginNorthing - p.Northing) / PixelSize), 0, Height - 1);
        var v = ValueAt(row, col);
        return v == NoDataValue ? null : v;
    }
}
