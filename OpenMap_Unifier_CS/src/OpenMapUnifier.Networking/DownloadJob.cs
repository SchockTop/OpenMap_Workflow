namespace OpenMapUnifier.Networking;

/// <summary>
/// One file to fetch. <see cref="Mirrors"/> are tried in order; the download
/// only counts as failed once every mirror has been exhausted (this is what
/// keeps DGM batches alive when one bayernwolke mirror flaps).
/// </summary>
public sealed record DownloadJob(string FileName, IReadOnlyList<string> Mirrors)
{
    public DownloadJob(string fileName, string url) : this(fileName, new[] { url }) { }
}
