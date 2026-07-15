using System.Text.Json;
using OpenMapUnifier.Raster;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// Writes a complete scene as a folder the OpenMap Unity viewer loads out of
/// the box (see unity/OpenMapViewer): manifest.json + raw float32 heightmap +
/// grayscale PNG overlays + trajectory/points JSON. Only formats Unity reads
/// natively — no GeoTIFF on the Unity side, no packages.
///
/// Everything is in scene-local ENU meters; the Unity scripts do the axis
/// swap to left-handed Y-up. Row 0 of the heightmap and of every overlay is
/// the northernmost row.
/// </summary>
public static class UnitySceneExport
{
    public static IReadOnlyList<string> Write(string directory, Map map,
        Trajectory? trajectory = null, SensorModel? sensor = null, PointSet? points = null,
        IReadOnlyList<(string Name, GroundMask Mask)>? overlays = null)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        var grid = map.Terrain.Grid;

        // Heightmap: raw little-endian float32, NoData flattened to the minimum
        // so the mesh has no spikes; the manifest carries the real min/max.
        var (min, max) = ValidRange(grid);
        var heights = new float[grid.Data.Length];
        for (var i = 0; i < heights.Length; i++)
            heights[i] = grid.Data[i] == grid.NoDataValue ? min : grid.Data[i];
        var heightsFile = Path.Combine(directory, "terrain_heights.r32");
        WriteFloats(heightsFile, heights);
        written.Add(heightsFile);

        var overlayInfos = new List<(string Name, string File, int R, int G, int B)>();
        if (overlays is not null)
        {
            var palette = new (int R, int G, int B)[]
                { (46, 204, 113), (231, 76, 60), (52, 152, 219), (241, 196, 15), (155, 89, 182) };
            var i = 0;
            foreach (var (name, mask) in overlays)
            {
                var pixels = new byte[mask.Width * mask.Height];
                for (var row = 0; row < mask.Height; row++)
                    for (var col = 0; col < mask.Width; col++)
                        pixels[row * mask.Width + col] = mask[row, col] ? (byte)255 : (byte)0;
                var file = Path.Combine(directory, $"overlay_{Sanitize(name)}.png");
                PngWriter.WriteGrayscale(file, pixels, mask.Width, mask.Height);
                written.Add(file);
                var c = palette[i++ % palette.Length];
                overlayInfos.Add((name, Path.GetFileName(file), c.R, c.G, c.B));
            }
        }

        string? trajectoryFile = null;
        if (trajectory is not null)
        {
            trajectoryFile = Path.Combine(directory, "trajectory.json");
            File.WriteAllText(trajectoryFile, WriteTrajectory(trajectory));
            written.Add(trajectoryFile);
        }

        string? pointsFile = null;
        if (points is not null)
        {
            pointsFile = Path.Combine(directory, "points.json");
            UnityPointsExport.WriteFile(pointsFile, points);
            written.Add(pointsFile);
        }

