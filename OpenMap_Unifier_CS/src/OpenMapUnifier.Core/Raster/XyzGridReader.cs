using System.Globalization;
using System.IO.Compression;

namespace OpenMapUnifier.Core.Raster;

/// <summary>
/// Reader for ASCII XYZ elevation grids ("easting northing height" per line),
/// the format Bayern serves DGM5 in (zipped, one .xyz per 1 km tile).
/// Coordinates in the file are pixel CENTERS on a regular grid.
/// </summary>
public static class XyzGridReader
{
    public static HeightGrid ReadZip(string zipPath, float noDataValue = -9999f)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
                        e.Name.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e => e.Length > 0)
                    ?? throw new InvalidDataException($"No data entry in {zipPath}.");
        using var stream = entry.Open();
        return Read(stream, noDataValue);
    }

    public static HeightGrid Read(Stream stream, float noDataValue = -9999f)
    {
        var points = new List<(double E, double N, float Z)>();
        using (var reader = new StreamReader(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var e) ||
                    !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    continue;
                points.Add((e, n, z));
            }
        }
        if (points.Count < 3)
            throw new InvalidDataException("Too few XYZ points to form a grid.");

        var eastings = points.Select(p => p.E).Distinct().OrderBy(v => v).ToArray();
        var step = eastings.Length > 1
            ? eastings.Skip(1).Zip(eastings, (a, b) => a - b).Min()
            : 1.0;

        double minE = double.PositiveInfinity, minN = double.PositiveInfinity;
        double maxE = double.NegativeInfinity, maxN = double.NegativeInfinity;
        foreach (var (e, n, _) in points)
        {
            minE = Math.Min(minE, e); maxE = Math.Max(maxE, e);
            minN = Math.Min(minN, n); maxN = Math.Max(maxN, n);
        }

        var width = (int)Math.Round((maxE - minE) / step) + 1;
        var height = (int)Math.Round((maxN - minN) / step) + 1;
        var data = new float[width * height];
        Array.Fill(data, noDataValue);
        foreach (var (e, n, z) in points)
        {
            var col = (int)Math.Round((e - minE) / step);
            var row = (int)Math.Round((maxN - n) / step);
            if (col >= 0 && col < width && row >= 0 && row < height)
                data[row * width + col] = z;
        }

        // XYZ coordinates are pixel centers; HeightGrid wants the top-left corner.
        return new HeightGrid(data, width, height,
            minE - step / 2.0, maxN + step / 2.0, step, noDataValue);
    }
}
