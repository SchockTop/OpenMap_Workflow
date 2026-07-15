namespace OpenMapUnifier.MapScene.Tabular;

/// <summary>What a source column/field means for the trajectory.</summary>
public enum FieldRole
{
    Ignore,
    Time,
    /// <summary>Planar X / easting (CRS from <see cref="TabularMapping.PositionEpsg"/>).</summary>
    X,
    /// <summary>Planar Y / northing.</summary>
    Y,
    /// <summary>Height above sea level, meters.</summary>
    Z,
    Latitude,
    Longitude,
    /// <summary>Heading, clockwise from north.</summary>
    Yaw,
    Pitch,
    Roll,
    /// <summary>Attitude quaternion components (body→ENU, see docs). When all
    /// four are mapped they win over Euler columns.</summary>
    QuatX,
    QuatY,
    QuatZ,
    QuatW,
    /// <summary>Any numeric column worth keeping — lands in TrajectorySample.Extra.</summary>
    Extra,
}

/// <summary>
/// Declares how a CSV/JSON file maps onto trajectory samples: which column or
/// field plays which role, what CRS positions are in, and the units. This is
/// exactly the "I select what row/field is what data point" step — build it in
/// code, edit it as JSON (it serializes cleanly), or start from
/// <see cref="TrajectoryLoader.SuggestMapping"/> and adjust.
/// </summary>
public sealed class TabularMapping
{
    /// <summary>Column name (CSV header / JSON field) → role.</summary>
    public Dictionary<string, FieldRole> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EPSG of X/Y columns (ignored for lat/lon roles). Default 25832.</summary>
    public int PositionEpsg { get; set; } = 25832;

    /// <summary>Divide raw time values by this to get seconds (1000 for ms).</summary>
    public double TimeDivisor { get; set; } = 1.0;

    /// <summary>Multiply raw angles by this to get degrees (180/π for radians).</summary>
    public double AngleToDegrees { get; set; } = 1.0;

    /// <summary>Sample index becomes the timeline when no Time column exists;
    /// this is the assumed spacing between rows in seconds.</summary>
    public double FallbackSampleInterval { get; set; } = 1.0;

    public string? ColumnFor(FieldRole role) =>
        Fields.FirstOrDefault(kv => kv.Value == role).Key;

    public void Validate()
    {
        var hasXy = ColumnFor(FieldRole.X) is not null && ColumnFor(FieldRole.Y) is not null;
        var hasLatLon = ColumnFor(FieldRole.Latitude) is not null && ColumnFor(FieldRole.Longitude) is not null;
        if (!hasXy && !hasLatLon)
            throw new InvalidOperationException(
                "Mapping needs a position: either X+Y (with PositionEpsg) or Latitude+Longitude.");
    }
}
