using LTFS.WinFsp.Core;

namespace LTFS.WinFsp.Core.Tests;

public sealed class TapeFileReaderTests
{
    [Fact]
    public async Task SmallSequentialReadsReuseBlockAndAvoidRepeatedLocates()
    {
        await using var tape = Tape();
        var reader = new TapeFileReader(tape, 4);
        var file = new VolumeFile("sequential", DateTimeOffset.UnixEpoch, 8, [new(0, 0, 0, 8)]);
        for (int i = 0; i < 8; i++)
        {
            byte[] result = new byte[1];
            Assert.Equal(1, await reader.ReadAsync(file, i, result));
            Assert.Equal((byte)i, result[0]);
        }
        Assert.Equal(1, tape.LocateCount);
        Assert.Equal(2, tape.ReadCount);
    }
    private static SimulatedTape Tape() => new(new Dictionary<TapePosition, byte[]>
    {
        [new(0, 0)] = [0, 1, 2, 3], [new(0, 1)] = [4, 5, 6, 7],
        [new(1, 5)] = [8, 9, 10, 11]
    });
    private static VolumeFile File() => new("example", DateTimeOffset.UnixEpoch, 7,
        [new(0, 0, 2, 5), new(1, 5, 1, 2)]);

    [Fact]
    public async Task ReadsAcrossBlocksAndPartitionsWithOffsetAndEof()
    {
        await using var tape = Tape();
        var reader = new TapeFileReader(tape, 4);
        byte[] result = new byte[20];
        Assert.Equal(6, await reader.ReadAsync(File(), 1, result));
        Assert.Equal(new byte[] { 3, 4, 5, 6, 9, 10 }, result[..6]);
        Assert.Equal(0, await reader.ReadAsync(File(), 7, result));
    }

    [Fact]
    public async Task ConcurrentReadsDoNotInterfereWithTapePosition()
    {
        await using var tape = Tape();
        tape.Latency = TimeSpan.FromMilliseconds(1);
        var reader = new TapeFileReader(tape, 4);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            byte[] result = new byte[7];
            Assert.Equal(7, await reader.ReadAsync(File(), 0, result));
            Assert.Equal(new byte[] { 2, 3, 4, 5, 6, 9, 10 }, result);
        }));
    }

    [Fact]
    public async Task FailureAndCancellationReleaseReaderForRetry()
    {
        await using var tape = Tape();
        var reader = new TapeFileReader(tape, 4);
        tape.Online = false;
        await Assert.ThrowsAsync<IOException>(async () => await reader.ReadAsync(File(), 0, new byte[7]));
        tape.Online = true;
        tape.FailAt = new(0, 1);
        await Assert.ThrowsAsync<IOException>(async () => await reader.ReadAsync(File(), 0, new byte[7]));
        tape.FailAt = null;
        tape.Latency = TimeSpan.FromSeconds(1);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadAsync(File(), 0, new byte[7], cancellation.Token));
        tape.Latency = TimeSpan.Zero;
        Assert.Equal(7, await reader.ReadAsync(File(), 0, new byte[7]));
    }

    [Fact]
    public async Task InvalidExtentAndTruncatedBlocksAreRejected()
    {
        await using var tape = Tape();
        var reader = new TapeFileReader(tape, 4);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadAsync(File() with { Length = 99 }, 0, new byte[7]));
        await using var shortTape = new SimulatedTape(new Dictionary<TapePosition, byte[]> { [new(0, 0)] = [0] });
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await new TapeFileReader(shortTape, 4).ReadAsync(File(), 0, new byte[7]));
    }
}
