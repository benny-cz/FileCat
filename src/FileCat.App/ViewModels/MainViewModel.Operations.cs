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
        var oc = new OperationCenterViewModel(Services.Jobs, Services.Providers.Display);
        Services.Jobs.DecisionRequested += d => Services.Ui.Post(() => EnqueueDecision(d));
        Services.Jobs.JobFinished += job => Services.Ui.Post(() => OnJobFinished(job));
        Services.Jobs.JobAdded += _ => Services.Ui.Post(UpdateJobActivity);
        Services.Io.HealthChanged += (device, health) => Services.Ui.Post(() => Notify(health == Core.Threading.DeviceHealth.NotResponding
            ? $"{device} is not responding. Other drives keep working; its operations continue when it responds again."
            : $"{device} responds again.", health == Core.Threading.DeviceHealth.NotResponding));
        return oc;
    }

    private partial async Task<bool> ExecuteOperationCommandAsync(string id)
    {
        if (Core.Search.ResultSetProvider.IsWorkingSetList(ActiveTab?.Location) && await WorkingSetListCommandAsync(id)) return true;
        switch (id)
        {
            case CommandIds.AddToWorkingSet: await AddToWorkingSetAsync(); return true;
            case CommandIds.WorkingSets: OpenWorkingSets(); return true;
            case CommandIds.Copy:
                if (TryGetFocusedRegistryItem(out _)) await CopyRegistryAsync();
                else await TransferAsync(JobKind.Copy);
                return true;
            case CommandIds.Move: await TransferAsync(JobKind.Move); return true;
            case CommandIds.Duplicate: await DuplicateAsync(); return true;
            case CommandIds.Rename:
                if (TryGetFocusedRegistryItem(out _)) await RenameRegistryAsync();
                else await RenameAsync();
                return true;
            case CommandIds.MakeDirectory: await MakeDirectoryAsync(); return true;
            case CommandIds.Delete: await DeleteAsync(permanent: false); return true;
            case CommandIds.DeletePermanent: await DeleteAsync(permanent: true); return true;
            case CommandIds.EditNew: await EditNewAsync(); return true;
            case CommandIds.Edit: await EditFocusedAsync(); return true;
            case CommandIds.View: ViewFocused(hex: false); return true;
            case CommandIds.ChangeJournal: OpenChangeJournal(); return true;
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
            case CommandIds.SftpConnect: await ConnectSftpAsync(); return true;
            case CommandIds.SftpDisconnect: DisconnectSftp(); return true;
            case CommandIds.RemoveFromSet: RemoveFromResultSet(); return true;
        }
        return false;
    }

    public bool CanCloseImmediately() => !Services.Jobs.HasActiveWork && UnsavedHexEditors().Count == 0;

    /// <summary>Closing presents unsaved edits and active work: finish, cancel safely, or keep the app open (plan §9.3).</summary>
    public async Task<bool> ConfirmExitWithActiveWorkAsync()
    {
        if (!await ConfirmCloseHexEditorsAsync()) return false;
        var active = Services.Jobs.Jobs.Where(j => !j.State.IsFinished()).ToList();
        if (active.Count == 0) return true;
        // A text block per paragraph and per operation: line breaks inside one wrapped text spin Avalonia's headless
        // layout (tests reach this when a test's job still waits for an answer).
        var content = new Avalonia.Controls.StackPanel { Spacing = 4, MaxWidth = 620 };
        content.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "Closing FileCat stops these operations at their next safe step. Completed steps stay completed; nothing is rolled back.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 6),
        });
        foreach (var line in ExactList(active.Select(j => $"{j.Title} — {j.State.Describe()}")).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            content.Children.Add(new Avalonia.Controls.TextBlock { Text = line, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var r = await Dialogs.ShowCustomAsync("Operations are still running", content,
            [new DialogButton("Keep FileCat open", "keep", IsDefault: true, IsCancel: true), new DialogButton("Exit when they finish", "later"), new DialogButton("Stop them and exit", "stop", IsDanger: true)]);
        if (r as string == "later")
        {
            _exitWhenIdle = true;
            Notify("FileCat closes when the running operations finish. Questions they ask are still shown.");
            return false;
        }
        if (r as string != "stop")
        {
            _exitWhenIdle = false;
            return false;
        }
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
            Notify(NothingChosenReason(tab));
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
            if (explicitDestination is null && Core.Search.ResultSetProvider.IsPersistent(destination))
            {
                // Toward a working set F5 and F6 collect references (FAR's Temporary Panel); nothing is copied or moved.
                await AddToTargetWorkingSetAsync(kind, items, destination!, sel?.Summary ?? SizeSummary(items));
                return;
            }
            string destText = destination is { IsFileSystem: true } d ? AppendSeparator(d.Path) : destination is null ? string.Empty : Services.Providers.Display(destination);
            if (items.Count == 1 && explicitDestination is null && destination is null) destText = string.Empty;
            var summary = explicitItems is null ? sel!.Value.Summary : SizeSummary(items);
            var request = await OperationDialogs.ShowTransferAsync(this, new TransferDialogInput(
                kind, items, summary, destText, target is null ? null : $"panel {target.Number}",
                sel?.HiddenMarked ?? 0, source.Scheme == Schemes.ResultSet, explicitDestination is null ? PanelDestinations(tab) : null));
            if (request is null) return;
            Location? destLocation;
            string? newName = null;
            if (destination is { IsFileSystem: false } && request.Destination == destText)
            {
                // An unchanged non-folder destination (an archive folder, say) is used as shown: re-parsing its display
                // text could name a different kind of location (an archive root's text is the archive file's path).
                destLocation = destination;
            }
            else if (!ResolveDestination(request.Destination, items, out destLocation, out newName, out var error))
            {
                Notify(error!, true);
                return;
            }
            // Hidden marks are excluded only when there are some and the user did not include them.
            finalItems = request.IncludeHidden || explicitItems is not null || (sel?.HiddenMarked ?? 0) == 0 ? items : tab.Listing.GetSelection(includeHiddenMarks: false);
            if (destLocation?.Scheme == Schemes.Zip)
            {
                await AddToArchiveAsync(kind, finalItems.ToList(), destLocation, request.Options);
                return;
            }
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

    /// <summary>The other panels' folders on disk that F5 and F6 can go to, in panel order.</summary>
    private List<PanelDestination> PanelDestinations(TabViewModel source)
    {
        var list = new List<PanelDestination>();
        foreach (var panel in Workspace.Panels)
        {
            if (ReferenceEquals(panel, source.Panel) || panel.ActiveTab?.Location is not { IsFileSystem: true } location) continue;
            list.Add(new PanelDestination(panel.Number, panel.ActiveTab.Title, AppendSeparator(location.Path)));
        }
        return list;
    }

    /// <summary>Why a command that acts on items has none: the cursor on "..", or an empty folder.</summary>
    internal static string NothingChosenReason(TabViewModel? tab) =>
        tab is not null && tab.Listing.TryGetFocused(out var focused) && focused.Kind == EntryKind.Parent
            ? "The cursor is on \"..\" (the folder above): move it to an item, or mark items."
            : "Nothing is focused or marked.";

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
        if (loc.Scheme == Schemes.Zip)
        {
            await RenameArchiveMemberAsync(tab, loc);
            return;
        }
        if (loc.Scheme is Schemes.Sftp or Schemes.Mtp)
        {
            await RenameRemoteAsync(tab, f);
            return;
        }
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
        if (tab?.Location?.Scheme == Schemes.Registry) { await CreateRegistryAsync(); return; }
        if (tab?.Location is { Scheme: Schemes.Zip } archiveFolder)
        {
            await CreateArchiveFolderAsync(tab, archiveFolder);
            return;
        }
        if (tab?.Location is { Scheme: Schemes.Sftp or Schemes.Mtp } remoteFolder && (Services.Providers.For(remoteFolder).GetCapabilities(remoteFolder) & LocationCapabilities.CreateDirectory) != 0)
        {
            await CreateRemoteFolderAsync(tab, remoteFolder);
            return;
        }
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
        if (TryGetFocusedRegistryItem(out _)) { await DeleteRegistryAsync(); return; }
        var sel = SourceSelection();
        if (sel is null) return;
        var (tab, items, hidden, summary) = sel.Value;
        IReadOnlyList<ItemRef>? finalItems = null, submitted = null;
        try
        {
            var loc = tab.Location!;
            if (loc.Scheme == Schemes.HiddenData)
            {
                await DeleteHiddenDataAsync(tab, items);
                return;
            }
            if (loc.Scheme == Schemes.Zip)
            {
                await DeleteArchiveMembersAsync(loc, items.ToList());
                return;
            }
            if (loc.Scheme is Schemes.Sftp or Schemes.Mtp)
            {
                await DeleteRemoteAsync(tab, items, summary);
                return;
            }
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
        var set = rs.Get(tab.Location);
        int n = rs.Remove(tab.Location, sel);
        tab.Refresh();
        if (set is { IsWorkingSet: true }) RefreshWorkingSetViews();
        Notify($"Removed {Formatters.Plural(n, "item", "items")} from {(set is { IsWorkingSet: true } w ? $"the working set \"{w.Title}\"" : "the result set")}. Nothing was deleted."
               + (set is { IsWorkingSet: true } ? WorkingSetsReadOnlyNote : string.Empty));
    }

    // ---- Job completion ---------------------------------------------------------------------------------

    private void OnJobFinished(Job job)
    {
        OnEditCommitFinished(job);
        OnJobFinishedCore(job);
    }

    private void OnJobFinishedCore(Job job)
    {
        Operations.UpdateSummary();
        if (_jobOrigins.Remove(job, out var origin))
        {
            var tab = origin.Tab;
            if (origin.Location.Scheme == Schemes.ResultSet) FollowSetChanges(job, origin.Location);
            if (tab.Location == origin.Location && job.Kind is not (JobKind.CreateDirectory or JobKind.CreateFile))
            {
                // Unmark what the job finished; failed and skipped roots stay marked for a retry.
                try { tab.Listing.MarkItems(job.Request.Sources, job.CompletedRootIndices, false); }
                catch (ObjectDisposedException) { }
            }
            // The tab may have been closed while the job ran: its listing is gone, and so is anything to focus.
            try
            {
                if (_focusAfter.Remove(job, out var focus) && job.State is JobState.Completed or JobState.CompletedWithIssues && tab.Location == origin.Location)
                    tab.Listing.Refresh();
                if (focus is not null) FocusWhenPresent(tab, focus);
            }
            catch (ObjectDisposedException) { }
        }
        // Permissions change without a new modification time: shown values are read again.
        if (job.Kind == JobKind.Attributes) Services.Metadata.Invalidate();
        RefreshAffected(job);
        // The job captured its sources; nothing reads them after this point.
        ItemSources.Release(job.Request.Sources);
        if (_editAfter.Remove(job, out var edit) && File.Exists(edit)) LaunchEditor(edit);
        if (job.Kind is JobKind.VerifyChecksums or JobKind.VerifyBeside) _ = OnVerifyFinishedAsync(job);
        else switch (job.State)
        {
            case JobState.Completed:
                if (job.Kind == JobKind.ArchiveTest) Notify($"{job.Title}: {job.Summary}.");
                else if (job.Kind is JobKind.Copy or JobKind.Move or JobKind.Recycle or JobKind.Delete or JobKind.Extract)
                    Notify($"{job.Title}: done{(job.BytesDone > 0 && job.Kind is not (JobKind.Recycle or JobKind.Delete) ? $" ({Formatters.SizeWithUnit(job.BytesDone)})" : "")}.");
                break;
            case JobState.CompletedWithIssues:
            case JobState.Failed:
                Notify($"{job.Title}: {job.State.Describe().ToLowerInvariant()} — {JobIssue.Summarize(job.Issues) ?? job.Summary}  (Ctrl+J shows details)",
                    job.State == JobState.Failed || job.ItemsFailed > 0);
                break;
            case JobState.Canceled:
                Notify($"{job.Title}: canceled. {job.Summary}. Steps already completed were kept.");
                break;
        }
        UpdateJobActivity();
        if (_exitWhenIdle && !Services.Jobs.HasActiveWork) View.CloseWhenIdle();
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

    /// <summary>Refreshes every tab that shows a folder on disk (after changes made outside a job, such as recovery).</summary>
    public void RefreshAll()
    {
        foreach (var p in Workspace.Panels)
        {
            foreach (var t in p.Tabs)
            {
                if (t.Location is { IsFileSystem: true }) t.Refresh();
            }
        }
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
        // A rebuilt archive changes every folder shown inside it.
        string? archive = job.Request.Archive?.ArchivePath is { } a ? Path.GetFullPath(a) : null;
        foreach (var p in Workspace.Panels)
        {
            foreach (var t in p.Tabs)
            {
                if (t.Location is { } l && (folders.Contains(l) || l.Scheme == Schemes.ResultSet || parents is null && l.IsFileSystem ||
                                            archive is not null && l.Scheme == Schemes.Zip && string.Equals(l.Container?.Path, archive, StringComparison.OrdinalIgnoreCase)))
                    t.Refresh();
            }
        }
    }

    private bool _exitWhenIdle;

    /// <summary>
    /// Continues an interrupted copy or move with a new job (plan §9.3: reality is inspected, nothing is replayed
    /// blindly). Partial files of the interruption are removed first, sources that no longer exist (already moved)
    /// are left out, and items that already arrived are skipped. False when nothing was started.
    /// </summary>
    public async Task<bool> RunInterruptedAgainAsync(InterruptedJob job)
    {
        var kind = job.Kind == nameof(JobKind.Move) ? JobKind.Move : JobKind.Copy;
        if (Location.Deserialize(job.Destination) is not { } destination)
        {
            Notify("The destination of this operation is not recorded; select the items and the destination again.");
            return false;
        }
        var (sources, partial) = await Task.Run(() =>
        {
            var paths = JournalRecovery.LoadSources(job);
            var existing = paths?.Select(p => Directory.Exists(p) ? ItemRef.ForFileSystemPath(p, EntryKind.Directory)
                : File.Exists(p) ? ItemRef.ForFileSystemPath(p, EntryKind.File) : null).OfType<ItemRef>().ToList();
            var leftovers = JournalRecovery.FindStagedLeftovers(job).Concat(JournalRecovery.FindIncompleteCopies(job))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return (existing, leftovers);
        });
        if (sources is null)
        {
            Notify("Not all source items of this operation are recorded; select them again to repeat it.");
            return false;
        }
        if (sources.Count == 0)
        {
            Notify("None of the source items exist any more: nothing is left to " + (kind == JobKind.Move ? "move." : "copy."));
            return false;
        }
        var done = kind == JobKind.Move ? "moved" : "copied";
        var message = $"{job.Title}: {sources.Count:N0} of {job.SourceCount:N0} source items still exist. Items that already arrived in {Services.Providers.Display(destination)} are skipped, so only the rest is {done}."
            + (partial.Count > 0 ? $"\n\nFirst, {Formatters.Plural(partial.Count, "partial file", "partial files")} left by the interruption will be deleted." : string.Empty);
        if (!await Dialogs.ConfirmAsync("Run again", message, kind == JobKind.Move ? "Move the rest" : "Copy the rest")) return false;
        int deleted = 0;
        foreach (var f in partial)
        {
            try
            {
                File.Delete(f);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        JournalRecovery.Close(job, $"Continued by a new operation; {deleted} partial file(s) deleted.");
        Services.Jobs.Submit(new JobRequest
        {
            Kind = kind,
            Sources = sources,
            Destination = destination,
            Options = new TransferOptions { Conflicts = ConflictPolicy.Skip, Verify = Enum.TryParse<VerifyMode>(Services.Settings.DefaultVerify, out var verify) ? verify : VerifyMode.Native },
        });
        return true;
    }

    /// <summary>At exit or sign-out: running jobs stop at their next safe boundary and journal the rest (plan §9.3).</summary>
    public void StopJobsForExit(TimeSpan wait)
    {
        var active = Services.Jobs.Jobs.Where(j => !j.State.IsFinished()).ToList();
        if (active.Count == 0) return;
        foreach (var j in active) j.Cancel();
        var deadline = DateTime.UtcNow + wait;
        while (Services.Jobs.HasActiveWork && DateTime.UtcNow < deadline) Thread.Sleep(25);
    }

    /// <summary>
    /// While jobs run or hex editors hold unsaved work: the optional keep-awake (jobs only), and a shutdown-block
    /// reason naming them at sign-out.
    /// </summary>
    private void UpdateJobActivity()
    {
        int active = Services.Jobs.Jobs.Count(j => !j.State.IsFinished());
        int editors = UnsavedHexEditors().Count;
        Services.Shell.SetKeepAwake(Services.Settings.KeepAwakeDuringJobs && active > 0);
        var reasons = new List<string>();
        if (active > 0) reasons.Add($"FileCat is running {Formatters.Plural(active, "file operation", "file operations")}; signing out interrupts them and FileCat reviews them at the next start.");
        if (editors > 0) reasons.Add($"{Formatters.Plural(editors, "hex editor has", "hex editors have")} unsaved changes.");
        Services.Shell.SetShutdownBlock(NativeOwner(), reasons.Count > 0 ? string.Join(" ", reasons) : null);
    }

    internal void RefreshSessionActivity() => UpdateJobActivity();

    /// <summary>Windows asks before signing out or shutting down: running operations and unsaved edits hold it so Windows can name them.</summary>
    public bool ShouldBlockSessionEnd => Services.Jobs.HasActiveWork || UnsavedHexEditors().Count > 0;

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
        await UndoJobAsync(job, last: true);
    }

    /// <summary>Undoes one specific finished operation (the Undo button on its row in the operations pane).</summary>
    public async Task UndoJobAsync(Job job, bool last = false)
    {
        if (!job.CanUndo) return;
        var steps = job.UndoSteps;
        if (steps.All(s => s.Kind == UndoKind.RegistryInverse))
        {
            await UndoRegistryAsync(job, steps);
            return;
        }
        var what = steps.Count == 1 ? Path.GetFileName(steps[0].To) : $"{steps.Count} items";
        if (!await Dialogs.ConfirmAsync("Undo", $"Undo {(last ? "the last operation" : "this operation")}?\n{job.Title}\n\nFileCat restores {what} only where the current state still matches what the operation left; anything changed since is kept and reported.", "Undo"))
            return;
        var fs = Services.Platform.FileOperations;
        var report = await Task.Run(() => UndoService.Undo(job, fs));
        job.MarkUndone();
        RefreshAffected(job);
        await Dialogs.AlertAsync("Undo result", string.Join("\n", report.Take(30)) + (report.Count > 30 ? $"\n… {report.Count - 30} more" : ""));
    }

    /// <summary>
    /// Registry undo runs the recorded inverse changes, newest first, as a new job. Each inverse is guarded by the
    /// state the original change left, so anything changed since is kept and reported rather than overwritten.
    /// </summary>
    private async Task UndoRegistryAsync(Job job, IReadOnlyList<UndoStep> steps)
    {
        var inverses = steps.Reverse().Select(s => s.Registry!).ToList();
        var lines = inverses.Take(12).Select(Platform.Windows.RegistryChangeRunner.Describe).ToList();
        if (inverses.Count > lines.Count) lines.Add($"… {inverses.Count - lines.Count:N0} more");
        int irreversible = job.Request.RegistryChanges.Count(c => c.Action == RegistryAction.DeleteKey) +
                           (job.Request.Registry is { Action: RegistryAction.DeleteKey } ? 1 : 0);
        if (!await Dialogs.ConfirmAsync("Undo Registry changes",
                $"Undo \"{job.Title}\"? FileCat applies these changes only where the data is still exactly what the operation left:\n" +
                string.Join("\n", lines) +
                (irreversible > 0 ? $"\n\n{Formatters.Plural(irreversible, "deleted key subtree is", "deleted key subtrees are")} not restored: their data was not retained." : ""),
                "Undo"))
            return;
        job.MarkUndone();
        var undo = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Registry,
            RegistryChanges = inverses,
            IndependentSteps = true,
            Destination = inverses[0].Key,
            Description = "Undo: " + job.Title,
        });
        if (ActiveTab is { } tab) Track(undo, tab);
    }

    // ---- View and edit ----------------------------------------------------------------------------------

    private void ViewFocused(bool hex)
    {
        var tab = ActiveTab;
        if (tab?.Location is null || !tab.Listing.TryGetFocused(out var f) || f.Kind == EntryKind.Parent) return;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        // A row about something else (a change-journal entry): its report.
        if (f.Tag is IReportedEntry reported)
        {
            new Views.ReportWindow("Change journal entry", reported.ReportTitle, _ => Task.FromResult(reported.Report())).Show();
            return;
        }
        if (item.Parent.Scheme == Schemes.Registry && item.Kind == EntryKind.RegistryKey)
        {
            _ = ViewRegistryKeyAsync(item);
            return;
        }
        if (f.IsContainer)
        {
            Notify("F3 views files. Folder sizes are computed with Space.");
            return;
        }
        if (item.Parent.Scheme == Schemes.Registry && item.Kind == EntryKind.RegistryValue)
        {
            ViewRegistryValue(item, hex);
            return;
        }
        // F3 honors a per-type View association; Alt+F3 always uses the internal viewer.
        if (!hex && item.FileSystemPath is { } viewPath && TryLaunchAssociation(Associations.View, viewPath)) return;
        _ = ViewItemAsync(item, f.Name, hex);
    }

    /// <summary>
    /// Opens content once, off the UI thread (a remote item may connect and ask for a password), and hands it to the
    /// viewer, which owns it from then on.
    /// </summary>
    private async Task ViewItemAsync(ItemRef item, string name, bool hex)
    {
        try
        {
            var source = await Task.Run(() => Services.Providers.For(item.Parent).OpenContent(item));
            if (source is null)
            {
                Notify("This item has no viewable content.", true);
                return;
            }
            ViewerLauncher.Open(Services, item, source, hex);
            if (item.Parent.IsFileSystem) Services.RecordFile(item.Parent, item.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        {
            Notify($"Cannot view \"{name}\": {(ex is OperationCanceledException ? "canceled." : ErrorText.Describe(ex))}", ex is not OperationCanceledException);
        }
    }

    private async Task EditFocusedAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is null || !tab.Listing.TryGetFocused(out var f) || f.Kind == EntryKind.Parent) return;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        if (item.Parent.Scheme == Schemes.Registry)
        {
            if (item.Kind == EntryKind.RegistryValue) await EditRegistryValueAsync(item);
            else Notify("F4 edits a selected value. Keys have no editable data property; use F7 to create a key or value.");
            return;
        }
        if (f.IsContainer)
        {
            Notify("F4 edits files. Folders have no content to edit.");
            return;
        }
        if (item.Parent.Scheme == Schemes.Zip)
        {
            await EditArchiveMemberAsync(item);
            return;
        }
        if (item.Parent.Scheme == Schemes.Sftp)
        {
            await EditRemoteFileAsync(item);
            return;
        }
        if (item.FileSystemPath is not { } path)
        {
            Notify("This location has no editable files. Copy the item to a folder with F5 to edit it.", true);
            return;
        }
        LaunchEditor(path);
        Services.RecordFile(item.Parent, item.Name);
    }

    private void LaunchEditor(string path)
    {
        var tool = Associations.Find(Services.Settings.Associations, Associations.Edit, Path.GetFileName(path)) ?? Services.Settings.Editor ?? ToolLauncher.DetectEditor();
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

    /// <summary>Runs the per-type association for an intent when one matches the file name; false otherwise.</summary>
    internal bool TryLaunchAssociation(string intent, string path)
    {
        if (Associations.Find(Services.Settings.Associations, intent, Path.GetFileName(path)) is not { } tool) return false;
        try
        {
            var result = ToolLauncher.Launch(tool, new ToolContext([path], Path.GetDirectoryName(path)!, ActiveTarget()?.Path), Services.Paths.TempDirectory);
            if (result.Warning is not null) Notify(result.Warning);
        }
        catch (ToolLaunchException ex)
        {
            Notify(ex.Message, true);
        }
        return true;
    }

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
        var files = sel.Where(s => !s.IsContainer && s.FileSystemPath is not null).ToList();
        if (files.Count == 0)
        {
            Notify("Checksums are computed for files; mark files or focus one.");
            return;
        }
        // A manifest is recognized, never verified automatically: ask which of the two is meant.
        if (files.All(f => Core.Operations.ChecksumManifests.IsManifestName(f.Name)))
        {
            var choice = await Dialogs.ShowCustomAsync("Checksum manifest",
                new Avalonia.Controls.TextBlock
                {
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 560,
                    Text = (files.Count == 1 ? $"\"{Formatters.SafeName(files[0].Name)}\" lists" : $"These {files.Count:N0} files list")
                           + " checksums of other files. Verify those files against it, or calculate checksums of the manifest itself?",
                },
                [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Checksum the manifest", "compute"),
                 new DialogButton("Verify listed files", "verify", IsDefault: true)]);
            if (choice as string is null or "cancel") return;
            if (choice as string == "verify")
            {
                await VerifyChecksumsAsync(files);
                return;
            }
        }
        await OperationDialogs.ShowChecksumsAsync(this, files.Select(f => f.FileSystemPath!).ToList());
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
