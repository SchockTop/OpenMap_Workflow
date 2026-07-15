using System.Numerics;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// Circular field of view around the boresight (spot sensors, conical-scan
/// seekers, simple LiDAR approximations). Rays sample concentric rings out to
/// the half-angle.
/// </summary>
public sealed record ConeSensor(
    string Name,
    double HalfAngleDeg = 20,
    double MaxRangeMeters = 5000,
    double MountYawDeg = 0,
    double MountPitchDeg = -90,
    double MountRollDeg = 0)
    : SensorModel(Name, MaxRangeMeters, MountYawDeg, MountPitchDeg, MountRollDeg)
{
    public override IEnumerable<SensorRay> Rays(TrajectorySample pose, int quality)
    {
        var rings = Math.Max(1, quality / 3);

        var orientation = OrientationFor(pose);
        var forward = Vector3.Transform(BodyForward, orientation);
        var right = Vector3.Transform(BodyRight, orientation);
        var up = Vector3.Transform(BodyUp, orientation);

        yield return new SensorRay(pose.Position, forward); // boresight

        for (var ring = 1; ring <= rings; ring++)
        {
            var angle = HalfAngleDeg * ring / rings * Math.PI / 180.0;
            var tan = Math.Tan(angle);
            // Ray count grows with ring circumference for even area coverage.
            var count = Math.Max(6, (int)Math.Round(2 * Math.PI * ring * quality / (3.0 * rings)));
            for (var i = 0; i < count; i++)
            {
                var phi = 2 * Math.PI * i / count;
                yield return new SensorRay(pose.Position, Vector3.Normalize(forward
                    + right * (float)(tan * Math.Cos(phi))
                    + up * (float)(tan * Math.Sin(phi))));
            }
        }
    }
}
