namespace OpenMapUnifier.Geodesy;

/// <summary>A geographic position in WGS84 / ETRS89 (degrees).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public override string ToString() =>
        FormattableString.Invariant($"lat={Latitude:F7} lon={Longitude:F7}");
}
