using System.Numerics;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.MapScene;
using OpenMapUnifier.MapScene.Scene;
using OpenMapUnifier.MapScene.Tabular;
using OpenMapUnifier.Raster;
using Xunit;

namespace OpenMapUnifier.Tests;

public class MapSceneTests
{
    // Synthetic 200x200 m terrain at 1 m: flat plain at 100 m with a ridge
    // (140 m) running north-south through the middle.
    private static TerrainLayer MakeRidgeTerrain()
    {
        const int size = 200;
        var data = new float[size * size];
        for (var row = 0; row < size; row++)
            for (var col = 0; col < size; col++)
                data[row * size + col] = col is >= 95 and <= 105 ? 140f : 100f;
        return new TerrainLayer(new HeightGrid(data, size, size, 500_000, 5_400_200, 1.0));
    }

    private static SceneAnchor AnchorOf(TerrainLayer terrain) =>
        SceneAnchor.CenterOf(terrain.Bounds);

    [Fact]
    public void Anchor_RoundTripsExactly()
    {
        var anchor = new SceneAnchor(new UtmPoint(691_607.86, 5_334_760.39));
        var local = anchor.ToLocal(new UtmPoint(691_700.00, 5_334_800.00), z: 520);
        Assert.Equal(92.14, local.X, 2);
        Assert.Equal(39.61, local.Y, 2);
        var back = anchor.ToUtm(local);
        Assert.Equal(691_700.00, back.Easting, 2);
        Assert.Equal(5_334_800.00, back.Northing, 2);
    }

    [Fact]
    public void LineOfSight_RidgeBlocksAndPlainDoesNot()
    {
        var terrain = MakeRidgeTerrain();
        var anchor = AnchorOf(terrain);
        var los = new LineOfSight(terrain, anchor);

        // Two observers at 110 m altitude on opposite sides of the 140 m ridge.
        var west = anchor.ToLocal(new UtmPoint(500_020, 5_400_100), z: 110);
        var east = anchor.ToLocal(new UtmPoint(500_180, 5_400_100), z: 110);
        Assert.False(los.CanSee(west, east));

        // Fly above the ridge -> clear.
        var westHigh = west with { Z = 150 };
        var eastHigh = east with { Z = 150 };
        Assert.True(los.CanSee(westHigh, eastHigh));

        // Two points on the same side see each other.
        var westNear = anchor.ToLocal(new UtmPoint(500_060, 5_400_100), z: 110);
        Assert.True(los.CanSee(west, westNear));
    }

    [Fact]
    public void LineOfSight_HitGround_FindsTheRidgeFace()
    {
        var terrain = MakeRidgeTerrain();
        var anchor = AnchorOf(terrain);
        var los = new LineOfSight(terrain, anchor);

        // Look east and slightly down from 120 m: the plain is at 100 m, the
        // ridge at 140 m — the ray must stop at the west face of the ridge.
        var origin = anchor.ToLocal(new UtmPoint(500_050, 5_400_100), z: 120);
        var hit = los.HitGround(origin, new Vector3(1, 0, -0.05f), maxRange: 500);

        Assert.NotNull(hit);
        var utm = anchor.ToUtm(hit!.Value);
        Assert.InRange(utm.Easting, 500_090, 500_100);
    }

    [Fact]
    public void GroundMask_SetAlgebraAndDilate()
    {
        var terrain = MakeRidgeTerrain();
        var ridge = GroundMask.FromCondition(terrain, p => terrain.HeightAt(p) > 120);
        var plain = ridge.Invert();

        Assert.Equal(ridge.CellCount() + plain.CellCount(), 200 * 200);
        Assert.Equal(0, ridge.Intersect(plain).CellCount());
        Assert.Equal(200 * 200, ridge.Union(plain).CellCount());

        // "Within 5 m of the ridge": dilation adds ~5 columns per side.
        var near = ridge.Dilate(5).Subtract(ridge);
        Assert.InRange(near.CellCount(), 8 * 200, 10 * 200);
        Assert.True(near.Contains(new UtmPoint(500_092.5, 5_400_100)));
        Assert.False(near.Contains(new UtmPoint(500_050.5, 5_400_100)));
    }

