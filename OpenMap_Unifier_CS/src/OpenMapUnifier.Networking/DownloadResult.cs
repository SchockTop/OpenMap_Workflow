namespace OpenMapUnifier.Networking;

/// <summary>Outcome of one <see cref="DownloadJob"/>: where the file landed,
/// whether it was skipped because it already existed, or why it failed.</summary>
public sealed record DownloadResult(DownloadJob Job, string? LocalPath, bool Success, bool Skipped, string? Error)
{
    public static DownloadResult Ok(DownloadJob job, string path, bool skipped = false) =>
        new(job, path, true, skipped, null);
    public static DownloadResult Fail(DownloadJob job, string error) =>
        new(job, null, false, false, error);
}
