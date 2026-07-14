using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenMapUnifier.Tests;

/// <summary>
/// Minimal little-endian GeoTIFF writer for tests: single band float32,
/// strip layout, compression none / LZW / Deflate, with ModelPixelScale,
/// ModelTiepoint and GDAL NoData tags — the shape of a real Bayern DGM tile.
/// </summary>
public static class TiffTestWriter
{
    public enum Compression { None = 1, Lzw = 5, Deflate = 8 }

    public static byte[] WriteFloat32(float[] data, int width, int height,
        double originE, double originN, double pixelSize,
        Compression compression = Compression.None, int rowsPerStrip = 2,
        string noData = "-9999")
    {
        var strips = new List<byte[]>();
        for (var row = 0; row < height; row += rowsPerStrip)
        {
            var rows = Math.Min(rowsPerStrip, height - row);
            var raw = new byte[rows * width * 4];
            for (var i = 0; i < rows * width; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(i * 4),
                    BitConverter.SingleToUInt32Bits(data[row * width + i]));
            strips.Add(compression switch
            {
                Compression.None => raw,
                Compression.Lzw => LzwEncode(raw),
                Compression.Deflate => ZlibCompress(raw),
                _ => throw new ArgumentOutOfRangeException(nameof(compression)),
            });
        }

        var noDataBytes = System.Text.Encoding.ASCII.GetBytes(noData + "\0");
        var scale = new[] { pixelSize, pixelSize, 0.0 };
        var tie = new[] { 0.0, 0.0, 0.0, originE, originN, 0.0 };

        // Layout: header(8) | IFD | external arrays | strip data
        var entries = 13;
        var ifdSize = 2 + entries * 12 + 4;
        var pos = 8 + ifdSize;

        var stripOffsetsPos = pos; pos += strips.Count * 4;
        var stripCountsPos = pos; pos += strips.Count * 4;
        var scalePos = pos; pos += scale.Length * 8;
        var tiePos = pos; pos += tie.Length * 8;
        var noDataPos = pos; pos += noDataBytes.Length;
        var dataStart = pos;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'I'); w.Write((byte)'I'); w.Write((ushort)42); w.Write(8u);

        w.Write((ushort)entries);
        void Entry(ushort tag, ushort type, uint count, uint value)
        {
            w.Write(tag); w.Write(type); w.Write(count); w.Write(value);
        }
        Entry(256, 3, 1, (uint)width);
        Entry(257, 3, 1, (uint)height);
        Entry(258, 3, 1, 32);
        Entry(259, 3, 1, (uint)compression);
        Entry(262, 3, 1, 1);
        // TIFF stores any field whose total size fits in 4 bytes inline, so a
        // single-strip file must carry offset/count in the entry itself.
        Entry(273, 4, (uint)strips.Count,
            strips.Count == 1 ? (uint)dataStart : (uint)stripOffsetsPos);
        Entry(277, 3, 1, 1);
        Entry(278, 3, 1, (uint)rowsPerStrip);
        Entry(279, 4, (uint)strips.Count,
            strips.Count == 1 ? (uint)strips[0].Length : (uint)stripCountsPos);
        Entry(339, 3, 1, 3);
        Entry(33550, 12, 3, (uint)scalePos);
        Entry(33922, 12, 6, (uint)tiePos);
        Entry(42113, 2, (uint)noDataBytes.Length, (uint)noDataPos);
        w.Write(0u); // next IFD

        var offset = dataStart;
        foreach (var s in strips) { w.Write((uint)offset); offset += s.Length; }
        foreach (var s in strips) w.Write((uint)s.Length);
        foreach (var v in scale) w.Write(v);
        foreach (var v in tie) w.Write(v);
        w.Write(noDataBytes);
        foreach (var s in strips) w.Write(s);

        return ms.ToArray();
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw);
        return ms.ToArray();
    }

    /// <summary>
    /// TIFF-variant LZW encoder (MSB-first, 9..12-bit codes, early change) —
    /// an independent counterpart to the library's decoder for round-tripping.
    /// </summary>
    private static byte[] LzwEncode(byte[] raw)
    {
        var output = new List<byte>();
        long bitBuffer = 0;
        var bitCount = 0;
        var codeBits = 9;

        void Put(int code)
        {
            bitBuffer = (bitBuffer << codeBits) | (uint)code;
            bitCount += codeBits;
            while (bitCount >= 8)
            {
                output.Add((byte)(bitBuffer >> (bitCount - 8)));
                bitCount -= 8;
            }
        }

        var table = new Dictionary<string, int>();
        var nextCode = 258;
        void Reset()
        {
            table.Clear();
            nextCode = 258;
            codeBits = 9;
        }

        Reset();
        Put(256); // ClearCode

        var prefix = "";
        foreach (var b in raw)
        {
            var candidate = prefix.Length == 0 ? ((char)b).ToString() : prefix + (char)b;
            if (candidate.Length == 1 || table.ContainsKey(candidate))
            {
                prefix = candidate;
                continue;
            }

            Put(prefix.Length == 1 ? prefix[0] : table[prefix]);
            table[candidate] = nextCode++;
            // The encoder's table leads the decoder's by exactly one entry, so
            // it widens one code later than the decoder's early-change point
            // (verified against imagecodecs' LZW).
            if (nextCode == 1 << codeBits)
            {
                if (codeBits < 12) codeBits++;
                else { Put(256); Reset(); }
            }
            prefix = ((char)b).ToString();
        }

        if (prefix.Length > 0)
            Put(prefix.Length == 1 ? prefix[0] : table[prefix]);
        Put(257); // EOI

        if (bitCount > 0)
            output.Add((byte)(bitBuffer << (8 - bitCount)));
        return output.ToArray();
    }
}
