using OpenMapUnifier.Core.Geodesy;

namespace OpenMapUnifier.Core.Elevation;

/// <summary>Convenience queries on top of any <see cref="IElevationProvider"/>.</summary>
public static class ElevationExtensions
{
    /// <summary>Elevation at a WGS84/ETRS89 lat/lon position.</summary>
    public static Task<double?> GetElevationAsync(this IElevationProvider provider,
        GeoPoint geo, ICoordinateTransform? transform = null, CancellationToken ct = default)
    {
        transform ??= Etrs89Utm32Transform.Instance;
        return provider.GetElevationAsync(transform.ToUtm32(geo), ct);
    }

    /// <summary>
    /// Terrain profile along the straight line from <paramref name="from"/> to
    /// <paramref name="to"/>, sampled at <paramref name="samples"/> evenly
    /// spaced points (inclusive of both ends).
    /// </summary>
    public static async Task<IReadOnlyList<(Utm32Point Position, double? Elevation)>> GetProfileAsync(
        this IElevationProvider provider, Utm32Point from, Utm32Point to, int samples = 100,
        CancellationToken ct = default)
    {
        if (samples < 2) throw new ArgumentOutOfRangeException(nameof(samples), "Need at least 2 samples.");
        var result = new (Utm32Point, double?)[samples];
        for (var i = 0; i < samples; i++)
        {
            var t = i / (double)(samples - 1);
            var p = new Utm32Point(
                from.Easting + (to.Easting - from.Easting) * t,
                from.Northing + (to.Northing - from.Northing) * t);
            result[i] = (p, await provider.GetElevationAsync(p, ct).ConfigureAwait(false));
        }
        return result;
    }

    /// <summary>
    /// Slope (degrees) and aspect (degrees clockwise from north) at a position,
    /// from central differences at <paramref name="stepMeters"/> spacing.
    /// Returns null when any of the four neighbor samples has no data.
    /// </summary>
    public static async Task<(double SlopeDegrees, double AspectDegrees)?> GetSlopeAspectAsync(
        this IElevationProvider provider, Utm32Point p, double stepMeters = 1.0,
        CancellationToken ct = default)
    {
        var east = await provider.GetElevationAsync(new Utm32Point(p.Easting + stepMeters, p.Northing), ct).ConfigureAwait(false);
        var west = await provider.GetElevationAsync(new Utm32Point(p.Easting - stepMeters, p.Northing), ct).ConfigureAwait(false);
        var north = await provider.GetElevationAsync(new Utm32Point(p.Easting, p.Northing + stepMeters), ct).ConfigureAwait(false);
        var south = await provider.GetElevationAsync(new Utm32Point(p.Easting, p.Northing - stepMeters), ct).ConfigureAwait(false);
        if (east is null || west is null || north is null || south is null) return null;

        var dzdx = (east.Value - west.Value) / (2 * stepMeters);
        var dzdy = (north.Value - south.Value) / (2 * stepMeters);
        var slope = Math.Atan(Math.Sqrt(dzdx * dzdx + dzdy * dzdy)) * 180.0 / Math.PI;
        // Aspect is the downhill direction, so negate the (uphill) gradient.
        var aspect = (Math.Atan2(-dzdx, -dzdy) * 180.0 / Math.PI + 360.0) % 360.0;
        return (slope, aspect);
    }
}
