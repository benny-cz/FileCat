using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

/// <summary>Owns one native review through approval and exact cleanup. Displayed paths confer no authority.</summary>
internal sealed class ArchiveCleanupReview : IDisposable
{
    private readonly IArchiveContentCleanupRecoveryLease lease;
    private readonly ArchiveContentCleanupRecovery recovery;
    private readonly int previouslyReviewed;
    private readonly string[] linkedRestorations;
    internal string ArchivePath => lease.Catalog.ArchivePath;
    internal IReadOnlyList<string> FilesToRemove { get; }
    internal IReadOnlyList<string> ObservedAbsent { get; }
    internal bool RemovesBackup => FilesToRemove.Contains(lease.Catalog.Artifacts[^1].Path, StringComparer.Ordinal);
    internal string TargetDescription => lease.Candidate.TargetState switch
    {
        ArchiveContentRecoveryTarget.OriginalIntact => "The current archive matches the version from before this operation.",
        ArchiveContentRecoveryTarget.PublishedIntact => "The current archive matches the prepared update.",
        ArchiveContentRecoveryTarget.RestoredIntact => "The current archive matches the retained original bytes.",
        _ => throw new InvalidOperationException("Unknown archive review state."),
    };

    internal static bool CanOffer(InterruptedJob job)
    {
        var facts = job.ArchiveTransactions;
        return job.Kind == nameof(JobKind.ArchiveUpdate) && facts.Problem is null && facts.Transactions.Count == 1 &&
            facts.ContentUpdates.Count == 1 && facts.ContentCleanups.Count == 1 && job.OpenIntents.Count == 0 &&
            job.StagingDirectories.Count == 0 && job.FillDirectories.Count == 0 && !job.FillDirectoriesCut && job.ManifestPath is null &&
            facts.Transactions[0].Nonce == facts.ContentUpdates[0].Nonce && facts.ContentUpdates[0].Nonce == facts.ContentCleanups[0].Nonce &&
            facts.Transactions[0].RecordVersion == ArchiveTransactionFacts.SchemaVersion &&
            facts.Transactions[0].Profile.Format is ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd;
    }

    internal ArchiveCleanupReview(IArchiveContentCleanupRecoveryLease lease)
    {
        this.lease = lease;
        try
        {
            recovery = new(lease);
            linkedRestorations = lease.Journal.RetainedArchiveRecovery().ContentRestorations.Select(f => f.RestoreJournalPath).ToArray();
            previouslyReviewed = recovery.Facts?.ReviewedCount ?? lease.Catalog.DeletedCount;
            var present = new List<string>(); var absent = new List<string>();
            for (int i = previouslyReviewed; i < lease.Catalog.Artifacts.Count; i++)
                (lease.HasArtifact(i) ? present : absent).Add(lease.Catalog.Artifacts[i].Path);
            FilesToRemove = present.AsReadOnly(); ObservedAbsent = absent.AsReadOnly();
        }
        catch { lease.Dispose(); throw; }
    }
    internal ArchiveContentRecoveryFacts Execute(CancellationToken ct) => recovery.ResumeCleanup(ct);
    internal void Finish() => recovery.Finish("User approved keeping the freshly reviewed archive and reconciling its exact retained-file catalog.");
    internal string Report(string outcome)
    {
        var facts = recovery.Facts;
        var text = new StringBuilder("Archive retained-file cleanup review\n\n");
        text.AppendLine(outcome).AppendLine().AppendLine("Archive: " + JsonSerializer.Serialize(ArchivePath));
        text.AppendLine("At review: " + TargetDescription);
        text.AppendLine("Reviewed archive SHA-256: " + lease.Candidate.Target.Sha256);
        text.AppendLine("Previously reconciled files: " + previouslyReviewed);
        text.AppendLine("Files present at review: " + FilesToRemove.Count);
        text.AppendLine("Files observed absent at review: " + ObservedAbsent.Count).AppendLine();
        foreach (string linked in linkedRestorations) text.AppendLine("Related restoration record (not inspected): " + JsonSerializer.Serialize(linked));
        for (int i = 0; i < lease.Catalog.Artifacts.Count; i++)
        {
            uint bit = 1u << i;
            string state = facts is not null && (facts.DeletedMask & bit) != 0 ? "Recorded deletion"
                : facts is not null && (facts.AbsentMask & bit) != 0 ? "Recorded absence"
                : i < previouslyReviewed ? "Previously reconciled"
                : "Unresolved; fresh review required";
            text.AppendLine(state + ": " + JsonSerializer.Serialize(lease.Catalog.Artifacts[i].Path));
        }
        text.AppendLine().AppendLine(facts?.IsResolved == true ? "All retained-file outcomes are recorded." : "Recovery tracking remains open.");
        return text.ToString();
    }
    public void Dispose() => recovery.Dispose();
}
