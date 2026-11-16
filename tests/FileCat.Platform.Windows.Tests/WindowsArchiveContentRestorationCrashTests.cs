using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    public static IEnumerable<object[]> RestorationCleanupCrashPoints()
    {
        for (int i = 0; i < 6; i++)
            foreach (string phase in new[] { "intent", "unlinked", "recorded" }) yield return [$"cleanup-{phase}-{i}"];
    }

    [Theory]
    [MemberData(nameof(RestorationCleanupCrashPoints))]
    [InlineData("opened")]
    [InlineData("stage-created")]
    [InlineData("prepared")]
    [InlineData("transaction-opened")]
    [InlineData("backup-sealed")]
    [InlineData("private-ready")]
    [InlineData("intent")]
    [InlineData("native-committed")]
    [InlineData("published-reviewed")]
    [InlineData("recorded")]
    [InlineData("cleanup-completed")]
    [InlineData("closed")]
    [InlineData("retry-after-intent")]
    public async Task Real_restoration_process_crashes_preserve_both_byte_versions_and_reconcile_separate_catalogs(string point)
    {
        RequireWindows();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1" ||
            Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CONTAINMENT") != "1") Assert.Skip("Run the explicit confined NTFS restoration lane.");
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new CrashFixture(); string root = fixture.Root;
        File.WriteAllText(Path.Combine(root, "sentinel"), "owned unchanged restoration sentinel");
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
        var oldContent = Assert.Single(oldJob.ArchiveTransactions.ContentUpdates);
        var oldCatalog = Assert.Single(oldJob.ArchiveTransactions.ContentCleanups);
        string originalHash = oldContent.Snapshot!.Sha256, currentHash = oldContent.Published!.Sha256;
        Assert.NotEqual(originalHash, currentHash);
        bool retry = point == "retry-after-intent";
        string checkpoint = retry ? "intent" : point;
        await KillRecoveryTestProcess(harness, ["--archive-content-restore-crash", root, checkpoint, native], "archive-content-restore-checkpoint:" + checkpoint);
        fixture.AwaitReleasedHandles();
        ArchiveContentRestorationFacts? prior = null;
        if (retry)
        {
            prior = Assert.Single(JournalRecovery.ReadArchiveTransactions(oldJob.JournalPath).ContentRestorations);
            string failedJournal = File.ReadAllText(Path.Combine(root, "restore-job.txt"));
            var failed = Assert.Single(JournalRecovery.ReadArchiveTransactions(failedJournal).ContentUpdates);
            using (var cleanup = WindowsArchiveCleanupRecovery.Open(failedJournal, RecoveryCrashHash(failedJournal), failed.Nonce, ct))
            {
                Assert.Equal(ArchiveContentRecoveryTarget.OriginalIntact, cleanup.ResumeCleanup(ct).TargetState);
                cleanup.Finish("failed fresh restoration independently cleaned before a new attempt");
            }
            // A fresh transaction computes a new write time, so this must not replay the former metadata digest.
            await Task.Delay(25, ct);
            await CompleteRestorationTestProcess(harness, ["--archive-content-restore-crash", root, "finish", native]);
            fixture.AwaitReleasedHandles(); point = "closed";
        }
        bool cleanupCrash = point.StartsWith("cleanup-", StringComparison.Ordinal) && point != "cleanup-completed";
        bool committed = cleanupCrash || point is "native-committed" or "published-reviewed" or "recorded" or "cleanup-completed" or "closed";
        bool recorded = cleanupCrash || point is "recorded" or "cleanup-completed" or "closed";
        bool intent = committed || point == "intent";
        string target = Path.Combine(root, "original.tar"), expected = committed ? originalHash : currentHash;
        Assert.Equal(expected, RecoveryCrashHash(target));
        Assert.Equal(originalHash, RecoveryCrashHash(oldContent.SnapshotPath));
        Assert.All(oldCatalog.Artifacts, row => Assert.True(File.Exists(row.Path)));
        Assert.Equal("owned unchanged restoration sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
        var old = JournalRecovery.ReadArchiveTransactions(oldJob.JournalPath); Assert.Null(old.Problem);
        var restoration = old.ContentRestorations.SingleOrDefault();
        if (!intent) Assert.Null(restoration);
        else
        {
            Assert.NotNull(restoration);
            Assert.Equal(recorded ? ArchiveContentRestorationPhase.Committed : ArchiveContentRestorationPhase.CommitIntent, restoration.Phase);
            Assert.Equal(currentHash, restoration.Before.Sha256); Assert.Equal(originalHash, restoration.Restored.Sha256);
            if (prior is not null)
            {
                Assert.Equal(2, restoration.Attempt); Assert.NotEqual(prior.RestoreNonce, restoration.RestoreNonce);
                Assert.NotEqual(prior.NativeTransactionId, restoration.NativeTransactionId); Assert.NotEqual(prior.RestoreJournalPath, restoration.RestoreJournalPath);
                Assert.NotEqual(prior.RestoredMetadataSha256, restoration.RestoredMetadataSha256);
            }
        }
        string freshJournal = File.ReadAllText(Path.Combine(root, "restore-job.txt"));
        var fresh = JournalRecovery.ReadArchiveTransactions(freshJournal); Assert.Null(fresh.Problem);
        var freshContent = fresh.ContentUpdates.SingleOrDefault(); var freshCatalog = fresh.ContentCleanups.SingleOrDefault();
        Assert.Equal(point == "closed", JobJournal.ReadAll(freshJournal).Any(record => record.Type == "end"));
        if (point is not ("opened" or "stage-created"))
        {
            string[] workers = File.ReadAllText(Path.Combine(root, "restore-workers.txt")).Split(',');
            Assert.Equal(2, workers.Length); Assert.NotEqual(workers[0], workers[1]);
        }
        using (var directory = WindowsArchiveDirectory.Open(root))
        using (var held = directory.OpenSource("original.tar"))
        {
            var actual = ((IArchiveRetainedFile)held).Observe(ct);
            Assert.Equal(oldContent.Published.Identity, actual.Version.Identity);
            Assert.Equal(oldContent.RequiredOriginSha256, actual.Origin.Digest);
            Assert.Equal(4, ArchiveWindowsOriginZone.Parse(actual.Origin).DeclaredZone);
            if (restoration is not null && committed) Assert.True(restoration.MatchesRestored(actual));
        }
        if (freshCatalog is not null)
        {
            Assert.NotNull(freshContent); Assert.NotEqual(oldContent.Nonce, freshContent.Nonce);
            Assert.NotEqual(oldContent.SnapshotPath, freshContent.SnapshotPath);
            bool cleaned = point is "cleanup-completed" or "closed";
            int physical = cleaned ? freshCatalog.Artifacts.Count : 0;
            if (cleanupCrash)
            {
                string[] parts = point.Split('-'); int index = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                int reviewed = parts[1] == "recorded" ? index + 1 : index;
                physical = reviewed + (parts[1] == "unlinked" ? 1 : 0);
                Assert.Equal(reviewed, freshCatalog.DeletedCount);
                Assert.Equal(parts[1] == "recorded" ? ArchiveContentCleanupPhase.Deleted : ArchiveContentCleanupPhase.DeleteIntent, freshCatalog.Phase);
            }
            for (int i = 0; i < freshCatalog.Artifacts.Count; i++) Assert.Equal(i >= physical, File.Exists(freshCatalog.Artifacts[i].Path));
            Assert.Equal(physical < freshCatalog.Artifacts.Count, File.Exists(freshContent.SnapshotPath));
            if (!cleaned)
            {
                if (File.Exists(freshContent.SnapshotPath)) Assert.Equal(currentHash, RecoveryCrashHash(freshContent.SnapshotPath));
                using var cleanup = WindowsArchiveCleanupRecovery.Open(freshJournal, RecoveryCrashHash(freshJournal), freshContent.Nonce, ct);
                var outcome = cleanup.ResumeCleanup(ct);
                Assert.Equal(committed ? ArchiveContentRecoveryTarget.PublishedIntact : ArchiveContentRecoveryTarget.OriginalIntact, outcome.TargetState);
                if (cleanupCrash)
                {
                    uint remaining = ((1u << freshCatalog.Artifacts.Count) - 1) & ~((1u << freshCatalog.DeletedCount) - 1);
                    uint absent = point.StartsWith("cleanup-unlinked-", StringComparison.Ordinal) ? 1u << freshCatalog.DeletedCount : 0;
                    Assert.Equal(absent, outcome.AbsentMask); Assert.Equal(remaining & ~absent, outcome.DeletedMask);
                }
                cleanup.Finish("fresh restoration job independently reconciled after actual process death");
            }
            Assert.All(freshCatalog.Artifacts, row => Assert.False(File.Exists(row.Path)));
            Assert.Equal(originalHash, RecoveryCrashHash(oldContent.SnapshotPath));
        }
        else if (point != "opened")
        {
            // Partial preparation/snapshot histories do not authorize deleting uncataloged files.
            Assert.True(fresh.RequiresReview);
            Assert.ThrowsAny<Exception>(() => WindowsArchiveCleanupRecovery.Open(freshJournal, RecoveryCrashHash(freshJournal),
                freshContent?.Nonce ?? fresh.Transactions.Single().Nonce, ct));
        }
        using (var cleanup = WindowsArchiveCleanupRecovery.Open(oldJob.JournalPath, RecoveryCrashHash(oldJob.JournalPath), oldContent.Nonce, ct))
        {
            var outcome = cleanup.ResumeCleanup(ct);
            Assert.Equal(committed ? ArchiveContentRecoveryTarget.RestoredIntact : ArchiveContentRecoveryTarget.PublishedIntact, outcome.TargetState);
            cleanup.Finish("old source catalog independently reconciled after actual restoration process death");
        }
        Assert.All(oldCatalog.Artifacts, row => Assert.False(File.Exists(row.Path)));
        Assert.Equal(expected, RecoveryCrashHash(target)); Assert.False(JournalRecovery.ReadArchiveTransactions(oldJob.JournalPath).RequiresReview);
        Assert.Equal("owned unchanged restoration sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
    }

    private static async Task CompleteRestorationTestProcess(string harness, string[] arguments)
    {
        string runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo(Path.Combine(runtime, "dotnet.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(harness); foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(45));
            Assert.True(process.ExitCode == 0, "Fresh restoration failed: " + await errors); Assert.Equal("", await output);
        }
        finally { await Reap(process); }
    }
}
