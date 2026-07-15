namespace OpenMapUnifier.Networking;

/// <summary>Progress report for one running download (TotalBytes is 0 when
/// the server sends no Content-Length).</summary>
public sealed record DownloadProgress(string FileName, long BytesReceived, long TotalBytes, string Status)
{
    public double Percent => TotalBytes > 0 ? 100.0 * BytesReceived / TotalBytes : 0;
}
