using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;

namespace FileCat.Core.Tests;

// Package route tests (route decision of 12 November 2026): the AppImage mounts its image on squashfs over FUSE, which is
// not a qualified place for the confined archive worker. Writing from it is unavailable and says what to do instead.
public sealed class LinuxArchivePackageRouteTests
{
    // The installed .deb's /opt/filecat must pass every check a confined worker start makes (ext4, a private
    // self-contained bundle, its size and inventory budgets); the reason is reported when it does not.
    [Fact]
    public void The_installed_deb_is_a_worker_location()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux package route lane.");
        string? installed = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_DEB_APP");
        if (string.IsNullOrEmpty(installed)) Assert.Skip("Run with the .deb installed (package route lane).");
        Assert.Null(LinuxArchiveIsolation.BundleProblem(installed));
    }

    [Fact]
    public void The_mounted_AppImage_is_not_a_worker_location_and_says_what_to_do()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux package route lane.");
        string? mounted = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_APPIMAGE_APP");
        string? image = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_APPIMAGE");
        if (string.IsNullOrEmpty(mounted) || string.IsNullOrEmpty(image)) Assert.Skip("Run with a mounted AppImage (package route lane).");
        Assert.True(File.Exists(mounted) && File.Exists(image), "The mounted AppImage executable and its image are required.");
        // The AppImage runtime sets APPIMAGE for the application it starts; this test stands in for that application.
        string? previous = Environment.GetEnvironmentVariable("APPIMAGE");
        Environment.SetEnvironmentVariable("APPIMAGE", image);
        try
        {
            using var temp = new TempDir();
            var backend = LinuxArchiveJobBackend.Create(ArchiveWriteFormat.Tar, mounted);
            var availability = backend.Availability(new ArchivePlan(Path.Combine(temp.Path, "route.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)));
            Assert.False(availability.Available);
            Assert.Contains("not available from the AppImage", availability.Reason);
            Assert.Contains(".deb", availability.Reason);
        }
        finally { Environment.SetEnvironmentVariable("APPIMAGE", previous); }
    }
}
