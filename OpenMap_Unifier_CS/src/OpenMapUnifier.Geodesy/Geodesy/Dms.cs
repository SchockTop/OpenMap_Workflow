using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenMapUnifier.Geodesy;

/// <summary>
/// Degrees/minutes/seconds parsing and formatting. Accepts the wild mix that
/// shows up in real-world data: 48°8'14.0"N, N48°08'14", 48d 8m 14s,
/// 48 08.233' N (degrees + decimal minutes), plain decimal degrees with N/S/E/W
/// suffixes, and comma decimal separators.
/// </summary>
public static class Dms
{
    // Unit symbols are optional AFTER each numeric part (never required as
    // separators — plain spaces work too), so "48 08 14 N", "48°08'14\"N",
    // "48d 8m 14s" and "33°55'S" (no seconds before the hemisphere) all parse.
    private static readonly Regex Component = new(
        @"(?<hemi1>[NSEWnsew])?\s*(?<deg>[+-]?\d{1,3}(?:[.,]\d+)?)\s*[°dD:]?\s*(?:(?<min>\d{1,2}(?:[.,]\d+)?)\s*['′mM:]?\s*(?:(?<sec>\d{1,2}(?:[.,]\d+)?)\s*[""″sS]?\s*)?)?(?<hemi2>[NSEWnsew])?",
        RegexOptions.Compiled);

    /// <summary>
    /// Parse one angular component ("48°8'14.0"N", "N 48 08.233", "48.137222").
    /// Returns the signed decimal degrees and which axis the hemisphere letter
    /// implies (lat for N/S, lon for E/W, null when there is none).
    /// </summary>
    public static (double Degrees, char? Axis)? ParseComponent(string text)
    {
        var m = Component.Match(text.Trim());
        if (!m.Success || m.Groups["deg"].Length == 0) return null;

        double Value(string name) => m.Groups[name].Success && m.Groups[name].Length > 0
            ? double.Parse(m.Groups[name].Value.Replace(',', '.'), CultureInfo.InvariantCulture)
            : 0;

        var degrees = Value("deg");
        var sign = degrees < 0 ? -1 : 1;
        degrees = Math.Abs(degrees) + Value("min") / 60.0 + Value("sec") / 3600.0;

        var hemi = (m.Groups["hemi1"].Success ? m.Groups["hemi1"].Value
                   : m.Groups["hemi2"].Success ? m.Groups["hemi2"].Value : "")
            .ToUpperInvariant();
        char? axis = hemi is "N" or "S" ? 'φ' : hemi is "E" or "W" ? 'λ' : null;
        if (hemi is "S" or "W") sign = -1;
        return (sign * degrees, axis);
    }

    /// <summary>
    /// Parse a coordinate PAIR from free text ("48°8'14"N 11°34'32"E",
    /// "48.137222, 11.575556", "N48 8.233 E11 34.533"). Uses hemisphere
    /// letters to fix the axis order; defaults to lat-first otherwise.
    /// Returns null unless both components parse and land in valid ranges.
    /// </summary>
    public static GeoPoint? ParsePair(string text)
    {
        // Split on a separator that is not inside a component: comma followed
        // by space, semicolon, slash, or the boundary before a second
        // hemisphere-prefixed block. Fall back to whitespace halves.
        var parts = Regex.Split(text.Trim(), @"\s*[;/]\s*|,\s+")
            .Where(p => p.Length > 0).ToArray();
        if (parts.Length != 2)
        {
            // "N48... E11..." style: split before the second N/S/E/W letter.
            var m = Regex.Match(text, @"^(?<a>.*?\S)\s+(?<b>[NSEWnsew].*)$");
            if (m.Success)
                parts = new[] { m.Groups["a"].Value, m.Groups["b"].Value };
            else
            {
                var halves = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (halves.Length == 2) parts = halves;
                else return null;
            }
        }

        var first = ParseComponent(parts[0]);
        var second = ParseComponent(parts[1]);
        if (first is not { } a || second is not { } b) return null;

        double lat, lon;
        if (a.Axis == 'λ' || b.Axis == 'φ')
        {
            lon = a.Degrees;
            lat = b.Degrees;
        }
        else
        {
            lat = a.Degrees;
            lon = b.Degrees;
        }
        if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return null;
        return new GeoPoint(lat, lon);
    }

    public static string Format(GeoPoint geo)
    {
        return $"{FormatComponent(geo.Latitude, 'N', 'S')} {FormatComponent(geo.Longitude, 'E', 'W')}";

        static string FormatComponent(double value, char positive, char negative)
        {
            var hemi = value < 0 ? negative : positive;
            value = Math.Abs(value);
            var degrees = (int)value;
            var minutes = (int)((value - degrees) * 60);
            var seconds = (value - degrees - minutes / 60.0) * 3600.0;
            return string.Create(CultureInfo.InvariantCulture,
                $"{degrees}°{minutes:D2}'{seconds:00.00}\"{hemi}");
        }
    }
}
