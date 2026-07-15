using System.Buffers.Binary;

namespace OpenMapUnifier.Raster;

/// <summary>
/// Minimal GeoTIFF writer: single band float32, uncompressed strips,
/// ModelPixelScale + ModelTiepoint georeferencing (EPSG:25832 semantics) and
/// the GDAL NoData tag — enough for QGIS/GDAL/Blender-GIS to load analysis
/// outputs (masks, merged terrain) with correct placement.
/// </summary>
public static class GeoTiffWriter
{
    public static void Write(string path, HeightGrid grid)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        var width = grid.Width;
        var height = grid.Height;
        var noData = System.Text.Encoding.ASCII.GetBytes(
            grid.NoDataValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0");

        const int entries = 13;
        var ifdSize = 2 + entries * 12 + 4;
        var pos = 8 + ifdSize;
        var scalePos = pos; pos += 3 * 8;
        var tiePos = pos; pos += 6 * 8;
        var noDataPos = pos; pos += noData.Length;
        var dataPos = pos;

        writer.Write((byte)'I'); writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write(8u);

        writer.Write((ushort)entries);
        void Entry(ushort tag, ushort type, uint count, uint value)
        {
            writer.Write(tag); writer.Write(type); writer.Write(count); writer.Write(value);
        }
        Entry(256, 3, 1, (uint)width);            // ImageWidth
        Entry(257, 3, 1, (uint)height);           // ImageLength
        Entry(258, 3, 1, 32);                     // BitsPerSample
        Entry(259, 3, 1, 1);                      // Compression: none
        Entry(262, 3, 1, 1);                      // Photometric: BlackIsZero
        Entry(273, 4, 1, (uint)dataPos);          // StripOffsets (single strip)
        Entry(277, 3, 1, 1);                      // SamplesPerPixel
        Entry(278, 3, 1, (uint)height);           // RowsPerStrip
        Entry(279, 4, 1, (uint)(width * height * 4)); // StripByteCounts
        Entry(339, 3, 1, 3);                      // SampleFormat: float
        Entry(33550, 12, 3, (uint)scalePos);      // ModelPixelScale
        Entry(33922, 12, 6, (uint)tiePos);        // ModelTiepoint
        Entry(42113, 2, (uint)noData.Length, (uint)noDataPos); // GDAL NoData
        writer.Write(0u);                         // next IFD

        writer.Write(grid.PixelSize); writer.Write(grid.PixelSize); writer.Write(0.0);
        writer.Write(0.0); writer.Write(0.0); writer.Write(0.0);
        writer.Write(grid.OriginEasting); writer.Write(grid.OriginNorthing); writer.Write(0.0);
        writer.Write(noData);

        var buffer = new byte[4];
        foreach (var value in grid.Data)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, BitConverter.SingleToUInt32Bits(value));
            writer.Write(buffer);
        }
    }
}
