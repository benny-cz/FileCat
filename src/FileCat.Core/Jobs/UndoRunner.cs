using FileCat.Core.FileSystem;
using FileCat.Core.Operations;

namespace FileCat.Core.Jobs;

/// <summary>Runs exactly one captured inverse, keeping each inverse's own safety rules.</summary>
internal sealed class UndoRunner(UndoPlan plan, IFileSystemOperations fs, Action checkpoint, JobJournal? journal = null,
    Action<UndoOutcome>? finished = null, Action<string>? current = null)
{
    private readonly UndoOutcome?[] _outcomes = new UndoOutcome?[plan.Steps.Count];
    public UndoReport Report(string unfinishedReason) => new(plan,
        plan.Steps.Select((s, i) => _outcomes[i] ?? new UndoOutcome(s.Kind, s.From, s.To, UndoOutcomeKind.NotAttempted, unfinishedReason)).ToArray(),
        journal?.NeedsRecovery == true, journal?.Path);

    private void Finish(int index, UndoOutcomeKind outcome, string reason)
    {
        var step = plan.Steps[index];
        if (_outcomes[index] is not null) return;
        var result = _outcomes[index] = new UndoOutcome(step.Kind, step.From, step.To, outcome, reason);
        finished?.Invoke(result);
    }

    public void Run()
    {
        var batch = plan.Steps.Select((s, i) => (Step: s, Index: i)).Where(x => x.Step.Kind == UndoKind.RenameBatchBack).ToArray();
        if (batch.Length > 0)
        {
            var bySource = batch.ToDictionary(x => x.Step.From, x => x.Step, PathUtil.SafetyComparer);
            var pairs = batch.Select(x => new BulkRenameRunner.Pair(x.Step.From, x.Step.To)).ToArray();
            BulkRenameRunner.Run(pairs, fs, checkpoint, (pair, now) =>
            {
                current?.Invoke("Rename back: " + pair.Source + " → " + pair.Target);
                var step = bySource[pair.Source];
                return !now.IsDirectory && (now.Size != step.Size || now.ModifiedUtc.Ticks != step.ModifiedTicks)
                    ? "It changed since the rename." : null;
            }, journal, (index, outcome) => Finish(batch[index].Index,
                outcome.Done ? UndoOutcomeKind.Restored : outcome.NotAttempted ? UndoOutcomeKind.NotAttempted : outcome.Kept ? UndoOutcomeKind.Kept : UndoOutcomeKind.Failed,
                outcome.Error ?? "Renamed back."));
        }
        for (int index = plan.Steps.Count - 1; index >= 0; index--)
        {
            var step = plan.Steps[index];
            if (step.Kind == UndoKind.RenameBatchBack) continue;
            checkpoint();
            current?.Invoke(UndoActions.Describe(step));
            int intent = 0;
            void Intent() => intent = journal?.Intent("undo-" + step.Kind, step.Kind == UndoKind.RestoreRecycled ? step.To : step.From, step.To) ?? 0;
            try
            {
                var (outcome, reason) = Apply(step, Intent);
                Finish(index, outcome, reason);
                if (intent != 0) journal!.Done(intent, outcome is UndoOutcomeKind.Restored or UndoOutcomeKind.Removed ? StepOutcome.Committed : StepOutcome.Failed, reason);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Finish(index, UndoOutcomeKind.Failed, ErrorText.Describe(ex));
                if (intent != 0) journal!.Done(intent, StepOutcome.Failed, ex.Message);
            }
            catch (Exception ex)
            {
                Finish(index, UndoOutcomeKind.Failed, "The inverse stopped unexpectedly: " + ErrorText.Describe(ex) + " Check both paths before making further changes.");
                throw;
            }
        }
    }

    private (UndoOutcomeKind Outcome, string Reason) Apply(UndoStep step, Action intent)
    {
        switch (step.Kind)
        {
            case UndoKind.MoveBack:
            {
                var now = fs.TryGetInfo(step.From);
                if (now is null) return (UndoOutcomeKind.Kept, "The item no longer exists at the recorded path.");
                // A directory inverse moves the current directory; it never promises a recursive content snapshot.
                if (!now.IsDirectory && (now.Size != step.Size || now.ModifiedUtc.Ticks != step.ModifiedTicks))
                    return (UndoOutcomeKind.Kept, "It changed since the operation.");
                if (fs.TryGetInfo(step.To) is not null && !string.Equals(step.From, step.To, StringComparison.OrdinalIgnoreCase))
                    return (UndoOutcomeKind.Kept, "An item exists again at the original location; it was not replaced.");
                intent();
                Directory.CreateDirectory(Path.GetDirectoryName(step.To)!);
                fs.Move(step.From, step.To, false);
                return (UndoOutcomeKind.Restored, "Moved back without replacing another item.");
            }
            case UndoKind.RestoreRecycled:
                if (step.RecycledId is null) return (UndoOutcomeKind.Kept, "The platform did not record a restorable Recycle Bin identity.");
                intent();
                return fs.TryRestoreRecycled(step.RecycledId, step.To, out var error)
                    ? (UndoOutcomeKind.Restored, "Restored from the Recycle Bin.")
                    : (UndoOutcomeKind.Kept, error ?? "The platform could not safely restore this recycled item.");
            case UndoKind.RemoveEmptyDirectory:
            {
                var directory = fs.TryGetInfo(step.From);
                if (directory is not { IsDirectory: true, IsLink: false } || Directory.EnumerateFileSystemEntries(step.From).Any())
                    return (UndoOutcomeKind.Kept, "The created folder is missing, was replaced, or is no longer empty.");
                intent(); fs.DeleteDirectory(step.From);
                return (UndoOutcomeKind.Removed, "Removed the empty created folder.");
            }
            case UndoKind.RemoveCreatedFile:
            {
                var file = fs.TryGetInfo(step.From);
                if (file is not { IsDirectory: false, IsLink: false, Size: 0 } || file.ModifiedUtc.Ticks != step.ModifiedTicks)
                    return (UndoOutcomeKind.Kept, "The created empty file is missing, was replaced, or changed since creation.");
                intent(); fs.DeleteFile(step.From);
                return (UndoOutcomeKind.Removed, "Removed the unchanged created empty file.");
            }
            case UndoKind.RemoveCreatedLink:
            {
                var link = fs.TryGetInfo(step.From);
                if (link is null) return (UndoOutcomeKind.Kept, "The link no longer exists.");
                if (!link.IsLink || link.LinkTarget is not { } target || !string.Equals(target.TrimEnd('\\', '/'), step.To.TrimEnd('\\', '/'), PathUtil.SafetyComparison))
                    return (UndoOutcomeKind.Kept, "It is no longer the link that was created.");
                intent();
                if (step.Size == 1) fs.DeleteDirectory(step.From); else fs.DeleteFile(step.From);
                return (UndoOutcomeKind.Removed, "Removed only the created link; its target was not removed.");
            }
            case UndoKind.RemoveCreatedHardLink:
            {
                var link = fs.TryGetInfo(step.From);
                if (link is null) return (UndoOutcomeKind.Kept, "The created hard-link name no longer exists.");
                // Shared bytes may legitimately have changed. Safety requires the same recorded identity at BOTH names.
                string? linkId = link is { IsDirectory: false, IsLink: false } ? fs.GetFileIdentity(step.From) : null;
                string? originalId = fs.TryGetInfo(step.To) is { IsDirectory: false, IsLink: false } ? fs.GetFileIdentity(step.To) : null;
                if (step.RecycledId is null || linkId != step.RecycledId || originalId != step.RecycledId)
                    return (UndoOutcomeKind.Kept, "The original name no longer retains the recorded file identity; removing the created name could lose data.");
                intent(); fs.DeleteFile(step.From);
                return (UndoOutcomeKind.Removed, "The original name still retains the same data.");
            }
            default:
                return (UndoOutcomeKind.Kept, "This inverse kind is not supported by the filesystem runner.");
        }
    }
}

