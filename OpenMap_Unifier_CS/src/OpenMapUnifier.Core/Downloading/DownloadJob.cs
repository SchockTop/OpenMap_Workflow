namespace OpenMapUnifier.Core.Downloading;

/// <summary>
/// One file to fetch. <see cref="Mirrors"/> are tried in order; the download
/// only counts as failed once every mirror has been exhausted (this is what
/// keeps DGM batches alive when one bayernwolke mirror flaps).
/// </summary>
public sealed record DownloadJob(string FileName, IReadOnlyList<string> Mirrors)
{
    public DownloadJob(string fileName, string url) : this(fileName, new[] { url }) { }
}

public sealed record DownloadResult(DownloadJob Job, string? LocalPath, bool Success, bool Skipped, string? Error)
{
    public static DownloadResult Ok(DownloadJob job, string path, bool skipped = false) =>
        new(job, path, true, skipped, null);
    public static DownloadResult Fail(DownloadJob job, string error) =>
        new(job, null, false, false, error);
}

public sealed record DownloadProgress(string FileName, long BytesReceived, long TotalBytes, string Status)
{
    public double Percent => TotalBytes > 0 ? 100.0 * BytesReceived / TotalBytes : 0;
}

public interface IDownloader
{
    Task<DownloadResult> DownloadAsync(DownloadJob job, string targetDirectory,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);

    Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(IEnumerable<DownloadJob> jobs, string targetDirectory,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
}
