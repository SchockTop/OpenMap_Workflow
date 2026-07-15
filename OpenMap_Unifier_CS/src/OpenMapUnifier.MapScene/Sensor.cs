using System.Numerics;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// A camera/sensor rigidly mounted on the trajectory's body. Mount angles are
/// relative to the body frame (a downward-looking camera: MountPitchDeg = -90).
/// Frame convention everywhere: local ENU, yaw clockwise from north, pitch up
/// positive, roll right positive.
/// </summary>
public sealed record Sensor(
    string Name,
    double FovHorizontalDeg = 60,
    double FovVerticalDeg = 45,
    double MaxRangeMeters = 5000,
    double MountYawDeg = 0,
    double MountPitchDeg = -90,
    double MountRollDeg = 0)
{
    /// <summary>Orientation of the sensor in the scene for a given body pose.</summary>
    public Quaternion OrientationFor(TrajectorySample pose) =>
        BodyRotation(pose.YawDeg, pose.PitchDeg, pose.RollDeg) *
        BodyRotation((float)MountYawDeg, (float)MountPitchDeg, (float)MountRollDeg);

    /// <summary>Boresight (viewing) direction in scene coordinates.</summary>
    public Vector3 BoresightFor(TrajectorySample pose) =>
        Vector3.Transform(Forward, OrientationFor(pose));

    /// <summary>
    /// Ray directions covering the field of view on a raysAcross x raysDown
    /// grid (tangent-plane spacing, like image pixels).
    /// </summary>
    public IEnumerable<Vector3> FrustumRays(TrajectorySample pose, int raysAcross, int raysDown)
    {
        var orientation = OrientationFor(pose);
        var forward = Vector3.Transform(Forward, orientation);
        var right = Vector3.Transform(Right, orientation);
        var up = Vector3.Transform(Up, orientation);

        var tanH = Math.Tan(FovHorizontalDeg * Math.PI / 360.0); // half-angle
        var tanV = Math.Tan(FovVerticalDeg * Math.PI / 360.0);

        for (var iv = 0; iv < raysDown; iv++)
        {
            var v = raysDown == 1 ? 0 : (2.0 * iv / (raysDown - 1)) - 1.0;
            for (var ih = 0; ih < raysAcross; ih++)
            {
                var h = raysAcross == 1 ? 0 : (2.0 * ih / (raysAcross - 1)) - 1.0;
                yield return Vector3.Normalize(
                    forward + right * (float)(h * tanH) + up * (float)(v * tanV));
            }
        }
    }

    // Body frame at zero attitude: forward = north, right = east, up = sky.
    private static readonly Vector3 Forward = new(0, 1, 0);
    private static readonly Vector3 Right = new(1, 0, 0);
    private static readonly Vector3 Up = new(0, 0, 1);

    /// <summary>
    /// Aerospace-order rotation (yaw, then pitch, then roll) expressed in the
    /// ENU frame: yaw turns clockwise from north (negative around Z-up),
    /// pitch raises the nose (positive around the right axis), roll banks
    /// right (positive around forward).
    /// </summary>
    internal static Quaternion BodyRotation(float yawDeg, float pitchDeg, float rollDeg)
    {
        var yaw = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), -yawDeg * DegToRad);
        var pitch = Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), pitchDeg * DegToRad);
        var roll = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), rollDeg * DegToRad);
        return yaw * pitch * roll;
    }

    private const float DegToRad = MathF.PI / 180f;
}
