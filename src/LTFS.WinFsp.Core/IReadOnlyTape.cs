namespace LTFS.WinFsp.Core;

/// <summary>
/// Minimal, deliberately immutable tape interface used by the LTFS reader.
/// Mutating SCSI operations do not belong in this contract.
/// </summary>
public interface IReadOnlyTape : IAsyncDisposable
{
    string DeviceId { get; }

    ValueTask<TapePosition> GetPositionAsync(CancellationToken cancellationToken = default);

    ValueTask LocateAsync(TapePosition position, CancellationToken cancellationToken = default);

    ValueTask<int> ReadBlockAsync(Memory<byte> destination, CancellationToken cancellationToken = default);
}

public readonly record struct TapePosition(byte Partition, long Block);
