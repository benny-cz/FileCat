using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed class FileSystemCapacityTests
{
    [Theory]
    [InlineData("/home/user", "/")]
    [InlineData("/media/usb/photos", "/media/usb")]
    [InlineData("/media/usb", "/media/usb")]
    [InlineData("/media/usbx/photos", "/media/usbx")]
    [InlineData("/media/usb/nested/file", "/media/usb/nested")]
    public void Unix_mount_selection_uses_the_longest_containing_mount(string path, string expected)
    {
        string[] mounts = ["/", "/media/usb/", "/media/usbx", "/media/usb/nested"];
        string root = FileSystemCapacity.SelectUnixMountRoot(path, mounts) ?? throw new Xunit.Sdk.XunitException("No mount selected.");
        root = root.TrimEnd('/');
        if (root.Length == 0) root = "/";
        Assert.Equal(expected, root);
    }

    [Fact]
    public void Windows_shares_have_distinct_capacity_roots_even_on_one_server()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows UNC roots are parsed by the Windows filesystem runtime."); return; }
        string first = FileSystemCapacity.WindowsVolumeRoot(@"\\files.example\team\docs")!;
        string second = FileSystemCapacity.WindowsVolumeRoot(@"\\files.example\backup\docs")!;
        Assert.NotEqual(first, second);
        Assert.Equal(@"\\files.example\team\", first, ignoreCase: true);
        Assert.Equal(@"\\files.example\backup\", second, ignoreCase: true);
    }

    [Fact]
    public void Actual_directory_capacity_has_a_resolved_volume_identity_and_nonnegative_available_bytes()
    {
        using var dir = new TempDir();
        var result = FileSystemCapacity.Query(dir.Path);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.VolumeIdentity));
        Assert.True(result.AvailableBytes >= 0);
    }
}
