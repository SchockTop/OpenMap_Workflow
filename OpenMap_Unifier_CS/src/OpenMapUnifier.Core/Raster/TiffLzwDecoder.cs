namespace OpenMapUnifier.Core.Raster;

/// <summary>
/// TIFF-variant LZW decoder (MSB-first bit order, 9-bit initial codes,
/// ClearCode 256 / EOI 257, with the spec's "early change" — the code width
/// bumps one code before the table is actually full). This is the compression
/// Bayern's DGM GeoTIFFs use.
/// </summary>
internal static class TiffLzwDecoder
{
    private const int ClearCode = 256;
    private const int EoiCode = 257;

    public static void Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        // Dictionary as (prefix code, appended byte) pairs; entries 0-255 are literals.
        var prefix = new int[4096];
        var suffix = new byte[4096];
        var length = new int[4096];
        for (var i = 0; i < 256; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
            length[i] = 1;
        }

        var nextCode = 258;
        var codeBits = 9;

        long bitBuffer = 0;
        var bitCount = 0;
        var inPos = 0;
        var outPos = 0;
        var oldCode = -1;
        var scratch = new byte[4096];

        while (outPos < output.Length)
        {
            while (bitCount < codeBits)
            {
                if (inPos >= input.Length) return;
                bitBuffer = (bitBuffer << 8) | input[inPos++];
                bitCount += 8;
            }
            var code = (int)((bitBuffer >> (bitCount - codeBits)) & ((1 << codeBits) - 1));
            bitCount -= codeBits;

            if (code == EoiCode) return;

            if (code == ClearCode)
            {
                nextCode = 258;
                codeBits = 9;
                oldCode = -1;
                continue;
            }

            int emitLen;
            if (code < nextCode && (code < 256 || code >= 258 || length[code] > 0))
            {
                emitLen = Emit(code, prefix, suffix, length, scratch);
            }
            else if (code == nextCode && oldCode >= 0)
            {
                // KwKwK case: string = old string + first byte of old string.
                var oldLen = Emit(oldCode, prefix, suffix, length, scratch);
                scratch[oldLen] = scratch[0];
                emitLen = oldLen + 1;
            }
            else
            {
                throw new InvalidDataException($"Corrupt LZW stream: code {code}, table size {nextCode}.");
            }

            var toCopy = Math.Min(emitLen, output.Length - outPos);
            scratch.AsSpan(0, toCopy).CopyTo(output[outPos..]);
            outPos += toCopy;

            if (oldCode >= 0 && nextCode < 4096)
            {
                prefix[nextCode] = oldCode;
                suffix[nextCode] = scratch[0];
                length[nextCode] = length[oldCode] + 1;
                nextCode++;
            }

            // TIFF "early change": widen when the NEXT code would not fit.
            if (nextCode == (1 << codeBits) - 1 && codeBits < 12)
                codeBits++;

            oldCode = code;
        }
    }

    private static int Emit(int code, int[] prefix, byte[] suffix, int[] length, byte[] scratch)
    {
        var len = length[code];
        var pos = len;
        var c = code;
        while (c >= 0)
        {
            scratch[--pos] = suffix[c];
            c = prefix[c];
        }
        return len;
    }
}
