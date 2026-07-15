using System.Numerics;
using OpenMapUnifier.Geodesy;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// The scene's local coordinate frame: an ENU frame (X = east, Y = north,
/// Z = up, meters) anchored at a UTM origin. Large UTM coordinates
/// (E ≈ 700,000, N ≈ 5,400,000) destroy float32 precision in graphics
/// pipelines, so everything scene-facing works in small local floats — the
/// same anchor concept the Blender pipeline uses. One anchor per scene;
/// conversions are exact (double) both ways.
/// </summary>
public sealed record SceneAnchor(UtmPoint Origin, int UtmZone = 32)
{
    public Etrs89UtmTransform Transform => Etrs89UtmTransform.ForZone(UtmZone);

    /// <summary>Anchor at the center of a bounding box.</summary>
    public static SceneAnchor CenterOf(BoundingBox box, int utmZone = 32) =>
        new(box.Center, utmZone);

    public Vector3 ToLocal(UtmPoint p, double z = 0) => new(
        (float)(p.Easting - Origin.Easting),
        (float)(p.Northing - Origin.Northing),
        (float)z);

    public UtmPoint ToUtm(Vector3 local) => new(
        Origin.Easting + local.X,
        Origin.Northing + local.Y);

    /// <summary>Local position (including Z) as exact doubles.</summary>
    public (double Easting, double Northing, double Z) ToUtm3(Vector3 local) =>
        (Origin.Easting + local.X, Origin.Northing + local.Y, local.Z);

    public GeoPoint ToGeo(Vector3 local) => Transform.ToGeo(ToUtm(local));

    public Vector3 FromGeo(GeoPoint geo, double z = 0) => ToLocal(Transform.ToUtm(geo), z);
}
