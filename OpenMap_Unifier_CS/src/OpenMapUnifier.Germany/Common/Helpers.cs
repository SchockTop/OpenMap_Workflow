using System.IO.Compression;
using System.Text.RegularExpressions;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Raster;

namespace OpenMapUnifier.Germany.Common;

public static class ZipHelpers
{
    /// <summary>Read one entry of a LOCAL zip into memory.</summary>
    public static byte[] ReadEntryBytes(string zipPath, Func<string, bool> entryFilter)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => e.Length > 0 && entryFilter(e.FullName))
                    ?? throw new InvalidDataException($"No matching entry in {zipPath}.");
        using var stream = entry.Open();
        using var ms = new MemoryStream((int)entry.Length);
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>GeoTIFF from an entry of a LOCAL zip (Brandenburg, Thüringen, ST...).</summary>
    public static HeightGrid ReadGeoTiffEntry(string zipPath, Func<string, bool> entryFilter) =>
        GeoTiffReader.Read(ReadEntryBytes(zipPath, entryFilter));
}

/// <summary>
/// (E,N)->URL map built once from a remote text index (directory XML, ATOM
/// feed, HTML listing). Needed where tile filenames embed per-tile years or
/// epochs (NRW, RLP) that cannot be derived from coordinates.
/// </summary>
public sealed class UrlIndex
{
    private readonly HttpClient _http;
    private readonly string _indexUrl;
    private readonly Regex _tilePattern;
    private readonly Func<Match, string> _urlFromMatch;
    private readonly SemaphoreSlim _gate = new(1);
    private Dictionary<(int E, int N), string>? _map;

    /// <param name="tilePattern">Regex over the index text; must expose groups "e" and "n" (km).</param>
    /// <param name="urlFromMatch">Builds the absolute download URL from a match.</param>
    public UrlIndex(HttpClient http, string indexUrl, Regex tilePattern, Func<Match, string> urlFromMatch)
    {
        _http = http;
        _indexUrl = indexUrl;
        _tilePattern = tilePattern;
        _urlFromMatch = urlFromMatch;
    }

    public async Task<string?> UrlForAsync(int eastKm, int northKm, CancellationToken ct = default)
    {
        var map = await GetMapAsync(ct).ConfigureAwait(false);
        return map.GetValueOrDefault((eastKm, northKm));
    }

    public async Task<IReadOnlyDictionary<(int E, int N), string>> GetMapAsync(CancellationToken ct = default)
    {
        if (_map is not null) return _map;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_map is not null) return _map;
            var text = await _http.GetStringAsync(_indexUrl, ct).ConfigureAwait(false);
            var map = new Dictionary<(int, int), string>();
            foreach (Match m in _tilePattern.Matches(text))
            {
                var key = (int.Parse(m.Groups["e"].Value), int.Parse(m.Groups["n"].Value));
                // Indexes list newest last as often as first — last one wins is
                // as arbitrary as first; overwrite keeps the code simple.
                map[key] = _urlFromMatch(m);
            }
            if (map.Count == 0)
                throw new InvalidDataException($"Tile index {_indexUrl} yielded no entries.");
            return _map = map;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Tile fetcher over one or more REMOTE zip archives (Hamburg, Bremen,
/// Saarland, Hessen): archive URLs are resolved once, central directories are
/// opened lazily, and a tile's entry is range-extracted into the cache
/// directory. Extracted tiles are cached on disk, so repeated queries skip
/// the network entirely.
/// </summary>
public sealed class RemoteArchiveFetcher
{
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _archiveUrls;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly List<RemoteZipReader> _opened = new();
    private IReadOnlyList<string>? _urls;
    private int _nextToOpen;

    public RemoteArchiveFetcher(HttpClient http,
        Func<CancellationToken, Task<IReadOnlyList<string>>> archiveUrls)
    {
        _http = http;
        _archiveUrls = archiveUrls;
    }

    public async Task<string?> FetchAsync(Func<string, bool> entryFilter, string cacheFileName,
        string cacheDirectory, CancellationToken ct = default)
    {
        var targetPath = Path.Combine(cacheDirectory, cacheFileName);
        if (File.Exists(targetPath))
            return targetPath;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(targetPath))
                return targetPath;

            _urls ??= await _archiveUrls(ct).ConfigureAwait(false);

            // Search already-opened archives first, then open further central
            // directories one at a time until the entry shows up.
            foreach (var zip in _opened)
            {
                if (zip.FindEntry(entryFilter) is { } hit)
                    return await zip.ExtractEntryAsync(hit, targetPath, ct).ConfigureAwait(false);
            }
            while (_nextToOpen < _urls.Count)
            {
                var zip = await RemoteZipReader.OpenAsync(_http, _urls[_nextToOpen++], ct).ConfigureAwait(false);
                _opened.Add(zip);
                if (zip.FindEntry(entryFilter) is { } hit)
                    return await zip.ExtractEntryAsync(hit, targetPath, ct).ConfigureAwait(false);
            }
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
