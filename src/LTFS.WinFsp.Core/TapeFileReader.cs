namespace LTFS.WinFsp.Core;

/// <summary>Own one reader per tape. Serializes locate/read pairs across files.</summary>
public sealed class TapeFileReader
{
    private readonly IReadOnlyTape tape;
    private readonly int blockSize;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly byte[] buffer;
    private TapePosition? nextPosition;
    private TapePosition? cachedPosition;
    private int cachedLength;

    public TapeFileReader(IReadOnlyTape tape, int blockSize)
    {
        this.tape = tape ?? throw new ArgumentNullException(nameof(tape));
        if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
        this.blockSize = blockSize;
        buffer = new byte[blockSize];
    }

    public async ValueTask<int> ReadAsync(VolumeFile file, long offset, Memory<byte> destination, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (file.Length < 0) throw new InvalidDataException("Negative file length.");
        long total = 0;
        foreach (var extent in file.Extents)
        {
            if (extent.StartBlock < 0 || extent.ByteOffset < 0 || extent.ByteOffset >= blockSize || extent.Length < 0)
                throw new InvalidDataException("Invalid extent.");
            total = checked(total + extent.Length);
        }
        if (total != file.Length) throw new InvalidDataException("Extents do not cover the file.");
        token.ThrowIfCancellationRequested();
        if (offset >= file.Length || destination.IsEmpty) return 0;
        int wanted = (int)Math.Min(destination.Length, file.Length - offset);
        await gate.WaitAsync(token);
        try
        {
            int copied = 0;
            long skip = offset;
            foreach (var extent in file.Extents)
            {
                if (skip >= extent.Length) { skip -= extent.Length; continue; }
                long physical = checked(extent.ByteOffset + skip);
                long block = checked(extent.StartBlock + physical / blockSize);
                int within = (int)(physical % blockSize);
                long remaining = extent.Length - skip;
                while (remaining > 0 && copied < wanted)
                {
                    token.ThrowIfCancellationRequested();
                    var requested = new TapePosition(extent.Partition, block);
                    if (cachedPosition != requested)
                    {
                        cachedPosition = null;
                        if (nextPosition != requested) await tape.LocateAsync(requested, token);
                        cachedLength = await tape.ReadBlockAsync(buffer, token);
                        nextPosition = requested with { Block = checked(block + 1) };
                        cachedPosition = requested;
                    }
                    int actual = cachedLength;
                    int count = (int)Math.Min(Math.Min(blockSize - within, remaining), wanted - copied);
                    if (actual < within + count) throw new EndOfStreamException("Truncated tape block.");
                    buffer.AsMemory(within, count).CopyTo(destination[copied..]);
                    copied += count;
                    remaining -= count;
                    block = checked(block + 1);
                    within = 0;
                }
                if (copied == wanted) break;
                skip = 0;
            }
            return copied;
        }
        catch { cachedPosition = nextPosition = null; throw; }
        finally { gate.Release(); }
    }
}
