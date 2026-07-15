namespace OpenMapUnifier.Networking;

/// <summary>Fetches <see cref="DownloadJob"/>s into a directory. The default
/// implementation is <see cref="HttpTileDownloader"/>; swap in your own for
/// testing or exotic transports.</summary>
public interface IDownloader
{
    Task<DownloadResult> DownloadAsync(DownloadJob job, string targetDirectory,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);

    Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(IEnumerable<DownloadJob> jobs, string targetDirectory,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
}
