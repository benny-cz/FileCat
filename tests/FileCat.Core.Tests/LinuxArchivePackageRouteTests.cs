using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;

namespace FileCat.Core.Tests;

// Package route tests (route decision of 12 November 2026): the AppImage mounts its image on squashfs over FUSE, which is
// not a qualified place for the confined archive worker. Writing from it is unavailable and says what to do instead.
// Only a bundle inside the AppImage's mount (APPDIR) is the AppImage's: its runtime also exports APPDIR and APPIMAGE to
// every process the application starts, and a .deb FileCat started from one of those is not refused for it (review
// finding of 12 November 2026).
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
        // Started from a process an AppImage started: its APPDIR and APPIMAGE do not make the .deb an AppImage.
        using var temp = new TempDir();
        using (new Variables(("APPIMAGE", Path.Combine(temp.Path, "Other.AppImage")), ("APPDIR", temp.Path)))
            LinuxArchiveIsolation.ValidateBundle(installed);
    }

    [Fact]
    public void The_mounted_AppImage_is_not_a_worker_location_and_says_what_to_do()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux package route lane.");
        string? mounted = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_APPIMAGE_APP");
        string? image = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_APPIMAGE");
        string? mount = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_APPIMAGE_DIR");
        if (string.IsNullOrEmpty(mounted) || string.IsNullOrEmpty(image) || string.IsNullOrEmpty(mount))
            Assert.Skip("Run with a mounted AppImage (package route lane).");
        Assert.True(File.Exists(mounted) && File.Exists(image) && Directory.Exists(mount), "The mounted AppImage, its image and its mount are required.");
        // The AppImage runtime sets APPIMAGE and APPDIR for the application it starts; this test stands in for that application.
        using (new Variables(("APPIMAGE", image), ("APPDIR", mount)))
        {
            using var temp = new TempDir();
            var backend = LinuxArchiveJobBackend.Create(ArchiveWriteFormat.Tar, mounted);
            var availability = backend.Availability(new ArchivePlan(Path.Combine(temp.Path, "route.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)));
            Assert.False(availability.Available);
            Assert.Contains("not available from the AppImage", availability.Reason);
            Assert.Contains(".deb", availability.Reason);
        }
    }

    [Fact]
    public void Only_a_bundle_inside_the_AppImage_mount_is_the_AppImage_s()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux AppImage rule.");
        using var temp = new TempDir();
        string mount = temp.Dir(".mount_FileCat"), inside = Path.Combine(mount, "usr", "lib", "filecat");
        Directory.CreateDirectory(inside);
        using (new Variables(("APPIMAGE", Path.Combine(temp.Path, "FileCat.AppImage")), ("APPDIR", mount)))
        {
            Assert.True(LinuxArchiveIsolation.InAppImage(inside));
            Assert.True(LinuxArchiveIsolation.InAppImage(mount));
            Assert.False(LinuxArchiveIsolation.InAppImage("/opt/filecat"));
            Assert.False(LinuxArchiveIsolation.InAppImage(mount + "-other/usr/lib/filecat"));
        }
        using (new Variables(("APPIMAGE", null), ("APPDIR", mount))) Assert.False(LinuxArchiveIsolation.InAppImage(inside));
        using (new Variables(("APPIMAGE", Path.Combine(temp.Path, "FileCat.AppImage")), ("APPDIR", null))) Assert.False(LinuxArchiveIsolation.InAppImage(inside));
    }

    // Sets environment variables for one test step and restores them after.
    private sealed class Variables : IDisposable
    {
        private readonly (string Name, string? Value)[] previous;
        internal Variables(params (string Name, string? Value)[] values)
        {
            previous = values.Select(value => (value.Name, Environment.GetEnvironmentVariable(value.Name))).ToArray();
            foreach (var (name, value) in values) Environment.SetEnvironmentVariable(name, value);
        }
        public void Dispose() { foreach (var (name, value) in previous) Environment.SetEnvironmentVariable(name, value); }
    }
}
