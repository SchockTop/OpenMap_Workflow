using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpenMapUnifier.Core.Geodesy;

namespace OpenMapUnifier.Core.Geometry;

/// <summary>
/// A simple 2D polygon (exterior ring only) used for tile selection.
/// Mirrors how the Python Unifier uses shapely: project a WGS84 polygon to
/// EPSG:25832, then test which grid tiles intersect it.
/// </summary>
public sealed class Polygon2D
{
    private readonly Utm32Point[] _points;

    public IReadOnlyList<Utm32Point> Points => _points;
    public BoundingBox Bounds { get; }

    public Polygon2D(IEnumerable<Utm32Point> exteriorRing)
    {
        _points = exteriorRing.ToArray();
        if (_points.Length >= 2 && _points[0] == _points[^1])
            _points = _points[..^1];
        if (_points.Length < 3)
            throw new ArgumentException("A polygon needs at least 3 distinct vertices.", nameof(exteriorRing));
        Bounds = BoundingBox.FromPoints(_points);
    }

    /// <summary>
    /// Parse a WKT POLYGON in geographic coordinates (lon lat [z], as produced by
    /// the Python Unifier's KML extractor) and project it to EPSG:25832.
    /// Accepts an optional leading "SRID=4326;" EWKT prefix and Z values.
    /// </summary>
    public static Polygon2D FromWgs84Wkt(string wkt, ICoordinateTransform? transform = null)
    {
        transform ??= Etrs89Utm32Transform.Instance;
        var body = wkt.Contains(';') ? wkt[(wkt.IndexOf(';') + 1)..] : wkt;
        var m = Regex.Match(body, @"POLYGON\s*[Z]?\s*\(\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
        if (!m.Success)
            throw new FormatException("Not a WKT POLYGON: " + wkt[..Math.Min(60, wkt.Length)]);

        var points = new List<Utm32Point>();
        foreach (var pair in m.Groups[1].Value.Split(','))
        {
            var parts = pair.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var lon = double.Parse(parts[0], CultureInfo.InvariantCulture);
            var lat = double.Parse(parts[1], CultureInfo.InvariantCulture);
            points.Add(transform.ToUtm32(new GeoPoint(lat, lon)));
        }
        return new Polygon2D(points);
    }

    /// <summary>
    /// Extract the first &lt;coordinates&gt; block ("lon,lat[,alt] ..." tuples) from
    /// KML content (e.g. a Google Earth polygon) and project it to EPSG:25832.
    /// </summary>
    public static Polygon2D FromKml(string kmlContent, ICoordinateTransform? transform = null)
    {
        transform ??= Etrs89Utm32Transform.Instance;
        var root = XDocument.Parse(kmlContent).Root
                   ?? throw new FormatException("Empty KML document.");
        var coords = root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "coordinates")?.Value
            ?? throw new FormatException("No <coordinates> element found in KML.");

        var points = new List<Utm32Point>();
        foreach (var tuple in coords.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = tuple.Split(',');
            if (parts.Length < 2) continue;
            var lon = double.Parse(parts[0], CultureInfo.InvariantCulture);
            var lat = double.Parse(parts[1], CultureInfo.InvariantCulture);
            points.Add(transform.ToUtm32(new GeoPoint(lat, lon)));
        }
        return new Polygon2D(points);
    }

    public static Polygon2D FromBoundingBox(BoundingBox box) => new(new[]
    {
        new Utm32Point(box.MinEasting, box.MinNorthing),
        new Utm32Point(box.MaxEasting, box.MinNorthing),
        new Utm32Point(box.MaxEasting, box.MaxNorthing),
        new Utm32Point(box.MinEasting, box.MaxNorthing),
    });

    public bool Contains(Utm32Point p)
    {
        // Ray casting; boundary points may land on either side, which is fine
        // for tile selection.
        var inside = false;
        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            var pi = _points[i];
            var pj = _points[j];
            if (pi.Northing > p.Northing != pj.Northing > p.Northing &&
                p.Easting < (pj.Easting - pi.Easting) * (p.Northing - pi.Northing) /
                            (pj.Northing - pi.Northing) + pi.Easting)
                inside = !inside;
        }
        return inside;
    }

    public bool Intersects(BoundingBox box)
    {
        if (!Bounds.Intersects(box)) return false;
        foreach (var p in _points)
            if (box.Contains(p)) return true;

        Span<Utm32Point> corners = stackalloc Utm32Point[]
        {
            new(box.MinEasting, box.MinNorthing),
            new(box.MaxEasting, box.MinNorthing),
            new(box.MaxEasting, box.MaxNorthing),
            new(box.MinEasting, box.MaxNorthing),
        };
        foreach (var c in corners)
            if (Contains(c)) return true;

        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            for (var k = 0; k < 4; k++)
            {
                if (SegmentsIntersect(_points[j], _points[i], corners[k], corners[(k + 1) % 4]))
                    return true;
            }
        }
        return false;
    }

    private static bool SegmentsIntersect(Utm32Point a, Utm32Point b, Utm32Point c, Utm32Point d)
    {
        static double Cross(Utm32Point o, Utm32Point p, Utm32Point q) =>
            (p.Easting - o.Easting) * (q.Northing - o.Northing) -
            (p.Northing - o.Northing) * (q.Easting - o.Easting);

        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}
