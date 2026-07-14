namespace OpenMapUnifier.Core.Geodesy;

/// <summary>An axis-aligned rectangle in EPSG:25832, meters.</summary>
public readonly record struct BoundingBox(double MinEasting, double MinNorthing, double MaxEasting, double MaxNorthing)
{
    public double Width => MaxEasting - MinEasting;
    public double Height => MaxNorthing - MinNorthing;
    public UtmPoint Center => new((MinEasting + MaxEasting) / 2.0, (MinNorthing + MaxNorthing) / 2.0);

    public bool Contains(UtmPoint p) =>
        p.Easting >= MinEasting && p.Easting <= MaxEasting &&
        p.Northing >= MinNorthing && p.Northing <= MaxNorthing;

    public bool Intersects(BoundingBox other) =>
        MinEasting <= other.MaxEasting && MaxEasting >= other.MinEasting &&
        MinNorthing <= other.MaxNorthing && MaxNorthing >= other.MinNorthing;

    /// <summary>A square box of <paramref name="radiusMeters"/> around a center point.</summary>
    public static BoundingBox Around(UtmPoint center, double radiusMeters) =>
        new(center.Easting - radiusMeters, center.Northing - radiusMeters,
            center.Easting + radiusMeters, center.Northing + radiusMeters);

    public static BoundingBox FromPoints(IEnumerable<UtmPoint> points)
    {
        double minE = double.PositiveInfinity, minN = double.PositiveInfinity;
        double maxE = double.NegativeInfinity, maxN = double.NegativeInfinity;
        var any = false;
        foreach (var p in points)
        {
            any = true;
            minE = Math.Min(minE, p.Easting);
            minN = Math.Min(minN, p.Northing);
            maxE = Math.Max(maxE, p.Easting);
            maxN = Math.Max(maxN, p.Northing);
        }
        if (!any) throw new ArgumentException("At least one point is required.", nameof(points));
        return new BoundingBox(minE, minN, maxE, maxN);
    }

    public override string ToString() =>
        FormattableString.Invariant($"[{MinEasting:F0},{MinNorthing:F0} .. {MaxEasting:F0},{MaxNorthing:F0}]");
}
