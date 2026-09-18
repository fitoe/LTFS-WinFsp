using LTFS.WinFsp.Core;

namespace LTFS.WinFsp.Windows;

public interface ITapeVolumeTransport : IReadOnlyTape
{
    ValueTask<bool> CheckReadyAsync(CancellationToken token = default);
    ValueTask SeekEndAsync(byte partition, CancellationToken token = default);
    ValueTask SpaceFilemarksAsync(int count, CancellationToken token = default);
}

/// <summary>Conservative clean-media mount. Rejects incomplete terminal indexes; performs no repair.</summary>
public static class TapeVolumeLoader
{
    public static async Task<(ReadOnlyVolume Volume, int BlockSize)> LoadAsync(ITapeVolumeTransport tape, CancellationToken token)
    {
        await tape.CheckReadyAsync(token);
        var first = await ReadLabelAsync(tape, 1, token);
        var second = await ReadLabelAsync(tape, 2, token);
        var mapping = LtfsMetadata.ValidateLabels(first, second);
        var a = await ReadTerminalIndexAsync(tape, 1, first, token);
        var b = await ReadTerminalIndexAsync(tape, 2, second, token);
        byte[] latest = LtfsMetadata.SelectLatest(first, a, b);
        using var stream = new MemoryStream(latest, false);
        return (LtfsIndexReader.Read(stream, mapping, first.BlockSize), first.BlockSize);
    }

    private static async Task<LtfsLabel> ReadLabelAsync(ITapeVolumeTransport tape, byte partition, CancellationToken token)
    {
        await tape.LocateAsync(new(partition, 0), token);
        var buffer = new byte[1024 * 1024];
        // VOL1, filemark, XML label. Verify every field before deriving the mapping.
        int length = await tape.ReadBlockAsync(buffer, token);
        if (length != 80 || System.Text.Encoding.ASCII.GetString(buffer, 0, 4) != "VOL1")
            throw new InvalidDataException("Missing 80-byte VOL1 record.");
        try { await tape.ReadBlockAsync(buffer, token); throw new InvalidDataException("Expected filemark after VOL1."); }
        catch (TapeDeviceException ex) when (ex.IsFilemark) { }
        length = await tape.ReadBlockAsync(buffer, token);
        var label = LtfsLabel.Parse(buffer[..length]);
        try { await tape.ReadBlockAsync(buffer, token); throw new InvalidDataException("Expected filemark after LTFS label."); }
        catch (TapeDeviceException ex) when (ex.IsFilemark) { }
        return label;
    }

    private static async Task<IndexCandidate> ReadTerminalIndexAsync(ITapeVolumeTransport tape, byte partition, LtfsLabel label, CancellationToken token)
    {
        await tape.LocateAsync(new(partition, 0), token);
        await tape.SeekEndAsync(partition, token);
        var end = await tape.GetPositionAsync(token);
        if (end.Block < 5 || end.Partition != partition) throw new InvalidDataException("Invalid partition end position.");
        // Clean LTFS partitions end in [index records][filemark][EOD]. First
        // verify that final filemark; never silently skip an incomplete tail.
        await tape.LocateAsync(new(partition, end.Block - 1), token);
        byte[] buffer = new byte[label.BlockSize];
        try { await tape.ReadBlockAsync(buffer, token); throw new InvalidDataException("Partition has an uncommitted tail; recovery is required."); }
        catch (TapeDeviceException ex) when (ex.IsFilemark) { }
        await tape.SpaceFilemarksAsync(-2, token);
        await tape.SpaceFilemarksAsync(1, token);
        var start = await tape.GetPositionAsync(token);
        if (start.Partition != partition || start.Block < 4 || start.Block >= end.Block - 1)
            throw new InvalidDataException("Invalid terminal index position.");
        using var xml = new MemoryStream();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                int read = await tape.ReadBlockAsync(buffer, token);
                if (read == 0 || xml.Length + read > 64 * 1024 * 1024) throw new InvalidDataException("Index exceeds supported size or has empty record.");
                xml.Write(buffer, 0, read);
            }
            catch (TapeDeviceException ex) when (ex.IsFilemark) { break; }
        }
        var after = await tape.GetPositionAsync(token);
        if (after != end) throw new InvalidDataException("Terminal index does not end at EOD.");
        return new(xml.ToArray(), label.Partition, start.Block);
    }
}
