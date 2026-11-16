using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    public static IEnumerable<object[]> ContentRecoveryCrashPoints()
    {
        foreach (string point in new[] { "opened", "reviewed", "completed", "finished", "absent-0", "needs-review-before", "needs-review-after" }) yield return [point, false];
        for (int i = 0; i < 6; i++)
            foreach (string phase in new[] { "intent", "unlinked", "recorded" }) yield return [$"{phase}-{i}", false];
        foreach (string point in new[] { "reviewed", "unlinked-5", "completed", "finished", "needs-review-before", "needs-review-after" }) yield return [point, true];
    }

    [Theory]
    [MemberData(nameof(ContentRecoveryCrashPoints))]
    public async Task Real_recovery_process_crashes_preserve_the_current_archive_and_resume_only_exact_remaining_cleanup(string point, bool original)
    {
        RequireWindows();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Run the explicit NTFS transaction qualification lane.");
        using var fixture = new CrashFixture(); string root = fixture.Root;
        File.WriteAllText(Path.Combine(root, "sentinel"), "owned unchanged recovery sentinel");
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", "FileCat.CoreCrashHarness.dll");
        string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
        string native = typeof(WindowsArchiveDirectory).Assembly.Location;
        string initialPoint = point == "absent-0" ? "cleanup-unlinked-0" : original ? "private-ready" : "native-committed";
        await KillRecoveryTestProcess(harness, ["--archive-content-crash", root, initialPoint, native, probe], "archive-content-checkpoint:" + initialPoint);
        fixture.AwaitReleasedHandles();
        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var content = Assert.Single(job.ArchiveTransactions.ContentUpdates); var catalog = Assert.Single(job.ArchiveTransactions.ContentCleanups);
        string target = Path.Combine(root, "original.tar"), targetHash = RecoveryCrashHash(target);
        await KillRecoveryTestProcess(harness, ["--archive-content-recovery-crash", root, point, native], "archive-content-recovery-checkpoint:" + point);
        fixture.AwaitReleasedHandles();
        Assert.Equal(targetHash, RecoveryCrashHash(target));
        Assert.Equal("owned unchanged recovery sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
        var interrupted = JournalRecovery.ReadArchiveTransactions(job.JournalPath); Assert.Null(interrupted.Problem);
        var facts = interrupted.ContentRecoveries.SingleOrDefault();
        string[] parts = point.Split('-'); int index = parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
        int reviewed = point is "completed" or "finished" ? catalog.Artifacts.Count : parts[0] is "recorded" or "absent" ? index + 1 : index;
        int physical = reviewed + (parts[0] == "unlinked" || point == "needs-review-after" ? 1 : 0);
        if (point == "opened") Assert.Null(facts);
        else
        {
            Assert.NotNull(facts); Assert.Equal(reviewed, facts.ReviewedCount);
            Assert.Equal(point switch
            {
                "reviewed" => ArchiveContentRecoveryPhase.Reviewed,
                "completed" or "finished" => ArchiveContentRecoveryPhase.Completed,
                "needs-review-before" or "needs-review-after" => ArchiveContentRecoveryPhase.NeedsReview,
                _ when parts[0] == "recorded" => ArchiveContentRecoveryPhase.Deleted,
                _ when parts[0] == "absent" => ArchiveContentRecoveryPhase.Absent,
                _ => ArchiveContentRecoveryPhase.DeleteIntent,
            }, facts.Phase);
            Assert.Equal(original ? ArchiveContentRecoveryTarget.OriginalIntact : ArchiveContentRecoveryTarget.PublishedIntact, facts.TargetState);
        }
        for (int i = 0; i < catalog.Artifacts.Count; i++) Assert.Equal(i >= physical, File.Exists(catalog.Artifacts[i].Path));
        Assert.Equal(physical < catalog.Artifacts.Count, File.Exists(content.SnapshotPath));
        Assert.Equal(point == "finished", JobJournal.ReadAll(job.JournalPath).Any(record => record.Type == "end"));
        if (point == "finished")
        {
            Assert.False(interrupted.RequiresReview);
            Assert.Throws<IOException>(() => WindowsArchiveCleanupRecovery.Open(job.JournalPath, RecoveryCrashHash(job.JournalPath), content.Nonce, TestContext.Current.CancellationToken));
            return;
        }
        uint absent = facts?.AbsentMask ?? 0, deleted = facts?.DeletedMask ?? 0;
        for (int i = reviewed; i < catalog.Artifacts.Count; i++)
        { if (File.Exists(catalog.Artifacts[i].Path)) deleted |= 1u << i; else absent |= 1u << i; }
        using (var recovered = WindowsArchiveCleanupRecovery.Open(job.JournalPath, RecoveryCrashHash(job.JournalPath), content.Nonce, TestContext.Current.CancellationToken))
        {
            var outcome = recovered.ResumeCleanup(TestContext.Current.CancellationToken);
            Assert.True(outcome.IsResolved); Assert.Equal(absent, outcome.AbsentMask); Assert.Equal(deleted, outcome.DeletedMask);
            recovered.Finish("fresh native scope reconciled an interrupted recovery process");
        }
        Assert.False(JournalRecovery.ReadArchiveTransactions(job.JournalPath).RequiresReview);
        Assert.All(catalog.Artifacts, artifact => Assert.False(File.Exists(artifact.Path)));
        Assert.Equal(targetHash, RecoveryCrashHash(target));
        Assert.Equal("owned unchanged recovery sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
    }

    private static string RecoveryCrashHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static async Task KillRecoveryTestProcess(string harness, string[] arguments, string checkpoint)
    {
        string runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo(Path.Combine(runtime, "dotnet.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(harness); foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            string? actual = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(actual == checkpoint, "Missing checkpoint: " + actual + (process.HasExited ? " stderr: " + await errors : ""));
            Assert.False(process.HasExited); process.Kill(entireProcessTree: true); await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode); Assert.Equal("", await errors.WaitAsync(timeout.Token));
        }
        finally { await Reap(process); }
    }
}
