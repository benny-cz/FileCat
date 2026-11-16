using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

/// <summary>
/// Review and approval of an interrupted Unix exchange update. The inspection observes both names and the remaining private
/// objects; on approval the recovery re-checks everything, records the observed outcome, removes the exact private objects
/// and folder, and closes tracking. The public archive is kept as it is; nothing is swapped back.
/// </summary>
internal sealed class ArchiveExchangeCleanupReview : IArchiveRetainedCleanupReview
{
    private readonly UnixArchiveExchangeRecovery recovery;
    private readonly UnixArchiveExchangeRecovery.Inspection inspection;
    private ArchiveExchangeUpdateFacts? outcome;
    public string ArchivePath => recovery.Facts.ArchivePath;
    public IReadOnlyList<string> FilesToRemove { get; }
    public IReadOnlyList<string> ObservedAbsent => inspection.Absent;
    public bool RemovesBackup => inspection.Published && inspection.Present.Contains(recovery.Facts.StagePath, StringComparer.Ordinal);
    public string TargetDescription => inspection.Published
        ? "The current archive matches the prepared update. The retained copy of the previous version will be removed."
        : "The current archive matches the version from before this operation. The unpublished update will be removed.";

    internal static bool CanOffer(InterruptedJob job) => ArchiveCleanupReview.CommonScope(job) &&
        job.ArchiveTransactions.ExchangeUpdates.Count == 1 && job.ArchiveTransactions.ContentUpdates.Count == 0 &&
        job.ArchiveTransactions.ContentCleanups.Count == 0 && job.ArchiveTransactions.CreationCleanups.Count == 0 &&
        job.ArchiveTransactions.Transactions[0].Nonce == job.ArchiveTransactions.ExchangeUpdates[0].Nonce &&
        job.ArchiveTransactions.ExchangeUpdates[0].Phase != ArchiveExchangeUpdatePhase.NeedsReview;

    internal ArchiveExchangeCleanupReview(UnixArchiveExchangeRecovery recovery, CancellationToken ct)
    {
        this.recovery = recovery;
        try
        {
            inspection = recovery.Inspect(ct);
            FilesToRemove = inspection.Present.Append(inspection.PrivateDirectory + Path.DirectorySeparatorChar).ToArray();
        }
        catch { recovery.Dispose(); throw; }
    }

    public void Execute(CancellationToken ct) => outcome = recovery.Resume(ct);
    public void Finish() => recovery.Finish("User approved keeping the reviewed archive and removing this update's exact private files.");

    public string Report(string result)
    {
        var facts = outcome ?? recovery.Facts;
        var text = new StringBuilder("Archive update cleanup review (Unix)\n\n");
        text.AppendLine(result).AppendLine().AppendLine("Archive: " + JsonSerializer.Serialize(ArchivePath));
        text.AppendLine("At review: " + TargetDescription);
        text.AppendLine("Recorded state: " + facts.Phase + (facts.WasPublished ? " (update published)" : " (update not published)"));
        text.AppendLine("Published archive SHA-256: " + (facts.Published?.Sha256 ?? "none"));
        text.AppendLine("Previous archive SHA-256: " + facts.Original.Sha256);
        text.AppendLine("Private folder: " + JsonSerializer.Serialize(facts.PrivateDirectoryPath));
        for (int i = 0; i < facts.CleanupCount; i++)
        {
            string path = i < facts.Sidecars.Count ? facts.Sidecars[i].Path : facts.StagePath;
            text.AppendLine((i < facts.CleanedCount ? "Removed exactly: " : "Retained: ") + JsonSerializer.Serialize(path));
        }
        text.AppendLine().AppendLine(facts.IsResolved ? "All private files of this update are removed." : "Recovery tracking remains open.");
        return text.ToString();
    }

    public void Dispose() => recovery.Dispose();
}
