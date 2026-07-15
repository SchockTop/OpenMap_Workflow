using System.Numerics;
using OpenMapUnifier.Geodesy;

namespace OpenMapUnifier.MapScene;

/// <summary>A named point in the scene (position in local ENU), with optional
/// attitude and a free-form tag for grouping in the consumer.</summary>
public sealed record ScenePoint(
    string Name,
    Vector3 Position,
    float YawDeg = 0,
    float PitchDeg = 0,
    float RollDeg = 0,
    string Tag = "");

/// <summary>
/// A collection of points you set in the scene — render targets, markers,
/// waypoints, analysis outputs. Build by hand or from trajectories/boresight
/// tracks, then export for Unity (<see cref="UnityPointsExport"/>) or any
/// other consumer.
/// </summary>
public sealed class PointSet
{
    private readonly List<ScenePoint> _points = new();

    public SceneAnchor Anchor { get; }
    public IReadOnlyList<ScenePoint> Points => _points;

    public PointSet(SceneAnchor anchor)
    {
        Anchor = anchor;
    }

    public PointSet Add(ScenePoint point)
    {
        _points.Add(point);
        return this;
    }

    /// <summary>Add a point by UTM position and height above sea level.</summary>
    public PointSet Add(string name, UtmPoint position, double z, string tag = "") =>
        Add(new ScenePoint(name, Anchor.ToLocal(position, z), Tag: tag));

    /// <summary>Add a point by lat/lon and height above sea level.</summary>
    public PointSet Add(string name, GeoPoint geo, double z, string tag = "") =>
        Add(new ScenePoint(name, Anchor.FromGeo(geo, z), Tag: tag));

    /// <summary>The trajectory itself as points (one per frame step).</summary>
    public PointSet AddTrajectory(Trajectory trajectory, double stepSeconds, string tag = "trajectory")
    {
        var i = 0;
        foreach (var pose in trajectory.Frames(stepSeconds))
            Add(new ScenePoint($"{tag}_{i++}", pose.Position,
                pose.YawDeg, pose.PitchDeg, pose.RollDeg, tag));
        return this;
    }

    /// <summary>
    /// Where the sensor looks per frame — the boresight ground track.
    /// Frames looking above the horizon or off-map are skipped.
    /// </summary>
    public PointSet AddBoresightTrack(Trajectory trajectory, SensorModel sensor,
        LineOfSight lineOfSight, double stepSeconds, string tag = "boresight")
    {
        var i = 0;
        foreach (var pose in trajectory.Frames(stepSeconds))
        {
            if (lineOfSight.BoresightGroundPoint(sensor, pose) is { } hit)
                Add(new ScenePoint($"{tag}_{i}", hit, Tag: tag));
            i++;
        }
        return this;
    }
}
