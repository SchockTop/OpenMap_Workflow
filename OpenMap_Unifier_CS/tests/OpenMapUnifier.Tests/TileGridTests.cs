using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Geometry;
using OpenMapUnifier.Grid;
using Xunit;

namespace OpenMapUnifier.Tests;

public class TileGridTests
{
    [Fact]
    public void TileFor_SnapsToKilometerGrid()
    {
        var tile = TileGrid.TileFor(new UtmPoint(729_500.5, 5_433_999.9));
        Assert.Equal(new TileId(729, 5433), tile);
    }

    [Fact]
    public void TileFor_2kmGrid_SnapsToEvenKilometers()
    {
        // Marienplatz (~691.6 km E, 5334.8 km N) must land on LoD2 tile 690_5334,
        // the case documented in the Python catalog.
        var tile = TileGrid.TileFor(new UtmPoint(691_607.86, 5_334_760.39), gridKm: 2);
        Assert.Equal(new TileId(690, 5334, 2), tile);
    }

    [Fact]
    public void TilesFor_BoundingBox_CoversAllIntersectedCells()
    {
        var box = new BoundingBox(691_200, 5_334_100, 693_800, 5_335_900);
        var tiles = TileGrid.TilesFor(box).ToList();
        Assert.Equal(6, tiles.Count); // 3 columns x 2 rows
        Assert.Contains(new TileId(691, 5334), tiles);
        Assert.Contains(new TileId(693, 5335), tiles);
    }

    [Fact]
    public void TilesFor_ExactTileBounds_YieldsExactlyThatTile()
    {
        var tiles = TileGrid.TilesFor(new BoundingBox(729_000, 5_433_000, 730_000, 5_434_000)).ToList();
        Assert.Single(tiles);
        Assert.Equal(new TileId(729, 5433), tiles[0]);
    }

    [Fact]
    public void TilesFor_Polygon_ExcludesNonIntersectedCorner()
    {
        // A triangle covering the lower-left half of a 2x2 km square: the
        // upper-right tile must not be selected.
        var poly = new Polygon2D(new[]
        {
            new UtmPoint(700_000, 5_400_000),
            new UtmPoint(702_000, 5_400_000),
            new UtmPoint(700_000, 5_402_000),
        });
        var tiles = TileGrid.TilesFor(poly).ToList();
        Assert.Equal(3, tiles.Count);
        Assert.DoesNotContain(new TileId(701, 5401), tiles);
    }

    [Fact]
    public void TileBounds_AreKilometerAligned()
    {
        var tile = new TileId(690, 5334, 2);
        Assert.Equal(690_000, tile.MinEasting);
        Assert.Equal(692_000, tile.MaxEasting);
        Assert.Equal(5_336_000, tile.MaxNorthing);
        Assert.Equal("690_5334", tile.Key);
    }
}
