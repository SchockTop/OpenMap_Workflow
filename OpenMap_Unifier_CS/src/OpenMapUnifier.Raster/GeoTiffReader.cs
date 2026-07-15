using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace OpenMapUnifier.Raster;

/// <summary>
/// Minimal, dependency-free GeoTIFF reader for elevation tiles. Supports the
/// subset Bayern's DGM/DOM products actually use: classic TIFF (II or MM),
/// single band, strip or tile layout, uncompressed / LZW / Deflate, sample
/// formats float32 and signed/unsigned integers, ModelPixelScale +
/// ModelTiepoint georeferencing, and the GDAL NoData ASCII tag.
/// </summary>
public static class GeoTiffReader
{
    public static HeightGrid Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Read(bytes);
    }

    public static HeightGrid Read(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            throw new InvalidDataException("File too small to be a TIFF.");

        var littleEndian = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        if (!littleEndian && !(tiff[0] == (byte)'M' && tiff[1] == (byte)'M'))
            throw new InvalidDataException("Not a TIFF (bad byte-order marker).");
        if (ReadU16(tiff, 2, littleEndian) != 42)
            throw new InvalidDataException("Not a classic TIFF (BigTIFF is not supported).");

        var ifdOffset = ReadU32(tiff, 4, littleEndian);
        var tags = ReadIfd(tiff, (int)ifdOffset, littleEndian);

        var width = (int)ReadValues(tiff, GetEntry(tags, 256, "ImageWidth"), littleEndian)[0];
        var height = (int)ReadValues(tiff, GetEntry(tags, 257, "ImageLength"), littleEndian)[0];
        var bitsPerSample = tags.TryGetValue(258, out var bps) ? (int)ReadValues(tiff, bps, littleEndian)[0] : 1;
        var compression = tags.TryGetValue(259, out var cmp) ? (int)ReadValues(tiff, cmp, littleEndian)[0] : 1;
        var samplesPerPixel = tags.TryGetValue(277, out var spp) ? (int)ReadValues(tiff, spp, littleEndian)[0] : 1;
        var predictor = tags.TryGetValue(317, out var prd) ? (int)ReadValues(tiff, prd, littleEndian)[0] : 1;
        var sampleFormat = tags.TryGetValue(339, out var sf) ? (int)ReadValues(tiff, sf, littleEndian)[0] : 1;

        if (samplesPerPixel != 1)
            throw new NotSupportedException($"Only single-band rasters are supported (got {samplesPerPixel} bands).");
        if (bitsPerSample is not (8 or 16 or 32))
            throw new NotSupportedException($"Unsupported bit depth {bitsPerSample}.");

        var bytesPerSample = bitsPerSample / 8;
        var raw = new byte[(long)width * height * bytesPerSample];

        if (tags.ContainsKey(322))
            ReadTiled(tiff, tags, littleEndian, width, height, bytesPerSample, compression, predictor, raw);
        else
            ReadStripped(tiff, tags, littleEndian, width, height, bytesPerSample, compression, predictor, raw);

        var data = ConvertSamples(raw, width * height, bitsPerSample, sampleFormat, littleEndian);

        var (originE, originN, pixelSize) = ReadGeoreference(tiff, tags, littleEndian);
        var noData = ReadNoData(tiff, tags, littleEndian) ?? -9999f;

        return new HeightGrid(data, width, height, originE, originN, pixelSize, noData);
    }

    // ---- pixel data ---------------------------------------------------------

    private static void ReadStripped(ReadOnlySpan<byte> tiff, Dictionary<int, IfdEntry> tags, bool le,
        int width, int height, int bytesPerSample, int compression, int predictor, byte[] raw)
    {
        var rowsPerStrip = tags.TryGetValue(278, out var rps)
            ? (int)ReadValues(tiff, rps, le)[0] : height;
        var offsets = ReadValues(tiff, GetEntry(tags, 273, "StripOffsets"), le);
        var counts = ReadValues(tiff, GetEntry(tags, 279, "StripByteCounts"), le);

        var rowBytes = width * bytesPerSample;
        for (var strip = 0; strip < offsets.Length; strip++)
        {
            var startRow = strip * rowsPerStrip;
            var rows = Math.Min(rowsPerStrip, height - startRow);
            if (rows <= 0) break;
            var expected = rows * rowBytes;
            var dest = raw.AsSpan(startRow * rowBytes, expected);
            Decompress(tiff.Slice((int)offsets[strip], (int)counts[strip]), dest, compression);
            ApplyPredictor(dest, predictor, width, rows, bytesPerSample);
        }
    }

    private static void ReadTiled(ReadOnlySpan<byte> tiff, Dictionary<int, IfdEntry> tags, bool le,
        int width, int height, int bytesPerSample, int compression, int predictor, byte[] raw)
    {
        var tileW = (int)ReadValues(tiff, GetEntry(tags, 322, "TileWidth"), le)[0];
        var tileH = (int)ReadValues(tiff, GetEntry(tags, 323, "TileLength"), le)[0];
        var offsets = ReadValues(tiff, GetEntry(tags, 324, "TileOffsets"), le);
        var counts = ReadValues(tiff, GetEntry(tags, 325, "TileByteCounts"), le);

        var tilesAcross = (width + tileW - 1) / tileW;
        var tileRowBytes = tileW * bytesPerSample;
        var tileBuf = new byte[tileRowBytes * tileH];

        for (var t = 0; t < offsets.Length; t++)
        {
            var tileRow = t / tilesAcross;
            var tileCol = t % tilesAcross;
            Decompress(tiff.Slice((int)offsets[t], (int)counts[t]), tileBuf, compression);
            ApplyPredictor(tileBuf, predictor, tileW, tileH, bytesPerSample);

            var copyCols = Math.Min(tileW, width - tileCol * tileW);
            var copyRows = Math.Min(tileH, height - tileRow * tileH);
            for (var r = 0; r < copyRows; r++)
            {
                var src = tileBuf.AsSpan(r * tileRowBytes, copyCols * bytesPerSample);
                var destOffset = ((tileRow * tileH + r) * (long)width + tileCol * tileW) * bytesPerSample;
                src.CopyTo(raw.AsSpan((int)destOffset));
            }
        }
    }

    private static void Decompress(ReadOnlySpan<byte> input, Span<byte> output, int compression)
    {
        switch (compression)
        {
            case 1: // none
                input[..output.Length].CopyTo(output);
                break;
            case 5: // LZW
                TiffLzwDecoder.Decode(input, output);
                break;
            case 8:
            case 32946: // Deflate (zlib-wrapped)
                using (var zs = new ZLibStream(new MemoryStream(input.ToArray()), CompressionMode.Decompress))
                    zs.ReadExactly(output);
                break;
            default:
                throw new NotSupportedException(
                    $"TIFF compression {compression} is not supported (supported: none, LZW, Deflate).");
        }
    }

    private static void ApplyPredictor(Span<byte> data, int predictor, int width, int rows, int bytesPerSample)
    {
        switch (predictor)
        {
            case 1:
                return;
            case 2: // horizontal differencing over the raw integer lanes.
                    // libtiff applies this to 32-bit lanes even for float32
                    // data (wrapping u32 addition) — Brandenburg's and RLP's
                    // DGM1 tiles are written exactly that way.
                for (var r = 0; r < rows; r++)
                {
                    var row = data.Slice(r * width * bytesPerSample, width * bytesPerSample);
                    switch (bytesPerSample)
                    {
                        case 1:
                            for (var c = 1; c < width; c++) row[c] += row[c - 1];
                            break;
                        case 2:
                            for (var c = 1; c < width; c++)
                            {
                                var prev = BinaryPrimitives.ReadUInt16LittleEndian(row[((c - 1) * 2)..]);
                                var cur = BinaryPrimitives.ReadUInt16LittleEndian(row[(c * 2)..]);
                                BinaryPrimitives.WriteUInt16LittleEndian(row[(c * 2)..], (ushort)(cur + prev));
                            }
                            break;
                        case 4:
                            for (var c = 1; c < width; c++)
                            {
                                var prev = BinaryPrimitives.ReadUInt32LittleEndian(row[((c - 1) * 4)..]);
                                var cur = BinaryPrimitives.ReadUInt32LittleEndian(row[(c * 4)..]);
                                BinaryPrimitives.WriteUInt32LittleEndian(row[(c * 4)..], unchecked(cur + prev));
                            }
                            break;
                        default:
                            throw new NotSupportedException($"Predictor 2 with {bytesPerSample * 8}-bit samples.");
                    }
                }
                return;
            default:
                throw new NotSupportedException($"TIFF predictor {predictor} is not supported.");
        }
    }

    private static float[] ConvertSamples(byte[] raw, int count, int bitsPerSample, int sampleFormat, bool le)
    {
        var data = new float[count];
        switch (bitsPerSample, sampleFormat)
        {
            case (32, 3): // float32 — the Bayern DGM case
                for (var i = 0; i < count; i++)
                {
                    var u = le
                        ? BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4))
                        : BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(i * 4));
                    data[i] = BitConverter.UInt32BitsToSingle(u);
                }
                break;
            case (16, 1):
                for (var i = 0; i < count; i++)
                    data[i] = le
                        ? BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i * 2))
                        : BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(i * 2));
                break;
            case (16, 2):
                for (var i = 0; i < count; i++)
                    data[i] = le
                        ? BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(i * 2))
                        : BinaryPrimitives.ReadInt16BigEndian(raw.AsSpan(i * 2));
                break;
            case (32, 1):
                for (var i = 0; i < count; i++)
                    data[i] = le
                        ? BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4))
                        : BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(i * 4));
                break;
            case (32, 2):
                for (var i = 0; i < count; i++)
                    data[i] = le
                        ? BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(i * 4))
                        : BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(i * 4));
                break;
            case (8, 1):
                for (var i = 0; i < count; i++) data[i] = raw[i];
                break;
            default:
                throw new NotSupportedException(
                    $"Unsupported sample type: {bitsPerSample}-bit, format {sampleFormat}.");
        }
        return data;
    }

    // ---- georeferencing ------------------------------------------------------

    private static (double OriginE, double OriginN, double PixelSize) ReadGeoreference(
        ReadOnlySpan<byte> tiff, Dictionary<int, IfdEntry> tags, bool le)
    {
        if (!tags.TryGetValue(33550, out var scaleTag) || !tags.TryGetValue(33922, out var tieTag))
            throw new InvalidDataException(
                "GeoTIFF georeferencing tags missing (ModelPixelScale/ModelTiepoint). " +
                "Derive the origin from the Bayern tile name instead.");

        var scale = ReadDoubles(tiff, scaleTag, le);
        var tie = ReadDoubles(tiff, tieTag, le);
        if (Math.Abs(scale[0] - scale[1]) > 1e-9)
            throw new NotSupportedException("Non-square pixels are not supported.");

        // Tiepoint maps raster (i,j) -> model (X,Y): origin corner = model - i*scale.
        var originE = tie[3] - tie[0] * scale[0];
        var originN = tie[4] + tie[1] * scale[1];
        return (originE, originN, scale[0]);
    }

    private static float? ReadNoData(ReadOnlySpan<byte> tiff, Dictionary<int, IfdEntry> tags, bool le)
    {
        if (!tags.TryGetValue(42113, out var tag)) return null;
        var text = ReadAscii(tiff, tag, le).Trim('\0', ' ');
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // ---- low-level TIFF plumbing ---------------------------------------------

    private readonly record struct IfdEntry(int Tag, int Type, uint Count, uint ValueOrOffset);

    private static Dictionary<int, IfdEntry> ReadIfd(ReadOnlySpan<byte> tiff, int offset, bool le)
    {
        var count = ReadU16(tiff, offset, le);
        var tags = new Dictionary<int, IfdEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var e = offset + 2 + i * 12;
            var tag = ReadU16(tiff, e, le);
            var type = ReadU16(tiff, e + 2, le);
            var cnt = ReadU32(tiff, e + 4, le);
            var val = ReadU32(tiff, e + 8, le);
            tags[tag] = new IfdEntry(tag, type, cnt, val);
        }
        return tags;
    }

    private static IfdEntry GetEntry(Dictionary<int, IfdEntry> tags, int tag, string name) =>
        tags.TryGetValue(tag, out var e) ? e : throw new InvalidDataException($"TIFF tag {name} ({tag}) missing.");

    private static int TypeSize(int type) => type switch
    {
        1 or 2 or 6 or 7 => 1,  // BYTE, ASCII, SBYTE, UNDEFINED
        3 or 8 => 2,            // SHORT, SSHORT
        4 or 9 or 11 => 4,      // LONG, SLONG, FLOAT
        5 or 10 or 12 => 8,     // RATIONAL, SRATIONAL, DOUBLE
        _ => throw new NotSupportedException($"TIFF field type {type}."),
    };

    private static long[] ReadValues(ReadOnlySpan<byte> tiff, IfdEntry e, bool le)
    {
        var size = TypeSize(e.Type);
        var total = size * (int)e.Count;

        // Values that fit in 4 bytes are stored inline in the entry's value
        // field. ReadIfd already decoded that field as a u32 in file byte
        // order, so re-encode it the same way to recover the raw bytes.
        ReadOnlySpan<byte> data;
        if (total <= 4)
        {
            var inline = new byte[4];
            if (le) BinaryPrimitives.WriteUInt32LittleEndian(inline, e.ValueOrOffset);
            else BinaryPrimitives.WriteUInt32BigEndian(inline, e.ValueOrOffset);
            data = inline.AsSpan(0, total);
        }
        else
        {
            data = tiff.Slice((int)e.ValueOrOffset, total);
        }

        var values = new long[e.Count];
        for (var i = 0; i < e.Count; i++)
        {
            values[i] = (e.Type, size) switch
            {
                (_, 1) => data[i],
                (_, 2) => ReadU16(data, i * 2, le),
                (9, 4) => le
                    ? BinaryPrimitives.ReadInt32LittleEndian(data[(i * 4)..])
                    : BinaryPrimitives.ReadInt32BigEndian(data[(i * 4)..]),
                (_, 4) => ReadU32(data, i * 4, le),
                _ => throw new NotSupportedException($"Cannot read TIFF type {e.Type} as integer."),
            };
        }
        return values;
    }

    private static double[] ReadDoubles(ReadOnlySpan<byte> tiff, IfdEntry e, bool le)
    {
        if (e.Type != 12) throw new InvalidDataException($"Expected DOUBLE tag, got type {e.Type}.");
        var values = new double[e.Count];
        for (var i = 0; i < e.Count; i++)
        {
            var u = le
                ? BinaryPrimitives.ReadUInt64LittleEndian(tiff.Slice((int)e.ValueOrOffset + i * 8, 8))
                : BinaryPrimitives.ReadUInt64BigEndian(tiff.Slice((int)e.ValueOrOffset + i * 8, 8));
            values[i] = BitConverter.UInt64BitsToDouble(u);
        }
        return values;
    }

    private static string ReadAscii(ReadOnlySpan<byte> tiff, IfdEntry e, bool le)
    {
        if (e.Count <= 4)
        {
            Span<byte> inline = stackalloc byte[4];
            if (le) BinaryPrimitives.WriteUInt32LittleEndian(inline, e.ValueOrOffset);
            else BinaryPrimitives.WriteUInt32BigEndian(inline, e.ValueOrOffset);
            return System.Text.Encoding.ASCII.GetString(inline[..(int)e.Count]);
        }
        return System.Text.Encoding.ASCII.GetString(tiff.Slice((int)e.ValueOrOffset, (int)e.Count));
    }

    private static ushort ReadU16(ReadOnlySpan<byte> s, int offset, bool le) => le
        ? BinaryPrimitives.ReadUInt16LittleEndian(s[offset..])
        : BinaryPrimitives.ReadUInt16BigEndian(s[offset..]);

    private static uint ReadU32(ReadOnlySpan<byte> s, int offset, bool le) => le
        ? BinaryPrimitives.ReadUInt32LittleEndian(s[offset..])
        : BinaryPrimitives.ReadUInt32BigEndian(s[offset..]);
}
