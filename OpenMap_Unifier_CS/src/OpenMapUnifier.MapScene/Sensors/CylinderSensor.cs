using System.Numerics;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// Fixed ground radius under the platform, independent of altitude —
/// "everything within R meters of the point below me" (downward radar,
/// proximity footprints). Sampled as parallel vertical rays across the disc,
/// so the footprint radius is exactly <see cref="RadiusMeters"/> at any
/// flight height. <see cref="SensorModel.MaxRangeMeters"/> is the cylinder's
/// depth; mount angles are ignored — the cylinder always points straight down.
/// </summary>
public sealed record CylinderSensor(
    string Name,
    double RadiusMeters = 250,
    double MaxRangeMeters = 5000)
    : SensorModel(Name, MaxRangeMeters)
{
    public override IEnumerable<SensorRay> Rays(TrajectorySample pose, int quality)
    {
        var rings = Math.Max(1, quality / 3);
        var down = new Vector3(0, 0, -1);

        yield return new SensorRay(pose.Position, down);

        for (var ring = 1; ring <= rings; ring++)
        {
            var r = RadiusMeters * ring / rings;
            var count = Math.Max(8, (int)Math.Round(2 * Math.PI * ring * quality / (3.0 * rings)));
            for (var i = 0; i < count; i++)
            {
                var phi = 2 * Math.PI * i / count;
                var origin = pose.Position + new Vector3(
                    (float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi)), 0);
                yield return new SensorRay(origin, down);
            }
        }
    }
}