/// <summary>The actual effects shown before approval and while a tracked inverse runs.</summary>
public static class UndoActions
{
    public static string Describe(UndoStep step) => step.Kind switch
    {
        UndoKind.MoveBack => $"Move back: \"{step.From}\" → \"{step.To}\"",
        UndoKind.RenameBatchBack => $"Rename back: \"{step.From}\" → \"{step.To}\"",
        UndoKind.RestoreRecycled => $"Restore from Recycle Bin: \"{step.To}\"",
        UndoKind.RemoveEmptyDirectory => $"Remove created empty folder: \"{step.From}\"",
        UndoKind.RemoveCreatedFile => $"Remove created empty file: \"{step.From}\"",
        UndoKind.RemoveCreatedLink => $"Remove created link: \"{step.From}\" (target retained: \"{step.To}\")",
        UndoKind.RemoveCreatedHardLink => $"Remove created hard-link name: \"{step.From}\" (data retained by original name: \"{step.To}\")",
        _ => $"Review inverse: \"{step.From}\" → \"{step.To}\"",
    };
}

internal sealed class UndoExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        var plan = Job.Request.UndoPlan ?? throw new InvalidOperationException("An approved inverse plan is required.");
        Job.AddTotals(plan.Steps.Count, 0); Job.TotalsFinal = true;
        void Outcome(UndoOutcome outcome)
        {
            if (outcome.Outcome is UndoOutcomeKind.Restored or UndoOutcomeKind.Removed) Job.ItemDone();
            else if (outcome.Outcome == UndoOutcomeKind.Failed) Job.ItemFailed();
            else Job.ItemSkipped();
            if (outcome.Outcome is UndoOutcomeKind.Failed or UndoOutcomeKind.Kept)
                Issue(outcome.Outcome == UndoOutcomeKind.Failed ? IssueSeverity.Error : IssueSeverity.Warning,
                    outcome.Kind == UndoKind.RestoreRecycled ? outcome.To : outcome.From, outcome.Reason,
                    outcome.Outcome == UndoOutcomeKind.Failed ? StepOutcome.Failed : StepOutcome.Skipped);
        }
        var runner = new UndoRunner(plan, Fs, Job.Checkpoint, Journal, Outcome, Job.SetCurrent);
        string unfinished = "The inverse stopped unexpectedly before this step ran; no automatic retry is offered.";
        try { runner.Run(); unfinished = "This step did not run."; }
        catch (OperationCanceledException) { unfinished = "Canceled before this inverse step ran; no automatic retry is offered."; throw; }
        finally { Job.SetUndoReport(runner.Report(unfinished)); }
    }
}
