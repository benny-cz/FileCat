using System.Reflection;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

// Test-only operation wrappers around actual native leases. No production checkpoint callbacks.
internal static class ArchiveContentRecoveryCrash
{
    internal static int Run(string root, string point, string windowsAssembly)
    {
        string[] points = ["opened", "reviewed", "completed", "finished", "absent-0", "needs-review-before", "needs-review-after",
            .. Enumerable.Range(0, 6).SelectMany(i => new[] { "intent-" + i, "unlinked-" + i, "recorded-" + i })];
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(root) || !Directory.Exists(root) ||
            !points.Contains(point) || !Path.IsPathFullyQualified(windowsAssembly)) return 64;
        var job = JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero).Single();
        var content = job.ArchiveTransactions.ContentUpdates.Single();
        string expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(job.JournalPath)));
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(windowsAssembly);
        var type = assembly.GetType("FileCat.Platform.Windows.Archives.WindowsArchiveCleanupRecovery", true)!;
        var open = type.GetMethod("OpenLease", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException(type.FullName, "OpenLease");
        var native = (IArchiveContentCleanupRecoveryLease)open.Invoke(null, [job.JournalPath, expected, content.Nonce, CancellationToken.None])!;
        using var recovery = new ArchiveContentCleanupRecovery(new Lease(native, point));
        WaitIf("opened", point);
        try { recovery.ResumeCleanup(CancellationToken.None); }
        catch (InjectedFailure) { WaitIf(point, point); throw; }
        WaitIf("completed", point);
        recovery.Finish("actual recovery process completed and closed its retained journal");
        WaitIf("finished", point);
        return 65;
    }
    private static void WaitIf(string checkpoint, string point)
    {
        if (checkpoint != point) return;
        Console.WriteLine("archive-content-recovery-checkpoint:" + point); Console.Out.Flush(); Console.ReadLine();
        throw new InvalidOperationException("The driver must terminate this actual recovery process externally.");
    }
    private sealed class InjectedFailure : IOException;
    private sealed class Lease(IArchiveContentCleanupRecoveryLease native, string point) : IArchiveContentCleanupRecoveryLease
    {
        public JobJournal Journal => native.Journal;
        public ArchiveContentCleanupFacts Catalog => native.Catalog;
        public ArchiveContentRecoveryFacts Candidate => native.Candidate;
        public bool HasArtifact(int index) => native.HasArtifact(index);
        public void ReviewArtifact(int index, CancellationToken ct) => native.ReviewArtifact(index, ct);
        public void RequireArtifactAbsent(int index) => native.RequireArtifactAbsent(index);
        public void DeleteArtifact(int index, CancellationToken ct)
        {
            if (index == 0 && point == "needs-review-before") throw new InjectedFailure();
            native.DeleteArtifact(index, ct);
            if (index == 0 && point == "needs-review-after") throw new InjectedFailure();
            WaitIf("unlinked-" + index, point);
        }
        public void ReviewTarget(CancellationToken ct)
        {
            native.ReviewTarget(ct);
            var facts = Journal.RetainedArchiveRecovery().ContentRecoveries.SingleOrDefault();
            if (facts is null) return;
            if (facts.Phase == ArchiveContentRecoveryPhase.Reviewed) WaitIf("reviewed", point);
            if (facts.Phase == ArchiveContentRecoveryPhase.DeleteIntent) WaitIf("intent-" + facts.ReviewedCount, point);
            if (facts.Phase == ArchiveContentRecoveryPhase.Deleted) WaitIf("recorded-" + (facts.ReviewedCount - 1), point);
            if (facts.Phase == ArchiveContentRecoveryPhase.Absent) WaitIf("absent-" + (facts.ReviewedCount - 1), point);
        }
        public void Dispose() => native.Dispose();
    }
}
