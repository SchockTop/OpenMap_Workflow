using System.Xml.Linq;
using OpenMapUnifier.Core.Downloading;

namespace OpenMapUnifier.Bayern;

/// <summary>
/// Parser for .meta4 metalink files as served by geodaten.bayern.de (e.g. the
/// LoD2 index). Keeps EVERY &lt;url&gt; per &lt;file&gt; so the downloader can fall
/// through mirrors — returning only the first one is what used to make height
/// batches fail when bayernwolke download1 was blocked.
/// </summary>
public static class MetalinkParser
{
    public static IReadOnlyList<DownloadJob> Parse(string metalinkXml)
    {
        var root = XDocument.Parse(metalinkXml).Root
                   ?? throw new FormatException("Empty metalink document.");
        var jobs = new List<DownloadJob>();
        foreach (var file in root.Descendants().Where(e => e.Name.LocalName == "file"))
        {
            var name = file.Attribute("name")?.Value;
            var urls = file.Elements()
                .Where(e => e.Name.LocalName == "url" && !string.IsNullOrWhiteSpace(e.Value))
                .Select(e => e.Value.Trim())
                .ToArray();
            if (!string.IsNullOrEmpty(name) && urls.Length > 0)
                jobs.Add(new DownloadJob(name, urls));
        }
        return jobs;
    }

    public static IReadOnlyList<DownloadJob> ParseFile(string path) =>
        Parse(File.ReadAllText(path));
}
