using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Tools;

namespace FileCat.App.ViewModels;

/// <summary>
/// File operations (plan §4.3–4.4, §9): every mutation goes through the job engine (AI-02). The destination
/// shown in the dialog is captured when the job is confirmed (AI-07); after an operation fully processed
/// items are unmarked and failed or skipped items stay marked for a retry (D-36).
/// </summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<Job, (TabViewModel Tab, Location Location)> _jobOrigins = new();
    private readonly Queue<PendingDecision> _decisions = new();
    private bool _showingDecision;
    private OperationCenterViewModel? _operations;

    public OperationCenterViewModel Operations => _operations ??= CreateOperations();

    private OperationCenterViewModel CreateOperations()
    {
        var oc = new OperationCenterViewModel(Services.Jobs);
        Services.Jobs.DecisionRequested += d => Services.Ui.Post(() => EnqueueDecision(d));
        Services.Jobs.JobFinished += job => Services.Ui.Post(() => OnJobFinished(job));
        return oc;
    }

    private partial async Task<bool> ExecuteOperationCommandAsync(string id)
    {
        switch (id)
        {
            case CommandIds.Copy: await TransferAsync(JobKind.Copy); return true;
            case CommandIds.Move: await TransferAsync(JobKind.Move); return true;
            case CommandIds.Duplicate: await DuplicateAsync(); return true;
            case CommandIds.Rename: await RenameAsync(); return true;
            case CommandIds.MakeDirectory: await MakeDirectoryAsync(); return true;
            case CommandIds.Delete: await DeleteAsync(permanent: false); return true;
            case CommandIds.DeletePermanent: await DeleteAsync(permanent: true); return true;
            case CommandIds.EditNew: await EditNewAsync(); return true;
            case CommandIds.Edit: EditFocused(); return true;
            case CommandIds.View: ViewFocused(hex: false); return true;
            case CommandIds.ViewAlternate: ViewFocused(hex: true); return true;
            case CommandIds.Undo: await UndoLastAsync(); return true;
            case CommandIds.Operations: Operations.IsOpen = !Operations.IsOpen; return true;
            case CommandIds.CopyToClipboard: await PutFilesOnClipboardAsync(cut: false); return true;
            case CommandIds.CutToClipboard: await PutFilesOnClipboardAsync(cut: true); return true;
            case CommandIds.PasteFromClipboard: await PasteAsync(); return true;
            case CommandIds.Checksum: await ChecksumAsync(); return true;
            case CommandIds.CopyUncPaths: await CopyUncPathsAsync(); return true;
            case CommandIds.ConnectNetworkDrive: ConnectDrive(connect: true); return true;
            case CommandIds.DisconnectNetworkDrive: ConnectDrive(connect: false); return true;
            case CommandIds.RemoveFromSet: RemoveFromResultSet(); return true;
        }
        return false;
    }

    public bool CanCloseImmediately() => !Services.Jobs.HasActiveWork;

    /// <summary>Closing presents active work: finish, cancel safely, or keep the app open (plan §9.3).</summary>
    public async Task<bool> ConfirmExitWithActiveWorkAsync()
    {
        var active = Services.Jobs.Jobs.Where(j => !j.State.IsFinished()).ToList();
        if (active.Count == 0) return true;
        var list = ExactList(active.Select(j => $"{j.Title} — {j.State.Describe()}"));
        var r = await Dialogs.ShowCustomAsync("Operations are still running",
            new Avalonia.Controls.TextBlock { Text = $"Closing FileCat stops these operations at their next safe step. Completed steps stay completed; nothing is rolled back.\n\n{list}", TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 620 },
            [new DialogButton("Keep FileCat open", "keep", IsDefault: true, IsCancel: true), new DialogButton("Stop them and exit", "stop", IsDanger: true)]);
        if (r as string != "stop") return false;
        foreach (var j in active) j.Cancel();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Services.Jobs.HasActiveWork && DateTime.UtcNow < deadline) await Task.Delay(50);
        return true;
    }

    // ---- Selection helpers ----------------------------------------------------------------------------

    private (TabViewModel Tab, IReadOnlyList<ItemRef> Items, int HiddenMarked, string Summary)? SourceSelection()
    {
        var tab = ActiveTab;
        if (tab?.Location is null) return null;
        var stats = tab.Listing.GetMarkStats();
        var items = tab.Listing.GetSelection(includeHiddenMarks: true);
        if (items.Count == 0)
        {
            Notify("Nothing is focused or marked.");
            return null;
        }
        // A captured (huge) selection is exactly the marks: summarize from the cached statistics, not by enumerating.
        var summary = items is SelectionSnapshot
            ? SizeSummary(stats.Files, stats.Directories, stats.Bytes, stats.SizesIncomplete)
            : SizeSummary(items);
        return (tab, items, stats.HiddenByFilter, summary);
    }

    private static string SizeSummary(IReadOnlyList<ItemRef> items)
    {
        long bytes = items.Where(i => !i.IsContainer && i.Size > 0).Sum(i => i.Size);
        int dirs = items.Count(i => i.IsContainer);
        return SizeSummary(items.Count - dirs, dirs, bytes, dirs > 0);
    }

    private static string SizeSummary(int files, int dirs, long bytes, bool folderContentsExcluded)
    {
        var parts = new List<string>();
        if (files > 0) parts.Add(Formatters.Plural(files, "file", "files"));
        if (dirs > 0) parts.Add(Formatters.Plural(dirs, "folder", "folders"));
        return string.Join(" and ", parts) + (bytes > 0 ? $" ({Formatters.SizeWithUnit(bytes)}{(folderContentsExcluded ? " + folder contents" : "")})" : "");
    }

    // ---- Copy / move ---------------------------------------------------------------------------------

    private async Task TransferAsync(JobKind kind, IReadOnlyList<ItemRef>? explicitItems = null, Location? explicitDestination = null)
    {
        var sel = SourceSelection();
        if (sel is null && explicitItems is null) return;
        var tab = sel?.Tab ?? ActiveTab!;
        var items = explicitItems ?? sel!.Value.Items;
        IReadOnlyList<ItemRef>? finalItems = null, submitted = null;
        try
        {
            var source = tab.Location!;
            var sourceCaps = Services.Providers.For(source).GetCapabilities(source);
            if (kind == JobKind.Move && (sourceCaps & LocationCapabilities.MoveSource) == 0)
            {
                Notify(Services.Providers.For(source).ExplainUnavailable(source, LocationCapabilities.MoveSource) + " Use F5 to copy instead.", true);
                return;
            }
            var target = Workspace.ActiveTarget;
            var destination = explicitDestination ?? target?.ActiveTab?.Location;
            string destText = destination is { IsFileSystem: true } d ? AppendSeparator(d.Path) : destination is null ? string.Empty : Services.Providers.Display(destination);
            if (items.Count == 1 && explicitDestination is null && destination is null) destText = string.Empty;
            var summary = explicitItems is null ? sel!.Value.Summary : SizeSummary(items);
            var request = await OperationDialogs.ShowTransferAsync(this, new TransferDialogInput(
                kind, items, summary, destText, target is null ? null : $"panel {target.Number}",
                sel?.HiddenMarked ?? 0, source.Scheme == Schemes.ResultSet));
            if (request is null) return;
            if (!ResolveDestination(request.Destination, items, out var destLocation, out var newName, out var error))
            {
                Notify(error!, true);
                return;
            }
            // Hidden marks are excluded only when there are some and the user did not include them.
            finalItems = request.IncludeHidden || explicitItems is not null || (sel?.HiddenMarked ?? 0) == 0 ? items : tab.Listing.GetSelection(includeHiddenMarks: false);
            if (kind == JobKind.Move && newName is null && destLocation is { } dl && ItemSources.Parents(finalItems)!.Contains(dl))
            {
                Notify("The items are already in that folder.");
                return;
            }
            AppServices.RememberText(Services.History.CopyDestinations, request.Destination);
            // Result-set items carry their relative folders; the executor recreates them unless flattening.
            var job = Services.Jobs.Submit(new JobRequest
            {
                Kind = source.IsFileSystem || kind == JobKind.Move ? kind : JobKind.Copy,
                Sources = finalItems,
                Destination = destLocation,
                NewName = newName,
                Options = request.Options,
                Mode = request.Queue ? QueueMode.Queue : QueueMode.Start,
            });
            submitted = finalItems;
            Track(job, tab);
            tab.Listing.RememberOperation(finalItems);
        }
        finally
        {
            ReleaseUnsubmitted(submitted, sel?.Items, finalItems);
        }
    }

    /// <summary>Releases captured selections that did not become a job's sources (the job's are released when it finishes).</summary>
    private static void ReleaseUnsubmitted(IReadOnlyList<ItemRef>? submitted, params IReadOnlyList<ItemRef>?[] captured)
    {
        foreach (var c in captured)
        {
            if (c is not null && !ReferenceEquals(c, submitted)) ItemSources.Release(c);
        }
    }

    private static string AppendSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    /// <summary>
    /// Destination text → location + optional new name: an existing folder receives the items; a path ending
    /// in a separator (or several items) names a folder to create; one item to a non-existing path is renamed.
    /// </summary>
    private bool ResolveDestination(string text, IReadOnlyList<ItemRef> items, out Location? destination, out string? newName, out string? error)
    {
        destination = null;
        newName = null;
        error = null;
        var cur = ActiveTab?.Location;
        var t = text.Trim();
        if (t.Length == 0)
        {
            error = "Enter a destination.";
            return false;
        }
        if (!Services.Providers.TryParse(t, cur, out var loc) || loc is null)
        {
            error = $"\"{t}\" is not a location FileCat can copy to.";
            return false;
        }
        if (!loc.IsFileSystem)
        {
            var caps = Services.Providers.For(loc).GetCapabilities(loc);
            if ((caps & LocationCapabilities.TransferTarget) == 0)
            {
                error = Services.Providers.For(loc).ExplainUnavailable(loc, LocationCapabilities.TransferTarget);
                return false;
            }
            destination = loc;
            return true;
        }
        var path = loc.Path;
        bool endsWithSep = t.EndsWith('\\') || t.EndsWith('/');
        if (Directory.Exists(path) || endsWithSep || items.Count > 1)
        {
            destination = loc;
            return true;
        }
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent))
        {
            destination = loc;
            return true;
        }
        var name = Path.GetFileName(path);
        if (PathUtil.ValidateNewName(name) is { } bad)
        {
            error = bad;
            return false;
        }
        destination = Location.FileSystem(parent);
        newName = name;
        return true;
    }

    private void Track(Job job, TabViewModel tab)
    {
        if (tab.Location is { } l) _jobOrigins[job] = (tab, l);
    }

    private async Task DuplicateAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is not { IsFileSystem: true } loc || !tab.Listing.TryGetFocused(out var f) || f.Kind == EntryKind.Parent) return;
        var suggestion = PathUtil.MakeUniqueName(f.Name, n => File.Exists(Path.Combine(loc.Path, n)) || Directory.Exists(Path.Combine(loc.Path, n)), f.IsContainer);
        var r = await Dialogs.PromptAsync(new PromptOptions("Duplicate here", $"Copy \"{f.Name}\" in this folder as:")
        {
            Text = suggestion,
            SelectStem = !f.IsContainer,
            Validate = n => ValidateSiblingName(loc.Path, n, f.Name),
            ConfirmText = "Duplicate",
        });
        if (r is null) return;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        var job = Services.Jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [item], Destination = loc, NewName = r.Text });
        Track(job, tab);
        _focusAfter[job] = r.Text;
    }

    private readonly Dictionary<Job, string> _focusAfter = new();

    private static string? ValidateSiblingName(string dir, string name, string? current = null)
    {
        if (PathUtil.ValidateNewName(name) is { } bad) return bad;
        if (current is not null && string.Equals(name, current, StringComparison.Ordinal)) return "The name is unchanged.";
        bool caseOnly = current is not null && string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(Path.Combine(dir, name)) || Directory.Exists(Path.Combine(dir, name)))) return "An item with this name already exists here.";
        return null;
    }

    private async Task RenameAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is not { } loc || !tab.Listing.TryGetFocused(out var f) || f.Kind == EntryKind.Parent) return;
        var caps = Services.Providers.For(loc).GetCapabilities(loc);
        if ((caps & LocationCapabilities.Rename) == 0 || !loc.IsFileSystem && loc.Scheme != Schemes.ResultSet)
        {
            Notify(Services.Providers.For(loc).ExplainUnavailable(loc, LocationCapabilities.Rename), true);
            return;
        }
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        var dir = Path.GetDirectoryName(item.FileSystemPath!)!;
        var newName = await View.RenameInlineAsync(new PromptOptions("Rename", $"New name for \"{Formatters.SafeName(f.Name)}\":")
        {
            Text = f.Name,
            SelectStem = !f.IsContainer,
            Validate = n => ValidateSiblingName(dir, n, f.Name),
            ConfirmText = "Rename",
        });
        if (newName is null) return;
        var job = Services.Jobs.Submit(new JobRequest { Kind = JobKind.Rename, Sources = [item], NewName = newName });
        Track(job, tab);
        _focusAfter[job] = newName;
    }

    private async Task MakeDirectoryAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is not { IsFileSystem: true } loc) return;
        var r = await Dialogs.PromptAsync(new PromptOptions("Create folder", "Folder name (use \\ for nested folders):")
        {
            Validate = n =>
            {
                var parts = n.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "The name cannot be empty.";
                foreach (var p in parts) if (PathUtil.ValidateNewName(p) is { } bad) return bad;
                return Directory.Exists(Path.Combine(loc.Path, n)) || File.Exists(Path.Combine(loc.Path, n)) ? "An item with this name already exists here." : null;
            },
            ConfirmText = "Create",
        });
        if (r is null) return;
        var job = Services.Jobs.Submit(new JobRequest { Kind = JobKind.CreateDirectory, Destination = loc, NewName = r.Text.Trim('\\', '/') });
        Track(job, tab);
        _focusAfter[job] = r.Text.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private async Task EditNewAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is not { IsFileSystem: true } loc) return;
        var r = await Dialogs.PromptAsync(new PromptOptions("Edit new file", "File name (an existing file is opened instead):")
        {
            Validate = n => PathUtil.ValidateNewName(n) ?? (Directory.Exists(Path.Combine(loc.Path, n)) ? "A folder with this name exists." : null),
            ConfirmText = "Create and edit",
        });
        if (r is null) return;
        var full = Path.Combine(loc.Path, r.Text);
        if (File.Exists(full))
        {
            LaunchEditor(full);
            return;
        }
        var job = Services.Jobs.Submit(new JobRequest { Kind = JobKind.CreateFile, Destination = loc, NewName = r.Text });
        Track(job, tab);
        _focusAfter[job] = r.Text;
        _editAfter[job] = full;
    }

    private readonly Dictionary<Job, string> _editAfter = new();

    // ---- Delete -------------------------------------------------------------------------------------

    private async Task DeleteAsync(bool permanent)
    {
        var sel = SourceSelection();
        if (sel is null) return;
        var (tab, items, hidden, summary) = sel.Value;
        IReadOnlyList<ItemRef>? finalItems = null, submitted = null;
        try
        {
            var loc = tab.Location!;
            var provider = Services.Providers.For(loc);
            var caps = provider.GetCapabilities(loc);
            if (loc.Scheme != Schemes.ResultSet && (caps & LocationCapabilities.Delete) == 0)
            {
                Notify(provider.ExplainUnavailable(loc, LocationCapabilities.Delete), true);
                return;
            }
            if (ItemSources.Parents(items)!.Any(p => !p.IsFileSystem))
            {
                Notify("Deleting is supported for file-system items here.", true);
                return;
            }
            // Pre-classify items the Recycle Bin cannot take and ask before starting (plan §9.2). Only a bounded
            // set of examples is kept; the count is exact.
            var fs = Services.Platform.FileOperations;
            var examples = new List<(string Name, RecycleClassification Why)>();
            int unrecyclable = 0;
            bool confirmedPermanent = false;
            if (!permanent)
            {
                if (items.Count > 10_000) Notify($"Checking {items.Count:N0} items for the Recycle Bin…");
                unrecyclable = await Task.Run(() =>
                {
                    int count = 0;
                    foreach (var i in items)
                    {
                        var why = fs.ClassifyRecycle(i.FileSystemPath!, i.IsContainer ? -1 : i.Size);
                        if (why is RecycleClassification.Recyclable or RecycleClassification.Unknown) continue;
                        count++;
                        if (examples.Count < 100) examples.Add((i.Name, why));
                    }
                    return count;
                });
                if (unrecyclable == items.Count)
                {
                    // No bin at all here: offer an explicit permanent deletion instead of a silent fallback.
                    var ok = await Dialogs.ConfirmAsync("Delete permanently?",
                        $"{RecycleText.Explain(examples[0].Why)} These items cannot be recycled.\n\nDelete {summary} permanently?\n{ExactList(items.Take(8).Select(i => i.Name), total: items.Count)}",
                        "Delete permanently", danger: true);
                    if (!ok) return;
                    permanent = true;
                    confirmedPermanent = true;
                }
            }
            var input = new DeleteDialogInput(items, summary, permanent, hidden, examples.Select(u => (u.Name, RecycleText.Explain(u.Why))).ToList(),
                loc.Scheme == Schemes.ResultSet, permanent ? 0 : unrecyclable);
            if (permanent) input = input with { Unrecyclable = [] };
            // Already confirmed as a permanent deletion, or no confirmation wanted: ask again only about hidden marks.
            if (hidden == 0 && (confirmedPermanent || !permanent && !Services.Settings.ConfirmRecycle && unrecyclable == 0))
                input = input with { SkipDialog = true };
            var choice = input.SkipDialog ? new DeleteDialogResult(false, false) : await OperationDialogs.ShowDeleteAsync(this, input);
            if (choice is null) return;
            finalItems = choice.IncludeHidden || hidden == 0 ? items : tab.Listing.GetSelection(includeHiddenMarks: false);
            var job = Services.Jobs.Submit(new JobRequest
            {
                Kind = permanent ? JobKind.Delete : JobKind.Recycle,
                Sources = finalItems,
                Options = new TransferOptions { PermanentlyDeleteUnrecyclable = choice.DeleteUnrecyclablePermanently },
            });
            submitted = finalItems;
            Track(job, tab);
            tab.Listing.RememberOperation(finalItems);
        }
        finally
        {
            ReleaseUnsubmitted(submitted, items, finalItems);
        }
    }

    private void RemoveFromResultSet()
    {
        var tab = ActiveTab;
        if (tab?.Location is not { Scheme: Schemes.ResultSet } || Services.Providers.For(tab.Location) is not Core.Search.ResultSetProvider rs) return;
        var sel = tab.Listing.GetSelection();
        int n = rs.Remove(tab.Location, sel);
        tab.Refresh();
        Notify($"Removed {Formatters.Plural(n, "item", "items")} from the result set. Nothing was deleted.");
    }

    // ---- Job completion ---------------------------------------------------------------------------------

    private void OnJobFinished(Job job)
    {
        Operations.UpdateSummary();
        if (_jobOrigins.Remove(job, out var origin))
        {
            var tab = origin.Tab;
            if (tab.Location == origin.Location && job.Kind is not (JobKind.CreateDirectory or JobKind.CreateFile))
            {
                // Unmark what the job finished; failed and skipped roots stay marked for a retry.
                try { tab.Listing.MarkItems(job.Request.Sources, job.CompletedRootIndices, false); }
                catch (ObjectDisposedException) { }
            }
            if (_focusAfter.Remove(job, out var focus) && job.State is JobState.Completed or JobState.CompletedWithIssues && tab.Location == origin.Location)
                tab.Listing.Refresh();
            if (focus is not null) FocusWhenPresent(tab, focus);
        }
        RefreshAffected(job);
        // The job captured its sources; nothing reads them after this point.
        ItemSources.Release(job.Request.Sources);
        if (_editAfter.Remove(job, out var edit) && File.Exists(edit)) LaunchEditor(edit);
        switch (job.State)
        {
            case JobState.Completed:
                if (job.Kind is JobKind.Copy or JobKind.Move or JobKind.Recycle or JobKind.Delete or JobKind.Extract)
                    Notify($"{job.Title}: done ({job.Summary}).");
                break;
            case JobState.CompletedWithIssues:
            case JobState.Failed:
                var first = job.Issues.FirstOrDefault(i => i.Severity >= IssueSeverity.Warning);
                Notify($"{job.Title}: {job.State.Describe().ToLowerInvariant()} — {first?.Message ?? job.Summary}  (Ctrl+J shows details)", job.State == JobState.Failed || job.ItemsFailed > 0);
                break;
            case JobState.Canceled:
                Notify($"{job.Title}: canceled. {job.Summary}. Steps already completed were kept.");
                break;
        }
        UpdateKeepAwake();
    }

    private void FocusWhenPresent(TabViewModel tab, string name)
    {
        if (tab.Listing.FocusName(name)) return;
        void Handler(object? s, ListingChange c)
        {
            if (tab.Listing.FocusName(name) || tab.Listing.State != ListingState.Loading && !tab.Listing.IsRefreshing) tab.Listing.Changed -= Handler;
        }
        tab.Listing.Changed += Handler;
    }

    /// <summary>Refreshes tabs showing a source or destination folder of the job (watchers also do this).</summary>
    private void RefreshAffected(Job job)
    {
        var folders = new HashSet<Location>();
        // Watchers usually refresh these already; a job spread over very many folders refreshes every folder tab.
        IReadOnlyCollection<Location>? parents;
        try { parents = ItemSources.Parents(job.Request.Sources, 256); }
        catch (ObjectDisposedException) { parents = null; } // released after the job finished (undo later)
        if (parents is not null) folders.UnionWith(parents);
        if (job.Request.Destination is { } d) folders.Add(d);
        foreach (var p in Workspace.Panels)
        {
            foreach (var t in p.Tabs)
            {
                if (t.Location is { } l && (folders.Contains(l) || l.Scheme == Schemes.ResultSet || parents is null && l.IsFileSystem)) t.Refresh();
            }
        }
    }

    private void UpdateKeepAwake()
    {
        if (!Services.Settings.KeepAwakeDuringJobs) return;
        Services.Shell.SetKeepAwake(Services.Jobs.HasActiveWork);
    }

    // ---- Decisions ------------------------------------------------------------------------------------

    private void EnqueueDecision(PendingDecision decision)
    {
        _decisions.Enqueue(decision);
        if (!_showingDecision) _ = ProcessDecisionsAsync();
    }

    private async Task ProcessDecisionsAsync()
    {
        _showingDecision = true;
        try
        {
            while (_decisions.TryDequeue(out var d))
            {
                if (d.Task.IsCompleted) continue;
                var decision = await OperationDialogs.ShowDecisionAsync(this, d);
                d.Resolve(decision);
            }
        }
        finally
        {
            _showingDecision = false;
            View.FocusActivePanel();
        }
    }

    // ---- Undo ---------------------------------------------------------------------------------------------

    private async Task UndoLastAsync()
    {
        var job = Services.Jobs.Jobs.Where(j => j.CanUndo).OrderByDescending(j => j.FinishedUtc).FirstOrDefault();
        var latest = Services.Jobs.Jobs.Where(j => j.State.IsFinished()).OrderByDescending(j => j.FinishedUtc).FirstOrDefault();
        if (latest is { UndoTruncated: true } && !ReferenceEquals(latest, job))
        {
            // Never silently undo an older operation when the latest one was too large to record.
            Notify($"\"{latest.Title}\" changed more than {Job.UndoLimit:N0} items, so no undo steps were recorded for it. Recycled items can still be restored from the Recycle Bin.", true);
            return;
        }
        if (job is null)
        {
            Notify("There is no operation that can be undone safely. Permanent deletions, overwrites, and copies are never undone automatically.");
            return;
        }
        var steps = job.UndoSteps;
        var what = steps.Count == 1 ? Path.GetFileName(steps[0].To) : $"{steps.Count} items";
        if (!await Dialogs.ConfirmAsync("Undo", $"Undo \"{job.Title}\"?\n\nFileCat restores {what} only where the current state still matches what the operation left; anything changed since is kept and reported.", "Undo"))
            return;
        var fs = Services.Platform.FileOperations;
        var report = await Task.Run(() => UndoService.Undo(job, fs));
        job.MarkUndone();
        RefreshAffected(job);
        await Dialogs.AlertAsync("Undo result", string.Join("\n", report.Take(30)) + (report.Count > 30 ? $"\n… {report.Count - 30} more" : ""));
    }

    // ---- View and edit ----------------------------------------------------------------------------------

    private void ViewFocused(bool hex)
    {
        var tab = ActiveTab;
        if (tab?.Location is null || !tab.Listing.TryGetFocused(out var f) || f.Kind == EntryKind.Parent) return;
        if (f.IsContainer)
        {
            Notify("F3 views files. Folder sizes are computed with Space.");
            return;
        }
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        var provider = Services.Providers.For(item.Parent);
        if (provider is Core.Search.ResultSetProvider) provider = Services.Providers.For(item.Parent);
        try
        {
            if (item.FileSystemPath is null && Services.Providers.For(item.Parent).OpenContent(item) is null)
            {
                Notify("This item has no viewable content.", true);
                return;
            }
            ViewerLauncher.Open(Services, item, hex);
            if (item.Parent.IsFileSystem) Services.RecordFile(item.Parent, item.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify($"Cannot view \"{f.Name}\": {ErrorText.Describe(ex)}", true);
        }
    }

    private void EditFocused()
    {
        var tab = ActiveTab;
        if (tab?.Location is null || !tab.Listing.TryGetFocused(out var f) || f.Kind == EntryKind.Parent) return;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        if (f.IsContainer)
        {
            Notify("F4 edits files. Folders have no content to edit.");
            return;
        }
        if (item.FileSystemPath is not { } path)
        {
            Notify("Editing items inside archives or remote locations uses explicit edit sessions, which are not available for this location yet. Copy the item out with F5 to edit it.", true);
            return;
        }
        LaunchEditor(path);
        Services.RecordFile(item.Parent, item.Name);
    }

    private void LaunchEditor(string path)
    {
        var tool = Services.Settings.Editor ?? ToolLauncher.DetectEditor();
        try
        {
            var result = ToolLauncher.Launch(tool, new ToolContext([path], Path.GetDirectoryName(path)!, ActiveTarget()?.Path), Services.Paths.TempDirectory);
            if (result.Warning is not null) Notify(result.Warning);
        }
        catch (ToolLaunchException ex)
        {
            Notify(ex.Message, true);
        }
    }

    private Location? ActiveTarget() => Workspace.ActiveTarget?.ActiveTab?.Location;

    // ---- Clipboard ---------------------------------------------------------------------------------------

    private async Task PutFilesOnClipboardAsync(bool cut)
    {
        var sel = ActiveTab?.Listing.GetSelection();
        var top = View.TopLevel;
        if (sel is null || sel.Count == 0 || top?.Clipboard is null) return;
        var paths = sel.Select(s => s.FileSystemPath).Where(p => p is not null).Cast<string>().ToList();
        if (paths.Count == 0)
        {
            Notify("Only file-system items can be placed on the clipboard; extract other items first.", true);
            return;
        }
        var storageItems = new List<IStorageItem>();
        foreach (var p in paths)
        {
            IStorageItem? si = Directory.Exists(p)
                ? await top.StorageProvider.TryGetFolderFromPathAsync(p)
                : await top.StorageProvider.TryGetFileFromPathAsync(p);
            if (si is not null) storageItems.Add(si);
        }
        await top.Clipboard.SetFilesAsync(storageItems);
        _clipboardCut = cut ? paths.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        Notify($"{(cut ? "Cut" : "Copied")} {Formatters.Plural(storageItems.Count, "item", "items")} to the clipboard.");
    }

    private HashSet<string>? _clipboardCut;

    private async Task PasteAsync()
    {
        var top = View.TopLevel;
        var tab = ActiveTab;
        if (top?.Clipboard is null || tab?.Location is null) return;
        var files = await top.Clipboard.TryGetFilesAsync();
        if (files is { Length: > 0 })
        {
            var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
            if (paths.Count == 0) return;
            if (!tab.Location.IsFileSystem)
            {
                Notify("Items can be pasted only into a file-system folder.", true);
                return;
            }
            bool cut = _clipboardCut is not null && paths.All(_clipboardCut.Contains);
            var items = paths.Select(p => ItemRef.ForFileSystemPath(p, Directory.Exists(p) ? EntryKind.Directory : EntryKind.File)).ToList();
            // A paste captures its destination at invocation (plan §4.2).
            var job = Services.Jobs.Submit(new JobRequest { Kind = cut ? JobKind.Move : JobKind.Copy, Sources = items, Destination = tab.Location });
            Track(job, tab);
            if (cut) _clipboardCut = null;
            return;
        }
        var text = await top.Clipboard.TryGetTextAsync();
        if (!string.IsNullOrWhiteSpace(text) && text.Length < 4096 && !text.Contains('\n'))
        {
            // Pasting a path navigates (Salamander).
            if (Services.Providers.TryParse(text.Trim(), tab.Location, out var loc) && loc is not null)
            {
                string? focus = null;
                if (loc.IsFileSystem && File.Exists(loc.Path))
                {
                    focus = Path.GetFileName(loc.Path);
                    loc = Location.FileSystem(Path.GetDirectoryName(loc.Path)!);
                }
                tab.Navigate(loc, focus);
                return;
            }
        }
        Notify("The clipboard holds no files or location.");
    }

    private async Task CopyUncPathsAsync()
    {
        var sel = ActiveTab?.Listing.GetSelection();
        if (sel is null || sel.Count == 0 || View.Clipboard is null) return;
        var lines = sel.Select(s => s.FileSystemPath).Where(p => p is not null).Select(p => Services.Shell.ToUncPath(p!)).ToList();
        await View.Clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
        Notify($"Copied {Formatters.Plural(lines.Count, "UNC path", "UNC paths")} to the clipboard.");
    }

    /// <summary>Handles files dropped onto a panel: the drop target is the explicit destination.</summary>
    public async Task DropFilesAsync(PanelViewModel panel, IReadOnlyList<string> paths, bool move)
    {
        Workspace.Activate(panel);
        var tab = panel.ActiveTab;
        if (tab?.Location is not { IsFileSystem: true } dest || paths.Count == 0) return;
        var items = paths.Select(p => ItemRef.ForFileSystemPath(p, Directory.Exists(p) ? EntryKind.Directory : EntryKind.File)).ToList();
        if (items.All(i => i.Parent.Equals(dest)))
        {
            Notify("The dropped items are already in this folder.");
            return;
        }
        var request = await OperationDialogs.ShowTransferAsync(this, new TransferDialogInput(move ? JobKind.Move : JobKind.Copy, items, SizeSummary(items),
            AppendSeparator(dest.Path), $"panel {panel.Number} (drop target)", 0, false));
        if (request is null) return;
        if (!ResolveDestination(request.Destination, items, out var destLocation, out var newName, out var error))
        {
            Notify(error!, true);
            return;
        }
        var job = Services.Jobs.Submit(new JobRequest { Kind = move ? JobKind.Move : JobKind.Copy, Sources = items, Destination = destLocation, NewName = newName, Options = request.Options, Mode = request.Queue ? QueueMode.Queue : QueueMode.Start });
        Track(job, tab);
    }

    // ---- Checksums ---------------------------------------------------------------------------------------

    private async Task ChecksumAsync()
    {
        var sel = ActiveTab?.Listing.GetSelection();
        if (sel is null || sel.Count == 0) return;
        var files = sel.Where(s => !s.IsContainer && s.FileSystemPath is not null).Select(s => s.FileSystemPath!).ToList();
        if (files.Count == 0)
        {
            Notify("Checksums are computed for files; mark files or focus one.");
            return;
        }
        await OperationDialogs.ShowChecksumsAsync(this, files);
    }

    private void ConnectDrive(bool connect)
    {
        var error = connect ? Services.Shell.ConnectNetworkDrive(NativeOwner()) : Services.Shell.DisconnectNetworkDrive(NativeOwner());
        if (error is not null) Notify(error, true);
        foreach (var p in Workspace.Panels)
            foreach (var t in p.Tabs)
                if (t.Location?.Scheme == Schemes.Computer) t.Refresh();
    }

    internal nint NativeOwner() => View.TopLevel?.TryGetPlatformHandle()?.Handle ?? 0;

    internal async Task<bool> SignInToServerAsync(string server)
    {
        var error = await Task.Run(() => Services.Shell.SignIn(server, NativeOwner()));
        if (error is null) return true;
        Notify(error, true);
        return false;
    }

    internal void CopyTextToClipboard(string text) => _ = View.Clipboard?.SetTextAsync(text);
}
