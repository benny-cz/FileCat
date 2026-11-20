using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

internal interface IArchiveRetainedCleanupReview : IDisposable
{
    string ArchivePath { get; }
    IReadOnlyList<string> FilesToRemove { get; }
    IReadOnlyList<string> ObservedAbsent { get; }
    bool RemovesBackup { get; }
    string TargetDescription { get; }
    void Execute(CancellationToken ct);
    void Finish();
    string Report(string outcome);
    // Dialog wording; a discard of a never-saved change keeps nothing new and may concern an archive that never existed.
    string Title => "Review archive cleanup";
    string ArchiveHeading => "Keep this archive: ";
    string KeepSummary => "Keep current archive";
    string ActionLabel => "Keep archive and clean up";
    string CompletedText => "Kept the reviewed archive and finished cleanup of its retained files.";
}

/// <summary>Owns one native review through approval and exact cleanup. Displayed paths confer no authority.</summary>
internal sealed class ArchiveCleanupReview : IArchiveRetainedCleanupReview
{
    private readonly IArchiveContentCleanupRecoveryLease lease;
    private readonly ArchiveContentCleanupRecovery recovery;
    private readonly int previouslyReviewed;
    private readonly string[] linkedRestorations;
    public string ArchivePath => lease.Catalog.ArchivePath;
    public IReadOnlyList<string> FilesToRemove { get; }
    public IReadOnlyList<string> ObservedAbsent { get; }
    public bool RemovesBackup => FilesToRemove.Contains(lease.Catalog.Artifacts[^1].Path, StringComparer.Ordinal);
    public string TargetDescription => lease.Candidate.TargetState switch
    {
        ArchiveContentRecoveryTarget.OriginalIntact => "The current archive matches the version from before this operation.",
        ArchiveContentRecoveryTarget.PublishedIntact => "The current archive matches the prepared update.",
        ArchiveContentRecoveryTarget.RestoredIntact => "The current archive matches the retained original bytes.",
        _ => throw new InvalidOperationException("Unknown archive review state."),
    };

    internal static bool CommonScope(InterruptedJob job)
    {
        var facts = job.ArchiveTransactions;
        return job.Kind == nameof(JobKind.ArchiveUpdate) && facts.Problem is null && facts.Transactions.Count == 1 &&
            job.OpenIntents.Count == 0 &&
            job.StagingDirectories.Count == 0 && job.FillDirectories.Count == 0 && !job.FillDirectoriesCut && job.ManifestPath is null &&
            facts.Transactions[0].RecordVersion == ArchiveTransactionFacts.SchemaVersion &&
            facts.Transactions[0].Profile.Format is ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd or
                ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip or ArchiveWriteFormat.Gzip or ArchiveWriteFormat.Bzip2 or ArchiveWriteFormat.Zstd;
    }
    internal static bool CanOffer(InterruptedJob job) => CommonScope(job) && job.ArchiveTransactions.ContentUpdates.Count == 1 &&
        job.ArchiveTransactions.ContentCleanups.Count == 1 && job.ArchiveTransactions.CreationCleanups.Count == 0 &&
        job.ArchiveTransactions.Transactions[0].Nonce == job.ArchiveTransactions.ContentUpdates[0].Nonce &&
        job.ArchiveTransactions.ContentUpdates[0].Nonce == job.ArchiveTransactions.ContentCleanups[0].Nonce;

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
    void IArchiveRetainedCleanupReview.Execute(CancellationToken ct) => Execute(ct);
    public void Finish() => recovery.Finish("User approved keeping the freshly reviewed archive and reconciling its exact retained-file catalog.");
    public string Report(string outcome)
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
