using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class WindowsContextMenuTests
{
    [Fact]
    public async Task Helper_stderr_is_fully_drained_while_only_the_logged_prefix_is_retained()
    {
        const int retained = 240;
        string prefix = new('A', retained);
        byte[] data = System.Text.Encoding.UTF8.GetBytes(prefix + new string('B', 4 << 20) + "END");
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new StreamReader(stream);

        string details = await WindowsContextMenu.ReadErrorPrefixAsync(reader);

        Assert.Equal(prefix, details);
        Assert.Equal(data.Length, stream.Position);
    }

    [Fact]
    public void Files_in_one_folder_use_the_shell_even_when_access_checks_would_fail()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows Shell menus are Windows-only.");
        string folder = Path.Combine(Path.GetTempPath(), "filecat-menu-check");
        Assert.True(WindowsContextMenu.CanShow([Path.Combine(folder, "first"), Path.Combine(folder, "second")]));
        Assert.False(WindowsContextMenu.CanShow([Path.Combine(folder, "first"), Path.Combine(folder, "subfolder", "second")]));
        Assert.False(WindowsContextMenu.CanShow([Path.Combine(folder, "bad\0name")]));
    }
}
