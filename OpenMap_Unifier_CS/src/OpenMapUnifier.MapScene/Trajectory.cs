using System.Numerics;
using OpenMapUnifier.Geodesy;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// One time-stamped pose. Position is scene-local ENU (X east, Y north,
/// Z up, meters above sea level); attitude is aerospace-style: yaw = heading
/// in degrees clockwise from north, pitch up positive, roll right-wing-down
/// positive. When the source log carries quaternions, <see cref="Orientation"/>
/// holds the exact body→ENU rotation (Euler fields are derived from it) and
/// wins wherever both exist. Extra columns from the source file ride along in
/// <see cref="Extra"/> so downstream tools lose nothing.
/// </summary>
public sealed record TrajectorySample(
    double Time,
    Vector3 Position,
    float YawDeg,
    float PitchDeg,
    float RollDeg,
    IReadOnlyDictionary<string, double>? Extra = null,
    Quaternion? Orientation = null)
{
    /// <summary>The body→ENU rotation, from the quaternion when present.</summary>
    public Quaternion BodyOrientation =>
        Orientation ?? SensorModel.BodyRotation(YawDeg, PitchDeg, RollDeg);
}

/// <summary>
/// A time-ordered path through the scene with pose interpolation — the
/// digital twin of a flight log. Query any time with <see cref="At"/> or
/// enumerate fixed-rate frames with <see cref="Frames"/>.
/// </summary>
public sealed class Trajectory
{
    private readonly TrajectorySample[] _samples;

    public SceneAnchor Anchor { get; }
    public IReadOnlyList<TrajectorySample> Samples => _samples;
    public double StartTime => _samples[0].Time;
    public double EndTime => _samples[^1].Time;
    public double Duration => EndTime - StartTime;

    public Trajectory(SceneAnchor anchor, IEnumerable<TrajectorySample> samples)
    {
        Anchor = anchor;
        _samples = samples.OrderBy(s => s.Time).ToArray();
        if (_samples.Length < 2)
            throw new ArgumentException("A trajectory needs at least 2 samples.", nameof(samples));
    }

    /// <summary>Pose at a time (linear position, shortest-arc yaw; clamped to the ends).</summary>
    public TrajectorySample At(double time)
    {
        if (time <= StartTime) return _samples[0];
        if (time >= EndTime) return _samples[^1];

        var hi = Array.BinarySearch(_samples, new TrajectorySample(time, default, 0, 0, 0),
            Comparer<TrajectorySample>.Create((a, b) => a.Time.CompareTo(b.Time)));
        if (hi >= 0) return _samples[hi];
        hi = ~hi;
        var a = _samples[hi - 1];
        var b = _samples[hi];
        var t = (float)((time - a.Time) / (b.Time - a.Time));

        Quaternion? orientation = a.Orientation is { } qa && b.Orientation is { } qb
            ? Quaternion.Slerp(qa, qb, t)
            : null;

        return new TrajectorySample(
            time,
            Vector3.Lerp(a.Position, b.Position, t),
            LerpAngle(a.YawDeg, b.YawDeg, t),
            float.Lerp(a.PitchDeg, b.PitchDeg, t),
            LerpAngle(a.RollDeg, b.RollDeg, t),
            Orientation: orientation);
    }

    /// <summary>Poses at a fixed rate — the per-frame view for analyses and rendering.</summary>
    public IEnumerable<TrajectorySample> Frames(double stepSeconds)
    {
        if (stepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
        for (var t = StartTime; t <= EndTime; t += stepSeconds)
            yield return At(t);
    }

    /// <summary>Total path length in meters.</summary>
    public double Length()
    {
        var total = 0.0;
        for (var i = 1; i < _samples.Length; i++)
            total += Vector3.Distance(_samples[i - 1].Position, _samples[i].Position);
        return total;
    }

    public UtmPoint PositionUtm(TrajectorySample s) => Anchor.ToUtm(s.Position);

    private static float LerpAngle(float a, float b, float t)
    {
        var delta = ((b - a) % 360 + 540) % 360 - 180;
        return a + delta * t;
    }
}
