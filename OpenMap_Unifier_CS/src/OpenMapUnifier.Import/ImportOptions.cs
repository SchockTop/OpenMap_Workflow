using OpenMapUnifier.Geodesy;

namespace OpenMapUnifier.Import;

/// <summary>
/// Tuning for <see cref="ChaoticJsonImporter"/>. The defaults handle the
/// common German-geodata mess; every knob exists because real-world files
/// violate some assumption eventually.
/// </summary>
public sealed class ImportOptions
{
    public static readonly ImportOptions Default = new();

    /// <summary>
    /// When set, planar pairs (named x/y-style fields, bare arrays, numeric
    /// strings) are interpreted as THIS CRS instead of auto-detected — use it
    /// when you know your file ("my exporter always writes Gauß-Krüger 4":
    /// AssumeEpsg = 31468). Fields explicitly named lat/lon are still treated
    /// as geographic degrees, and values that are numerically impossible for
    /// the assumed CRS fall back to auto-detection.
    /// </summary>
    public int? AssumeEpsg { get; set; }

    /// <summary>
    /// Minimum confidence for UNNAMED finds (bare arrays, strings). Below it,
    /// the candidate is dropped — otherwise every [1, 2] in the document would
    /// masquerade as a coordinate. Default 0.6.
    /// </summary>
    public double MinConfidence { get; set; } = 0.6;

    /// <summary>
    /// Minimum confidence for NAMED planar pairs (x/y, easting/northing...).
    /// Lower than <see cref="MinConfidence"/> because the field names already
    /// vouch for "this is a coordinate". Default 0.5.
    /// </summary>
    public double MinNamedConfidence { get; set; } = 0.5;

    /// <summary>
    /// Where results are expected to land; drives detection scoring. Default
    /// Germany (with margin). Widen it when your data isn't German.
    /// </summary>
    public DetectionRegion Region { get; set; } = DetectionRegion.Germany;

    /// <summary>Extra field names treated as latitude (merged with built-ins).</summary>
    public List<string> LatitudeKeys { get; } = new();

    /// <summary>Extra field names treated as longitude.</summary>
    public List<string> LongitudeKeys { get; } = new();

    /// <summary>Extra field names treated as planar X/easting.</summary>
    public List<string> XKeys { get; } = new();

    /// <summary>Extra field names treated as planar Y/northing.</summary>
    public List<string> YKeys { get; } = new();

    /// <summary>Extra field names treated as elevation.</summary>
    public List<string> ZKeys { get; } = new();
}
