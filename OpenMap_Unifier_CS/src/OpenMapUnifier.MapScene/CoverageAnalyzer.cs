namespace OpenMapUnifier.MapScene;

/// <summary>
/// Sweeps a sensor along a trajectory and accumulates every ground cell the
/// sensor actually sees (terrain occlusion included, because each frustum ray
/// is marched against the terrain) — "all the ground seen by a sensor flying
/// over". Resolution knobs: frame step in seconds and rays per frustum axis;
/// denser is more exact and slower, so start coarse.
/// </summary>
public sealed class CoverageAnalyzer
{
    private readonly LineOfSight _lineOfSight;
    private readonly TerrainLayer _terrain;
    private readonly SceneAnchor _anchor;

    public CoverageAnalyzer(TerrainLayer terrain, SceneAnchor anchor)
    {
        _terrain = terrain;
        _anchor = anchor;
        _lineOfSight = new LineOfSight(terrain, anchor);
    }

    /// <summary>Ground seen by the sensor over the whole trajectory.</summary>
    public GroundMask SeenGround(Trajectory trajectory, SensorModel sensor,
        double frameStepSeconds = 1.0, int quality = 32)
    {
        var mask = new GroundMask(_terrain);
        foreach (var pose in trajectory.Frames(frameStepSeconds))
            MarkFrame(mask, sensor, pose, quality);
        return mask;
    }

    /// <summary>Ground seen in a single frame (footprint with occlusion).</summary>
    public GroundMask Footprint(SensorModel sensor, TrajectorySample pose, int quality = 64)
    {
        var mask = new GroundMask(_terrain);
        MarkFrame(mask, sensor, pose, quality);
        return mask;
    }

    private void MarkFrame(GroundMask mask, SensorModel sensor, TrajectorySample pose, int quality)
    {
        var grid = _terrain.Grid;
        foreach (var ray in sensor.Rays(pose, quality))
        {
            var hit = _lineOfSight.HitGround(ray.Origin, ray.Direction, sensor.MaxRangeMeters);
            if (hit is null) continue;
            var utm = _anchor.ToUtm(hit.Value);
            var col = (int)((utm.Easting - grid.OriginEasting) / grid.PixelSize);
            var row = (int)((grid.OriginNorthing - utm.Northing) / grid.PixelSize);
            if (row >= 0 && row < mask.Height && col >= 0 && col < mask.Width)
                mask[row, col] = true;
        }
    }
}
