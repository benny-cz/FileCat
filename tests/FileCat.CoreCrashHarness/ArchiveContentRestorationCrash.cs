using System.Reflection;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

// Test-only wrappers around the actual fresh restoration recipe and confined native workers.
// The driver kills this parent externally; production has no checkpoint callbacks.
internal static class ArchiveContentRestorationCrash
{
    internal static readonly string[] Points = ["opened", "stage-created", "prepared", "transaction-opened", "backup-sealed",
        "private-ready", "intent", "native-committed", "published-reviewed", "recorded", "cleanup-completed", "closed"];

    internal static async Task<int> Run(string root, string point, string windowsAssembly)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(root) || !Directory.Exists(root) ||
            (!Points.Contains(point) && point != "finish") || !Path.IsPathFullyQualified(windowsAssembly)) return 64;
        var job = JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero).Single();
        var content = job.ArchiveTransactions.ContentUpdates.Single();
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(job.JournalPath)));
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(windowsAssembly);
        var type = assembly.GetType("FileCat.Platform.Windows.Archives.WindowsArchiveRestorationLease", true)!;
        var open = type.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException(type.FullName, "Open");
        using var native = (IArchiveContentRestorationLease)open.Invoke(null, [job.JournalPath, digest, content.Nonce, CancellationToken.None])!;
        using var fresh = JobJournal.Create(Path.Combine(root, "restore-journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(root, "original.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned fresh restoration crash", "owned", [], []));
        File.WriteAllText(Path.Combine(root, "restore-job.txt"), fresh.Path);
        var workerType = assembly.GetType("FileCat.Platform.Windows.Shell.ArchiveAppContainer", true)!;
        string assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_PROBE_ASSETS") ?? throw new IOException("Supply complete confined probe assets.");
        if (!Path.IsPathFullyQualified(assets)) throw new IOException("Confined probe assets must be absolute.");
        workerType.GetMethod("ProvisionRuntimeReadAccess", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [assets]);
        var children = new List<object>();
        using var restored = await ArchiveContentRestoration.RestoreAsync(new Lease(native, point, CheckWorkers), fresh,
            new((nonce, cpu) => Start("archive-assess", nonce, cpu)), new((nonce, cpu) => Start("archive-verify", nonce, cpu)), TimeSpan.FromMinutes(2), CancellationToken.None);
        CheckWorkers(); WaitIf("recorded", point);
        restored.Update.Cleanup(fresh, CancellationToken.None); WaitIf("cleanup-completed", point);
        fresh.Finish(JobState.Completed, "fresh restoration and its separate private cleanup completed"); WaitIf("closed", point);
        return point == "finish" ? 0 : 65;

        IArchiveWorkerProcess Start(string role, Guid nonce, TimeSpan cpu)
        {
            object child = workerType.GetMethod("Start", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, [Path.Combine(assets, "FileCat.WorkerProbe.exe"), role + " " + nonce, cpu])!;
            children.Add(child); return (IArchiveWorkerProcess)child;
        }
        void CheckWorkers()
        {
            if (children.Count != 2 || children.Any(child => !(bool)workerType.GetProperty("HasExited")!.GetValue(child)! ||
                    (uint)workerType.GetProperty("ExitCode")!.GetValue(child)! != 0) ||
                children.Select(child => (int)workerType.GetProperty("ProcessId")!.GetValue(child)!).Distinct().Count() != 2)
                throw new IOException("Both distinct confined restoration workers must have exited successfully.");
            File.WriteAllText(Path.Combine(root, "restore-workers.txt"), string.Join(',', children.Select(child => workerType.GetProperty("ProcessId")!.GetValue(child))));
        }
    }
    private static void WaitIf(string checkpoint, string point)
    {
        if (checkpoint != point) return;
        Console.WriteLine("archive-content-restore-checkpoint:" + point); Console.Out.Flush(); Console.ReadLine();
        throw new InvalidOperationException("The driver must terminate this actual restoration process externally.");
    }
    private sealed class Lease(IArchiveContentRestorationLease native, string point, Action checkWorkers) : IArchiveContentRestorationLease
    {
        private bool stageCreated;
        public JobJournal Journal => native.Journal;
        public ArchiveTransactionFacts Preparation => native.Preparation;
        public ArchiveContentUpdateFacts Content => native.Content;
        public ArchiveContentCleanupFacts Catalog => native.Catalog;
        public ArchiveRetainedBinding Target => native.Target;
        public ArchiveRetainedBinding Snapshot => native.Snapshot;
        public IArchiveContentUpdateSource TargetSource
        {
            get { checkWorkers(); WaitIf("prepared", point); return new Source(native.TargetSource, point); }
        }
        public ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum) => native.Reserve(nonce, kind, maximum);
        public IArchiveContentUpdateStage Create(ArchiveArtifactReservation reservation)
        {
            var file = native.Create(reservation);
            if (!stageCreated && reservation.Kind == ArchiveArtifactKind.Stage) { stageCreated = true; WaitIf("stage-created", point); }
            return file;
        }
        public void Claim(CancellationToken ct) { native.Claim(ct); WaitIf("opened", point); }
        public void Review(CancellationToken ct) => native.Review(ct);
        public void ReviewSnapshot(CancellationToken ct) => native.ReviewSnapshot(ct);
        public void Dispose() => native.Dispose();
    }
    private sealed class Source(IArchiveContentUpdateSource native, string point) : IArchiveContentUpdateSource
    {
        public ArchiveContentUpdateRecipe Recipe => native.Recipe;
        public string ObjectIdentity => native.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct) => native.Observe(ct);
        public void Review(ArchiveFileObservation expected, CancellationToken ct) => native.Review(expected, ct);
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => native.OpenReadView(expected, ct);
        public ArchiveArtifactReservation ReserveContentSnapshot(Guid nonce, ArchiveSpaceEstimate space, CancellationToken ct) => native.ReserveContentSnapshot(nonce, space, ct);
        public IArchiveContentTransaction BeginContentTransaction(ArchiveFileObservation reviewed, Guid nonce, TimeSpan timeout, CancellationToken ct)
        {
            var transaction = native.BeginContentTransaction(reviewed, nonce, timeout, ct);
            WaitIf("transaction-opened", point); return new Transaction(transaction, point);
        }
    }
    private sealed class Transaction(IArchiveContentTransaction native, string point) : IArchiveContentTransaction
    {
        public Guid TransactionId => native.TransactionId;
        public ArchiveFileObservation RetainOriginal(IArchiveContentUpdateStage snapshot, CancellationToken ct)
        { var result = native.RetainOriginal(snapshot, ct); WaitIf("backup-sealed", point); return result; }
        public ArchiveContentUpdateReview PrepareOutput(IArchiveContentUpdateStage output, ArchiveVerifiedNativeStage verified, ArchiveOriginSnapshot origin, CancellationToken ct)
        { var result = native.PrepareOutput(output, verified, origin, ct); WaitIf("private-ready", point); return result; }
        public ArchiveFileObservation Commit(ArchiveContentUpdateReview review, CancellationToken ct)
        { WaitIf("intent", point); var result = native.Commit(review, ct); WaitIf("native-committed", point); return result; }
        public ArchiveFileObservation ReviewPublished(ArchiveContentUpdateReview review, CancellationToken ct)
        { var result = native.ReviewPublished(review, ct); WaitIf("published-reviewed", point); return result; }
        public void Rollback() => native.Rollback();
        public void Dispose() => native.Dispose();
    }
}
