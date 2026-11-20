using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisPrivateStorageTests
{
    [Fact]
    public async Task UnixNativeWideDirectoryScanSpillsItsLiveFrontierInsteadOfDroppingChildren()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native live frontier fixture."); Ordinary(); using var fixture = new NativeFixture();
        await FileCat.Tests.AnalysisSourceFrontierFixture.VerifyWideSource(fixture.Path, path => new UnixAnalysisBackend(path),
            path => new UnixAnalysisPrivateDirectory(path, "private"), Cancellation);
    }
    [Fact]
    public async Task UnixNativeRehydratedReadWitnessRejectsAQueuedDirectoryReplacement()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native live frontier race fixture."); Ordinary(); using var fixture = new NativeFixture();
        await FileCat.Tests.AnalysisSourceFrontierFixture.VerifyReplacementAfterQueueing(fixture.Path, path => new UnixAnalysisBackend(path),
            path => new UnixAnalysisPrivateDirectory(path, "private"), Cancellation);
    }
}
