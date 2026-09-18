using LTFS.WinFsp.Windows;
using Xunit;

public sealed class DiscoveryTests
{
    [Fact]
    public void EnumerationOnlyReturnsTapeNames()
    {
        foreach (var device in WindowsTape.EnumerateDevices())
            Assert.Matches("^(?i:Tape)[0-9]+$", device);
    }

    [Theory]
    [InlineData("PhysicalDrive0")]
    [InlineData("C:\\important.txt")]
    [InlineData("Tape0\\other")]
    [InlineData("")]
    public void RejectsNonTapePathsBeforeOpeningAnything(string name)
        => Assert.Throws<ArgumentException>(() => new WindowsTape(name));

    [Fact]
    public void FilemarkIsNotEndOfData()
    {
        var filemark = new TapeDeviceException(1101, "read");
        Assert.True(filemark.IsFilemark);
        Assert.False(filemark.IsEndOfData);
        var end = new TapeDeviceException(1104, "read");
        Assert.True(end.IsEndOfData);
        Assert.False(end.IsFilemark);
    }
}