        var manifestFile = Path.Combine(directory, "manifest.json");
        File.WriteAllText(manifestFile, WriteManifest(map, grid, min, max,
            overlayInfos, trajectoryFile, pointsFile, sensor));
        written.Add(manifestFile);
        return written;
    }

    private static string WriteManifest(Map map, HeightGrid grid, float min, float max,
        IReadOnlyList<(string Name, string File, int R, int G, int B)> overlays,
        string? trajectoryFile, string? pointsFile, SensorModel? sensor)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);

            var anchor = map.Anchor;
            var geo = anchor.Transform.ToGeo(anchor.Origin);
            w.WriteStartObject("anchor");
            w.WriteNumber("utmEasting", anchor.Origin.Easting);
            w.WriteNumber("utmNorthing", anchor.Origin.Northing);
            w.WriteNumber("utmZone", anchor.UtmZone);
            w.WriteNumber("epsg", anchor.UtmZone == 33 ? 25833 : 25832);
            w.WriteNumber("latitude", Math.Round(geo.Latitude, 8));
            w.WriteNumber("longitude", Math.Round(geo.Longitude, 8));
            w.WriteEndObject();

            w.WriteStartObject("terrain");
            w.WriteString("heightsFile", "terrain_heights.r32");
            w.WriteNumber("width", grid.Width);
            w.WriteNumber("height", grid.Height);
            w.WriteNumber("pixelSize", grid.PixelSize);
            // Unity-local position of the center of the top-left cell:
            // x east, zTop north (Unity z), heights become Unity y.
            w.WriteNumber("originX",
                Math.Round(grid.OriginEasting + 0.5 * grid.PixelSize - anchor.Origin.Easting, 4));
            w.WriteNumber("originZTop",
                Math.Round(grid.OriginNorthing - 0.5 * grid.PixelSize - anchor.Origin.Northing, 4));
            w.WriteNumber("minHeight", Math.Round(min, 2));
            w.WriteNumber("maxHeight", Math.Round(max, 2));
            // Drop a georeferenced PNG/JPG next to the manifest and name it
            // here to drape imagery; empty = hillshaded height gradient.
            w.WriteString("orthoFile", "");
            w.WriteEndObject();

            w.WriteStartArray("overlays");
            foreach (var o in overlays)
            {
                w.WriteStartObject();
                w.WriteString("name", o.Name);
                w.WriteString("file", o.File);
                w.WriteNumber("r", o.R);
                w.WriteNumber("g", o.G);
                w.WriteNumber("b", o.B);
                w.WriteBoolean("visibleByDefault", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteString("trajectoryFile",
                trajectoryFile is null ? "" : Path.GetFileName(trajectoryFile));
            w.WriteString("pointsFile", pointsFile is null ? "" : Path.GetFileName(pointsFile));

            if (sensor is not null)
            {
                w.WriteStartObject("sensor");
                w.WriteString("type", sensor switch
                {
                    PyramidSensor => "pyramid",
                    ConeSensor => "cone",
                    CylinderSensor => "cylinder",
                    _ => "pyramid",
                });
                w.WriteString("name", sensor.Name);
                w.WriteNumber("maxRangeMeters", sensor.MaxRangeMeters);
                w.WriteNumber("mountYawDeg", sensor.MountYawDeg);
                w.WriteNumber("mountPitchDeg", sensor.MountPitchDeg);
                w.WriteNumber("mountRollDeg", sensor.MountRollDeg);
                w.WriteNumber("fovHorizontalDeg", (sensor as PyramidSensor)?.FovHorizontalDeg ?? 0);
                w.WriteNumber("fovVerticalDeg", (sensor as PyramidSensor)?.FovVerticalDeg ?? 0);
                w.WriteNumber("halfAngleDeg", (sensor as ConeSensor)?.HalfAngleDeg ?? 0);
                w.WriteNumber("radiusMeters", (sensor as CylinderSensor)?.RadiusMeters ?? 0);
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string WriteTrajectory(Trajectory trajectory)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartArray("samples");
            foreach (var s in trajectory.Samples)
            {
                w.WriteStartObject();
                w.WriteNumber("t", Math.Round(s.Time, 4));
                w.WriteNumber("x", Math.Round(s.Position.X, 4));
                w.WriteNumber("y", Math.Round(s.Position.Y, 4));
                w.WriteNumber("z", Math.Round(s.Position.Z, 4));
                w.WriteNumber("yawDeg", Math.Round(s.YawDeg, 4));
                w.WriteNumber("pitchDeg", Math.Round(s.PitchDeg, 4));
                w.WriteNumber("rollDeg", Math.Round(s.RollDeg, 4));
                var q = s.Orientation;
                w.WriteNumber("qx", Math.Round(q?.X ?? 0, 6));
                w.WriteNumber("qy", Math.Round(q?.Y ?? 0, 6));
                w.WriteNumber("qz", Math.Round(q?.Z ?? 0, 6));
                w.WriteNumber("qw", Math.Round(q?.W ?? 1, 6));
                w.WriteBoolean("hasQuat", q is not null);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static (float Min, float Max) ValidRange(HeightGrid grid)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in grid.Data)
        {
            if (v == grid.NoDataValue) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return min <= max ? (min, max) : (0, 0);
    }

    private static void WriteFloats(string path, float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        return new string(chars.ToArray());
    }
}
