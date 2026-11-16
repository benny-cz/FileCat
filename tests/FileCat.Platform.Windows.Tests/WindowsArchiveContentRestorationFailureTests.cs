using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    [Theory]
    [InlineData("cancel-opened")]
    [InlineData("cancel-prepared")]
    [InlineData("cancel-intent")]
    [InlineData("cancel-native-committed")]
    [InlineData("cancel-published-reviewed")]
    [InlineData("fail-backup-sealed")]
    [InlineData("fail-intent")]
    [InlineData("fail-native-committed")]
    [InlineData("fail-published-reviewed")]
    public async Task Real_restoration_failures_and_cancellation_preserve_public_effects_and_all_recovery_copies(string fault)
    {
        RequireWindows();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1" ||
            Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CONTAINMENT") != "1") Assert.Skip("Run the explicit confined NTFS restoration lane.");
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new CrashFixture(); string root = fixture.Root;
        File.WriteAllText(Path.Combine(root, "sentinel"), "owned unchanged restoration fault sentinel");
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", "FileCat.CoreCrashHarness.dll");
        string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
        string native = typeof(WindowsArchiveDirectory).Assembly.Location;
        await KillRecoveryTestProcess(harness, ["--archive-content-crash", root, "committed-recorded", native, probe], "archive-content-checkpoint:committed-recorded");
        fixture.AwaitReleasedHandles();
        var oldJob = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var oldContent = Assert.Single(oldJob.ArchiveTransactions.ContentUpdates); var oldCatalog = Assert.Single(oldJob.ArchiveTransactions.ContentCleanups);
        await CompleteRestorationTestProcess(harness, ["--archive-content-restore-crash", root, fault, native]);
        fixture.AwaitReleasedHandles();
        bool committed = fault.EndsWith("native-committed", StringComparison.Ordinal) || fault.EndsWith("published-reviewed", StringComparison.Ordinal);
        bool intent = committed || fault.EndsWith("intent", StringComparison.Ordinal);
        bool recorded = committed && fault.StartsWith("cancel-", StringComparison.Ordinal);
        string target = Path.Combine(root, "original.tar"), originalHash = oldContent.Snapshot!.Sha256, currentHash = oldContent.Published!.Sha256;
        string expected = committed ? originalHash : currentHash;
        Assert.Equal(expected, RecoveryCrashHash(target)); Assert.Equal(originalHash, RecoveryCrashHash(oldContent.SnapshotPath));
        Assert.All(oldCatalog.Artifacts, row => Assert.True(File.Exists(row.Path)));
        Assert.Equal(recorded ? "committed-despite-cancel" : fault.StartsWith("cancel-", StringComparison.Ordinal) ? "cancelled" : "failed",
            File.ReadAllText(Path.Combine(root, "restore-fault.txt")));
        var old = JournalRecovery.ReadArchiveTransactions(oldJob.JournalPath); Assert.Null(old.Problem);
        if (!intent) Assert.Empty(old.ContentRestorations);
        else Assert.Equal(recorded ? ArchiveContentRestorationPhase.Committed : ArchiveContentRestorationPhase.NeedsReview, Assert.Single(old.ContentRestorations).Phase);
        string freshJournal = File.ReadAllText(Path.Combine(root, "restore-job.txt"));
        var fresh = JournalRecovery.ReadArchiveTransactions(freshJournal); Assert.Null(fresh.Problem);
        Assert.DoesNotContain(JobJournal.ReadAll(freshJournal), record => record.Type == "end");
        var content = fresh.ContentUpdates.SingleOrDefault(); var catalog = fresh.ContentCleanups.SingleOrDefault();
        if (content is not null)
            Assert.Equal(recorded ? ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts : fault == "fail-backup-sealed"
                ? ArchiveContentUpdatePhase.RolledBackWithRetainedArtifacts : ArchiveContentUpdatePhase.NeedsReview, content.Phase);
        if (catalog is not null)
        {
            Assert.NotNull(content); Assert.All(catalog.Artifacts, row => Assert.True(File.Exists(row.Path)));
            Assert.Equal(currentHash, RecoveryCrashHash(content.SnapshotPath));
            using (var cleanup = WindowsArchiveCleanupRecovery.Open(freshJournal, RecoveryCrashHash(freshJournal), content.Nonce, ct))
            {
                Assert.Equal(committed ? ArchiveContentRecoveryTarget.PublishedIntact : ArchiveContentRecoveryTarget.OriginalIntact, cleanup.ResumeCleanup(ct).TargetState);
                cleanup.Finish("fresh restore failure independently reconciled without replay");
            }
            Assert.All(catalog.Artifacts, row => Assert.False(File.Exists(row.Path)));
        }
        else if (fault != "cancel-opened") Assert.True(fresh.RequiresReview);
        using (var directory = WindowsArchiveDirectory.Open(root))
        using (var held = directory.OpenSource("original.tar"))
        {
            var actual = ((IArchiveRetainedFile)held).Observe(ct);
            Assert.Equal(oldContent.Published.Identity, actual.Version.Identity); Assert.Equal(oldContent.RequiredOriginSha256, actual.Origin.Digest);
            Assert.Equal(4, ArchiveWindowsOriginZone.Parse(actual.Origin).DeclaredZone);
        }
        using (var cleanup = WindowsArchiveCleanupRecovery.Open(oldJob.JournalPath, RecoveryCrashHash(oldJob.JournalPath), oldContent.Nonce, ct))
        {
            Assert.Equal(committed ? ArchiveContentRecoveryTarget.RestoredIntact : ArchiveContentRecoveryTarget.PublishedIntact, cleanup.ResumeCleanup(ct).TargetState);
            cleanup.Finish("source restore catalog independently reconciled after failure or cancellation");
        }
        Assert.Equal(expected, RecoveryCrashHash(target)); Assert.All(oldCatalog.Artifacts, row => Assert.False(File.Exists(row.Path)));
        Assert.Equal("owned unchanged restoration fault sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
    }
}
