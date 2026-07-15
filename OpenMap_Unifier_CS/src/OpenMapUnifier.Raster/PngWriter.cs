using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenMapUnifier.Raster;

/// <summary>
/// Minimal 8-bit grayscale PNG writer — enough for mask/overlay textures that
/// game engines (Unity's Texture2D.LoadImage, browsers, GIS tools) read
/// natively, with zero external packages. Rows are top-first, matching the
/// raster convention used everywhere else in this codebase.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static void WriteGrayscale(string path, byte[] pixels, int width, int height)
    {
        if (pixels.Length != width * height)
            throw new ArgumentException($"Pixel length {pixels.Length} != {width}x{height}.", nameof(pixels));
        using var fs = File.Create(path);
        fs.Write(Signature);
        WriteChunk(fs, "IHDR", Ihdr(width, height));
        WriteChunk(fs, "IDAT", Compress(Filter(pixels, width, height)));
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static byte[] Ihdr(int width, int height)
    {
        var b = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), height);
        b[8] = 8;  // bit depth
        b[9] = 0;  // color type: grayscale
        return b;  // compression 0, filter 0, interlace 0
    }

    // Each scanline is prefixed with filter type 0 (None).
    private static byte[] Filter(byte[] pixels, int width, int height)
    {
        var raw = new byte[(width + 1) * height];
        for (var row = 0; row < height; row++)
            Array.Copy(pixels, row * width, raw, row * (width + 1) + 1, width);
        return raw;
    }

    private static byte[] Compress(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        var crc = Crc32(typeBytes, 0xFFFFFFFF);
        crc = Crc32(data, crc);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFF);
        s.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data, uint crc)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
