using FileCat.Core.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Fact]
    public async Task NativeOriginalIdMapOutlivesItsWorkspaceOwnerAndClosesAllAnonymousFiles()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        await FileCat.Tests.AnalysisInventoryPathMapCases.NativeAsync((parent, leaf) => new WindowsAnalysisPrivateDirectory(parent, leaf));
    }
}
