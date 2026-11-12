using FileCat.App.Services;

namespace FileCat.App.Tests;

// Plan §16.1: ordinary startup registers only qualified writing routes: those listed for this runtime, and nothing on a
// runtime with none (win-arm64, linux-arm64).
public sealed class ArchiveProductionCompositionTests
{
    [Fact]
    public void Ordinary_startup_registers_the_listed_routes_of_this_runtime_and_nothing_else()
    {
        string runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        bool listed = FileCat.Core.Archives.ArchiveWriteRoutes.Qualified.Any(route => route.RuntimeIdentifier == runtime);
        var backends = AppServices.ProductionArchiveBackends();
        Assert.Equal(listed, backends is not null);
        if (backends is null) return;
        // Every listed format is composed. A target is covered where its file system is listed and the writer can write it
        // (here an elevated host, or macOS's temporary folder reached through /var, cannot); otherwise its reason is given.
        string target = Path.Combine(Path.GetTempPath(), "filecat-route-" + Guid.NewGuid().ToString("N") + ".archive");
        bool listedHere = AppServices.FileSystemOf(Path.GetTempPath()) is { } fs && FileCat.Core.Archives.ArchiveWriteRoutes.Qualified
            .Any(route => route.RuntimeIdentifier == runtime && string.Equals(route.FileSystem, fs, StringComparison.OrdinalIgnoreCase));
        string? problem = AppServices.PlatformTargetProblem(target);
        foreach (var route in FileCat.Core.Archives.ArchiveWriteRoutes.Qualified.Where(route => route.RuntimeIdentifier == runtime))
        {
            var plan = new FileCat.Core.Archives.ArchivePlan(target, null, [],
                Profile: route.Format == FileCat.Core.Archives.ArchiveWriteFormat.Rar5 ? FileCat.Core.Archives.ArchiveWriteProfile.ForRar5() : new(route.Format));
            Assert.Equal(listedHere && problem is null, backends.Covers(plan));
            if (listedHere && problem is not null) Assert.Equal(problem, backends.Availability(plan).Reason);
        }
    }

    [Fact]
    public void The_filesystem_of_a_folder_comes_from_its_own_mount()
    {
        string folder = Path.GetTempPath();
        string? fileSystem = AppServices.FileSystemOf(folder);
        Assert.False(string.IsNullOrEmpty(fileSystem));
        Assert.Null(AppServices.FileSystemOf(Path.Combine(folder, "\0invalid")));
    }

    // A mapped network drive reports its server's file system: no route covers it, so a ZIP there keeps the old route and
    // the other formats stay read-only with the file-system reason, as on a UNC path.
    [Theory]
    [InlineData(DriveType.Network, "NTFS", null)]
    [InlineData(DriveType.Network, "ext4", null)]
    [InlineData(DriveType.Fixed, "NTFS", "NTFS")]
    [InlineData(DriveType.Removable, "exFAT", "exFAT")]
    public void A_network_drive_has_no_local_file_system(DriveType type, string reported, string? expected) =>
        Assert.Equal(expected, AppServices.LocalFileSystem(type, reported));
}
