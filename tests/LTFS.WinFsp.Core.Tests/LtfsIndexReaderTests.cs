using System.Text;
using System.Xml;
using LTFS.WinFsp.Core;

namespace LTFS.WinFsp.Core.Tests;

public sealed class LtfsIndexReaderTests
{
    private const string Index = """
        <ltfsindex version="2.4.0"><directory><name>Test tape</name><contents>
        <file><name>example.bin</name><length>10</length><modifytime>2026-01-01T00:00:00.123456789Z</modifytime><extentinfo><extent>
        <partition>b</partition><startblock>5</startblock><byteoffset>2</byteoffset><bytecount>10</bytecount><fileoffset>0</fileoffset>
        </extent></extentinfo></file></contents></directory></ltfsindex>
        """;
    private static ReadOnlyVolume Read(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return LtfsIndexReader.Read(stream, new Dictionary<string, byte> { ["b"] = 1 }, 4);
    }
    [Fact]
    public void ReadsPartitionMappingAndNanosecondTimestamp()
    {
        var volume = Read(Index);
        var file = Assert.IsType<VolumeFile>(Assert.Single(volume.Root.Children));
        Assert.Equal(new FileExtent(1, 5, 2, 10), Assert.Single(file.Extents));
        Assert.Equal("Test tape", volume.Label);
        Assert.Equal(2026, file.ModifiedAt.Year);
    }
    [Theory]
    [InlineData("example.bin", "../escape")]
    [InlineData("example.bin", "CON")]
    [InlineData("<fileoffset>0", "<fileoffset>1")]
    [InlineData("<length>10", "<length>11")]
    [InlineData("<partition>b", "<partition>x")]
    [InlineData("<byteoffset>2", "<byteoffset>4")]
    public void RejectsUnsafeOrUnsupportedIndex(string from, string to)
        => Assert.Throws<InvalidDataException>(() => Read(Index.Replace(from, to)));
    [Fact]
    public void RejectsDtd() => Assert.Throws<XmlException>(() => Read("<!DOCTYPE ltfsindex [<!ENTITY e SYSTEM 'file:///secret'>]>" + Index));
}
