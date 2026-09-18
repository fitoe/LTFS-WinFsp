namespace LTFS.WinFsp.Core;

public sealed class ReadOnlyVolume
{
    public ReadOnlyVolume(string label, VolumeDirectory root)
    {
        Label = string.IsNullOrWhiteSpace(label) ? "LTFS" : label;
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public string Label { get; }

    public VolumeDirectory Root { get; }
}

public abstract record VolumeEntry(string Name, DateTimeOffset ModifiedAt);

public sealed record VolumeDirectory(
    string Name,
    DateTimeOffset ModifiedAt,
    IReadOnlyList<VolumeEntry> Children) : VolumeEntry(Name, ModifiedAt);

public sealed record VolumeFile(
    string Name,
    DateTimeOffset ModifiedAt,
    long Length,
    IReadOnlyList<FileExtent> Extents) : VolumeEntry(Name, ModifiedAt);

public readonly record struct FileExtent(byte Partition, long StartBlock, int ByteOffset, long Length);
