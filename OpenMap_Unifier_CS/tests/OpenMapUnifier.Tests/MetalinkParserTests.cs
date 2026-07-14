using OpenMapUnifier.Bayern;
using Xunit;

namespace OpenMapUnifier.Tests;

public class MetalinkParserTests
{
    [Fact]
    public void Parse_KeepsEveryMirrorPerFile()
    {
        const string meta4 = """
            <?xml version="1.0" encoding="UTF-8"?>
            <metalink xmlns="urn:ietf:params:xml:ns:metalink">
              <file name="690_5334.gml">
                <size>157286400</size>
                <url>https://download1.bayernwolke.de/a/lod2/citygml/690_5334.gml</url>
                <url>https://download2.bayernwolke.de/a/lod2/citygml/690_5334.gml</url>
              </file>
              <file name="692_5334.gml">
                <url>https://download1.bayernwolke.de/a/lod2/citygml/692_5334.gml</url>
              </file>
            </metalink>
            """;
        var jobs = MetalinkParser.Parse(meta4);

        Assert.Equal(2, jobs.Count);
        Assert.Equal("690_5334.gml", jobs[0].FileName);
        Assert.Equal(2, jobs[0].Mirrors.Count);
        Assert.StartsWith("https://download1", jobs[0].Mirrors[0]);
        Assert.StartsWith("https://download2", jobs[0].Mirrors[1]);
        Assert.Single(jobs[1].Mirrors);
    }

    [Fact]
    public void Parse_SkipsFilesWithoutUrls()
    {
        const string meta4 = """
            <metalink><file name="empty.gml"></file></metalink>
            """;
        Assert.Empty(MetalinkParser.Parse(meta4));
    }
}
