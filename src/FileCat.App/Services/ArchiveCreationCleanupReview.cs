using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

internal sealed class ArchiveCreationCleanupReview : IArchiveRetainedCleanupReview
{
    private readonly IArchiveCreationCleanupLease lease;
    private readonly ArchiveCreationCleanup cleanup;
    public string ArchivePath { get; }
    public IReadOnlyList<string> FilesToRemove { get; }
    public IReadOnlyList<string> ObservedAbsent { get; }
    public bool RemovesBackup => false;
    public string TargetDescription => "The current archive matches the verified creation. Only its recorded private sidecars will be removed.";
    internal static bool CanOffer(InterruptedJob job) => ArchiveCleanupReview.CommonScope(job) &&
        job.ArchiveTransactions.ContentUpdates.Count == 0 && job.ArchiveTransactions.ContentCleanups.Count == 0 &&
        job.ArchiveTransactions.Transactions[0].Original is null && job.ArchiveTransactions.CreationCleanups.Count == 1 &&
        job.ArchiveTransactions.Transactions[0].Nonce == job.ArchiveTransactions.CreationCleanups[0].Nonce;
    internal ArchiveCreationCleanupReview(IArchiveCreationCleanupLease lease, string archivePath)
    {
        this.lease = lease; ArchivePath = archivePath;
        try
        {
            cleanup = new(lease); var present = new List<string>(); var absent = new List<string>();
            for (int i = cleanup.Facts.ReviewedCount; i < cleanup.Facts.Artifacts.Count; i++)
                (lease.HasArtifact(i) ? present : absent).Add(cleanup.Facts.Artifacts[i].Path);
            FilesToRemove = present.AsReadOnly(); ObservedAbsent = absent.AsReadOnly();
        }
        catch { lease.Dispose(); throw; }
    }
    public void Execute(CancellationToken ct) => cleanup.Execute(ct);
    public void Finish() => cleanup.Finish("User approved keeping the verified created archive and reconciling its exact retained-sidecar catalog.");
    public string Report(string outcome)
    {
        var facts = cleanup.Facts; var text = new StringBuilder("Archive creation retained-file cleanup review\n\n");
        text.AppendLine(outcome).AppendLine().AppendLine("Archive: " + JsonSerializer.Serialize(ArchivePath));
        text.AppendLine("Reviewed archive SHA-256: " + facts.Target.Sha256);
        for (int i = 0; i < facts.Artifacts.Count; i++)
        {
            uint bit = 1u << i;
            string state = (facts.DeletedMask & bit) != 0 ? "Recorded deletion" : (facts.AbsentMask & bit) != 0 ? "Recorded absence" : "Unresolved; fresh review required";
            text.AppendLine(state + ": " + JsonSerializer.Serialize(facts.Artifacts[i].Path));
        }
        text.AppendLine(facts.IsResolved ? "All retained-file outcomes are recorded." : "Recovery tracking remains open."); return text.ToString();
    }
    public void Dispose() => cleanup.Dispose();
}
