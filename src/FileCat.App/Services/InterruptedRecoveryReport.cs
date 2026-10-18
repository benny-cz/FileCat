using System.Globalization;
using System.Text;
using System.Text.Json;
using FileCat.Core.Jobs;
using FileCat.Core.FileSystem;

namespace FileCat.App.Services;

internal static class InterruptedRecoveryReport
{
    private const int DetailLimit = 10_000;

    internal static string Format(InterruptedJob job, string action,
        IReadOnlyList<string> reviewedStaged, IReadOnlyList<string> unreviewedStaged, CopyReview copies,
        bool stagedLimit, IReadOnlyList<string>? deleted = null, IReadOnlyList<string>? kept = null,
        IReadOnlyList<string>? renameEffects = null, IReadOnlyList<string>? allSources = null)
    {
        var text = new StringBuilder();
        var sources = allSources ?? job.Sources;
        int shown = 0;
        long known = 0;
        static string Count(long number) => number.ToString("N0", CultureInfo.InvariantCulture);
        void Line(string value = "")
        {
            text.AppendLine(value);
            if (text.Length > RecoveryReviewStore.MaxReportCharacters)
                throw new InvalidDataException("The recovery report exceeds the retained-detail budget. The journal remains open; use Hide for now and inspect the journal directly.");
        }
        void Detail(string label, string path, string? target = null)
        {
            known++;
            if (shown >= DetailLimit) return;
            Line(label + ": " + JsonSerializer.Serialize(path) +
                 (target is null ? "" : " → " + JsonSerializer.Serialize(target)));
            shown++;
        }

        Line("FileCat interrupted-operation review");
        Line("Title: " + JsonSerializer.Serialize(job.Title));
        Line("Kind: " + job.Kind);
        Line("Action: " + action);
        Line("Captured (UTC): " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Line("Journal: " + JsonSerializer.Serialize(job.JournalPath));
        Line("Destination: " + (job.Destination is null ? "not recorded" : JsonSerializer.Serialize(job.Destination)));
        Line($"Completed journal steps: {Count(job.CompletedSteps)}; open intents: {Count(job.OpenIntents.Count)}");
        Line($"Recorded source roots: {Count(sources.Count)} retained in this report of " +
             (job.SourceCount >= 0 ? Count(job.SourceCount) + " declared" : "an unknown declared total") + "; " +
             (job.SourceCount == sources.Count ? "all declared roots are represented" : "additional roots may be unavailable"));
        Line("Quoted paths use JSON escaping. Decoding the quoted value recovers its exact characters.");
        Line("This is a review-time snapshot. Files may change afterward. Closing tracking does not delete or repair files.");
        Line();
        if (deleted is not null)
            foreach (string path in deleted) Detail("Deleted by cleanup", path);
        if (kept is not null)
            foreach (string path in kept) Detail("Kept after cleanup", path);
        if (renameEffects is not null)
            foreach (string effect in renameEffects) Detail("Rename review outcome", effect);
        foreach (string path in reviewedStaged)
            Detail(deleted?.Contains(path, PathUtil.SafetyComparer) == true
                ? "Reviewed staged file deleted by cleanup" : "Reviewed staged file kept at this snapshot", path);
        foreach (string path in unreviewedStaged) Detail("Staged file not completely reviewed; kept", path);
        foreach (var copy in copies.Incomplete) Detail(deleted?.Contains(copy.Path, PathUtil.SafetyComparer) == true
            ? "Incomplete copy deleted by cleanup" : "Incomplete copy kept", copy.Path, copy.Source);
        foreach (string path in copies.Differing) Detail("Differing copy kept; compare with its source", path);
        foreach (var intent in job.OpenIntents)
        {
            Detail($"Open step {Count(intent.Step)} ({intent.Operation}) source", intent.Path, intent.Target);
            if (intent.Staged is { } staged) Detail("  staged path", staged);
            if (intent.Via is { } via) Detail("  temporary rename path", via);
        }
        foreach (string path in job.StagingDirectories) Detail("Staging directory", path);
        foreach (string path in sources) Detail("Recorded source", path);
        foreach (var fill in job.FillDirectories) Detail("Direct-copy mapping", fill.Source, fill.Destination);
        Line();
        Line($"Known detail rows: {Count(known)}; retained here: {Count(shown)}; omitted by this report's limit: {Count(known - shown)}.");
        if (job.SourceCount > sources.Count)
            Line($"{Count(job.SourceCount - sources.Count)} source roots are not retained in this report; their paths may no longer be available after tracking closes.");
        else if (job.SourceCount < 0)
            Line("The complete source-root count could not be reconstructed from the journal.");
        if (stagedLimit || copies.LimitReached || job.FillDirectoriesCut)
            Line("Further files or directories may exist beyond the recovery review limits. Their count and paths are unknown; no completeness claim is made.");
        return text.ToString();
    }
}
