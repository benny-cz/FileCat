using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisScanningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeUnixCreationAndChangeTimesComeFromMetadataAndSurviveTheMemoryAcquisition(bool fileRoot)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native timestamp fixture."); Ordinary();
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "timestamp.bin");
        File.WriteAllText(path, "source data must stay unchanged");
        ArchiveUnixOuterMetadata.Facts expected;
        using (var file = File.OpenHandle(path))
            expected = ArchiveUnixOuterMetadata.Capture(file, TestContext.Current.CancellationToken).Native;
        using var source = new UnixAnalysisBackend(fileRoot ? path : temp.Path);
        var entry = fileRoot ? source.RootEntry : source.Children(source.RootEntry, TestContext.Current.CancellationToken).Single(e => e.Name == "timestamp.bin");
        Assert.Equal(Time(Assert.IsType<ArchiveUnixOuterMetadata.Timestamp>(expected.Created)), entry.CreatedUtc);
        Assert.Equal(1UL, entry.CreatedPrecisionTicks);
        Assert.Equal(Time(expected.Changed), entry.ChangedUtc);
        Assert.Equal(1UL, entry.ChangedPrecisionTicks);
        using var io = new DeviceIoScheduler(); using var session = new AnalysisCoordinator(io).Start([source]); await Done(session);
        var node = session.Snapshot().Nodes.Single(n => n.Name == "timestamp.bin");
        Assert.Equal(entry.CreatedUtc, node.Times!.CreatedUtc); Assert.Equal(entry.ChangedUtc, node.Times.ChangedUtc);
        Assert.Equal("source data must stay unchanged", File.ReadAllText(path));
    }

    private static DateTimeOffset Time(ArchiveUnixOuterMetadata.Timestamp value) =>
        DateTimeOffset.FromUnixTimeSeconds(value.Seconds).AddTicks(value.Nanoseconds / 100);
}
