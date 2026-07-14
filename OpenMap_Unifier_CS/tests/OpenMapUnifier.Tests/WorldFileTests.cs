using OpenMapUnifier.Core.Raster;
using Xunit;

namespace OpenMapUnifier.Tests;

public class WorldFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("omu-tests-").FullName;

    [Theory]
    [InlineData("32672_5424.tif", 672, 5424)]  // DOP with zone prefix
    [InlineData("729_5433.tif", 729, 5433)]    // DGM without prefix
    [InlineData("32689_5333_20_DOM.tif", 689, 5333)] // DOM20 with suffix
    public void ParseTileId_HandlesAllBayernNamings(string name, int east, int north)
    {
        Assert.Equal((east, north), WorldFile.ParseTileId(name));
    }

    [Fact]
    public void ParseTileId_RejectsNonTileNames()
    {
        Assert.Null(WorldFile.ParseTileId("cutout.tif"));
    }

    [Fact]
    public void WriteSidecars_ReferencesUpperLeftPixelCenter()
    {
        var tif = Path.Combine(_dir, "729_5433.tif");
        File.WriteAllBytes(tif, Array.Empty<byte>());

        var (tfw, prj) = WorldFile.WriteSidecarsForBayernTile(tif, 1.0);

        Assert.NotNull(tfw);
        var lines = File.ReadAllLines(tfw!);
        Assert.Equal("1", lines[0]);
        Assert.Equal("-1", lines[3]);
        Assert.Equal("729000.5", lines[4]);  // corner 729000 + half pixel
        Assert.Equal("5433999.5", lines[5]); // corner 5434000 - half pixel
        Assert.Contains("25832", File.ReadAllText(prj!));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
