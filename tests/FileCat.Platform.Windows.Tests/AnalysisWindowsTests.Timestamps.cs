using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWindowsCreationAndChangeTimesComeFromNtfsMetadataAndSurviveTheMemoryAcquisition(bool fileRoot)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs an ordinary Windows process.");
        using var temp = new EpochTempDir();
        Assert.SkipWhen(new DriveInfo(Path.GetPathRoot(temp.Path)!).DriveFormat != "NTFS", "NTFS timestamp precision fixture.");
        string path = Path.Combine(temp.Path, "timestamp.bin"); File.WriteAllText(path, "source data must stay unchanged");
        var expected = new DateTimeOffset(File.GetCreationTimeUtc(path));
        using var source = new WindowsAnalysisBackend(fileRoot ? path : temp.Path);
        var entry = fileRoot ? source.RootEntry : source.Children(source.RootEntry, TestContext.Current.CancellationToken).Single(e => e.Name == "timestamp.bin");
        Assert.Equal(expected, entry.CreatedUtc); Assert.Equal(1UL, entry.CreatedPrecisionTicks);
        Assert.NotNull(entry.ChangedUtc); Assert.Equal(1UL, entry.ChangedPrecisionTicks);
        using var io = new DeviceIoScheduler(); using var session = new AnalysisCoordinator(io).Start([source]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var node = session.Snapshot().Nodes.Single(n => n.Name == "timestamp.bin");
        Assert.Equal(entry.CreatedUtc, node.Times!.CreatedUtc); Assert.Equal(entry.ChangedUtc, node.Times.ChangedUtc);
        Assert.Equal("source data must stay unchanged", File.ReadAllText(path));
    }
}
