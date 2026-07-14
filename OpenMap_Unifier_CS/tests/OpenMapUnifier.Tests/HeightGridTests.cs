using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Raster;
using Xunit;

namespace OpenMapUnifier.Tests;

public class HeightGridTests
{
    // 2x2 grid, 1 m pixels, top-left corner at (100, 200):
    //   row 0 (N 199..200): 10 20
    //   row 1 (N 198..199): 30 40
    private static HeightGrid MakeGrid() => new(
        new float[] { 10, 20, 30, 40 }, 2, 2, 100, 200, 1.0);

    [Fact]
    public void Sample_AtPixelCenter_ReturnsExactValue()
    {
        var grid = MakeGrid();
        Assert.Equal(10, grid.Sample(new Utm32Point(100.5, 199.5)));
        Assert.Equal(40, grid.Sample(new Utm32Point(101.5, 198.5)));
    }

    [Fact]
    public void Sample_BetweenCenters_InterpolatesBilinearly()
    {
        var grid = MakeGrid();
        // Exact middle of all four pixel centers -> mean.
        Assert.Equal(25.0, grid.Sample(new Utm32Point(101.0, 199.0))!.Value, 6);
        // Halfway between 10 and 20 along the top row.
        Assert.Equal(15.0, grid.Sample(new Utm32Point(101.0, 199.5))!.Value, 6);
    }

    [Fact]
    public void Sample_OutsideGrid_ReturnsNull()
    {
        var grid = MakeGrid();
        Assert.Null(grid.Sample(new Utm32Point(99.9, 199.5)));
        Assert.Null(grid.Sample(new Utm32Point(101.0, 202.0)));
    }

    [Fact]
    public void Sample_NoDataNeighbor_FallsBackToNearestValidPixel()
    {
        var grid = new HeightGrid(new float[] { 10, -9999, 30, 40 }, 2, 2, 100, 200, 1.0);
        // Interpolation window touches the NoData pixel -> nearest pixel wins.
        Assert.Equal(10, grid.Sample(new Utm32Point(100.6, 199.4)));
        // Nearest pixel itself is NoData -> null.
        Assert.Null(grid.Sample(new Utm32Point(101.5, 199.5)));
    }

    [Fact]
    public void SampleNearest_PicksContainingPixel()
    {
        var grid = MakeGrid();
        Assert.Equal(30, grid.SampleNearest(new Utm32Point(100.1, 198.1)));
    }

    [Fact]
    public void Bounds_MatchOriginAndSize()
    {
        var b = MakeGrid().Bounds;
        Assert.Equal(new BoundingBox(100, 198, 102, 200), b);
    }
}
