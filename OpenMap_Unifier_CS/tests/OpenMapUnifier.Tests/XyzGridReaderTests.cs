using System.Text;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Raster;
using Xunit;

namespace OpenMapUnifier.Tests;

public class XyzGridReaderTests
{
    [Fact]
    public void Read_BuildsGridFromPixelCenters()
    {
        // 3x2 grid, 5 m spacing, centers starting at (729002.5, 5433002.5) —
        // the DGM5 layout.
        var xyz = """
            729002.5 5433007.5 100.0
            729007.5 5433007.5 101.0
            729012.5 5433007.5 102.0
            729002.5 5433002.5 103.0
            729007.5 5433002.5 104.0
            729012.5 5433002.5 105.0
            """;
        var grid = XyzGridReader.Read(new MemoryStream(Encoding.ASCII.GetBytes(xyz)));

        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.Equal(5.0, grid.PixelSize);
        Assert.Equal(729_000, grid.OriginEasting);
        Assert.Equal(5_433_010, grid.OriginNorthing);
        // Row 0 is the northern row.
        Assert.Equal(100f, grid.ValueAt(0, 0));
        Assert.Equal(105f, grid.ValueAt(1, 2));
        Assert.Equal(101.0, grid.Sample(new UtmPoint(729_007.5, 5_433_007.5)));
    }

    [Fact]
    public void Read_MissingPointsBecomeNoData()
    {
        var xyz = """
            0.5 0.5 1.0
            1.5 0.5 2.0
            0.5 1.5 3.0
            """;
        var grid = XyzGridReader.Read(new MemoryStream(Encoding.ASCII.GetBytes(xyz)));
        Assert.Equal(-9999f, grid.ValueAt(0, 1));
    }
}
