namespace LTFS.WinFsp.Core;

/// <summary>A deterministic tape for tests; never opens a physical device.</summary>
public sealed class SimulatedTape : IReadOnlyTape
{
    private readonly Dictionary<TapePosition, byte[]> blocks;
    private TapePosition position;
    private bool disposed;
    public string DeviceId => "SIMULATED";
    public bool Online { get; set; } = true;
    public TapePosition? FailAt { get; set; }
    public TimeSpan Latency { get; set; }
    public int LocateCount { get; private set; }
    public int ReadCount { get; private set; }

    public SimulatedTape(IEnumerable<KeyValuePair<TapePosition, byte[]>> blocks)
        => this.blocks = blocks.ToDictionary(p => p.Key, p => p.Value.ToArray());

    private async ValueTask CheckAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        if (Latency > TimeSpan.Zero) await Task.Delay(Latency, token);
        if (!Online) throw new IOException("Simulated tape is offline.");
    }

    public async ValueTask<TapePosition> GetPositionAsync(CancellationToken cancellationToken = default)
    {
        await CheckAsync(cancellationToken);
        return position;
    }

    public async ValueTask LocateAsync(TapePosition target, CancellationToken cancellationToken = default)
    {
        if (target.Block < 0) throw new ArgumentOutOfRangeException(nameof(target));
        await CheckAsync(cancellationToken);
        position = target;
        LocateCount++;
    }

    public async ValueTask<int> ReadBlockAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        await CheckAsync(cancellationToken);
        if (FailAt == position) throw new IOException("Injected tape read failure.");
        if (!blocks.TryGetValue(position, out var block)) throw new EndOfStreamException("No block at tape position.");
        if (destination.Length < block.Length) throw new ArgumentException("Buffer must fit a complete block.", nameof(destination));
        block.CopyTo(destination);
        ReadCount++;
        position = position with { Block = checked(position.Block + 1) };
        return block.Length;
    }

    public ValueTask DisposeAsync() { disposed = true; return ValueTask.CompletedTask; }
}
