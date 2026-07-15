using System.Numerics;
using OpenMapUnifier.Geodesy;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// Terrain intersection and inter-visibility by ray marching the height grid.
/// Flat-earth assumption: below ~5 km ranges the curvature drop (r²/2R ≈ 2 m
/// at 5 km) is smaller than DGM1's own vegetation/edge noise; for longer
/// sensor ranges add a curvature term before trusting results.
/// </summary>
public sealed class LineOfSight
{
    private readonly TerrainLayer _terrain;
    private readonly SceneAnchor _anchor;
    private readonly double _step;
    private readonly double _ceiling;

    /// <param name="stepMeters">March step; default 0.75x the terrain cell size.</param>
    public LineOfSight(TerrainLayer terrain, SceneAnchor anchor, double? stepMeters = null)
    {
        _terrain = terrain;
        _anchor = anchor;
        _step = stepMeters ?? terrain.Resolution * 0.75;
        _ceiling = terrain.MaxHeight() + 1.0;
    }

    /// <summary>
    /// First point where a ray from <paramref name="origin"/> (scene-local)
    /// along <paramref name="direction"/> meets the ground. Null when the ray
    /// leaves the terrain or exceeds <paramref name="maxRange"/> first.
    /// </summary>
    public Vector3? HitGround(Vector3 origin, Vector3 direction, double maxRange = 10_000)
    {
        var dir = Vector3.Normalize(direction);
        var stepVec = dir * (float)_step;
        var position = origin;

        for (double travelled = 0; travelled <= maxRange; travelled += _step)
        {
            position += stepVec;

            // Above every terrain point and climbing — nothing left to hit.
            if (position.Z > _ceiling && dir.Z >= 0) return null;

            var ground = _terrain.HeightAt(_anchor.ToUtm(position));
            if (ground is null) return null; // left coverage
            if (position.Z <= ground.Value)
            {
                // Refine: back up half a step for a better contact estimate.
                var refined = position - stepVec * 0.5f;
                var g = _terrain.HeightAt(_anchor.ToUtm(refined)) ?? ground.Value;
                return refined.Z <= g
                    ? new Vector3(refined.X, refined.Y, (float)g)
                    : new Vector3(position.X, position.Y, (float)ground.Value);
            }
        }
        return null;
    }

    /// <summary>True when the straight line between two scene points clears the terrain.</summary>
    public bool CanSee(Vector3 from, Vector3 to)
    {
        var delta = to - from;
        var distance = delta.Length();
        if (distance < 1e-6) return true;
        var dir = delta / distance;
        var stepVec = dir * (float)_step;
        var position = from;

        for (double travelled = _step; travelled < distance - _step; travelled += _step)
        {
            position += stepVec;
            var ground = _terrain.HeightAt(_anchor.ToUtm(position));
            if (ground is { } g && position.Z <= g)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Ground point the sensor boresight looks at for a pose — "where is it
    /// looking this frame". Null when looking above the horizon / off-map.
    /// </summary>
    public Vector3? BoresightGroundPoint(SensorModel sensor, TrajectorySample pose) =>
        HitGround(pose.Position, sensor.BoresightFor(pose), sensor.MaxRangeMeters);
}
