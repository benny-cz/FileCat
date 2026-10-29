using FileCat.Platform.Windows.Shell;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class LocalIconFileLeaseTests
{
    [Fact]
    public void Local_icon_path_is_bound_and_a_linked_component_is_refused()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows NT path handles are required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-icon-lease-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
            string icon = Path.Combine(folder, "owned.ico");
            File.WriteAllBytes(icon, [0, 1, 2, 3]);
            using (var lease = LocalIconFileLease.TryOpen(icon))
            {
                Assert.NotNull(lease);
                Assert.ThrowsAny<IOException>(() => File.Move(icon, icon + ".moved"));
            }
            using (var directoryLease = LocalIconFileLease.TryOpenDirectory(folder))
            {
                Assert.NotNull(directoryLease);
                Assert.ThrowsAny<IOException>(() => Directory.Move(folder, folder + ".moved"));
            }
            File.Move(icon, icon + ".moved");
            File.Move(icon + ".moved", icon);

            string fileLink = Path.Combine(root, "file-link.ico");
            string folderLink = Path.Combine(root, "folder-link");
            try
            {
                File.CreateSymbolicLink(fileLink, icon);
                Directory.CreateSymbolicLink(folderLink, folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Creating owned symbolic links is unavailable: " + ex.GetType().Name);
            }
            Assert.Null(LocalIconFileLease.TryOpen(fileLink));
            Assert.Null(LocalIconFileLease.TryOpen(Path.Combine(folderLink, "owned.ico")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void An_ordinary_fixed_drive_system_icon_still_extracts()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows native icons required.");
        string library = Path.Combine(Environment.SystemDirectory, "shell32.dll");
        Assert.True(File.Exists(library));
        Assert.True(WindowsIcons.TryExtract(new IconLocation(library, 0), 16,
            out int width, out int height, out byte[] pixels));
        Assert.True(width > 0 && height > 0);
        Assert.Equal(width * height * 4, pixels.Length);
    }
}
