using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Fact]
    public async Task NativeCoordinatorUsesIndexedStorageAndExcludesItsProtectedDirectory()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture();
        await FileCat.Tests.AnalysisObservationCaptureFixture.VerifyCoordinator(fixture.Path, path => new Analysis.WindowsAnalysisBackend(path),
            path => new WindowsAnalysisPrivateDirectory(path, "private"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NativeMetadataCaptureCoalescesActualHardLinksWithoutReadingFileContent()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture();
        await FileCat.Tests.AnalysisObservationCaptureFixture.VerifyNative(fixture.Path, path => new Analysis.WindowsAnalysisBackend(path),
            () => new WindowsAnalysisPrivateDirectory(fixture.Path, "scratch"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NativeTypedGraphKeepsItsExactRelatedIndexesAfterOwnerClosure()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture();
        await FileCat.Tests.AnalysisIndexedGraphFixture.VerifyNative(() => new WindowsAnalysisPrivateDirectory(fixture.Path, "scratch"), fixture.Path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NativeExternalIndexKeepsExactRangesAndReleasesRunArtifacts()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture(); var cancellation = TestContext.Current.CancellationToken;
        using var io = new DeviceIoScheduler(); string device = "native-sort-" + Guid.NewGuid().ToString("N");
        var workspace = await AnalysisIo.Run(io, device, _ => new AnalysisScratchWorkspace(new WindowsAnalysisPrivateDirectory(fixture.Path, "scratch")), cancellation);
        AnalysisSortedIndex? index = null; AnalysisSortedIndexReader? reader = null;
        try
        {
            using var source = Enumerable.Range(1, 1000).Reverse().Select(n => new AnalysisSortEntry([(byte)(n % 31), 0xff], (ulong)n)).GetEnumerator();
            index = await AnalysisExternalSort.BuildAsync(workspace, io, device, source, cancellation, 4096);
            reader = await AnalysisIo.Run(io, device, _ => index.Acquire(), cancellation);
            await AnalysisIo.Run(io, device, token =>
            {
                Assert.Equal(1000UL, reader.Count); Assert.Equal(33UL, reader.FindRange([7, 0xff], token).Count);
                Assert.Equal(reader.FindRange([7, 0xff], token), reader.PrefixRange([7], token)); return true;
            }, cancellation);
        }
        finally
        {
            if (reader is not null) await AnalysisIo.ReleaseScratchAsync(io, device, reader);
            if (index is not null) await AnalysisIo.ReleaseScratchAsync(io, device, index);
            await AnalysisIo.ReleaseScratchAsync(io, device, workspace);
        }
        Assert.Equal(0, workspace.ReservedBytes); Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Path, "scratch")));
    }

    [Fact]
    public void NativeWorkspaceSharesDirectoryAndQuotaUntilAllArenaReadersClose()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture(); var cancellation = TestContext.Current.CancellationToken;
        using var native = new WindowsAnalysisPrivateDirectory(fixture.Path, "scratch");
        var profile = new AnalysisScratchBudget(1024 * 1024); using var workspace = new AnalysisScratchWorkspace(native, 512 * 1024, profile);
        using var data = workspace.CreateArena(); using var runs = workspace.CreateArena();
        var address = data.Append([1, 2, 3], cancellation); runs.Append([4, 5], cancellation); using var reader = data.Capture();
        workspace.Dispose(); runs.Dispose(); data.Dispose(); native.Verify(); Assert.Equal(51, profile.ReservedBytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.Read(address, cancellation)); reader.Dispose();
        Assert.Equal(0, profile.ReservedBytes); Assert.Throws<ObjectDisposedException>(() => native.Verify());
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Path, "scratch")));
    }

    [Fact]
    public void NativeRelatedTablesRetainOneCoherentRevisionAfterOwnerClosure()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture(); var cancellation = TestContext.Current.CancellationToken;
        var budget = new AnalysisScratchBudget(1024 * 1024);
        using var arena = new AnalysisScratchArena(new WindowsAnalysisPrivateDirectory(fixture.Path, "scratch"), budget, 1024 * 1024);
        using var store = new AnalysisScratchComposite(arena, 10, 10, 10);
        for (int table = 0; table < 3; table++) store.Append(table, [(byte)(table + 1)], cancellation);
        store.Publish(cancellation); using var first = store.AcquireCommitted(); store.Dispose();
        using var retained = first.Retain(); first.Dispose();
        string foreign = Path.Combine(fixture.Path, "scratch", "user-file.txt"); File.WriteAllText(foreign, "preserve me");
        for (int table = 0; table < 3; table++) Assert.Equal(new byte[] { (byte)(table + 1) }, retained.Read(table, 1, cancellation));
        Assert.True(budget.ReservedBytes > 0); retained.Dispose(); Assert.Equal(0, budget.ReservedBytes);
        Assert.Equal(new[] { foreign }, Directory.GetFiles(Path.GetDirectoryName(foreign)!)); Assert.Equal("preserve me", File.ReadAllText(foreign));
    }

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
