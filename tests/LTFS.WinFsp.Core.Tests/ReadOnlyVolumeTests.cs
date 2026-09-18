using LTFS.WinFsp.Core;

namespace LTFS.WinFsp.Core.Tests;

public sealed class ReadOnlyVolumeTests
{
    [Fact]
    public void EmptyLabelUsesSafeDefault()
    {
        var root = new VolumeDirectory(string.Empty, DateTimeOffset.UnixEpoch, []);
        var volume = new ReadOnlyVolume(" ", root);

        Assert.Equal("LTFS", volume.Label);
        Assert.Same(root, volume.Root);
    }
}
