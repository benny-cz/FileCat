using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisPrivateRootExclusionTests
{
    [Fact]
    public async Task ExplicitFileRootInsidePrivateStorageRetainsOnlyExcludedEvidence()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Unix native fixtures.");
        string parent = Path.Combine(Environment.CurrentDirectory, ".tmp", "filecat-private-root-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(parent);
        try
        {
            await FileCat.Tests.AnalysisSourceFrontierFixture.VerifyPrivateFileRoot(parent, path => new UnixAnalysisBackend(path),
                path => new UnixAnalysisPrivateDirectory(path, "private"), TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(parent, true); }
    }
}
