using FileCat.Core.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Fact]
    public async Task NativeScratchDeleteOnCloseSurvivesAbruptProcessDeath()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture();
        await FileCat.Tests.AnalysisScratchCrashFixture.KillOwnedChild(fixture.Path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void NativeScratchUsesExclusiveDeleteOnCloseAndRetainsLeasedRecords()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture(); string path = Path.Combine(fixture.Path, "scratch");
        var budget = new AnalysisScratchBudget(1024 * 1024);
        using var arena = new AnalysisScratchArena(new WindowsAnalysisPrivateDirectory(fixture.Path, "scratch"), budget, 1024 * 1024);
        byte[] payload = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();
        var record = arena.Append(payload, TestContext.Current.CancellationToken); using var checkpoint = arena.Capture();
        string temporary = Assert.Single(Directory.GetFiles(path)); Assert.StartsWith(".scratch-", Path.GetFileName(temporary));
        Assert.ThrowsAny<IOException>(() => File.Open(temporary, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        string foreign = Path.Combine(path, "user-file.txt"); File.WriteAllText(foreign, "preserve me");
        arena.Dispose(); Assert.True(File.Exists(temporary)); Assert.Equal(payload, checkpoint.Read(record, TestContext.Current.CancellationToken));
        Assert.True(budget.ReservedBytes > payload.Length); checkpoint.Dispose(); Assert.Equal(0, budget.ReservedBytes);
        Assert.False(File.Exists(temporary)); Assert.Equal("preserve me", File.ReadAllText(foreign)); Assert.Equal(new[] { foreign }, Directory.GetFiles(path));
    }
}
