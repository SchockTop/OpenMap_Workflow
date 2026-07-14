using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Geometry;
using Xunit;

namespace OpenMapUnifier.Tests;

public class PolygonTests
{
    [Fact]
    public void FromWgs84Wkt_HandlesEwktPrefixAndZValues()
    {
        // Shape the Python Unifier's KML extractor produces, plus Z values as
        // Google Earth emits them.
        var poly = Polygon2D.FromWgs84Wkt(
            "SRID=4326;POLYGON((11.57 48.13 0, 11.58 48.13 0, 11.58 48.14 0, 11.57 48.14 0, 11.57 48.13 0))");
        Assert.Equal(4, poly.Points.Count);
        Assert.True(poly.Bounds.MinEasting is > 691_000 and < 692_000);
    }

    [Fact]
    public void FromKml_ExtractsFirstCoordinatesElement()
    {
        const string kml = """
            <kml xmlns="http://www.opengis.net/kml/2.2"><Document><Placemark>
            <Polygon><outerBoundaryIs><LinearRing><coordinates>
              11.57,48.13,0 11.58,48.13,0 11.58,48.14,0 11.57,48.14,0 11.57,48.13,0
            </coordinates></LinearRing></outerBoundaryIs></Polygon>
            </Placemark></Document></kml>
            """;
        var poly = Polygon2D.FromKml(kml);
        Assert.Equal(4, poly.Points.Count);
    }

    [Fact]
    public void Contains_DetectsInsideAndOutside()
    {
        var poly = new Polygon2D(new[]
        {
            new Utm32Point(0, 0), new Utm32Point(10, 0), new Utm32Point(10, 10), new Utm32Point(0, 10),
        });
        Assert.True(poly.Contains(new Utm32Point(5, 5)));
        Assert.False(poly.Contains(new Utm32Point(15, 5)));
    }

    [Fact]
    public void Intersects_TrueWhenEdgeCrossesBoxWithoutVertexInside()
    {
        // A long thin triangle passing through the box with no vertex inside it
        // and no box corner inside the triangle.
        var poly = new Polygon2D(new[]
        {
            new Utm32Point(-10, 4), new Utm32Point(20, 4.6), new Utm32Point(-10, 5.2),
        });
        Assert.True(poly.Intersects(new BoundingBox(0, 0, 10, 10)));
    }

    [Fact]
    public void Intersects_FalseForDisjointBox()
    {
        var poly = new Polygon2D(new[]
        {
            new Utm32Point(0, 0), new Utm32Point(10, 0), new Utm32Point(5, 10),
        });
        Assert.False(poly.Intersects(new BoundingBox(20, 20, 30, 30)));
    }

    [Fact]
    public void Intersects_TrueWhenBoxFullyInsidePolygon()
    {
        var poly = new Polygon2D(new[]
        {
            new Utm32Point(0, 0), new Utm32Point(100, 0), new Utm32Point(100, 100), new Utm32Point(0, 100),
        });
        Assert.True(poly.Intersects(new BoundingBox(40, 40, 60, 60)));
    }
}
