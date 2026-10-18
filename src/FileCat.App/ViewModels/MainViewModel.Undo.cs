using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<Guid> _undoReviews = [];

    private async Task UndoLastAsync()
    {
        var jobs = Services.Jobs.Jobs;
        if (_undoReviews.Count > 0 || jobs.Any(j => j.Request.UndoPlan is not null && !j.State.IsFinished()))
        {
            Notify("An Undo is being reviewed or is still running. Review it in Operations (Ctrl+J) before starting another Undo.");
            return;
        }
        var job = jobs.Where(j => j.CanUndo).OrderByDescending(j => j.FinishedUtc).FirstOrDefault();
        var latest = jobs.Where(j => j.State.IsFinished()).OrderByDescending(j => j.FinishedUtc).FirstOrDefault();
        if (latest is { UndoTruncated: true } && !ReferenceEquals(latest, job))
        {
            Notify($"\"{Formatters.SafeName(latest.Title)}\" changed more than {Job.UndoLimit:N0} items, so no undo steps were recorded for it. Recycled items can still be restored from the Recycle Bin.", true);
            return;
        }
        if (job is null)
        {
            Notify("There is no operation that can be undone safely. Permanent deletions, overwrites, and copies are never undone automatically.");
            return;
        }
        await UndoJobAsync(job, mostRecentUndoable: true, newerOperationsExist: latest is not null && !ReferenceEquals(latest, job));
    }

    /// <summary>A claim owns the immutable review before the first await; an admitted job owns all later mutation.</summary>
    public async Task UndoJobAsync(Job job, bool mostRecentUndoable = false, bool newerOperationsExist = false)
    {
        if (!EditSessionsActive) return;
        using var reservation = job.TryReserveUndo();
        if (reservation is null) return;
        _undoReviews.Add(job.Id);
        try
        {
            var plan = reservation.Plan;
            bool registry = plan.Steps.All(s => s.Kind == UndoKind.RegistryInverse);
            bool approved;
            if (registry)
            {
                var inverses = plan.Steps.Reverse().Select(s => s.Registry!).ToList();
                var lines = inverses.Select(Platform.Windows.RegistryChangeRunner.Describe);
                int irreversible = job.Request.RegistryChanges.Count(c => c.Action == RegistryAction.DeleteKey) +
                                   (job.Request.Registry is { Action: RegistryAction.DeleteKey } ? 1 : 0);
                approved = await Dialogs.ConfirmAsync(mostRecentUndoable ? "Review most recent undoable Registry operation" : "Undo Registry changes",
                    (newerOperationsExist ? "More recent finished operations are not covered by this Undo.\n\n" : "") +
                    $"Undo \"{Formatters.SafeName(job.Title)}\"? FileCat applies these changes only where the data is still exactly what the operation left:\n" +
                    string.Join("\n", lines.Select(Formatters.SafeName)) +
                    (irreversible > 0 ? $"\n\n{Formatters.Plural(irreversible, "deleted key subtree is", "deleted key subtrees are")} not restored: their data was not retained." : ""),
                    "Undo", danger: true);
            }
            else approved = await UndoReviewDialog.ShowAsync(this, plan, mostRecentUndoable, newerOperationsExist);
            if (!approved || !EditSessionsActive) return;
            // This transfer consumes only the approved snapshot, and only after JobManager owns the inverse.
            var undo = Services.Jobs.SubmitUndo(reservation);
            if (ActiveTab is { } tab) Track(undo, tab);
            Operations.IsOpen = true;
            Notify($"{Formatters.SafeName(undo.Title)}: {undo.State.Describe().ToLowerInvariant()}. Progress and the complete result are kept in Operations (Ctrl+J).");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Notify("Undo could not be started: " + Formatters.SafeName(ex.Message) + " The original steps remain available if admission did not complete.", true);
        }
        finally { _undoReviews.Remove(job.Id); }
    }

    internal ReportWindow? ShowUndoResults(Job job)
    {
        if (!EditSessionsActive) return null;
        if (job.UndoReport is not { } report)
        {
            Notify("The Undo report is available after this attempt finishes.");
            return null;
        }
        // The user explicitly opens this retained report. Background completion never takes keyboard focus.
        var window = new ReportWindow("Undo report", Formatters.SafeName(report.Plan.OriginalTitle),
            _ => Task.FromResult(report.ToText(Formatters.SafeName)));
        window.Show();
        return window;
    }

    internal async Task ShowUndoAffectedAsync(Job job)
    {
        if (job.UndoReport is not { } report) return;
        var files = await Task.Run(() =>
        {
            var result = new List<ItemRef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in report.Plan.Steps)
            {
                IEnumerable<string> paths = step.Kind == UndoKind.RestoreRecycled ? [step.To]
                    : step.Kind is UndoKind.MoveBack or UndoKind.RenameBatchBack or UndoKind.RemoveCreatedHardLink ? [step.From, step.To] : [step.From];
                foreach (string path in paths)
                {
                    if (!Path.IsPathFullyQualified(path) || !seen.Add(path)) continue;
                    try
                    {
                        if (Services.Platform.FileOperations.TryGetInfo(path) is { } info)
                            result.Add(ItemRef.ForFileSystemPath(path, info.IsDirectory ? EntryKind.Directory : EntryKind.File));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
                }
            }
            return result;
        });
        if (!EditSessionsActive) return;
        if (files.Count == 0)
        {
            Notify("None of the affected paths currently exists. The Undo report keeps the recorded paths and outcomes.");
            return;
        }
        var set = Services.ResultSets.Create("Undo affected files", "Existing affected paths from " + report.Plan.OriginalTitle);
        set.FullFolders = true;
        foreach (var file in files) set.Add(file, file.Parent.Path);
        set.IsComplete = true;
        OpenResultSet(set, null);
    }
}
