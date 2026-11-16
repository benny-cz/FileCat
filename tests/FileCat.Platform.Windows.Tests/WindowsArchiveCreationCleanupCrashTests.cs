using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    public static IEnumerable<object[]> CreationCleanupCrashPoints()
    {
        foreach (string point in new[] { "opened", "completed", "finished", "absent-0", "needs-review-before", "needs-review-after" }) yield return [point];
        for (int i = 0; i < 4; i++)
            foreach (string phase in new[] { "intent", "unlinked", "recorded" }) yield return [$"{phase}-{i}"];
    }

    [Theory, MemberData(nameof(CreationCleanupCrashPoints))]
    public async Task Real_creation_cleanup_process_crashes_resume_exact_remaining_sidecars(string point)
    {
        RequireWindows();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Run the explicit NTFS archive cleanup lane.");
        using var fixture = new CrashFixture(); string root = fixture.Root;
        File.WriteAllText(Path.Combine(root, "sentinel"), "owned unchanged creation cleanup sentinel");
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", "FileCat.CoreCrashHarness.dll");
        string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
        string native = typeof(WindowsArchiveDirectory).Assembly.Location;
        await KillRecoveryTestProcess(harness, ["--archive-creation-crash", root, "committed-warning", native, probe], "archive-creation-checkpoint:committed-warning");
        fixture.AwaitReleasedHandles();
        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var catalog = Assert.Single(job.ArchiveTransactions.CreationCleanups);
        string target = Path.Combine(root, "result.tar"), targetHash = RecoveryCrashHash(target);
        if (point == "absent-0")
        {
            await KillRecoveryTestProcess(harness, ["--archive-creation-cleanup-crash", root, "unlinked-0", native], "archive-creation-cleanup-checkpoint:unlinked-0");
            fixture.AwaitReleasedHandles();
        }
        await KillRecoveryTestProcess(harness, ["--archive-creation-cleanup-crash", root, point, native], "archive-creation-cleanup-checkpoint:" + point);
        fixture.AwaitReleasedHandles();
        Assert.Equal(targetHash, RecoveryCrashHash(target));
        Assert.Equal("owned unchanged creation cleanup sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
        var interrupted = JournalRecovery.ReadArchiveTransactions(job.JournalPath); Assert.Null(interrupted.Problem);
        var facts = Assert.Single(interrupted.CreationCleanups);
        string[] parts = point.Split('-'); int index = parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
        int reviewed = point is "completed" or "finished" ? 4 : parts[0] is "recorded" or "absent" ? index + 1 : index;
        int physical = reviewed + (parts[0] == "unlinked" || point == "needs-review-after" ? 1 : 0);
        Assert.Equal(reviewed, facts.ReviewedCount);
        Assert.Equal(point switch
        {
            "opened" => ArchiveCreationCleanupPhase.Cataloged,
            "completed" or "finished" => ArchiveCreationCleanupPhase.Completed,
            "needs-review-before" or "needs-review-after" => ArchiveCreationCleanupPhase.NeedsReview,
            _ when parts[0] == "recorded" => ArchiveCreationCleanupPhase.Deleted,
            _ when parts[0] == "absent" => ArchiveCreationCleanupPhase.Absent,
            _ => ArchiveCreationCleanupPhase.DeleteIntent,
        }, facts.Phase);
        for (int i = 0; i < 4; i++) Assert.Equal(i >= physical, File.Exists(catalog.Artifacts[i].Path));
        Assert.Equal(point == "finished", JobJournal.ReadAll(job.JournalPath).Any(record => record.Type == "end"));
        if (point == "finished")
        {
            Assert.False(interrupted.RequiresReview);
            Assert.Throws<IOException>(() => WindowsArchiveCreationCleanupRecovery.OpenCurrentLease(job.JournalPath, catalog.Nonce, TestContext.Current.CancellationToken));
            return;
        }
        uint absent = facts.AbsentMask, deleted = facts.DeletedMask;
        for (int i = reviewed; i < 4; i++)
        { if (File.Exists(catalog.Artifacts[i].Path)) deleted |= 1u << i; else absent |= 1u << i; }
        using (var recovered = new ArchiveCreationCleanup(WindowsArchiveCreationCleanupRecovery.OpenCurrentLease(job.JournalPath, catalog.Nonce, TestContext.Current.CancellationToken)))
        {
            var outcome = recovered.Execute(TestContext.Current.CancellationToken);
            Assert.True(outcome.IsResolved); Assert.Equal(absent, outcome.AbsentMask); Assert.Equal(deleted, outcome.DeletedMask);
            recovered.Finish("Fresh native creation scope reconciled the interrupted process.");
        }
        Assert.False(JournalRecovery.ReadArchiveTransactions(job.JournalPath).RequiresReview);
        Assert.All(catalog.Artifacts, artifact => Assert.False(File.Exists(artifact.Path)));
        Assert.Equal(targetHash, RecoveryCrashHash(target));
    }
}
