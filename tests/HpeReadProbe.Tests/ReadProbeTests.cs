using System.Security.Cryptography;
using Xunit;
using Probe = HpeReadProbe.ReadProbe;

public class ReadProbeTests
{
    private static byte[] Data => Enumerable.Range(0, 4097).Select(i => (byte)(i % 251)).ToArray();

    [Theory]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(8192)]
    public void RequestSizeDoesNotChangeData(int request)
    {
        byte[] data = Data;
        using var source = new MemoryStream(data, false);
        var result = Probe.Measure(source, request, data.Length + 1);
        Assert.Equal(data.Length, result.BytesRead);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), result.Sha256);
        Assert.True(result.ObservedEof);
        Assert.Equal((data.Length + request - 1) / request + 1, result.ReadCalls);
    }

    [Fact]
    public void LimitDoesNotReadExtraBytesOrClaimEof()
    {
        using var source = new MemoryStream(Data, false);
        var result = Probe.Measure(source, 1024, 1500);
        Assert.Equal(1500, source.Position);
        Assert.Equal(2, result.ReadCalls);
        Assert.False(result.ObservedEof);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Data.AsSpan(0, 1500))), result.Sha256);
    }

    [Fact]
    public void EmptyFileHasEmptyHash()
    {
        using var source = new MemoryStream();
        var result = Probe.Measure(source, 64, 128);
        Assert.Equal(0, result.BytesRead);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())), result.Sha256);
        Assert.True(result.ObservedEof);
    }

    private sealed class ShortStream(byte[] data) : MemoryStream(data, false)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(7, count));
    }

    [Fact]
    public void ShortReadsAreNotMistakenForEof()
    {
        using var source = new ShortStream(Data);
        var result = Probe.Measure(source, 1024, 9000);
        Assert.Equal(Data.Length, result.BytesRead);
        Assert.True(result.ShortReads > 0);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Data)), result.Sha256);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(67108865, 1)]
    [InlineData(1, 0)]
    public void RejectsInvalidLimits(int request, long limit)
    {
        using var source = new MemoryStream(Data);
        Assert.Throws<ArgumentOutOfRangeException>(() => Probe.Measure(source, request, limit));
        Assert.Equal(0, source.Position);
    }

    [Fact]
    public void CancellationDoesNotRead()
    {
        using var source = new MemoryStream(Data);
        Assert.Throws<OperationCanceledException>(() => Probe.Measure(source, 64, 100, new CancellationToken(true)));
        Assert.Equal(0, source.Position);
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("test read failure");
    }

    [Fact]
    public void ReadFailureIsNotReportedAsSuccess()
    {
        using var source = new BrokenStream();
        Assert.Throws<IOException>(() => Probe.Measure(source, 64, 100));
    }

    [Theory]
    [InlineData(@"\\.\TAPE0")]
    [InlineData(@"\\?\GLOBALROOT\Device\Tape0")]
    public void RejectsRawDevicePaths(string path) => Assert.Throws<ArgumentException>(() => Probe.ValidatePath(path));
}
