namespace OpenMapUnifier.Core.Downloading;

public sealed class DownloaderOptions
{
    /// <summary>Parallel downloads in <see cref="HttpTileDownloader.DownloadAllAsync"/>.</summary>
    public int MaxParallel { get; set; } = 4;

    /// <summary>Retries per mirror on transient failure (timeouts, 5xx).</summary>
    public int RetriesPerMirror { get; set; } = 2;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Existing complete files are skipped, matching the Python Unifier.</summary>
    public bool SkipExisting { get; set; } = true;

    public string UserAgent { get; set; } = "OpenMapUnifier.NET/1.0";
}

/// <summary>
/// HttpClient-based tile downloader. Semantics mirror the Python Unifier's
/// MapDownloader: skip-if-exists, write to "&lt;name&gt;.part" then atomically
/// rename, verify byte count against Content-Length, and fall through the
/// mirror list before reporting failure.
/// </summary>
public sealed class HttpTileDownloader : IDownloader, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly DownloaderOptions _options;

    public HttpTileDownloader(DownloaderOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new DownloaderOptions();
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        });
        _http.Timeout = _options.RequestTimeout;
    }

    public async Task<DownloadResult> DownloadAsync(DownloadJob job, string targetDirectory,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (job.Mirrors.Count == 0)
            return DownloadResult.Fail(job, "No URLs to try.");

        var targetPath = Path.Combine(targetDirectory, job.FileName);
        var partPath = targetPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDirectory);

        if (_options.SkipExisting && File.Exists(targetPath))
        {
            progress?.Report(new DownloadProgress(job.FileName, 0, 0, "Skipped (exists)"));
            return DownloadResult.Ok(job, targetPath, skipped: true);
        }

        // A leftover .part from an interrupted run is restarted from zero —
        // the servers don't reliably honour Range requests.
        TryDelete(partPath);

        string? lastError = null;
        for (var m = 0; m < job.Mirrors.Count; m++)
        {
            for (var attempt = 0; attempt <= _options.RetriesPerMirror; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var (ok, error, retryable) = await DownloadOneAsync(
                    job.Mirrors[m], job.FileName, targetPath, partPath, progress, ct).ConfigureAwait(false);
                if (ok)
                    return DownloadResult.Ok(job, targetPath);

                lastError = error;
                if (!retryable) break;
                if (attempt < _options.RetriesPerMirror)
                    await Task.Delay(_options.RetryBaseDelay * (1 << attempt), ct).ConfigureAwait(false);
            }
        }

        progress?.Report(new DownloadProgress(job.FileName, 0, 0, lastError ?? "Error"));
        return DownloadResult.Fail(job, lastError ?? "Unknown error");
    }

    public async Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(IEnumerable<DownloadJob> jobs,
        string targetDirectory, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(_options.MaxParallel);
        var tasks = jobs.Select(async job =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await DownloadAsync(job, targetDirectory, progress, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string? Error, bool Retryable)> DownloadOneAsync(string url, string fileName,
        string targetPath, string partPath, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                // 4xx won't get better on retry of the same mirror; 5xx / 429 might.
                return (false, $"HTTP {status} from {url}", status >= 500 || status == 429);
            }

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            long received = 0;
            await using (var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(partPath, FileMode.Create, FileAccess.Write,
                             FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new DownloadProgress(fileName, received, totalBytes, "Downloading"));
                }
            }

            if (totalBytes > 0 && received != totalBytes)
            {
                TryDelete(partPath);
                return (false, $"Truncated: got {received} of {totalBytes} bytes", true);
            }

            File.Move(partPath, targetPath, overwrite: true);
            progress?.Report(new DownloadProgress(fileName, received, totalBytes, "Completed"));
            return (true, null, false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryDelete(partPath);
            return (false, "Timeout", true);
        }
        catch (HttpRequestException e)
        {
            TryDelete(partPath);
            return (false, $"Network error: {e.Message}", true);
        }
        catch (IOException e)
        {
            TryDelete(partPath);
            return (false, $"I/O error: {e.Message}", false);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
