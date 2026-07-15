using OpenMapUnifier.Geodesy;
using OpenMapUnifier.MapScene.Tabular;

namespace OpenMapUnifier.MapScene.Scene;

/// <summary>
/// Executes a <see cref="SceneDocument"/>: load the map, load the trajectory,
/// build the sensor, compute the requested areas and outputs, write the
/// files. Returns the paths written; progress goes to <c>log</c>.
/// </summary>
public static class SceneRunner
{
    public static async Task<IReadOnlyList<string>> RunAsync(SceneDocument doc,
        string baseDirectory, Action<string>? log = null, CancellationToken ct = default)
    {
        log ??= _ => { };
        var written = new List<string>();
        string Resolve(string p) => Path.GetFullPath(Path.Combine(baseDirectory, p));

        var map = await BuildMapAsync(doc.Map, Resolve, log, ct).ConfigureAwait(false);

        Trajectory? trajectory = null;
        if (doc.Trajectory is { } t)
        {
            var file = Resolve(t.File);
            var mapping = t.Mapping ?? TrajectoryLoader.SuggestMapping(file);
            trajectory = TrajectoryLoader.Load(file, mapping, map.Anchor);
            log(FormattableString.Invariant(
                $"Trajectory: {trajectory.Samples.Count} samples over {trajectory.Duration:F1} s, {trajectory.Length():F0} m path"));
        }

        var sensor = doc.Sensor?.Build();
        if (sensor is not null)
            log($"Sensor: {sensor.Name} ({doc.Sensor!.Type})");

        foreach (var area in doc.Areas)
        {
            var mask = area switch
            {
                { ObjectsTallerThan: { } h } => map.ObjectsTallerThan(h),
                { InClass: { } c } => map.InClass(c),
                _ => throw new InvalidDataException(
                    $"Area '{area.Name}' needs a condition: objectsTallerThan or inClass."),
            };
            if (area.DilateMeters > 0)
                mask = mask.Dilate(area.DilateMeters);
            log(FormattableString.Invariant(
                $"Area '{area.Name}': {mask.AreaSquareMeters():F0} m²"));
            if (area.GeoTiff is { } tiff)
            {
                mask.SaveGeoTiff(Resolve(tiff));
                written.Add(Resolve(tiff));
            }
        }

        var o = doc.Outputs;
        if (o.CoverageGeoTiff is { } coveragePath)
        {
            if (trajectory is null || sensor is null)
                throw new InvalidDataException("The coverage output needs both a trajectory and a sensor.");
            var seen = map.Coverage.SeenGround(trajectory, sensor, o.FrameStepSeconds, o.Quality);
            seen.SaveGeoTiff(Resolve(coveragePath));
            written.Add(Resolve(coveragePath));
            log(FormattableString.Invariant(
                $"Coverage: {seen.AreaSquareMeters():F0} m² seen -> {coveragePath}"));
        }

        if (o.UnityPoints is { } pointsPath)
        {
            if (trajectory is null)
                throw new InvalidDataException("The Unity points output needs a trajectory.");
            var points = new PointSet(map.Anchor);
            if (o.IncludeTrajectory)
                points.AddTrajectory(trajectory, o.PointStepSeconds);
            if (o.IncludeBoresight && sensor is not null)
                points.AddBoresightTrack(trajectory, sensor, map.LineOfSight, o.PointStepSeconds);
            UnityPointsExport.WriteFile(Resolve(pointsPath), points);
            written.Add(Resolve(pointsPath));
            log($"Points: {points.Points.Count} -> {pointsPath}");
        }

        return written;
    }

    private static async Task<Map> BuildMapAsync(MapSpec spec, Func<string, string> resolve,
        Action<string> log, CancellationToken ct)
    {
        Map map;
        if (spec.TerrainFile is { } terrainFile)
        {
            map = Map.FromFiles(resolve(terrainFile),
                spec.SurfaceFile is { } s ? resolve(s) : null, spec.UtmZone);
            log($"Map: {terrainFile} ({map.Terrain.Grid.Width}x{map.Terrain.Grid.Height} cells)");
        }
        else if (spec.State is { } state)
        {
            if (spec.Bbox is not { Length: 4 })
                throw new InvalidDataException("map.bbox must be [minE, minN, maxE, maxN].");
            var region = new BoundingBox(spec.Bbox[0], spec.Bbox[1], spec.Bbox[2], spec.Bbox[3]);
            map = await Map.LoadAsync(state, region, resolve(spec.Cache),
                spec.Resolution, spec.SurfaceModel, ct).ConfigureAwait(false);
            log($"Map: {state} {region} ({map.Terrain.Grid.Width}x{map.Terrain.Grid.Height} cells)");
        }
        else
        {
            throw new InvalidDataException("map needs either terrainFile or state + bbox.");
        }

        if (spec.ClassificationFile is { } classification)
            map.WithClassification(resolve(classification));
        return map;
    }
}
