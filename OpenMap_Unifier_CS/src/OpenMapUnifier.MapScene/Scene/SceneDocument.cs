using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMapUnifier.MapScene.Tabular;

namespace OpenMapUnifier.MapScene.Scene;

/// <summary>
/// A whole MapScene session as one JSON file: which map to load, which
/// trajectory file with which column mapping, which sensor, which areas to
/// compute and which outputs to write. Run it with <see cref="SceneRunner"/>
/// (or `openmap scene file.json`) — the declarative twin of writing the same
/// calls in C#. All file paths are relative to the document's directory.
/// </summary>
public sealed class SceneDocument
{
    public MapSpec Map { get; set; } = new();
    public TrajectorySpec? Trajectory { get; set; }
    public SensorSpec? Sensor { get; set; }
    public List<AreaSpec> Areas { get; set; } = new();
    public OutputSpec Outputs { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SceneDocument Load(string path) => Parse(File.ReadAllText(path));

    public static SceneDocument Parse(string json) =>
        JsonSerializer.Deserialize<SceneDocument>(json, Options)
        ?? throw new InvalidDataException("Scene document is empty.");
}

/// <summary>Where the terrain comes from: local files (offline) or a state
/// download (cached). Exactly one of TerrainFile / State must be set.</summary>
public sealed class MapSpec
{
    public string? TerrainFile { get; set; }
    public string? SurfaceFile { get; set; }
    public int UtmZone { get; set; } = 32;

    public string? State { get; set; }
    /// <summary>minE, minN, maxE, maxN in the state's UTM zone.</summary>
    public double[]? Bbox { get; set; }
    public string Cache { get; set; } = "tilecache";
    public double Resolution { get; set; } = 1.0;
    public bool SurfaceModel { get; set; }

    public string? ClassificationFile { get; set; }
}

public sealed class TrajectorySpec
{
    public string File { get; set; } = "";
    /// <summary>Column mapping; omit to auto-suggest from the headers.</summary>
    public TabularMapping? Mapping { get; set; }
}

/// <summary>One sensor, discriminated by <see cref="Type"/>; only the fields
/// of the chosen shape matter (pyramid: Fov*, cone: HalfAngleDeg, cylinder:
/// RadiusMeters).</summary>
public sealed class SensorSpec
{
    public string Type { get; set; } = "pyramid";
    public string Name { get; set; } = "sensor";
    public double MaxRangeMeters { get; set; } = 5000;
    public double MountYawDeg { get; set; }
    public double MountPitchDeg { get; set; } = -90;
    public double MountRollDeg { get; set; }

    public double FovHorizontalDeg { get; set; } = 60;
    public double FovVerticalDeg { get; set; } = 45;
    public double HalfAngleDeg { get; set; } = 20;
    public double RadiusMeters { get; set; } = 250;

    public SensorModel Build() => Type.ToLowerInvariant() switch
    {
        "pyramid" or "camera" or "frustum" => new PyramidSensor(Name, FovHorizontalDeg,
            FovVerticalDeg, MaxRangeMeters, MountYawDeg, MountPitchDeg, MountRollDeg),
        "cone" or "level" => new ConeSensor(Name, HalfAngleDeg,
            MaxRangeMeters, MountYawDeg, MountPitchDeg, MountRollDeg),
        "cylinder" => new CylinderSensor(Name, RadiusMeters, MaxRangeMeters),
        _ => throw new InvalidDataException(
            $"Unknown sensor type '{Type}'. Use pyramid, cone or cylinder."),
    };
}

/// <summary>A named ground area built from one condition, optionally grown by
/// DilateMeters, written as a GeoTIFF mask when GeoTiff is set.</summary>
public sealed class AreaSpec
{
    public string Name { get; set; } = "area";
    /// <summary>nDSM condition (needs the surface model): objects taller than this.</summary>
    public double? ObjectsTallerThan { get; set; }
    /// <summary>Classification condition (needs ClassificationFile): cells of this class.</summary>
    public int? InClass { get; set; }
    public double DilateMeters { get; set; }
    public string? GeoTiff { get; set; }
}

public sealed class OutputSpec
{
    /// <summary>Coverage mask ("all ground seen by the sensor over the whole
    /// trajectory") as GeoTIFF; needs trajectory + sensor.</summary>
    public string? CoverageGeoTiff { get; set; }
    public double FrameStepSeconds { get; set; } = 1.0;
    public int Quality { get; set; } = 32;

    /// <summary>Render-target points for Unity (see UnityPointsExport).</summary>
    public string? UnityPoints { get; set; }
    public double PointStepSeconds { get; set; } = 1.0;
    public bool IncludeTrajectory { get; set; } = true;
    public bool IncludeBoresight { get; set; } = true;
}
