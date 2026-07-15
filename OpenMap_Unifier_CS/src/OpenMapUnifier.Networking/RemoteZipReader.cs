using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenMapUnifier.Networking;

public sealed record RemoteZipEntry(string Name, long CompressedSize, long UncompressedSize,
    int Method, long LocalHeaderOffset)
{
    public bool IsDeflate => Method == 8;
    public bool IsStored => Method == 0;
}

/// <summary>
/// Reads single entries out of a remote zip archive using HTTP Range requests —
/// several states (Hamburg, Bremen, Saarland) only publish whole-city or
/// per-Landkreis archives of 0.4-37 GB, but their servers support ranges, so a
/// single 1 km tile (~1-20 MB) can be extracted without downloading the rest.
/// Supports classic zip and zip64 (Bremen's DOP archive exceeds 4 GB).
/// </summary>
public sealed class RemoteZipReader
{
    private const uint EocdSignature = 0x06054b50;
    private const uint Eocd64LocatorSignature = 0x07064b50;
    private const uint Eocd64Signature = 0x06064b50;
    private const uint CentralDirSignature = 0x02014b50;
    private const uint LocalHeaderSignature = 0x04034b50;

    private readonly HttpClient _http;

    public string Url { get; }
    public IReadOnlyList<RemoteZipEntry> Entries { get; private set; } = Array.Empty<RemoteZipEntry>();

    private RemoteZipReader(HttpClient http, string url)
    {
        _http = http;
        Url = url;
    }

    public static async Task<RemoteZipReader> OpenAsync(HttpClient http, string url, CancellationToken ct = default)
    {
        var reader = new RemoteZipReader(http, url);
        await reader.ReadCentralDirectoryAsync(ct).ConfigureAwait(false);
        return reader;
    }

    public RemoteZipEntry? FindEntry(Func<string, bool> nameMatch) =>
        Entries.FirstOrDefault(e => nameMatch(e.Name));

    /// <summary>Download and decompress a single entry.</summary>
    public async Task<byte[]> ReadEntryAsync(RemoteZipEntry entry, CancellationToken ct = default)
    {
        // The central directory's offset points at the local header, whose
        // name/extra lengths can differ from the central record — read it to
        // find where the data actually starts.
        var header = await FetchRangeAsync(entry.LocalHeaderOffset, 30, ct).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalHeaderSignature)
            throw new InvalidDataException($"Bad local header for '{entry.Name}' in {Url}.");
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        var dataOffset = entry.LocalHeaderOffset + 30 + nameLength + extraLength;

