using System.Numerics;

namespace OpenMapUnifier.MapScene;

/// <summary>One sampling ray of a sensor volume, scene-local. Perspective
/// sensors emit all rays from the platform; a cylinder emits parallel ones.</summary>
public readonly record struct SensorRay(Vector3 Origin, Vector3 Direction);

/// <summary>
/// Base for all sensor shapes: a device rigidly mounted on the trajectory's
/// body via mount angles (a downward-looking sensor: MountPitchDeg = -90).
/// Concrete shapes (<see cref="PyramidSensor"/>, <see cref="ConeSensor"/>,
/// <see cref="CylinderSensor"/>) only differ in the ray bundle they emit;
/// everything else — pose math, line-of-sight, coverage — is shared.
/// Frame convention everywhere: local ENU, yaw clockwise from north, pitch up
/// positive, roll right positive.
/// </summary>
public abstract record SensorModel(
    string Name,
    double MaxRangeMeters = 5000,
    double MountYawDeg = 0,
    double MountPitchDeg = -90,
    double MountRollDeg = 0)
{
    // Body frame at zero attitude: forward = north, right = east, up = sky.
    protected static readonly Vector3 BodyForward = new(0, 1, 0);
    protected static readonly Vector3 BodyRight = new(1, 0, 0);
    protected static readonly Vector3 BodyUp = new(0, 0, 1);

    /// <summary>Orientation of the sensor in the scene for a given body pose.</summary>
    public Quaternion OrientationFor(TrajectorySample pose) =>
        pose.BodyOrientation *
        BodyRotation((float)MountYawDeg, (float)MountPitchDeg, (float)MountRollDeg);

    /// <summary>Boresight (viewing) direction in scene coordinates.</summary>
    public Vector3 BoresightFor(TrajectorySample pose) =>
        Vector3.Transform(BodyForward, OrientationFor(pose));

    /// <summary>
    /// Rays sampling the sensor's sensitive volume for one pose.
    /// <paramref name="quality"/> scales the ray count; higher = finer
    /// footprints, linearly slower.
    /// </summary>
    public abstract IEnumerable<SensorRay> Rays(TrajectorySample pose, int quality);

    /// <summary>
    /// Aerospace-order rotation (yaw, then pitch, then roll) expressed in the
    /// ENU frame: yaw turns clockwise from north (negative around Z-up),
    /// pitch raises the nose (positive around the right axis), roll banks
    /// right (positive around forward).
    /// </summary>
    public static Quaternion BodyRotation(float yawDeg, float pitchDeg, float rollDeg)
    {
        var yaw = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), -yawDeg * DegToRad);
        var pitch = Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), pitchDeg * DegToRad);
        var roll = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), rollDeg * DegToRad);
        return yaw * pitch * roll;
    }

    /// <summary>
    /// Euler angles (our convention) from a body→ENU quaternion — for
    /// displaying/logging quaternion-sourced attitudes. Roll is measured
    /// against the zero-roll frame (right = forward x worldUp), degenerate
    /// only when looking straight up/down.
    /// </summary>
    public static (float YawDeg, float PitchDeg, float RollDeg) ToEuler(Quaternion q)
    {
        var forward = Vector3.Transform(BodyForward, q);
        var up = Vector3.Transform(BodyUp, q);

        var yaw = MathF.Atan2(forward.X, forward.Y) / DegToRad;
        var pitch = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f)) / DegToRad;

        var right0 = Vector3.Cross(forward, BodyUp);
        if (right0.LengthSquared() < 1e-12f)
            return (yaw, pitch, 0); // gimbal-lock: roll folded into yaw
        right0 = Vector3.Normalize(right0);
        var up0 = Vector3.Cross(right0, forward);
        var roll = MathF.Atan2(Vector3.Dot(up, right0), Vector3.Dot(up, up0)) / DegToRad;
        return (yaw, pitch, roll);
    }

    protected const float DegToRad = MathF.PI / 180f;
}
