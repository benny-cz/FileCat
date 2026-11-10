using System.ComponentModel;
using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>Post-publication source accounting over the captured native graph. No pathname grants deletion.</summary>
internal static class ArchiveNativeMoveSources
{
    internal static void Remove(Job job, JobJournal journal, ArchivePlan request, ArchiveCapturedSelection selection,
        Action<bool, CancellationToken> reviewPublished, IReadOnlySet<int>? committedInputs = null)
    {
        if (!request.MoveSources || !selection.CanRemoveSources || selection.SourceSpecification is not { } specification ||
            request.Changes.Count != job.Request.Sources.Count) throw new ArgumentException("A native move requires its frozen source selection.");
        var selectors = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < specification.Count; i++) selectors.Add(specification[i].Path, i + 1);
        var roots = new Dictionary<int, int>();
        foreach (var member in selection.Members)
            if (selection.CapturedNode(member.Id).Parent is null) roots.Add(member.RootSelector, member.Id);
        var removed = new HashSet<int>(); var blocked = new HashSet<ArchiveSelectionNode>();
        int files = 0, directories = 0, settled = 0;
        job.AddTotals(selection.Members.Count, 0);
        job.RecordUndoRemainder(request.ArchivePath, "Archive moves have no automatic inverse; Undo does not remove archive members or recreate originals.");
        try
        {
            job.SetSummary("Archive saved; removing originals.");
            job.Checkpoint(); reviewPublished(true, job.Token);
            foreach (var member in selection.Members.Where(member => member.Type == ArchiveMemberType.File)) RemoveOne(member);
            foreach (var member in selection.Members.Where(member => member.Type == ArchiveMemberType.Directory)
                .OrderByDescending(member => selection.CapturedNode(member.Id).Depth)) RemoveOne(member);
        }
        catch (OperationCanceledException)
        { Warn(request.ArchivePath, "Move canceled after the archive was saved; remaining originals were kept."); }
        catch (Exception error) when (Expected(error))
        { Warn(request.ArchivePath, "Source removal stopped: " + error.Message); }
        finally
        {
            // Cancellation or a lost publication pin may stop traversal before every item is visited.
            for (; settled < selection.Members.Count; settled++) job.ItemSkipped();
            for (int i = 0; i < request.Changes.Count; i++)
            {
                string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Changes[i].SourcePath!));
                if (selectors.TryGetValue(path, out int selector) && roots.TryGetValue(selector, out int id) && removed.Contains(id)) job.RootCompleted(i);
                else job.RootFailed(i);
            }
            int kept = checked(selection.Members.Count - removed.Count + selection.ExcludedCount);
            job.SetSummary(kept == 0 ? $"Archive saved; moved {Count(files, "original file")} and {Count(directories, "folder")}."
                : $"Archive saved; removed {Count(files, "original file")} and {Count(directories, "folder")}; {Count(kept, "source item")} " +
                    (kept == 1 ? "was" : "were") + " not confirmed removed" +
                    (job.IsCancellationRequested ? "; move canceled." : "."));
        }

        void RemoveOne(ArchiveCapturedMember member)
        {
            job.Checkpoint(); reviewPublished(false, job.Token);
            var node = selection.CapturedNode(member.Id);
            string parent = Path.GetDirectoryName(specification[node.RootSelector - 1].Path)!;
            string path = Path.Combine(parent, node.NativeRelativePath.Replace('/', Path.DirectorySeparatorChar));
            job.SetCurrent("Removing original: " + path);
            if (committedInputs is not null && !committedInputs.Contains(member.Id))
            {
                BlockParents(node); job.ItemSkipped(); settled++;
                return;
            }
            if (blocked.Contains(node) || selection.HasExcludedDescendants(member.Id))
            {
                BlockParents(node); job.ItemSkipped(); settled++;
                Warn(path, "The source folder contains an excluded item or a child not confirmed moved."); return;
            }
            // Where the platform renames a file aside before removing it, that exact name is journaled first, so an
            // interrupted move's report says where the original is (plan §11.6.1: never lose an original).
            string? aside = member.Type == ArchiveMemberType.File && selection.RemovesThroughAsideName
                ? ".filecat-move-" + Guid.NewGuid().ToString("N") : null;
            int step = journal.Intent("archive-native-move-delete", path, request.ArchivePath,
                aside is null ? null : Path.Combine(Path.GetDirectoryName(path)!, aside));
            try
            {
                selection.RemoveCaptured(member.Id, job.Token, aside);
            }
            catch (OperationCanceledException) { journal.Done(step, StepOutcome.CanceledBeforeChange); throw; }
            catch (Exception error) when (Expected(error))
            {
                journal.Done(step, StepOutcome.PartiallyApplied, "Archive saved; source removal not confirmed. " + error.Message);
                BlockParents(node); job.ItemSkipped(); settled++; Warn(path, error.Message); return;
            }
            // The native mutation succeeded. A later journal failure must not authorize a second deletion attempt.
            removed.Add(member.Id); if (member.Type == ArchiveMemberType.File) files++; else directories++;
            job.ItemDone(); settled++; journal.Done(step, StepOutcome.Committed);
        }
        void BlockParents(ArchiveSelectionNode node)
        { for (var parent = node.Parent; parent is not null; parent = parent.Parent) blocked.Add(parent); }
        void Warn(string path, string reason) => job.AddIssue(new(IssueSeverity.Warning, path,
            "Archive saved; " + reason, StepOutcome.Committed));
    }
    private static string Count(int count, string noun) => count == 1 ? "1 " + noun : $"{count:N0} {noun}s";
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception;
}
