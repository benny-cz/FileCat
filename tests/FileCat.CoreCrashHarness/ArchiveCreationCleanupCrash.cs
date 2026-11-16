using System.Reflection;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

// Test-only wrappers around fresh native leases; the parent is killed outside this process.
internal static class ArchiveCreationCleanupCrash
{
    internal static int Run(string root, string point, string windowsAssembly)
    {
        string[] points = ["opened", "completed", "finished", "absent-0", "needs-review-before", "needs-review-after",
            .. Enumerable.Range(0, 4).SelectMany(i => new[] { "intent-" + i, "unlinked-" + i, "recorded-" + i })];
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(root) || !Directory.Exists(root) ||
            !points.Contains(point) || !Path.IsPathFullyQualified(windowsAssembly)) return 64;
        var job = JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero).Single();
        var catalog = job.ArchiveTransactions.CreationCleanups.Single();
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(windowsAssembly);
        var type = assembly.GetType("FileCat.Platform.Windows.Archives.WindowsArchiveCreationCleanupRecovery", true)!;
        var open = type.GetMethod("OpenCurrentLease", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException(type.FullName, "OpenCurrentLease");
        var native = (IArchiveCreationCleanupLease)open.Invoke(null, [job.JournalPath, catalog.Nonce, CancellationToken.None])!;
        using var cleanup = new ArchiveCreationCleanup(new Lease(native, point));
        WaitIf("opened", point);
        try { cleanup.Execute(CancellationToken.None); }
        catch (InjectedFailure) { WaitIf(point, point); throw; }
        WaitIf("completed", point);
        cleanup.Finish("Actual creation recovery process completed and closed its journal.");
        WaitIf("finished", point);
        return 65;
    }
    private static void WaitIf(string checkpoint, string point)
    {
        if (checkpoint != point) return;
        Console.WriteLine("archive-creation-cleanup-checkpoint:" + point); Console.Out.Flush(); Console.ReadLine();
        throw new InvalidOperationException("The driver must terminate this actual process externally.");
    }
    private sealed class InjectedFailure : IOException;
    private sealed class Lease(IArchiveCreationCleanupLease native, string point) : IArchiveCreationCleanupLease
    {
        public JobJournal Journal => native.Journal;
        public ArchiveCreationCleanupFacts Catalog => native.Catalog;
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
            var facts = Journal.RetainedArchiveRecovery().CreationCleanups.Single();
            if (facts.Phase == ArchiveCreationCleanupPhase.DeleteIntent) WaitIf("intent-" + facts.ReviewedCount, point);
            if (facts.Phase == ArchiveCreationCleanupPhase.Deleted) WaitIf("recorded-" + (facts.ReviewedCount - 1), point);
            if (facts.Phase == ArchiveCreationCleanupPhase.Absent) WaitIf("absent-" + (facts.ReviewedCount - 1), point);
        }
        public void Dispose() => native.Dispose();
    }
}
