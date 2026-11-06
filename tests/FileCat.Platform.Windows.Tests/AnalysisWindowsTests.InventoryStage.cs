using FileCat.Core.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Fact]
    public async Task NativeStagingRetainsTheWholeContainerAndOriginalIdMapAfterInputAndWorkspaceClose()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        await FileCat.Tests.AnalysisInventoryStageCases.NativeAsync((parent, leaf) => new WindowsAnalysisPrivateDirectory(parent, leaf));
    }
}