    [Fact]
    public void GroundMask_FromObjectHeight_FindsTreesViaNdsm()
    {
        var terrain = MakeRidgeTerrain();
        // Surface = terrain + 12 m "canopy" in the NE quadrant.
        var surfaceData = new float[200 * 200];
        for (var row = 0; row < 200; row++)
            for (var col = 0; col < 200; col++)
            {
                var ground = terrain.Grid.ValueAt(row, col);
                surfaceData[row * 200 + col] = row < 100 && col >= 100 ? ground + 12 : ground;
            }
        var surface = new TerrainLayer(new HeightGrid(surfaceData, 200, 200, 500_000, 5_400_200, 1.0));

        var trees = GroundMask.FromObjectHeight(terrain, surface, minHeightMeters: 3);
        Assert.Equal(100 * 100, trees.CellCount());
        Assert.True(trees.Contains(new UtmPoint(500_150.5, 5_400_150.5)));
    }

    [Fact]
    public void Coverage_DownwardSensor_SeesPlainButNotBehindRidge()
    {
        var terrain = MakeRidgeTerrain();
        var anchor = AnchorOf(terrain);
        var coverage = new CoverageAnalyzer(terrain, anchor);

        // Hover west of the ridge at 130 m, camera looking straight east and
        // 20 degrees down: the ridge shadows the ground behind it.
        var pose = new TrajectorySample(0,
            anchor.ToLocal(new UtmPoint(500_040, 5_400_100), z: 130), YawDeg: 90, PitchDeg: 0, RollDeg: 0);
        var sensor = new PyramidSensor("cam", FovHorizontalDeg: 40, FovVerticalDeg: 30,
            MaxRangeMeters: 400, MountPitchDeg: -20);

        var seen = coverage.Footprint(sensor, pose, quality: 60);

        Assert.True(seen.CellCount() > 0);
        // Ground shadowed by the ridge (east of it, low) must NOT be seen.
        Assert.False(seen.Contains(new UtmPoint(500_120.5, 5_400_100.5)));
    }

    [Fact]
    public void Trajectory_InterpolatesPoseAndYawWrap()
    {
        var anchor = new SceneAnchor(new UtmPoint(500_000, 5_400_000));
        var trajectory = new Trajectory(anchor, new[]
        {
            new TrajectorySample(0, new Vector3(0, 0, 100), 350, 0, 0),
            new TrajectorySample(10, new Vector3(100, 0, 120), 10, 0, 0),
        });

        var mid = trajectory.At(5);
        Assert.Equal(50, mid.Position.X, 3);
        Assert.Equal(110, mid.Position.Z, 3);
        // 350° -> 10° must pass through 0°, not 180°.
        Assert.Equal(0, ((mid.YawDeg % 360) + 360) % 360, 3);
        // 3D length: sqrt(100² + 20²).
        Assert.Equal(101.98, trajectory.Length(), 0.05);
    }

