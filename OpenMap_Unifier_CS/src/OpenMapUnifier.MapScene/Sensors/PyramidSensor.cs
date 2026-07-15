using System.Numerics;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// Rectangular frustum — the classic camera/imager: horizontal and vertical
/// field of view around the boresight, rays on a tangent-plane grid like
/// image pixels.
/// </summary>
public sealed record PyramidSensor(
    string Name,
    double FovHorizontalDeg = 60,
    double FovVerticalDeg = 45,
    double MaxRangeMeters = 5000,
    double MountYawDeg = 0,
    double MountPitchDeg = -90,
    double MountRollDeg = 0)
    : SensorModel(Name, MaxRangeMeters, MountYawDeg, MountPitchDeg, MountRollDeg)
{
    public override IEnumerable<SensorRay> Rays(TrajectorySample pose, int quality)
    {
        var raysAcross = Math.Max(2, quality);
        var raysDown = Math.Max(2, quality * 3 / 4);

        var orientation = OrientationFor(pose);
        var forward = Vector3.Transform(BodyForward, orientation);
        var right = Vector3.Transform(BodyRight, orientation);
        var up = Vector3.Transform(BodyUp, orientation);

        var tanH = Math.Tan(FovHorizontalDeg * Math.PI / 360.0); // half-angle
        var tanV = Math.Tan(FovVerticalDeg * Math.PI / 360.0);

        for (var iv = 0; iv < raysDown; iv++)
        {
            var v = (2.0 * iv / (raysDown - 1)) - 1.0;
            for (var ih = 0; ih < raysAcross; ih++)
            {
                var h = (2.0 * ih / (raysAcross - 1)) - 1.0;
                yield return new SensorRay(pose.Position, Vector3.Normalize(
                    forward + right * (float)(h * tanH) + up * (float)(v * tanV)));
            }
        }
    }
}
