using System.Collections.Concurrent;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.App.Views;
using FileCat.App.Services;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<string> _interruptedActions = new(PathUtil.SafetyComparer);
    private readonly Dictionary<string, RecoveryActionScope> _recoveryReviews = new(PathUtil.SafetyComparer);
    private sealed class RecoveryActionScope(MainViewModel owner, InterruptedJob job) : IDisposable
    {
        public readonly CancellationTokenSource StopSource = new();
        public readonly ConcurrentQueue<string> DeletedPaths = new();
        public readonly ConcurrentQueue<string> RenameEffects = new();
        public RecoveryActionPhase Phase { get; private set; } = RecoveryActionPhase.Reviewing;
        public string JournalPath => job.JournalPath;
        public bool Stopped => StopSource.IsCancellationRequested;
        public void Check()
        {
            owner.CheckSessionAction();
            StopSource.Token.ThrowIfCancellationRequested();
        }
        public void SetPhase(RecoveryActionPhase phase, string status)
        {
            Phase = phase;
            owner.Operations.AnnounceRecovery(status);
            owner.Operations.Interrupted.FirstOrDefault(row => PathUtil.SafetyComparer.Equals(row.Job.JournalPath, job.JournalPath))
                ?.SetActionPhase(phase, status);
        }
        public void Stop()
        {
            if (Phase is RecoveryActionPhase.Idle or RecoveryActionPhase.Submitted) return;
            StopSource.Cancel();
            SetPhase(Phase, Phase == RecoveryActionPhase.Applying
                ? "Stopping after the current safe step; completed changes are kept."
                : "Stopping review; no changes have been approved.");
        }
        public void Dispose() => StopSource.Dispose();
    }

    private async Task PreserveStoppedRecoveryAsync(InterruptedJob job, RecoveryActionScope scope, InterruptedReview? review)
    {
        if (!EditSessionsActive || review is null || scope.Phase != RecoveryActionPhase.Applying) return;
        try
        {
            bool changed = !scope.DeletedPaths.IsEmpty || !scope.RenameEffects.IsEmpty;
            string report = await Task.Run(() => InterruptedRecoveryReport.Format(job,
                changed ? "Stopped after approved cleanup; listed effects were already committed; recovery tracking remains open"
                    : "Stopped before any reviewed cleanup effect or new-job submission; recovery tracking remains open",
                review.Staged.Select(s => s.Path).ToArray(), review.Unreviewed, review.Copies, review.Limited,
                deleted: scope.DeletedPaths.ToArray(), renameEffects: scope.RenameEffects.ToArray()));
            var record = new RecoveryReviewRecord(job.JournalPath, job.Title, job.Kind, DateTime.UtcNow,
                changed ? "Stopped cleanup; committed effects retained" : "Stopped before changes", report, ClosureConfirmed: false);
            await MutationIoAsync(job.JournalPath, () => { RecoveryReviewStore.Save(Services.Paths.JournalDirectory, record); return true; }, CheckSessionAction);
            Operations.AddReviewedRecovery(record);
            Notify(changed
                ? "Recovery cleanup stopped after a safe step. Completed changes are kept, the journal remains open, and a report lists the known effects."
                : "Recovery review stopped before changes. The journal remains open and the review report is retained.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        {
            if (EditSessionsActive)
                Notify("Recovery stopped and the journal remains open; the review report could not be saved: " + Formatters.SafeName(ex.Message), true);
        }
    }

    private bool TryBeginRecoveryAction(InterruptedJob job, out RecoveryActionScope scope)
    {
        scope = null!;
        if (!EditSessionsActive || !_interruptedActions.Add(job.JournalPath)) return false;
        scope = new RecoveryActionScope(this, job);
        _recoveryReviews.Add(job.JournalPath, scope);
        scope.SetPhase(RecoveryActionPhase.Reviewing, "Reviewing recovery sources…");
        return true;
    }

    private void EndRecoveryAction(RecoveryActionScope scope)
    {
        _recoveryReviews.Remove(scope.JournalPath);
        _interruptedActions.Remove(scope.JournalPath);
        scope.SetPhase(RecoveryActionPhase.Idle, scope.Phase == RecoveryActionPhase.Submitted
            ? "Recovery action submitted. Check its operation and tracking status above."
            : scope.Stopped ? "Review stopped. Any completed cleanup is kept; the recovery journal remains available."
            : scope.Phase == RecoveryActionPhase.Applying
                ? "Recovery action ended before submission. Any completed cleanup is kept; review the journal and report."
                : "Review closed without approval. The recovery journal remains available.");
        scope.Dispose();
    }

    internal void StopInterruptedReview(InterruptedJob job)
    {
        if (_recoveryReviews.TryGetValue(job.JournalPath, out var scope)) scope.Stop();
    }
    private sealed record InterruptedReview(IReadOnlyList<JournalRecovery.StagedFileReview> Staged, IReadOnlyList<string> Unreviewed, CopyReview Copies, bool Limited);
    private sealed record PartialCleanupResult(int Deleted, IReadOnlyList<string> DeletedPaths, IReadOnlyList<string> Kept);

    private async Task<InterruptedReview> ReviewInterruptedAsync(InterruptedJob job, RecoveryActionScope scope)
    {
        var paths = new HashSet<string>(PathUtil.SafetyComparer); bool limited = false;
        foreach (var dir in job.StagingDirectories.Distinct(PathUtil.SafetyComparer))
        {
            var found = await MutationIoAsync(dir, () => JournalRecovery.FindStagedLeftovers(job with { StagingDirectories = [dir], OpenIntents = [] }, 1000, scope.Check), scope.Check);
            foreach (var path in found) { if (paths.Count == 1000) { limited = true; break; } paths.Add(path); }
            if (found.Count == 1000) limited = true;
            if (limited) break;
        }
        foreach (var intent in job.OpenIntents)
        {
            scope.Check(); if (intent.Staged is not { } path) continue;
            if (paths.Count == 1000) { limited = true; break; }
            if (await MutationIoAsync(path, () => File.Exists(path), scope.Check)) paths.Add(path);
        }
        var staged = new List<JournalRecovery.StagedFileReview>(); var unreviewed = new List<string>();
        long remaining = JournalRecovery.StagedReviewByteLimit;
        foreach (var path in paths)
        {
            var version = await MutationIoAsync(path, () => JournalRecovery.ReviewStagedFile(path, Services.Providers.For(Location.FileSystem(path)), scope.Check, remaining), scope.Check);
            if (version is null) unreviewed.Add(path); else { staged.Add(version); remaining -= version.Length; }
        }
        var incomplete = new List<IncompleteCopy>(); var differing = new List<string>(); bool copiesLimited = job.FillDirectoriesCut;
        // Each destination has its own admission. Cross-device source/destination atomicity remains a separate concern.
        foreach (var group in job.FillDirectories.GroupBy(f => f.Destination, PathUtil.SafetyComparer))
        {
            if (incomplete.Count + differing.Count >= 1000) { copiesLimited = true; break; }
            var copies = await MutationIoAsync(group.Key, () => JournalRecovery.ReviewCopies(job with { FillDirectories = group.ToList() }, 1000 - incomplete.Count - differing.Count, scope.Check), scope.Check);
            incomplete.AddRange(copies.Incomplete); differing.AddRange(copies.Differing); copiesLimited |= copies.LimitReached;
        }
        return new(staged, unreviewed, new CopyReview(incomplete, differing, copiesLimited), limited);
    }

    private static string StagedReviewNotes(InterruptedReview review) =>
        (review.Unreviewed.Count > 0 ? "\n\nThese staged files could not be completely reviewed and will be kept:\n" + InterruptedJobText.Bullets(review.Unreviewed) : "") +
        (review.Limited ? "\n\nThe staged-file review reached its limit. Files outside this review are kept; inspect the remaining files separately." : "");

    private async Task<PartialCleanupResult> DeleteInterruptedPartialsAsync(InterruptedReview review, RecoveryActionScope scope)
    {
        int deleted = 0; var deletedPaths = new List<string>(); var kept = new List<string>(review.Unreviewed);
        foreach (var version in review.Staged)
        {
            var result = await MutationIoAsync(version.Path, () =>
            {
                bool removed = JournalRecovery.DeleteReviewedStagedFile(version, Services.Providers.For(Location.FileSystem(version.Path)), scope.Check);
                if (removed) scope.DeletedPaths.Enqueue(version.Path);
                return (Removed: removed, Exists: !removed && File.Exists(version.Path));
            }, scope.Check);
            if (result.Removed) { deleted++; deletedPaths.Add(version.Path); }
            else if (result.Exists) kept.Add(version.Path);
        }
        foreach (var copy in review.Copies.Incomplete)
        {
            var result = await MutationIoAsync(copy.Path, () =>
            {
                int count = JournalRecovery.DeleteIncompleteCopies([copy], out var files, scope.Check);
                if (count > 0) scope.DeletedPaths.Enqueue(copy.Path);
                return (Count: count, Kept: files);
            }, scope.Check);
            deleted += result.Count;
            if (result.Count > 0) deletedPaths.Add(copy.Path);
            kept.AddRange(result.Kept);
        }
        return new(deleted, deletedPaths.Distinct(PathUtil.SafetyComparer).ToList(), kept.Distinct(PathUtil.SafetyComparer).ToList());
    }

    public Task<bool> ReviewNewInterruptedTransferAsync(InterruptedJob job) => RunInterruptedOwnedAsync(job, newTransfer: true);

    private async Task<bool> RunInterruptedOwnedAsync(InterruptedJob job, bool newTransfer = false)
    {
        if (!TryBeginRecoveryAction(job, out var scope)) return false;
        bool submitted = false;
        InterruptedReview? reviewed = null;
        try
        {
            scope.Check();
            if (!await MutationIoAsync(job.JournalPath, () => JournalRecovery.IsInterrupted(job), scope.Check)) return false;
            if (job.Kind is not (nameof(JobKind.Copy) or nameof(JobKind.Move))) return false;
            if (!newTransfer && !job.CanContinueTransfer)
            { Notify(job.TransferIntentProblem ?? "The original transfer options are incomplete. Choose Review a new transfer to decide its scope and options again.", true); return false; }
            var kind = job.Kind == nameof(JobKind.Move) ? JobKind.Move : JobKind.Copy;
            if (RecoverableTransferIntent.LocalDestination(job.Destination) is not { } destination)
            { Notify("The local destination of this operation is not recorded; select the items and the destination again."); return false; }
            if (job.SourceCount is <= 0 or > RecoverableTransferIntent.RootLimit)
            { Notify("The source selection exceeds the complete recovery review limit. Select the sources again to review a new transfer; cleanup remains available.", true); return false; }
            var paths = await MutationIoAsync(job.JournalPath, () => JournalRecovery.LoadSources(job), scope.Check);
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
                var version = await MutationIoAsync(path, () => SourcePathReview.Capture(path, Services.Jobs.FileOperations, scope.Check), scope.Check);
                if (version is not null)
                {
                    var local = ItemRef.ForFileSystemPath(path, version.Info.IsDirectory ? EntryKind.Directory : EntryKind.File);
                    var item = new ItemRef(local.Parent, local.Name, local.Kind) { RelativeFolder = intent?.Roots[index].RelativeFolder };
                    var provider = Services.Providers.For(Location.FileSystem(path));
                    var tree = await MutationIoAsync(path, () => FileTreeReview.Capture(path, Services.Jobs.FileOperations, provider, scope.Check, sourceBytes, sourceItems, allowLinks: true), scope.Check);
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
                scope.SetPhase(RecoveryActionPhase.AwaitingApproval, "Choose and review a new transfer…");
                string initialDestination = job.SourceCount == 1 && job.NewName is { Length: > 0 } oldName && PathUtil.ValidateNewName(oldName) is null
                    ? Path.Combine(destination.Path, oldName) : AppendSeparator(destination.Path);
                var draft = await OperationDialogs.ShowTransferAsync(this, new TransferDialogInput(kind, sources, SizeSummary(sources), initialDestination,
                    "recorded destination", 0, false)
                {
                    BaseLocation = destination,
                    InitialLocation = destination,
                    RecoveryReviewNotice = (job.TransferIntentProblem ?? "The original transfer options are incomplete.") +
                        " This is a new transfer. The available recorded source paths are prefilled; original relative-folder mappings and filtering cannot be assumed. Choose the destination, matching rules, verification and conflict policy below. Selected roots will go directly into the destination. Timestamps and attributes will be preserved.",
                }, scope.StopSource.Token);
                scope.Check();
                if (draft is null) return false;
                if (draft.ResolvedDestination is not { IsFileSystem: true } newDestination)
                { Notify("Choose a local destination for this reviewed transfer.", true); return false; }
                destination = newDestination;
                newName = draft.NewName;
                options = draft.Options;
                mode = draft.Queue ? QueueMode.Queue : QueueMode.Start;
            }
            scope.SetPhase(RecoveryActionPhase.Reviewing, "Reviewing staged files and source changes…");
            var review = reviewed = await ReviewInterruptedAsync(job, scope);
            var partial = review.Staged.Select(s => s.Path).Concat(review.Copies.Incomplete.Select(i => i.Path)).ToList();
            string transferReview = InterruptedJobText.TransferReview(kind, destination, sources, newName, options, mode, continuing: !newTransfer);
            var message = $"{Formatters.SafeName(job.Title)}: {sources.Count:N0} of {job.SourceCount:N0} recorded source roots are available for this review. Unavailable roots are omitted."
                + (newTransfer ? " The source-to-destination mapping and options below describe the new transfer you selected." : " The original name mapping, matching rules, folder layout and verification choices are preserved.")
                + transferReview
                + (partial.Count > 0 ? $"\n\nFirst, {Formatters.Plural(partial.Count, "partial file", "partial files")} left by the interruption will be deleted:\n" + InterruptedJobText.Bullets(partial) : "")
                + InterruptedJobText.CopyNotes(review.Copies) + StagedReviewNotes(review);
            string action = (newTransfer ? "Start new " : "Continue ") + (kind == JobKind.Move ? "move" : "copy");
            bool consequential = newTransfer || kind == JobKind.Move || partial.Count > 0 || options.Conflicts is
                ConflictPolicy.Replace or ConflictPolicy.ReplaceIfNewer or ConflictPolicy.KeepBothRenameExisting;
            var highlights = InterruptedJobText.ApprovalHighlights(kind, destination, sources,
                job.SourceCount, newName, options, mode, newTransfer);
            var warnings = InterruptedJobText.ApprovalWarnings(kind, sources.Count, job.SourceCount,
                options, newTransfer, partial.Count,
                review.Unreviewed.Count, review.Copies.Differing.Count,
                review.Limited || review.Copies.LimitReached);
            scope.SetPhase(RecoveryActionPhase.AwaitingApproval, "Awaiting approval of the reviewed recovery plan…");
            bool approved = Dialogs is OverlayDialogService overlay
                ? await overlay.ConfirmWithCancellationAsync(action, message, action, consequential,
                    scope.StopSource.Token, highlights, collapseDetails: true, warnings: warnings)
                : await Dialogs.ConfirmAsync(action, message, action, danger: consequential);
            scope.Check();
            if (!approved) return false;
            if (!await InterruptedSourcesStillReviewedAsync(sourceReviews, sourceTrees, scope)) return false;
            scope.SetPhase(RecoveryActionPhase.Applying, "Applying reviewed cleanup; completed changes are kept…");
            scope.Check(); var result = await DeleteInterruptedPartialsAsync(review, scope);
            if (result.Kept.Count > 0)
                await Dialogs.AlertAsync(action, "These files were kept because they changed or could not be completely reviewed. The new operation uses the conflict policy shown in your review:\n" + InterruptedJobText.Bullets(result.Kept));
            scope.Check();
            if (!await InterruptedSourcesStillReviewedAsync(sourceReviews, sourceTrees, scope)) return false;
            string report = await Task.Run(() => InterruptedRecoveryReport.Format(job,
                newTransfer ? "Reviewed new transfer approved; see the closure status for tracking" : "Continuation with recorded intent approved; see the closure status for tracking",
                review.Staged.Select(s => s.Path).ToArray(), review.Unreviewed, review.Copies, review.Limited,
                result.DeletedPaths, result.Kept, allSources: paths));
            if ((long)report.Length + transferReview.Length > RecoveryReviewStore.MaxReportCharacters)
                throw new InvalidDataException("The reviewed transfer report exceeds the retention budget; the old journal remains open.");
            report += "\nReviewed transfer choices and complete available-root mapping:" + transferReview;
            var record = new RecoveryReviewRecord(job.JournalPath, job.Title, job.Kind, DateTime.UtcNow,
                newTransfer ? "Reviewed new transfer" : "Continued recorded transfer", report, ClosureConfirmed: false);
            await MutationIoAsync(job.JournalPath, () => { RecoveryReviewStore.Save(Services.Paths.JournalDirectory, record); return true; }, scope.Check);
            Operations.AddReviewedRecovery(record);
            // Submission is the UI admission boundary. The old journal stays open through every approval/alert.
            scope.Check();
            Services.Jobs.Submit(new JobRequest { Kind = kind, Sources = sources, Destination = destination, NewName = newName,
                ExpectedSources = sourceReviews, ExpectedSourceTrees = sourceTrees, Options = options, Mode = mode });
            submitted = true;
            scope.SetPhase(RecoveryActionPhase.Submitted, "New operation submitted; use its own row to stop it.");
            bool closed = await MutationIoAsync(job.JournalPath, () => JournalRecovery.TryClose(job,
                $"{(newTransfer ? "Replaced by an explicitly reviewed new transfer" : "Continued with recorded transfer intent and recovery Skip policy")}; {result.Deleted} partial file(s) deleted."), CheckSessionAction);
            if (!closed) Notify("The new operation started, but the old journal could not be closed. It is kept for review.", true);
            else
            {
                record = record with { ClosureConfirmed = true };
                try
                {
                    await MutationIoAsync(job.JournalPath, () => { RecoveryReviewStore.Save(Services.Paths.JournalDirectory, record); return true; }, CheckSessionAction);
                    Operations.AddReviewedRecovery(record);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { Notify("The old journal closed, but the saved report still says closure was not confirmed: " + Formatters.SafeName(ex.Message), true); }
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!submitted) await PreserveStoppedRecoveryAsync(job, scope, reviewed);
            return submitted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        { if (EditSessionsActive) Notify("Cannot continue this operation: " + Formatters.SafeName(ex.Message) + " The old journal remains available if no replacement was admitted.", true); return submitted; }
        finally { EndRecoveryAction(scope); }
    }

    private async Task<bool> InterruptedSourcesStillReviewedAsync(IReadOnlyDictionary<ItemRef, SourcePathReview> reviews, IReadOnlyDictionary<ItemRef, ReviewedSourceTree> trees, RecoveryActionScope scope)
    {
        foreach (var (item, reviewed) in reviews)
        {
            var path = item.FileSystemPath!;
            var tree = trees[item];
            if (await MutationIoAsync(path, () => reviewed.Matches(path, Services.Jobs.FileOperations, scope.Check) &&
                tree.Tree.Matches(Services.Jobs.FileOperations, tree.Provider, scope.Check), scope.Check)) continue;
            Notify("A source changed or could not be checked while this operation was being reviewed. The old journal is kept; select the sources again to decide what to copy or move.", true);
            return false;
        }
        return true;
    }

    internal async Task<bool> CleanupInterruptedAsync(InterruptedJob job)
    {
        if (!TryBeginRecoveryAction(job, out var scope)) return false;
        InterruptedReview? reviewed = null;
        try
        {
            if (!await MutationIoAsync(job.JournalPath, () => JournalRecovery.IsInterrupted(job), scope.Check)) return false;
            var review = reviewed = await ReviewInterruptedAsync(job, scope);
            var renames = new List<JournalRecovery.RenameReview>(); var unreviewedRenames = new List<string>(); bool renameLimited = false;
            long renameBytes = JournalRecovery.StagedReviewByteLimit; int renameItems = 1000;
            foreach (var intent in job.OpenIntents.Where(i => i.Operation == JobJournal.RenameViaOp && i.Via is not null))
            {
                if (renames.Count + unreviewedRenames.Count == 1000) { renameLimited = true; break; }
                var version = await MutationIoAsync(intent.Via!, () => JournalRecovery.ReviewRename(intent, Services.Jobs.FileOperations, Services.Providers.For(Location.FileSystem(intent.Via!)), scope.Check, renameBytes, renameItems), scope.Check);
                if (version is null) unreviewedRenames.Add(intent.Via!);
                else { renames.Add(version); renameBytes -= version.Bytes; renameItems -= version.Items.Count; }
            }
            var partial = review.Staged.Select(s => s.Path).Concat(review.Copies.Incomplete.Select(c => c.Path)).ToList();
            bool unresolvedRemoteUpload = InterruptedRecoveryReport.HasUnreviewedRemoteUpload(job);
            var message = (partial.Count > 0 ? "These reviewed partial files will be deleted:\n" + InterruptedJobText.Bullets(partial) : "No completely reviewed partial files remain.")
                + InterruptedJobText.CopyNotes(review.Copies) + StagedReviewNotes(review);
            if (unresolvedRemoteUpload)
                message += "\n\n" + InterruptedRecoveryReport.RemoteUploadReviewWarning(job);
            if (renames.Count > 0) message += "\n\nThese reviewed interrupted renames will be finished if their contents and paths remain unchanged (the original name is used when the new name is taken):\n" + InterruptedJobText.Bullets(renames.Select(r => r.Intent.Path + " → " + r.Intent.Target).ToList());
            if (unreviewedRenames.Count > 0) message += "\n\nThese temporary items could not be completely reviewed and will be kept with the journal:\n" + InterruptedJobText.Bullets(unreviewedRenames);
            if (renameLimited) message += "\n\nThe rename review reached its limit. Remaining temporary names are kept; inspect them separately.";
            var intents = job.OpenIntents.Where(i => i.Operation != JobJournal.RenameViaOp).Take(10).Select(i => $"• {i.Operation}: {i.Path}{(i.Target is null ? "" : " → " + i.Target)}");
            message += "\n\nSteps that were in progress (inspect these items yourself):\n" + string.Join("\n", intents);
            string action = (renames.Count > 0, partial.Count > 0) switch { (true, true) => "Finish renaming and delete partial files", (true, false) => "Finish renaming", (false, true) => "Delete partial files", _ => unresolvedRemoteUpload ? "Keep remote upload for review" : "OK" };
            var highlights = new List<string>
            {
                $"Action: {action}",
                $"Reviewed partial files to delete: {partial.Count:N0}",
                $"Reviewed interrupted renames to finish: {renames.Count:N0}",
                $"Temporary names kept for manual review: {unreviewedRenames.Count:N0}" +
                    (renameLimited ? " (review limit reached)" : "")
            };
            if (unresolvedRemoteUpload)
            {
                highlights.Add("Remote upload: server files are not inspected or deleted; recovery tracking stays open");
                if (InterruptedRecoveryReport.ExpectedRemoteStagePrefix(job) is { } prefix)
                    highlights.Add("Candidate temporary names begin: " + prefix);
                var targets = InterruptedRecoveryReport.RemoteUploadTargets(job);
                foreach (var target in targets.Take(3)) highlights.Add("Target: " + target);
                if (targets.Count == 4) highlights.Add("More remote targets: see the full review and retained report");
            }
            scope.SetPhase(RecoveryActionPhase.AwaitingApproval, "Awaiting approval of the reviewed cleanup…");
            bool approved = Dialogs is OverlayDialogService overlay
                ? await overlay.ConfirmWithCancellationAsync("Interrupted operation", message, action,
                    partial.Count > 0 || renames.Count > 0 || unresolvedRemoteUpload, scope.StopSource.Token, highlights)
                : await Dialogs.ConfirmAsync("Interrupted operation", message, action, danger: partial.Count > 0 || renames.Count > 0 || unresolvedRemoteUpload);
            scope.Check();
            if (!approved) return false;
            scope.SetPhase(RecoveryActionPhase.Applying, "Applying reviewed cleanup; completed changes are kept…");
            scope.Check(); var result = await DeleteInterruptedPartialsAsync(review, scope);
            int renamed = 0; var notRenamed = new List<string>(); var renameEffects = new List<string>();
            bool unresolvedRenames = renameLimited || unreviewedRenames.Count > 0;
            foreach (var rename in renames)
            {
                var outcome = await MutationIoAsync(rename.Intent.Via!, () =>
                {
                    var result = JournalRecovery.FinishReviewedRename(rename, Services.Jobs.FileOperations,
                        Services.Providers.For(Location.FileSystem(rename.Intent.Via!)), scope.Check);
                    if (result.Resolved)
                        scope.RenameEffects.Enqueue(result.Finished > 0
                            ? $"Moved temporary {rename.Intent.Via} to {rename.Intent.Target}"
                            : $"Restored temporary {rename.Intent.Via} to original {rename.Intent.Path}");
                    return result;
                }, scope.Check);
                renamed += outcome.Finished; notRenamed.AddRange(outcome.Report); unresolvedRenames |= !outcome.Resolved;
                if (outcome.Resolved)
                    renameEffects.Add(outcome.Finished > 0
                        ? $"Moved temporary {rename.Intent.Via} to {rename.Intent.Target}"
                        : $"Restored temporary {rename.Intent.Via} to original {rename.Intent.Path}");
                else renameEffects.AddRange(outcome.Report);
            }
            renameEffects.AddRange(unreviewedRenames.Select(path => "Unreviewed temporary name kept: " + path));
            if (result.Kept.Count > 0) await Dialogs.AlertAsync("Interrupted operation", "These files were kept because they changed or could not be completely reviewed:\n" + InterruptedJobText.Bullets(result.Kept));
            scope.Check();
            if (notRenamed.Count > 0) await Dialogs.AlertAsync("Interrupted rename", string.Join("\n", notRenamed.Take(20)));
            scope.Check();
            var recordedSources = await MutationIoAsync(job.JournalPath, () => JournalRecovery.LoadSources(job), scope.Check);
            string report = await Task.Run(() => InterruptedRecoveryReport.Format(job,
                unresolvedRenames ? "Cleanup attempted; unresolved rename remains and recovery tracking stays open"
                    : unresolvedRemoteUpload ? "Local cleanup reviewed; remote upload still requires inspection and recovery tracking stays open"
                    : "Reviewed cleanup; outcome details below",
                review.Staged.Select(s => s.Path).ToArray(), review.Unreviewed, review.Copies, review.Limited || renameLimited,
                result.DeletedPaths, result.Kept, renameEffects, recordedSources));
            var record = new RecoveryReviewRecord(job.JournalPath, job.Title, job.Kind, DateTime.UtcNow,
                unresolvedRenames || unresolvedRemoteUpload ? "Cleanup attempted; recovery remains open" : "Reviewed cleanup", report, ClosureConfirmed: false);
            await MutationIoAsync(job.JournalPath, () => { RecoveryReviewStore.Save(Services.Paths.JournalDirectory, record); return true; }, scope.Check);
            Operations.AddReviewedRecovery(record);
            if (unresolvedRenames || unresolvedRemoteUpload)
            {
                Notify(unresolvedRenames
                    ? "Temporary rename items remain unresolved. The old journal is kept for review; the cleanup report is retained."
                    : "A remote upload still needs inspection. The local cleanup report is retained and recovery tracking stays open.", true);
                if (renames.Count > 0) RefreshAll();
                return false;
            }
            scope.Check();
            scope.SetPhase(RecoveryActionPhase.Submitted, "Finalizing recovery tracking; completed cleanup is kept.");
            bool closed = await MutationIoAsync(job.JournalPath, () => JournalRecovery.TryClose(job, $"Reviewed; {result.Deleted} partial file(s) deleted, {renamed} rename(s) finished."), CheckSessionAction);
            if (closed)
            {
                record = record with { ClosureConfirmed = true };
                try
                {
                    await MutationIoAsync(job.JournalPath, () => { RecoveryReviewStore.Save(Services.Paths.JournalDirectory, record); return true; }, CheckSessionAction);
                    Operations.AddReviewedRecovery(record);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { Notify("Cleanup closed tracking, but the saved report still says closure was not confirmed: " + Formatters.SafeName(ex.Message), true); }
            }
            Notify(closed ? $"Removed {Formatters.Plural(result.Deleted, "partial file", "partial files")}; finished {Formatters.Plural(renamed, "rename", "renames")}." : "The old journal could not be closed. It is kept for review.", !closed);
            if (renames.Count > 0) RefreshAll();
            return closed;
        }
        catch (OperationCanceledException)
        {
            await PreserveStoppedRecoveryAsync(job, scope, reviewed);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        { if (EditSessionsActive) Notify("Cleanup could not finish: " + Formatters.SafeName(ex.Message) + " Some reviewed steps may already have run; the journal remains available if it was not closed.", true); return false; }
        finally { EndRecoveryAction(scope); }
    }

    internal async Task<ReportWindow?> ShowInterruptedReportAsync(InterruptedJob job)
    {
        if (!TryBeginRecoveryAction(job, out var scope)) return null;
        try
        {
            if (!await MutationIoAsync(job.JournalPath, () => JournalRecovery.IsInterrupted(job), scope.Check))
            { Notify("This recovery journal is no longer available for review.", true); return null; }
            var review = await ReviewInterruptedAsync(job, scope);
            var recordedSources = await MutationIoAsync(job.JournalPath, () => JournalRecovery.LoadSources(job), scope.Check);
            string report = await Task.Run(() => InterruptedRecoveryReport.Format(job, "Inspection only; no files or recovery tracking changed",
                review.Staged.Select(s => s.Path).ToArray(), review.Unreviewed, review.Copies, review.Limited,
                allSources: recordedSources));
            scope.Check();
            var window = new ReportWindow("Interrupted recovery report", Formatters.SafeName(job.Title), _ => Task.FromResult(report));
            window.Show();
            return window;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        { if (EditSessionsActive) Notify("The recovery report could not be opened: " + Formatters.SafeName(ex.Message), true); return null; }
        finally { EndRecoveryAction(scope); }
    }

    internal async Task<bool> MarkInterruptedReviewedAsync(InterruptedJob job)
    {
        if (Dialogs.IsOpen)
        {
            Notify("Finish or cancel the open dialog before marking this recovery reviewed.");
            return false;
        }
        if (!TryBeginRecoveryAction(job, out var scope)) return false;
        try
        {
            if (!await MutationIoAsync(job.JournalPath, () => JournalRecovery.IsInterrupted(job), scope.Check)) return false;
            var review = await ReviewInterruptedAsync(job, scope);
            var recordedSources = await MutationIoAsync(job.JournalPath, () => JournalRecovery.LoadSources(job), scope.Check);
            string report = await Task.Run(() => InterruptedRecoveryReport.Format(job, "Marked reviewed; no files changed by this action",
                review.Staged.Select(s => s.Path).ToArray(), review.Unreviewed, review.Copies, review.Limited,
                allSources: recordedSources));
            int renames = job.OpenIntents.Count(i => i.Operation == JobJournal.RenameViaOp);
            bool unresolvedRemoteUpload = InterruptedRecoveryReport.HasUnreviewedRemoteUpload(job);
            string message = $"Stop offering automatic recovery for {Formatters.SafeName(job.Title)} on future starts? " +
                "This does not delete or repair files. The journal will be closed and the recovery actions will no longer be offered. " +
                "A bounded, copyable report of the known paths and review limits will remain in Operations under Reviewed recovery reports. " +
                (renames > 0 ? $"\n\n{renames:N0} interrupted rename intent{(renames == 1 ? " has" : "s have")} a temporary name. Its path and possible target remain in the report; inspect or finish it before stopping reminders. " : "") +
                (unresolvedRemoteUpload ? "\n\n" + InterruptedRecoveryReport.RemoteUploadReviewWarning(job) : "") +
                (review.Limited || review.Copies.LimitReached || job.FillDirectoriesCut
                    ? "\n\nThe review reached a limit. Other affected files may exist; their count and paths are unknown. " : "") +
                "\n\nUse Review report first if you need to copy or save the full retained detail before this decision.";
            scope.SetPhase(RecoveryActionPhase.AwaitingApproval, "Awaiting approval to stop recovery reminders…");
            var highlights = new List<string>
            {
                "Action: stop offering recovery for this interrupted operation",
                "File changes: none; existing files are not repaired or deleted",
                "A bounded report remains in Operations for this session",
            };
            if (renames > 0) highlights.Add($"Interrupted rename intents to inspect: {renames:N0}");
            if (unresolvedRemoteUpload)
            {
                highlights.Add("Remote upload: inspect server files before stopping recovery reminders");
                if (InterruptedRecoveryReport.ExpectedRemoteStagePrefix(job) is { } prefix)
                    highlights.Add("Candidate temporary names begin: " + prefix);
                var targets = InterruptedRecoveryReport.RemoteUploadTargets(job);
                foreach (var target in targets.Take(3)) highlights.Add("Target: " + target);
                if (targets.Count == 4) highlights.Add("More remote targets: see the full review and retained report");
            }
            if (review.Limited || review.Copies.LimitReached || job.FillDirectoriesCut)
                highlights.Add("Review limit reached; other affected files may be unknown");
            bool approved = Dialogs is OverlayDialogService overlay
                ? await overlay.ConfirmWithCancellationAsync("Stop interrupted recovery reminders?", message,
                    "Mark reviewed and stop reminders", renames > 0 || job.OpenIntents.Count > 0 || review.Limited || review.Copies.LimitReached,
                    scope.StopSource.Token, highlights, "Key points", "Full details")
                : await Dialogs.ConfirmAsync("Stop interrupted recovery reminders?", message,
                    "Mark reviewed and stop reminders", danger: renames > 0 || job.OpenIntents.Count > 0 || review.Limited || review.Copies.LimitReached);
            scope.Check();
            if (!approved) return false;
            scope.SetPhase(RecoveryActionPhase.Applying, "Saving the reviewed report and closing recovery tracking…");
            if (!await MutationIoAsync(job.JournalPath, () => JournalRecovery.IsInterrupted(job), scope.Check)) return false;
            var record = new RecoveryReviewRecord(job.JournalPath, job.Title, job.Kind, DateTime.UtcNow,
                "Marked reviewed without file changes", report, ClosureConfirmed: false);
            await MutationIoAsync(job.JournalPath, () => { RecoveryReviewStore.Save(Services.Paths.JournalDirectory, record); return true; }, scope.Check);
            Operations.AddReviewedRecovery(record);
            scope.Check();
            scope.SetPhase(RecoveryActionPhase.Submitted, "Finalizing the reviewed recovery journal…");
            bool closed = await MutationIoAsync(job.JournalPath,
                () => JournalRecovery.TryClose(job, "Marked reviewed by the user; no files changed. Diagnostic report retained."), CheckSessionAction);
            if (!closed)
            {
                Notify("The report was saved, but recovery tracking could not be closed. The interrupted notice remains.", true);
                return false;
            }
            record = record with { ClosureConfirmed = true };
            try
            {
                await MutationIoAsync(job.JournalPath, () => { RecoveryReviewStore.Save(Services.Paths.JournalDirectory, record); return true; }, CheckSessionAction);
                Operations.AddReviewedRecovery(record);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { Notify("Recovery tracking closed, but the saved report still says closure was not confirmed: " + Formatters.SafeName(ex.Message), true); }
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        { if (EditSessionsActive) Notify("Recovery tracking was kept open: " + Formatters.SafeName(ex.Message), true); return false; }
        finally { EndRecoveryAction(scope); }
    }
}