    [Fact]
    public void TrajectoryLoader_MapsCsvColumnsAndSuggests()
    {
        var dir = Directory.CreateTempSubdirectory("mapscene-");
        try
        {
            var csv = Path.Combine(dir.FullName, "flight.csv");
            File.WriteAllLines(csv, new[]
            {
                "time;easting;northing;alt;heading;speed",
                "0;691000,5;5334000,5;520;90;31,5",
                "10;691100,5;5334000,5;540;90;32,0",
            });

            var suggested = TrajectoryLoader.SuggestMapping(csv);
            Assert.Equal(FieldRole.Time, suggested.Fields["time"]);
            Assert.Equal(FieldRole.X, suggested.Fields["easting"]);
            Assert.Equal(FieldRole.Y, suggested.Fields["northing"]);
            Assert.Equal(FieldRole.Z, suggested.Fields["alt"]);
            Assert.Equal(FieldRole.Yaw, suggested.Fields["heading"]);

            suggested.Fields["speed"] = FieldRole.Extra;
            var trajectory = TrajectoryLoader.LoadCsv(csv, suggested);

            Assert.Equal(2, trajectory.Samples.Count);
            Assert.Equal(10, trajectory.Duration, 6);
            var utm = trajectory.PositionUtm(trajectory.Samples[0]);
            Assert.Equal(691000.5, utm.Easting, 2);
            Assert.Equal(520, trajectory.Samples[0].Position.Z, 2);
            Assert.Equal(90, trajectory.Samples[0].YawDeg, 2);
            Assert.Equal(31.5, trajectory.Samples[0].Extra!["speed"], 3);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void GeoTiffWriter_RoundTripsThroughReader()
    {
        var dir = Directory.CreateTempSubdirectory("mapscene-");
        try
        {
            var terrain = MakeRidgeTerrain();
            var path = Path.Combine(dir.FullName, "ridge.tif");
            GeoTiffWriter.Write(path, terrain.Grid);

            var back = GeoTiffReader.Read(path);
            Assert.Equal(terrain.Grid.Data, back.Data);
            Assert.Equal(terrain.Grid.OriginEasting, back.OriginEasting);
            Assert.Equal(terrain.Grid.PixelSize, back.PixelSize);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Sensor_DownLookingCameraPointsDown()
    {
        var pose = new TrajectorySample(0, Vector3.Zero, YawDeg: 0, PitchDeg: 0, RollDeg: 0);
        var sensor = new PyramidSensor("nadir", MountPitchDeg: -90);
        var boresight = sensor.BoresightFor(pose);
        Assert.Equal(-1, boresight.Z, 3);

        // Forward-looking camera on a north-heading body looks north.
        var forward = new PyramidSensor("front", MountPitchDeg: 0);
        var dir = forward.BoresightFor(pose);
        Assert.Equal(1, dir.Y, 3);

        // Heading east turns it east.
        var east = forward.BoresightFor(pose with { YawDeg = 90 });
        Assert.Equal(1, east.X, 3);
    }

    [Fact]
    public void SensorModel_EulerRoundTripsThroughQuaternion()
    {
        foreach (var (yaw, pitch, roll) in new[]
                 { (0f, 0f, 0f), (90f, 0f, 0f), (45f, -30f, 10f), (200f, 15f, -25f) })
        {
            var (y, p, r) = SensorModel.ToEuler(SensorModel.BodyRotation(yaw, pitch, roll));
            Assert.Equal(yaw, ((y % 360) + 360) % 360, 3);
            Assert.Equal(pitch, p, 3);
            Assert.Equal(roll, r, 3);
        }
    }

    [Fact]
    public void Trajectory_SlerpsQuaternionAttitude()
    {
        var anchor = new SceneAnchor(new UtmPoint(500_000, 5_400_000));
        var trajectory = new Trajectory(anchor, new[]
        {
            new TrajectorySample(0, new Vector3(0, 0, 100), 0, 0, 0,
                Orientation: SensorModel.BodyRotation(0, 0, 0)),
            new TrajectorySample(10, new Vector3(100, 0, 100), 90, 0, 0,
                Orientation: SensorModel.BodyRotation(90, 0, 0)),
        });

        var mid = trajectory.At(5);
        Assert.NotNull(mid.Orientation);
        var (yaw, pitch, roll) = SensorModel.ToEuler(mid.BodyOrientation);
        Assert.Equal(45, yaw, 3);
        Assert.Equal(0, pitch, 3);
        Assert.Equal(0, roll, 3);
    }

    [Fact]
    public void TrajectoryLoader_QuaternionColumnsWinOverEuler()
    {
        var dir = Directory.CreateTempSubdirectory("mapscene-");
        try
        {
            var csv = Path.Combine(dir.FullName, "quat.csv");
            // First row's quaternion = BodyRotation(90, 0, 0): a 90° right turn.
            File.WriteAllLines(csv, new[]
            {
                "time,x,y,z,yaw,qx,qy,qz,qw",
                "0,691000,5334000,520,7,0,0,-0.70710678,0.70710678",
                "10,691100,5334000,540,7,0,0,0,1",
            });

            var mapping = TrajectoryLoader.SuggestMapping(csv);
            Assert.Equal(FieldRole.QuatX, mapping.Fields["qx"]);
            Assert.Equal(FieldRole.QuatW, mapping.Fields["qw"]);

            var trajectory = TrajectoryLoader.LoadCsv(csv, mapping);
            Assert.NotNull(trajectory.Samples[0].Orientation);
            // Euler derived from the quaternion, not the bogus yaw column.
            Assert.Equal(90, trajectory.Samples[0].YawDeg, 3);
            Assert.Equal(0, trajectory.Samples[1].YawDeg, 3);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ConeSensor_FootprintIsDiscOfExpectedRadius()
    {
        var terrain = MakeRidgeTerrain();
        var anchor = AnchorOf(terrain);
        var coverage = new CoverageAnalyzer(terrain, anchor);

        // 30 m above the flat plain, looking straight down with 20° half-angle:
        // ground radius = 30 * tan(20°) ≈ 10.9 m.
        var center = new UtmPoint(500_040, 5_400_100);
        var pose = new TrajectorySample(0, anchor.ToLocal(center, z: 130), 0, 0, 0);
        var sensor = new ConeSensor("spot", HalfAngleDeg: 20, MaxRangeMeters: 200);

        var seen = coverage.Footprint(sensor, pose, quality: 90);

        Assert.True(seen.Contains(new UtmPoint(500_040.5, 5_400_100.5)));
        Assert.InRange(MaxDistanceFrom(seen, center), 9.0, 11.8);
    }

    [Fact]
    public void CylinderSensor_GroundRadiusIndependentOfAltitude()
    {
        var terrain = MakeRidgeTerrain();
        var anchor = AnchorOf(terrain);
        var coverage = new CoverageAnalyzer(terrain, anchor);
        var sensor = new CylinderSensor("radar", RadiusMeters: 15);
        var center = new UtmPoint(500_040, 5_400_100);

        foreach (var altitude in new[] { 130.0, 180.0 })
        {
            var pose = new TrajectorySample(0, anchor.ToLocal(center, z: altitude), 0, 0, 0);
            var seen = coverage.Footprint(sensor, pose, quality: 90);
            Assert.True(seen.Contains(new UtmPoint(500_040.5, 5_400_100.5)));
            Assert.InRange(MaxDistanceFrom(seen, center), 13.0, 16.5);
        }
    }

    private static double MaxDistanceFrom(GroundMask mask, UtmPoint center)
    {
        var max = 0.0;
        for (var row = 0; row < mask.Height; row++)
            for (var col = 0; col < mask.Width; col++)
                if (mask[row, col])
                    max = Math.Max(max, mask.CellCenter(row, col).DistanceTo(center));
        return max;
    }

    [Fact]
    public void UnityPointsExport_CarriesAnchorAndUnityAxes()
    {
        var anchor = new SceneAnchor(new UtmPoint(691_000, 5_334_000));
        var points = new PointSet(anchor)
            .Add(new ScenePoint("target", new Vector3(10, 20, 30),
                YawDeg: 90, PitchDeg: -15, RollDeg: 5, Tag: "render"));

        using var doc = System.Text.Json.JsonDocument.Parse(UnityPointsExport.Write(points));
        var root = doc.RootElement;

        var a = root.GetProperty("anchor");
        Assert.Equal(691_000, a.GetProperty("utmEasting").GetDouble(), 3);
        Assert.Equal(25832, a.GetProperty("epsg").GetInt32());
        Assert.InRange(a.GetProperty("latitude").GetDouble(), 47, 49);

        var p = root.GetProperty("points")[0];
        Assert.Equal("target", p.GetProperty("name").GetString());
        // ENU (x east, y north, z up) → Unity left-handed Y-up: (east, up, north).
        Assert.Equal(10, p.GetProperty("unityX").GetDouble(), 3);
        Assert.Equal(30, p.GetProperty("unityY").GetDouble(), 3);
        Assert.Equal(20, p.GetProperty("unityZ").GetDouble(), 3);
        Assert.Equal(15, p.GetProperty("unityEulerX").GetDouble(), 3);
        Assert.Equal(90, p.GetProperty("unityEulerY").GetDouble(), 3);
        Assert.Equal(-5, p.GetProperty("unityEulerZ").GetDouble(), 3);
    }

    [Fact]
    public void PointSet_BoresightTrackFollowsTheGround()
    {
        var terrain = MakeRidgeTerrain();
        var anchor = AnchorOf(terrain);
        var los = new LineOfSight(terrain, anchor);
        var trajectory = new Trajectory(anchor, new[]
        {
            new TrajectorySample(0, anchor.ToLocal(new UtmPoint(500_020, 5_400_100), z: 160), 0, 0, 0),
            new TrajectorySample(10, anchor.ToLocal(new UtmPoint(500_060, 5_400_100), z: 160), 0, 0, 0),
        });
        var nadir = new PyramidSensor("nadir");

        var points = new PointSet(anchor)
            .AddBoresightTrack(trajectory, nadir, los, stepSeconds: 5);

        Assert.Equal(3, points.Points.Count);
        // Straight-down looks hit the plain at 100 m under each pose.
        foreach (var p in points.Points)
            Assert.Equal(100, p.Position.Z, 0.5);
    }

    [Fact]
    public void PngWriter_ProducesValidGrayscalePng()
    {
        var dir = Directory.CreateTempSubdirectory("mapscene-");
        try
        {
            var pixels = new byte[] { 0, 64, 128, 255, 10, 20 };
            var path = Path.Combine(dir.FullName, "mask.png");
            PngWriter.WriteGrayscale(path, pixels, 3, 2);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);

            // IHDR: length 13 at offset 8, then type, then width/height big-endian.
            Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
            Assert.Equal(3, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);
            Assert.Equal(2, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
            Assert.Equal(8, bytes[24]);  // bit depth
            Assert.Equal(0, bytes[25]);  // grayscale

            // Inflate the IDAT payload and check filter bytes + pixel rows.
            // Signature(8) + IHDR chunk(25) -> IDAT length at 33, data at 41.
            Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(bytes, 37, 4));
            var idatLength = (bytes[33] << 24) | (bytes[34] << 16) | (bytes[35] << 8) | bytes[36];
            using var z = new System.IO.Compression.ZLibStream(
                new MemoryStream(bytes, 41, idatLength),
                System.IO.Compression.CompressionMode.Decompress);
            using var raw = new MemoryStream();
            z.CopyTo(raw);
            Assert.Equal(new byte[] { 0, 0, 64, 128, 0, 255, 10, 20 }, raw.ToArray());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnitySceneExport_WritesLoadableBundle()
    {
        var dir = Directory.CreateTempSubdirectory("mapscene-");
        try
        {
            var terrainPath = Path.Combine(dir.FullName, "terrain.tif");
            GeoTiffWriter.Write(terrainPath, MakeRidgeTerrain().Grid);
            var map = Map.FromFiles(terrainPath);
            var trajectory = new Trajectory(map.Anchor, new[]
            {
                new TrajectorySample(0, new Vector3(-80, 0, 160), 90, 0, 0),
                new TrajectorySample(10, new Vector3(-40, 0, 160), 90, 0, 0),
            });
            var sensor = new ConeSensor("spot", HalfAngleDeg: 25);
            var ridge = GroundMask.FromCondition(map.Terrain, p => map.Terrain.HeightAt(p) > 120);

            var bundle = Path.Combine(dir.FullName, "bundle");
            var files = UnitySceneExport.Write(bundle, map, trajectory, sensor,
                new PointSet(map.Anchor).AddTrajectory(trajectory, 5),
                new[] { ("ridge", ridge) });

            Assert.Equal(5, files.Count);
            Assert.Equal(200 * 200 * 4,
                new FileInfo(Path.Combine(bundle, "terrain_heights.r32")).Length);

            using var manifest = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(bundle, "manifest.json")));
            var root = manifest.RootElement;
            var terrain = root.GetProperty("terrain");
            Assert.Equal(200, terrain.GetProperty("width").GetInt32());
            Assert.Equal(100, terrain.GetProperty("minHeight").GetDouble(), 2);
            Assert.Equal(140, terrain.GetProperty("maxHeight").GetDouble(), 2);
            // Anchor at grid center: top-left cell center is 99.5 m west/north.
            Assert.Equal(-99.5, terrain.GetProperty("originX").GetDouble(), 2);
            Assert.Equal(99.5, terrain.GetProperty("originZTop").GetDouble(), 2);
            Assert.Equal("cone", root.GetProperty("sensor").GetProperty("type").GetString());
            Assert.Equal(25, root.GetProperty("sensor").GetProperty("halfAngleDeg").GetDouble(), 2);
            Assert.Equal("overlay_ridge.png",
                root.GetProperty("overlays")[0].GetProperty("file").GetString());

            using var traj = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(bundle, "trajectory.json")));
            var sample = traj.RootElement.GetProperty("samples")[0];
            Assert.Equal(-80, sample.GetProperty("x").GetDouble(), 3);
            Assert.False(sample.GetProperty("hasQuat").GetBoolean());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SceneDocument_RunsEndToEndFromFiles()
    {
        var dir = Directory.CreateTempSubdirectory("mapscene-");
        try
        {
            GeoTiffWriter.Write(Path.Combine(dir.FullName, "terrain.tif"), MakeRidgeTerrain().Grid);
            File.WriteAllLines(Path.Combine(dir.FullName, "flight.csv"), new[]
            {
                "time,easting,northing,alt,heading",
                "0,500020,5400100,160,90",
                "10,500060,5400100,160,90",
            });
            File.WriteAllText(Path.Combine(dir.FullName, "scene.json"), """
                {
                  "map": { "terrainFile": "terrain.tif" },
                  "trajectory": { "file": "flight.csv" },
                  "sensor": { "type": "cone", "name": "spot", "halfAngleDeg": 25, "maxRangeMeters": 300 },
                  "outputs": {
                    "coverageGeoTiff": "coverage.tif",
                    "unityPoints": "points.json",
                    "pointStepSeconds": 5,
                    "unityScene": "bundle"
                  }
                }
                """);

            var doc = SceneDocument.Load(Path.Combine(dir.FullName, "scene.json"));
            var messages = new List<string>();
            var written = await SceneRunner.RunAsync(doc, dir.FullName, messages.Add);

            // coverage.tif + points.json + 5 bundle files (heights, coverage
            // overlay, trajectory, points, manifest).
            Assert.Equal(7, written.Count);
            using var bundleManifest = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(dir.FullName, "bundle", "manifest.json")));
            Assert.Equal("coverage", bundleManifest.RootElement
                .GetProperty("overlays")[0].GetProperty("name").GetString());
            Assert.Equal("cone", bundleManifest.RootElement
                .GetProperty("sensor").GetProperty("type").GetString());
            var coverage = GeoTiffReader.Read(Path.Combine(dir.FullName, "coverage.tif"));
            Assert.Contains(coverage.Data, v => v == 1f);

            using var points = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(dir.FullName, "points.json")));
            // 3 trajectory points + 3 boresight hits at 5 s steps over 10 s.
            Assert.Equal(6, points.RootElement.GetProperty("points").GetArrayLength());
            Assert.Contains(messages, m => m.StartsWith("Coverage:"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
