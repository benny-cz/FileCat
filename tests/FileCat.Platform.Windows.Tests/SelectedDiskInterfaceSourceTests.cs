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

    [Fact]
    public void Volume_selection_distinguishes_byte_clones_on_different_disk_interfaces()
    {
        const string clonedGuid = "a75c39bd-652d-4efb-871e-6a4a1e93e127";
        var original = new PhysicalDisk(7, 64 * 1024 * 1024, "A", "virtual", false, [], clonedGuid, @"\\?\SCSI#FileCat#A");
        var replacement = original with { Number = 7, Model = "B", InterfacePath = @"\\?\SCSI#FileCat#B" };

        Assert.NotNull(SelectedDiskInterfaceSource.VolumeSelectionIdentity(original));
        Assert.NotEqual(SelectedDiskInterfaceSource.VolumeSelectionIdentity(original),
            SelectedDiskInterfaceSource.VolumeSelectionIdentity(replacement));
        Assert.Null(SelectedDiskInterfaceSource.VolumeSelectionIdentity(original with { Identity = null }));
        Assert.Null(SelectedDiskInterfaceSource.VolumeSelectionIdentity(original with { InterfacePath = null }));
        Assert.Null(SelectedDiskInterfaceSource.VolumeSelectionIdentity(original with { Length = 0 }));
    }

    [Fact]
    public void Volume_selection_distinguishes_same_interface_and_guid_with_changed_disk_length()
    {
        const string clonedGuid = "a75c39bd-652d-4efb-871e-6a4a1e93e127";
        var selected = new PhysicalDisk(7, 64 * 1024 * 1024, "A", "virtual", false, [], clonedGuid, @"\\?\SCSI#FileCat#slot7");
        var replacement = selected with { Length = 128 * 1024 * 1024, Model = "B" };

        Assert.NotEqual(SelectedDiskInterfaceSource.VolumeSelectionIdentity(selected),
            SelectedDiskInterfaceSource.VolumeSelectionIdentity(replacement));
    }

    [Fact]
    public void Volume_selection_distinguishes_same_interface_guid_and_length_with_changed_disk_number()
    {
        const string clonedGuid = "a75c39bd-652d-4efb-871e-6a4a1e93e127";
        var selected = new PhysicalDisk(7, 64 * 1024 * 1024, "A", "virtual", false, [], clonedGuid, @"\\?\SCSI#FileCat#slot7");
        var replacement = selected with { Number = 8, Model = "B" };

        Assert.NotEqual(SelectedDiskInterfaceSource.VolumeSelectionIdentity(selected),
            SelectedDiskInterfaceSource.VolumeSelectionIdentity(replacement));
    }

    [Fact]
    public void Whole_disk_selection_distinguishes_same_interface_guid_and_length_with_changed_disk_number()
    {
        const string clonedGuid = "a75c39bd-652d-4efb-871e-6a4a1e93e127";
        var selected = new PhysicalDisk(7, 64 * 1024 * 1024, "A", "virtual", false, [], clonedGuid, @"\\?\SCSI#FileCat#slot7");
        var replacement = selected with { Number = 8, Model = "B" };

        Assert.NotNull(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(selected));
        Assert.NotEqual(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(selected),
            SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(replacement));
        Assert.Null(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(selected with { Number = -1 }));
        Assert.Null(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(selected with { Identity = null }));
        Assert.Null(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(selected with { InterfacePath = null }));
        Assert.Null(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(selected with { Length = 0 }));
    }

    [Fact]
    public void Disposable_virtual_volume_reports_the_selected_backing_interface_from_its_held_reader()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows volume interfaces are required.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_I487_VIRTUAL_VOLUME");
        if (string.IsNullOrEmpty(root)) Assert.Skip("Set FILECAT_I487_VIRTUAL_VOLUME to an owned disposable VHD volume root.");
        string device = DeviceTopology.VolumeDevice(root!) ?? throw new IOException("The disposable volume has no GUID path.");
        var numbers = DeviceTopology.DisksOf(device);
        Assert.NotNull(numbers);
        int number = Assert.Single(numbers);
        var selected = Assert.Single(DeviceTopology.Disks(), disk => disk.Number == number);
        string expected = Assert.IsType<string>(SelectedDiskInterfaceSource.VolumeSelectionIdentity(selected));

        using var bound = Assert.IsType<SelectedDiskInterfaceSource>(SelectedDiskInterfaceSource.Bind(
            device, DirectDeviceSource.Open(device, "disposable virtual volume")));
        Assert.Equal(expected, bound.SelectionIdentity);
        Assert.True(bound.IsCurrentDevicePath(device));
    }

    [Fact]
    public void Disposable_virtual_disk_reports_the_selected_number_from_its_held_reader()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows disk interfaces are required.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_I487_VIRTUAL_VOLUME");
        if (string.IsNullOrEmpty(root)) Assert.Skip("Set FILECAT_I487_VIRTUAL_VOLUME to an owned disposable VHD volume root.");
        string device = DeviceTopology.VolumeDevice(root!) ?? throw new IOException("The disposable volume has no GUID path.");
        int number = Assert.Single(DeviceTopology.DisksOf(device) ?? throw new IOException("The disposable volume has no backing disk."));
        var selected = Assert.Single(DeviceTopology.Disks(), disk => disk.Number == number);
        string interfacePath = Assert.IsType<string>(selected.InterfacePath);
        string expected = Assert.IsType<string>(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(selected));

        using var bound = Assert.IsType<SelectedDiskInterfaceSource>(SelectedDiskInterfaceSource.Bind(
            interfacePath, DirectDeviceSource.Open(interfacePath, "disposable virtual disk")));
        Assert.Equal(expected, bound.SelectionIdentity);
        Assert.True(bound.IsCurrentDevicePath(interfacePath));
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
