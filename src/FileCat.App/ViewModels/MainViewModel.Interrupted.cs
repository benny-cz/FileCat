using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.App.Views;
using FileCat.App.Services;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<string> _interruptedActions = new(PathUtil.SafetyComparer);
    private sealed record InterruptedReview(IReadOnlyList<JournalRecovery.StagedFileReview> Staged, IReadOnlyList<string> Unreviewed, CopyReview Copies, bool Limited);

    private async Task<InterruptedReview> ReviewInterruptedAsync(InterruptedJob job)
    {
        var paths = new HashSet<string>(PathUtil.SafetyComparer); bool limited = false;
        foreach (var dir in job.StagingDirectories.Distinct(PathUtil.SafetyComparer))
        {
            var found = await MutationIoAsync(dir, () => JournalRecovery.FindStagedLeftovers(job with { StagingDirectories = [dir], OpenIntents = [] }, 1000, CheckSessionAction), CheckSessionAction);
            foreach (var path in found) { if (paths.Count == 1000) { limited = true; break; } paths.Add(path); }
            if (found.Count == 1000) limited = true;
            if (limited) break;
        }
        foreach (var intent in job.OpenIntents)
        {
            CheckSessionAction(); if (intent.Staged is not { } path) continue;
            if (paths.Count == 1000) { limited = true; break; }
            if (await MutationIoAsync(path, () => File.Exists(path), CheckSessionAction)) paths.Add(path);
        }
        var staged = new List<JournalRecovery.StagedFileReview>(); var unreviewed = new List<string>();
        long remaining = JournalRecovery.StagedReviewByteLimit;
        foreach (var path in paths)
        {
            var version = await MutationIoAsync(path, () => JournalRecovery.ReviewStagedFile(path, Services.Providers.For(Location.FileSystem(path)), CheckSessionAction, remaining), CheckSessionAction);
            if (version is null) unreviewed.Add(path); else { staged.Add(version); remaining -= version.Length; }
        }
        var incomplete = new List<IncompleteCopy>(); var differing = new List<string>(); bool copiesLimited = job.FillDirectoriesCut;
        // Each destination has its own admission. Cross-device source/destination atomicity remains a separate concern.
        foreach (var group in job.FillDirectories.GroupBy(f => f.Destination, PathUtil.SafetyComparer))
        {
            if (incomplete.Count + differing.Count >= 1000) { copiesLimited = true; break; }
            var copies = await MutationIoAsync(group.Key, () => JournalRecovery.ReviewCopies(job with { FillDirectories = group.ToList() }, 1000 - incomplete.Count - differing.Count, CheckSessionAction), CheckSessionAction);
            incomplete.AddRange(copies.Incomplete); differing.AddRange(copies.Differing); copiesLimited |= copies.LimitReached;
        }
        return new(staged, unreviewed, new CopyReview(incomplete, differing, copiesLimited), limited);
    }

    private static string StagedReviewNotes(InterruptedReview review) =>
        (review.Unreviewed.Count > 0 ? "\n\nThese staged files could not be completely reviewed and will be kept:\n" + InterruptedJobText.Bullets(review.Unreviewed) : "") +
        (review.Limited ? "\n\nThe staged-file review reached its limit. Files outside this review are kept; inspect the remaining files separately." : "");

    private async Task<(int Deleted, IReadOnlyList<string> Kept)> DeleteInterruptedPartialsAsync(InterruptedReview review)
    {
        int deleted = 0; var kept = new List<string>(review.Unreviewed);
        foreach (var version in review.Staged)
        {
            var result = await MutationIoAsync(version.Path, () =>
            {
                bool removed = JournalRecovery.DeleteReviewedStagedFile(version, Services.Providers.For(Location.FileSystem(version.Path)), CheckSessionAction);
                return (Removed: removed, Exists: !removed && File.Exists(version.Path));
            }, CheckSessionAction);
            if (result.Removed) deleted++; else if (result.Exists) kept.Add(version.Path);
        }
        foreach (var copy in review.Copies.Incomplete)
        {
            var result = await MutationIoAsync(copy.Path, () =>
            {
                int count = JournalRecovery.DeleteIncompleteCopies([copy], out var files, CheckSessionAction); return (Count: count, Kept: files);
            }, CheckSessionAction);
            deleted += result.Count; kept.AddRange(result.Kept);
        }
        return (deleted, kept.Distinct(PathUtil.SafetyComparer).ToList());
    }

    public Task<bool> ReviewNewInterruptedTransferAsync(InterruptedJob job) => RunInterruptedOwnedAsync(job, newTransfer: true);

    private async Task<bool> RunInterruptedOwnedAsync(InterruptedJob job, bool newTransfer = false)
    {
        if (!EditSessionsActive || !_interruptedActions.Add(job.JournalPath)) return false;
        bool submitted = false;
        try
        {
            CheckSessionAction();
            if (!await MutationIoAsync(job.JournalPath, () => JournalRecovery.IsInterrupted(job), CheckSessionAction)) return false;
            if (job.Kind is not (nameof(JobKind.Copy) or nameof(JobKind.Move))) return false;
            if (!newTransfer && !job.CanContinueTransfer)
            { Notify(job.TransferIntentProblem ?? "The original transfer options are incomplete. Choose Review a new transfer to decide its scope and options again.", true); return false; }
            var kind = job.Kind == nameof(JobKind.Move) ? JobKind.Move : JobKind.Copy;
            if (RecoverableTransferIntent.LocalDestination(job.Destination) is not { } destination)
            { Notify("The local destination of this operation is not recorded; select the items and the destination again."); return false; }
            if (job.SourceCount is <= 0 or > RecoverableTransferIntent.RootLimit)
            { Notify("The source selection exceeds the complete recovery review limit. Select the sources again to review a new transfer; cleanup remains available.", true); return false; }
            var paths = await MutationIoAsync(job.JournalPath, () => JournalRecovery.LoadSources(job), CheckSessionAction);
            if (paths is null) { Notify("Not all source items of this operation are recorded; select them again to repeat it."); return false; }
            if (paths.Any(path => !RecoverableTransferIntent.ValidPath(path)))
            { Notify("A recorded source path is invalid. Select the sources again to review a new transfer.", true); return false; }
            var intent = newTransfer ? null : job.TransferIntent;
            if (intent is not null && !intent.MatchesSources(paths))
            { Notify("The source manifest no longer matches the recorded transfer mapping. Select the sources again; the old journal is kept.", true); return false; }
            var sources = new List<ItemRef>(); var sourceReviews = new Dictionary<ItemRef, SourcePathReview>();
            var sourceTrees = new Dictionary<ItemRef, ReviewedSourceTree>();
            long sourceBytes = JournalRecovery.StagedReviewByteLimit; int sourceItems = 1000;
            for (int index = 0; index < paths.Count; index++)
            {
                string path = paths[index];
                var version = await MutationIoAsync(path, () => SourcePathReview.Capture(path, Services.Jobs.FileOperations, CheckSessionAction), CheckSessionAction);
                if (version is not null)
                {
                    var local = ItemRef.ForFileSystemPath(path, version.Info.IsDirectory ? EntryKind.Directory : EntryKind.File);
                    var item = new ItemRef(local.Parent, local.Name, local.Kind) { RelativeFolder = intent?.Roots[index].RelativeFolder };
                    var provider = Services.Providers.For(Location.FileSystem(path));
                    var tree = await MutationIoAsync(path, () => FileTreeReview.Capture(path, Services.Jobs.FileOperations, provider, CheckSessionAction, sourceBytes, sourceItems, allowLinks: true), CheckSessionAction);
                    if (tree is null || !tree.Items.First(i => i.Path == path).Version.Equals(version))
                    { Notify("A source could not be completely reviewed. The old journal is kept; select the sources again to decide what to copy or move.", true); return false; }
                    sourceTrees[item] = new(tree, provider); sourceBytes -= tree.Bytes; sourceItems -= tree.Items.Count;
                    sources.Add(item); sourceReviews[item] = version;
                }
            }
            if (sources.Count == 0) { Notify("None of the source items could be found and checked. Select the sources again to decide what to " + (kind == JobKind.Move ? "move." : "copy.")); return false; }
            string? newName = intent?.NewName;
            var options = intent?.RecoveryOptions() ?? new TransferOptions { Conflicts = ConflictPolicy.Skip };
            var mode = intent?.Mode ?? QueueMode.Start;
            if (newTransfer)
            {
                string initialDestination = job.SourceCount == 1 && job.NewName is { Length: > 0 } oldName && PathUtil.ValidateNewName(oldName) is null
                    ? Path.Combine(destination.Path, oldName) : AppendSeparator(destination.Path);
                var draft = await OperationDialogs.ShowTransferAsync(this, new TransferDialogInput(kind, sources, SizeSummary(sources), initialDestination,
                    "recorded destination", 0, false)
                {
                    RecoveryReviewNotice = (job.TransferIntentProblem ?? "The original transfer options are incomplete.") +
                        " This is a new transfer. The available recorded source paths are prefilled; original relative-folder mappings and filtering cannot be assumed. Choose the destination, matching rules, verification and conflict policy below. Selected roots will go directly into the destination. Timestamps and attributes will be preserved.",
                });
                CheckSessionAction();
                if (draft is null) return false;
                // Relative edits are interpreted against the captured destination, independent of a later active tab.
                if (!Services.Providers.TryParse(draft.Destination, destination, out var parsed) || parsed is not { IsFileSystem: true })
                { Notify("Choose a local destination for this reviewed transfer.", true); return false; }
                string destinationText = parsed.Path + (draft.Destination.EndsWith('/') || draft.Destination.EndsWith('\\') ? Path.DirectorySeparatorChar.ToString() : "");
                if (!ResolveDestination(destinationText, sources, out var newDestination, out newName, out var error) || newDestination is not { IsFileSystem: true })
                { Notify(error ?? "Choose a local destination for this reviewed transfer.", true); return false; }
                destination = newDestination;
                options = draft.Options;
                mode = draft.Queue ? QueueMode.Queue : QueueMode.Start;
            }
            var review = await ReviewInterruptedAsync(job);
            var partial = review.Staged.Select(s => s.Path).Concat(review.Copies.Incomplete.Select(i => i.Path)).ToList();
            var message = $"{Formatters.SafeName(job.Title)}: {sources.Count:N0} of {job.SourceCount:N0} recorded source roots are available for this review. Unavailable roots are omitted."
                + (newTransfer ? " The source-to-destination mapping and options below describe the new transfer you selected." : " The original name mapping, matching rules, folder layout and verification choices are preserved.")
                + InterruptedJobText.TransferReview(kind, destination, sources, newName, options, mode, continuing: !newTransfer)
                + (partial.Count > 0 ? $"\n\nFirst, {Formatters.Plural(partial.Count, "partial file", "partial files")} left by the interruption will be deleted:\n" + InterruptedJobText.Bullets(partial) : "")
                + InterruptedJobText.CopyNotes(review.Copies) + StagedReviewNotes(review);
            string action = (newTransfer ? "Start new " : "Continue ") + (kind == JobKind.Move ? "move" : "copy");
            bool consequential = kind == JobKind.Move || partial.Count > 0 || options.Conflicts is
                ConflictPolicy.Replace or ConflictPolicy.ReplaceIfNewer or ConflictPolicy.KeepBothRenameExisting;
            if (!await Dialogs.ConfirmAsync(action, message, action, danger: consequential)) return false;
            if (!await InterruptedSourcesStillReviewedAsync(sourceReviews, sourceTrees)) return false;
            CheckSessionAction(); var result = await DeleteInterruptedPartialsAsync(review);
            if (result.Kept.Count > 0)
                await Dialogs.AlertAsync(action, "These files were kept because they changed or could not be completely reviewed. The new operation uses the conflict policy shown in your review:\n" + InterruptedJobText.Bullets(result.Kept));
            CheckSessionAction();
            if (!await InterruptedSourcesStillReviewedAsync(sourceReviews, sourceTrees)) return false;
            // Submission is the UI admission boundary. The old journal stays open through every approval/alert.
            Services.Jobs.Submit(new JobRequest { Kind = kind, Sources = sources, Destination = destination, NewName = newName,
                ExpectedSources = sourceReviews, ExpectedSourceTrees = sourceTrees, Options = options, Mode = mode });
            submitted = true;
            bool closed = await MutationIoAsync(job.JournalPath, () => JournalRecovery.TryClose(job,
                $"{(newTransfer ? "Replaced by an explicitly reviewed new transfer" : "Continued with recorded transfer intent and recovery Skip policy")}; {result.Deleted} partial file(s) deleted."), CheckSessionAction);
            if (!closed) Notify("The new operation started, but the old journal could not be closed. It is kept for review.", true);
            return true;
        }
        catch (OperationCanceledException) { return submitted; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { if (EditSessionsActive) Notify($"Cannot continue this operation: {ex.Message}", true); return false; }
        finally { _interruptedActions.Remove(job.JournalPath); }
    }

    private async Task<bool> InterruptedSourcesStillReviewedAsync(IReadOnlyDictionary<ItemRef, SourcePathReview> reviews, IReadOnlyDictionary<ItemRef, ReviewedSourceTree> trees)
    {
        foreach (var (item, reviewed) in reviews)
        {
            var path = item.FileSystemPath!;
            var tree = trees[item];
            if (await MutationIoAsync(path, () => reviewed.Matches(path, Services.Jobs.FileOperations, CheckSessionAction) &&
                tree.Tree.Matches(Services.Jobs.FileOperations, tree.Provider, CheckSessionAction), CheckSessionAction)) continue;
            Notify("A source changed or could not be checked while this operation was being reviewed. The old journal is kept; select the sources again to decide what to copy or move.", true);
            return false;
        }
        return true;
    }

    internal async Task<bool> CleanupInterruptedAsync(InterruptedJob job)
    {
        if (!EditSessionsActive || !_interruptedActions.Add(job.JournalPath)) return false;
        try
        {
            if (!await MutationIoAsync(job.JournalPath, () => JournalRecovery.IsInterrupted(job), CheckSessionAction)) return false;
            var review = await ReviewInterruptedAsync(job);
            var renames = new List<JournalRecovery.RenameReview>(); var unreviewedRenames = new List<string>(); bool renameLimited = false;
            long renameBytes = JournalRecovery.StagedReviewByteLimit; int renameItems = 1000;
            foreach (var intent in job.OpenIntents.Where(i => i.Operation == JobJournal.RenameViaOp && i.Via is not null))
            {
                if (renames.Count + unreviewedRenames.Count == 1000) { renameLimited = true; break; }
                var version = await MutationIoAsync(intent.Via!, () => JournalRecovery.ReviewRename(intent, Services.Jobs.FileOperations, Services.Providers.For(Location.FileSystem(intent.Via!)), CheckSessionAction, renameBytes, renameItems), CheckSessionAction);
                if (version is null) unreviewedRenames.Add(intent.Via!);
                else { renames.Add(version); renameBytes -= version.Bytes; renameItems -= version.Items.Count; }
            }
            var partial = review.Staged.Select(s => s.Path).Concat(review.Copies.Incomplete.Select(c => c.Path)).ToList();
            var message = (partial.Count > 0 ? "These reviewed partial files will be deleted:\n" + InterruptedJobText.Bullets(partial) : "No completely reviewed partial files remain.")
                + InterruptedJobText.CopyNotes(review.Copies) + StagedReviewNotes(review);
            if (renames.Count > 0) message += "\n\nThese reviewed interrupted renames will be finished if their contents and paths remain unchanged (the original name is used when the new name is taken):\n" + InterruptedJobText.Bullets(renames.Select(r => r.Intent.Path + " → " + r.Intent.Target).ToList());
            if (unreviewedRenames.Count > 0) message += "\n\nThese temporary items could not be completely reviewed and will be kept with the journal:\n" + InterruptedJobText.Bullets(unreviewedRenames);
            if (renameLimited) message += "\n\nThe rename review reached its limit. Remaining temporary names are kept; inspect them separately.";
            var intents = job.OpenIntents.Where(i => i.Operation != JobJournal.RenameViaOp).Take(10).Select(i => $"• {i.Operation}: {i.Path}{(i.Target is null ? "" : " → " + i.Target)}");
            message += "\n\nSteps that were in progress (inspect these items yourself):\n" + string.Join("\n", intents);
            string action = (renames.Count > 0, partial.Count > 0) switch { (true, true) => "Finish renaming and delete partial files", (true, false) => "Finish renaming", (false, true) => "Delete partial files", _ => "OK" };
            if (!await Dialogs.ConfirmAsync("Interrupted operation", message, action, danger: partial.Count > 0 || renames.Count > 0)) return false;
            CheckSessionAction(); var result = await DeleteInterruptedPartialsAsync(review);
            int renamed = 0; var notRenamed = new List<string>(); bool unresolvedRenames = renameLimited || unreviewedRenames.Count > 0;
            foreach (var rename in renames)
            {
                var outcome = await MutationIoAsync(rename.Intent.Via!, () => JournalRecovery.FinishReviewedRename(rename, Services.Jobs.FileOperations, Services.Providers.For(Location.FileSystem(rename.Intent.Via!)), CheckSessionAction), CheckSessionAction);
                renamed += outcome.Finished; notRenamed.AddRange(outcome.Report); unresolvedRenames |= !outcome.Resolved;
            }
            if (result.Kept.Count > 0) await Dialogs.AlertAsync("Interrupted operation", "These files were kept because they changed or could not be completely reviewed:\n" + InterruptedJobText.Bullets(result.Kept));
            CheckSessionAction();
            if (notRenamed.Count > 0) await Dialogs.AlertAsync("Interrupted rename", string.Join("\n", notRenamed.Take(20)));
            CheckSessionAction();
            if (unresolvedRenames) { Notify("Temporary rename items remain unresolved. The old journal is kept for review.", true); if (renames.Count > 0) RefreshAll(); return false; }
            bool closed = await MutationIoAsync(job.JournalPath, () => JournalRecovery.TryClose(job, $"Reviewed; {result.Deleted} partial file(s) deleted, {renamed} rename(s) finished."), CheckSessionAction);
            Notify(closed ? $"Removed {Formatters.Plural(result.Deleted, "partial file", "partial files")}; finished {Formatters.Plural(renamed, "rename", "renames")}." : "The old journal could not be closed. It is kept for review.", !closed);
            if (renames.Count > 0) RefreshAll();
            return closed;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { if (EditSessionsActive) Notify($"Cannot clean up this operation: {ex.Message}", true); return false; }
        finally { _interruptedActions.Remove(job.JournalPath); }
    }

    internal async Task<bool> DismissInterruptedAsync(InterruptedJob job)
    {
        if (!EditSessionsActive || !_interruptedActions.Add(job.JournalPath)) return false;
        try
        {
            if (!await MutationIoAsync(job.JournalPath, () => JournalRecovery.IsInterrupted(job), CheckSessionAction)) return false;
            return await MutationIoAsync(job.JournalPath, () => JournalRecovery.TryClose(job, "Dismissed by the user."), CheckSessionAction);
        }
        catch (OperationCanceledException) { return false; }
        finally { _interruptedActions.Remove(job.JournalPath); }
    }
}
