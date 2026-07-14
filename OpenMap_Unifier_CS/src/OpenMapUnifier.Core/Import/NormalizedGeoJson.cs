using System.Text.Json;

namespace OpenMapUnifier.Core.Import;

/// <summary>
/// Serializes importer results as a normalized GeoJSON FeatureCollection:
/// Point geometry in WGS84 (as the GeoJSON spec requires), with provenance
/// per feature — source JSON path, raw matched text, detected CRS,
/// confidence — plus the coordinate pre-converted to a target CRS in the
/// properties for direct downstream use.
/// </summary>
public static class NormalizedGeoJson
{
    public static string Write(IEnumerable<FoundCoordinate> coordinates, int targetEpsg = 25832)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("type", "FeatureCollection");
            w.WriteStartArray("features");
            foreach (var f in coordinates)
            {
                w.WriteStartObject();
                w.WriteString("type", "Feature");
                w.WriteStartObject("geometry");
                w.WriteString("type", "Point");
                w.WriteStartArray("coordinates");
                w.WriteNumberValue(Math.Round(f.Geo.Longitude, 8));
                w.WriteNumberValue(Math.Round(f.Geo.Latitude, 8));
                if (f.Z is { } z) w.WriteNumberValue(Math.Round(z, 3));
                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteStartObject("properties");
                w.WriteString("sourcePath", f.Path);
                w.WriteString("sourceRaw", f.Raw);
                w.WriteNumber("detectedEpsg", f.Guess.Epsg);
                w.WriteString("detectedCrs", f.Guess.CrsName);
                w.WriteNumber("confidence", Math.Round(f.Guess.Confidence, 3));
                w.WriteString("reason", f.Guess.Reason);
                var (tx, ty) = f.In(targetEpsg);
                w.WriteNumber($"epsg{targetEpsg}_x", Math.Round(tx, 3));
                w.WriteNumber($"epsg{targetEpsg}_y", Math.Round(ty, 3));
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public static void WriteFile(string path, IEnumerable<FoundCoordinate> coordinates,
        int targetEpsg = 25832) =>
        File.WriteAllText(path, Write(coordinates, targetEpsg));
}
