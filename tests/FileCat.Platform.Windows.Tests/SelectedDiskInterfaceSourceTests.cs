using FileCat.Platform.Windows.Recovery;
using FileCat.Recovery;

namespace FileCat.Platform.Windows.Tests;

public sealed class SelectedDiskInterfaceSourceTests
{
    [Fact]
    public void Missing_selected_disk_interface_refuses_and_closes_its_reader()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows disk interfaces are required.");
        var source = new Reader("fixture-identity");
        string missing = @"\\?\SCSI#Disk&Ven_FileCat&Prod_Missing#not-a-present-device#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}";

        Assert.Throws<IOException>(() => SelectedDiskInterfaceSource.Bind(missing, source));
        Assert.Equal(1, source.Disposals);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void Unverifiable_volume_backing_refuses_and_closes_its_reader()
    {
        var source = new Reader(null);
        Assert.Throws<IOException>(() => SelectedDiskInterfaceSource.Bind(
            @"\\?\Volume{12345678-1234-1234-1234-123456789abc}", source));

        Assert.Equal(1, source.Disposals);
        Assert.Equal(0, source.Reads);
    }

    private sealed class Reader(string? identity) : IBlockSource, IDeviceSelectionIdentity
    {
        public string Description => "owned reader fixture";
        public long Length => 4096;
        public string? SelectionIdentity => identity;
        public int Disposals { get; private set; }
        public int Reads { get; private set; }
        public int Read(long offset, Span<byte> buffer) { Reads++; return 0; }
        public void Dispose() => Disposals++;
    }
}
