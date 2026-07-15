using System.Text.Json;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// Writes a <see cref="PointSet"/> as JSON shaped for direct consumption in a
/// Unity project (deserializable with JsonUtility — flat fields, arrays, no
/// dictionaries).
///
/// Coordinate handover: our scene frame is right-handed ENU (X east, Y north,
/// Z up); Unity is left-handed Y-up. The export therefore carries BOTH:
/// (x, y, z) = ENU meters, and (unityX, unityY, unityZ) = (east, up, north) —
/// place the anchor GameObject at your world origin and assign positions
/// verbatim. unityEulerX/Y/Z map our aerospace attitude to Unity's rotation
/// order: (−pitch, yaw, −roll) for Transform.eulerAngles.
/// </summary>
public static class UnityPointsExport
{
    public static void WriteFile(string path, PointSet points) =>
        File.WriteAllText(path, Write(points));

    public static string Write(PointSet points)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();

            w.WriteStartObject("anchor");
            w.WriteNumber("utmEasting", points.Anchor.Origin.Easting);
            w.WriteNumber("utmNorthing", points.Anchor.Origin.Northing);
            w.WriteNumber("utmZone", points.Anchor.UtmZone);
            w.WriteNumber("epsg", points.Anchor.UtmZone == 33 ? 25833 : 25832);
            var geo = points.Anchor.Transform.ToGeo(points.Anchor.Origin);
            w.WriteNumber("latitude", Math.Round(geo.Latitude, 8));
            w.WriteNumber("longitude", Math.Round(geo.Longitude, 8));
            w.WriteString("frame", "local ENU meters; unity = (east, up, north), left-handed Y-up");
            w.WriteEndObject();

            w.WriteStartArray("points");
            foreach (var p in points.Points)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                w.WriteString("tag", p.Tag);
                w.WriteNumber("x", Round(p.Position.X));
                w.WriteNumber("y", Round(p.Position.Y));
                w.WriteNumber("z", Round(p.Position.Z));
                w.WriteNumber("unityX", Round(p.Position.X));
                w.WriteNumber("unityY", Round(p.Position.Z));
                w.WriteNumber("unityZ", Round(p.Position.Y));
                w.WriteNumber("unityEulerX", Round(-p.PitchDeg));
                w.WriteNumber("unityEulerY", Round(p.YawDeg));
                w.WriteNumber("unityEulerZ", Round(-p.RollDeg));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());

        static double Round(float v)
        {
            var r = Math.Round(v, 4);
            return r == 0 ? 0 : r; // never emit JSON "-0"
        }
    }
}
