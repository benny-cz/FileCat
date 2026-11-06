using FileCat.Core.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Fact]
    public async Task NativeWideDirectoryScanSpillsItsLiveFrontierInsteadOfDroppingChildren()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures."); using var fixture = new PrivateFixture();
        await FileCat.Tests.AnalysisSourceFrontierFixture.VerifyWideSource(fixture.Path, path => new Analysis.WindowsAnalysisBackend(path),
            path => new WindowsAnalysisPrivateDirectory(path, "private"), TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task NativeRehydratedReadWitnessRejectsAQueuedDirectoryReplacement()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures."); using var fixture = new PrivateFixture();
        await FileCat.Tests.AnalysisSourceFrontierFixture.VerifyReplacementAfterQueueing(fixture.Path, path => new Analysis.WindowsAnalysisBackend(path),
            path => new WindowsAnalysisPrivateDirectory(path, "private"), TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task NativeExplicitFileRootInsidePrivateStorageRetainsOnlyExcludedEvidence()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures."); using var fixture = new PrivateFixture();
        await FileCat.Tests.AnalysisSourceFrontierFixture.VerifyPrivateFileRoot(fixture.Path, path => new Analysis.WindowsAnalysisBackend(path),
            path => new WindowsAnalysisPrivateDirectory(path, "private"), TestContext.Current.CancellationToken);
    }
}
