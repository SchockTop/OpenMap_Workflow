using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenMapUnifier.Core.Raster;

/// <summary>
/// Worldfile (.tfw) + .prj sidecar generation, ported from the Python
/// Unifier's worldfile.py. Some Bayern raw tiles historically shipped without
/// internal GeoTIFF tags; the tile filename encodes everything needed.
/// </summary>
public static class WorldFile
{
    public const string Epsg25832Wkt =
        "PROJCS[\"ETRS89 / UTM zone 32N\"," +
        "GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\"," +
        "SPHEROID[\"GRS 1980\",6378137,298.257222101]]," +
        "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]]," +
        "PROJECTION[\"Transverse_Mercator\"]," +
        "PARAMETER[\"latitude_of_origin\",0]," +
        "PARAMETER[\"central_meridian\",9]," +
        "PARAMETER[\"scale_factor\",0.9996]," +
        "PARAMETER[\"false_easting\",500000]," +
        "PARAMETER[\"false_northing\",0]," +
        "UNIT[\"metre\",1],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH]," +
        "AUTHORITY[\"EPSG\",\"25832\"]]";

    private static readonly Regex TileIdPattern = new(@"^(?:32)?(\d{3})_(\d{4})", RegexOptions.Compiled);

    /// <summary>Parse "(32)672_5424(...)" style tile filenames to (eastKm, northKm).</summary>
    public static (int EastKm, int NorthKm)? ParseTileId(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var m = TileIdPattern.Match(stem);
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Write a .tfw next to <paramref name="rasterPath"/>. Worldfiles reference
    /// the CENTER of the upper-left pixel (ESRI convention), hence the
    /// half-pixel shift from the corner coordinates.
    /// </summary>
    public static string WriteWorldFile(string rasterPath, double pixelSizeMeters,
        double topLeftX, double topLeftY)
    {
        var cx = topLeftX + pixelSizeMeters / 2.0;
        var cy = topLeftY - pixelSizeMeters / 2.0;
        var path = Path.ChangeExtension(rasterPath, ".tfw");
        File.WriteAllText(path, string.Create(CultureInfo.InvariantCulture,
            $"{pixelSizeMeters}\n0.0\n0.0\n-{pixelSizeMeters}\n{cx}\n{cy}\n"));
        return path;
    }

    public static string WritePrj(string rasterPath, string wkt = Epsg25832Wkt)
    {
        var path = Path.ChangeExtension(rasterPath, ".prj");
        File.WriteAllText(path, wkt);
        return path;
    }

    /// <summary>
    /// Generate .tfw + .prj for a Bayern raw tile from its filename, or
    /// (null, null) if the name doesn't match the grid scheme.
    /// </summary>
    public static (string? Tfw, string? Prj) WriteSidecarsForBayernTile(string rasterPath, double pixelSizeMeters)
    {
        var parsed = ParseTileId(rasterPath);
        if (parsed is not var (eastKm, northKm)) return (null, null);
        var tfw = WriteWorldFile(rasterPath, pixelSizeMeters, eastKm * 1000.0, (northKm + 1) * 1000.0);
        var prj = WritePrj(rasterPath);
        return (tfw, prj);
    }
}