        var compressed = await FetchRangeAsync(dataOffset, entry.CompressedSize, ct).ConfigureAwait(false);
        switch (entry.Method)
        {
            case 0:
                return compressed;
            case 8:
                var output = new byte[entry.UncompressedSize];
                await using (var deflate = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress))
                    await deflate.ReadExactlyAsync(output, ct).ConfigureAwait(false);
                return output;
            default:
                throw new NotSupportedException($"Zip compression method {entry.Method} ('{entry.Name}').");
        }
    }

    /// <summary>Extract an entry to a file (atomically via .part rename).</summary>
    public async Task<string> ExtractEntryAsync(RemoteZipEntry entry, string targetPath, CancellationToken ct = default)
    {
        var data = await ReadEntryAsync(entry, ct).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");
        var partPath = targetPath + ".part";
        await File.WriteAllBytesAsync(partPath, data, ct).ConfigureAwait(false);
        File.Move(partPath, targetPath, overwrite: true);
        return targetPath;
    }

    private async Task ReadCentralDirectoryAsync(CancellationToken ct)
    {
        // EOCD sits in the last 22..(22+65535) bytes; 128 KB tail covers every
        // real-world comment plus the zip64 locator that precedes it.
        var tail = await FetchTailAsync(128 * 1024, ct).ConfigureAwait(false);
        var eocdPos = LastIndexOfSignature(tail.Data, EocdSignature);
        if (eocdPos < 0)
            throw new InvalidDataException($"No zip end-of-central-directory found in {Url}.");

        long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.Data.AsSpan(eocdPos + 16));
        long cdSize = BinaryPrimitives.ReadUInt32LittleEndian(tail.Data.AsSpan(eocdPos + 12));
        long entryCount = BinaryPrimitives.ReadUInt16LittleEndian(tail.Data.AsSpan(eocdPos + 10));

        if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF || entryCount == 0xFFFF)
        {
            // zip64: locator directly precedes the EOCD record.
            var locatorPos = eocdPos - 20;
            if (locatorPos < 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(tail.Data.AsSpan(locatorPos)) != Eocd64LocatorSignature)
                throw new InvalidDataException($"zip64 markers without a zip64 locator in {Url}.");
            var eocd64Offset = BinaryPrimitives.ReadInt64LittleEndian(tail.Data.AsSpan(locatorPos + 8));
            var eocd64 = await FetchRangeAsync(eocd64Offset, 56, ct).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(eocd64) != Eocd64Signature)
                throw new InvalidDataException($"Bad zip64 end-of-central-directory in {Url}.");
            entryCount = BinaryPrimitives.ReadInt64LittleEndian(eocd64.AsSpan(32));
            cdSize = BinaryPrimitives.ReadInt64LittleEndian(eocd64.AsSpan(40));
            cdOffset = BinaryPrimitives.ReadInt64LittleEndian(eocd64.AsSpan(48));
        }

        var cd = await FetchRangeAsync(cdOffset, cdSize, ct).ConfigureAwait(false);
        var entries = new List<RemoteZipEntry>((int)Math.Min(entryCount, 1_000_000));
        var pos = 0;
        while (pos + 46 <= cd.Length &&
               BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos)) == CentralDirSignature)
        {
            var method = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 10));
            long compressed = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 20));
            long uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 24));
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 28));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 30));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 32));
            long localOffset = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 42));
            var name = Encoding.UTF8.GetString(cd, pos + 46, nameLength);

            // zip64 extra field (id 0x0001) overrides any 0xFFFFFFFF values, in
            // the fixed order: uncompressed, compressed, local header offset.
            var extraPos = pos + 46 + nameLength;
            var extraEnd = extraPos + extraLength;
            while (extraPos + 4 <= extraEnd)
            {
                var fieldId = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(extraPos));
                var fieldSize = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(extraPos + 2));
                if (fieldId == 0x0001)
                {
                    var fieldPos = extraPos + 4;
                    if (uncompressed == 0xFFFFFFFF)
                    {
                        uncompressed = BinaryPrimitives.ReadInt64LittleEndian(cd.AsSpan(fieldPos));
                        fieldPos += 8;
                    }
                    if (compressed == 0xFFFFFFFF)
                    {
                        compressed = BinaryPrimitives.ReadInt64LittleEndian(cd.AsSpan(fieldPos));
                        fieldPos += 8;
                    }
                    if (localOffset == 0xFFFFFFFF)
                        localOffset = BinaryPrimitives.ReadInt64LittleEndian(cd.AsSpan(fieldPos));
                    break;
                }
                extraPos += 4 + fieldSize;
            }

            entries.Add(new RemoteZipEntry(name, compressed, uncompressed, method, localOffset));
            pos += 46 + nameLength + extraLength + commentLength;
        }
        Entries = entries;
    }

    private async Task<(byte[] Data, long TotalLength)> FetchTailAsync(int tailSize, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(null, tailSize);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
            throw new NotSupportedException(
                $"Server does not support HTTP range requests (got {(int)response.StatusCode}) for {Url} — " +
                "remote zip extraction requires ranges.");
        var data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var total = response.Content.Headers.ContentRange?.Length ?? data.Length;
        return (data, total);
    }

    private async Task<byte[]> FetchRangeAsync(long offset, long length, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + length - 1);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (data.Length < length)
            throw new InvalidDataException(
                $"Range request returned {data.Length} of {length} bytes for {Url}.");
        return data.Length == length ? data : data[..(int)length];
    }

    private static int LastIndexOfSignature(byte[] data, uint signature)
    {
        Span<byte> sig = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sig, signature);
        for (var i = data.Length - 4; i >= 0; i--)
        {
            if (data[i] == sig[0] && data[i + 1] == sig[1] && data[i + 2] == sig[2] && data[i + 3] == sig[3])
                return i;
        }
        return -1;
    }
}
