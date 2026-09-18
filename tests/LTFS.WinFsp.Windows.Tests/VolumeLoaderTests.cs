using System.Text;
using LTFS.WinFsp.Core;
using LTFS.WinFsp.Windows;
using Xunit;

public sealed class VolumeLoaderTests
{
    private const string Id = "93302d5f-4e6c-4af2-b341-e56ac591b611";
    private static byte[] Label(string part) => Encoding.UTF8.GetBytes($"""
        <ltfslabel version="2.4.0"><volumeuuid>{Id}</volumeuuid><location><partition>{part}</partition></location>
        <partitions><index>a</index><data>b</data></partitions><blocksize>65536</blocksize></ltfslabel>
        """);
    private static byte[] Index(string part, int generation, string name = "volume") => Encoding.UTF8.GetBytes($"""
        <ltfsindex version="2.4.0"><volumeuuid>{Id}</volumeuuid><generationnumber>{generation}</generationnumber>
        <location><partition>{part}</partition><startblock>4</startblock></location>
        <directory><name>{name}</name><contents/></directory></ltfsindex>
        """);

    [Fact]
    public async Task ReadsBothLabelsAndSelectsNewerCommittedIndex()
    {
        await using var tape = new FakeTape();
        tape.Records[2][4] = Index("b", 2, "new");
        var result = await TapeVolumeLoader.LoadAsync(tape, default);
        Assert.Equal("new", result.Volume.Label);
        Assert.Equal(65536, result.BlockSize);
    }
    [Fact]
    public async Task RefusesIncompleteTailInsteadOfUsingOlderIndex()
    {
        await using var tape = new FakeTape();
        tape.Records[2].Add([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => TapeVolumeLoader.LoadAsync(tape, default));
    }
    [Fact]
    public async Task RefusesConflictingSameGeneration()
    {
        await using var tape = new FakeTape();
        tape.Records[2][4] = Index("b", 1, "conflict");
        await Assert.ThrowsAsync<InvalidDataException>(() => TapeVolumeLoader.LoadAsync(tape, default));
    }
    [Fact]
    public async Task RejectsForeignIndex()
    {
        await using var tape = new FakeTape();
        tape.Records[2][4] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Index("b", 2)).Replace(Id, Guid.NewGuid().ToString()));
        await Assert.ThrowsAsync<InvalidDataException>(() => TapeVolumeLoader.LoadAsync(tape, default));
    }
    [Fact]
    public async Task RejectsWrongSelfLocation()
    {
        await using var tape = new FakeTape();
        tape.Records[2][4] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Index("b", 2)).Replace("<startblock>4", "<startblock>9"));
        await Assert.ThrowsAsync<InvalidDataException>(() => TapeVolumeLoader.LoadAsync(tape, default));
    }
    [Fact]
    public async Task RejectsDuplicatePartitionLabels()
    {
        await using var tape = new FakeTape();
        tape.Records[2][2] = Label("a");
        await Assert.ThrowsAsync<InvalidDataException>(() => TapeVolumeLoader.LoadAsync(tape, default));
    }
    [Fact]
    public async Task CancellationStopsDiscovery()
    {
        await using var tape = new FakeTape();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TapeVolumeLoader.LoadAsync(tape, stop.Token));
    }
    [Fact]
    public void MapsWindowsPartitionsFromLabelsNotAlphabeticalOrder()
    {
        var mapping = LtfsMetadata.ValidateLabels(LtfsLabel.Parse(Label("b")), LtfsLabel.Parse(Label("a")));
        Assert.Equal((byte)1, mapping["b"]);
        Assert.Equal((byte)2, mapping["a"]);
    }

    private sealed class FakeTape : ITapeVolumeTransport
    {
        public Dictionary<byte, List<byte[]?>> Records { get; } = new()
        {
            [1] = [Encoding.ASCII.GetBytes("VOL1".PadRight(80)), null, Label("a"), null, Index("a", 1), null],
            [2] = [Encoding.ASCII.GetBytes("VOL1".PadRight(80)), null, Label("b"), null, Index("b", 1), null]
        };
        private TapePosition position = new(1, 0);
        public string DeviceId => "FAKE";
        public ValueTask<bool> CheckReadyAsync(CancellationToken token = default) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(true); }
        public ValueTask<TapePosition> GetPositionAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(position);
        public ValueTask LocateAsync(TapePosition target, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); position = target; return ValueTask.CompletedTask; }
        public ValueTask SeekEndAsync(byte partition, CancellationToken token = default) { position = new(partition, Records[partition].Count); return ValueTask.CompletedTask; }
        public ValueTask SpaceFilemarksAsync(int count, CancellationToken token = default)
        {
            var records = Records[position.Partition];
            long block = position.Block;
            while (count < 0)
            {
                do { if (--block < 0) throw new IOException("BOT"); } while (records[(int)block] != null);
                count++;
            }
            while (count > 0)
            {
                while (records[(int)block++] != null) { }
                count--;
            }
            position = position with { Block = block };
            return ValueTask.CompletedTask;
        }
        public ValueTask<int> ReadBlockAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = Records[position.Partition][(int)position.Block];
            position = position with { Block = position.Block + 1 };
            if (data == null) throw new TapeDeviceException(1101, "filemark");
            data.CopyTo(buffer); return ValueTask.FromResult(data.Length);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
