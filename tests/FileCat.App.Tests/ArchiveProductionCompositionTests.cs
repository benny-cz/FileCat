using FileCat.App.Services;

namespace FileCat.App.Tests;

// Plan §16.1: ordinary startup registers only qualified writing routes; this build has none, so native writing stays off.
public sealed class ArchiveProductionCompositionTests
{
    [Fact]
    public void Ordinary_startup_registers_no_unqualified_archive_writer()
        => Assert.Null(AppServices.ProductionArchiveBackends());

    [Fact]
    public void The_filesystem_of_a_folder_comes_from_its_own_mount()
    {
        string folder = Path.GetTempPath();
        string? fileSystem = AppServices.FileSystemOf(folder);
        Assert.False(string.IsNullOrEmpty(fileSystem));
        Assert.Null(AppServices.FileSystemOf(Path.Combine(folder, "\0invalid")));
    }
}
