using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

/// <summary>
/// Reviewed removal of the unfinished files of an archive change that never reached publication (stopped, refused or
/// interrupted before saving). Opening only observes; the discard record is written after approval. Paths shown here are
/// observations; the native lease binds every object by its recorded identity.
/// </summary>
internal sealed class ArchiveDiscardReview : IArchiveRetainedCleanupReview
{
    private readonly ArchiveTransactionDiscard discard;
    private readonly bool creation;
    public string ArchivePath { get; }
    public IReadOnlyList<string> FilesToRemove { get; }
    public IReadOnlyList<string> ObservedAbsent { get; }
    public bool RemovesBackup => false;
    public string TargetDescription => creation
        ? "This new archive was never saved. Only FileCat's own unfinished files for it will be removed."
        : "This archive change was never saved, so the archive was not changed by it. Only FileCat's own unfinished files for it will be removed.";
    public string Title => "Review unfinished archive files";
    public string ArchiveHeading => creation ? "Not created: " : "Keep this archive unchanged: ";
    public string KeepSummary => creation ? "No archive was created" : "Keep current archive unchanged";
    public string ActionLabel => "Remove unfinished files";
    public string CompletedText => "Removed the unfinished files of an archive change that was never saved.";

    internal static bool CanOffer(InterruptedJob job)
    {
        var facts = job.ArchiveTransactions;
        return ArchiveCleanupReview.CommonScope(job) && facts.ContentUpdates.Count == 0 && facts.ContentCleanups.Count == 0 &&
            facts.CreationCleanups.Count == 0 && facts.ExchangeUpdates.Count == 0 && ArchiveDiscardFacts.IsDiscardable(facts.Transactions[0]) &&
            facts.Discards.All(discard => discard.Nonce == facts.Transactions[0].Nonce) && facts.Discards.Count <= 1;
    }

    internal ArchiveDiscardReview(IArchiveDiscardLease lease)
    {
        try
        {
            ArchivePath = lease.Preparation.ArchivePath; creation = lease.Preparation.Original is null;
            discard = new(lease);
        }
        catch { lease.Dispose(); throw; }
        try { (FilesToRemove, ObservedAbsent) = discard.Observe(); }
        catch { discard.Dispose(); throw; }
    }
    public void Execute(CancellationToken ct) => discard.Execute(ct);
    public void Finish() => discard.Finish("User approved removing the unfinished files of an archive change that was never saved.");
    public string Report(string outcome)
    {
        var facts = discard.Facts; var text = new StringBuilder("Unfinished archive change cleanup review\n\n");
        text.AppendLine(outcome).AppendLine().AppendLine((creation ? "Archive not created: " : "Archive kept unchanged: ") + JsonSerializer.Serialize(ArchivePath));
        for (int i = 0; i < facts.Artifacts.Count; i++)
        {
            uint bit = 1u << i;
            string state = (facts.DeletedMask & bit) != 0 ? "Recorded removal" : (facts.AbsentMask & bit) != 0 ? "Recorded absence" : "Unresolved; fresh review required";
            text.AppendLine(state + ": " + JsonSerializer.Serialize(facts.Artifacts[i].Path));
        }
        foreach (string path in facts.Unrecorded) text.AppendLine("Reserved name, never recorded (must be absent): " + JsonSerializer.Serialize(path));
        text.AppendLine(facts.IsResolved ? "All unfinished-file outcomes are recorded." : "Recovery tracking remains open."); return text.ToString();
    }
    public void Dispose() => discard.Dispose();
}
