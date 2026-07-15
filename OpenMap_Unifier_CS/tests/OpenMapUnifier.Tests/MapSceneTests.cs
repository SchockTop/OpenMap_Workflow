using System.Numerics;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.MapScene;
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
        var sensor = new Sensor("cam", FovHorizontalDeg: 40, FovVerticalDeg: 30,
            MaxRangeMeters: 400, MountPitchDeg: -20);

        var seen = coverage.Footprint(sensor, pose, raysAcross: 60, raysDown: 40);

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
        var sensor = new Sensor("nadir", MountPitchDeg: -90);
        var boresight = sensor.BoresightFor(pose);
        Assert.Equal(-1, boresight.Z, 3);

        // Forward-looking camera on a north-heading body looks north.
        var forward = new Sensor("front", MountPitchDeg: 0);
        var dir = forward.BoresightFor(pose);
        Assert.Equal(1, dir.Y, 3);

        // Heading east turns it east.
        var east = forward.BoresightFor(pose with { YawDeg = 90 });
        Assert.Equal(1, east.X, 3);
    }
}
