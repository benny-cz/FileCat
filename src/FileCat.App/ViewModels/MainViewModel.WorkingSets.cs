using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.App.ViewModels;

/// <summary>
/// Persistent working sets (plan §11, P7; FAR's Temporary Panel): named sets of references collected from many
/// folders. Membership is not ownership: Remove from set and deleting a set never touch the items, while F8 inside a
/// set deletes the originals with the usual explicit wording.
/// </summary>
public sealed partial class MainViewModel
{
    private string WorkingSetsReadOnlyNote => Services.WorkingSets.IsReadOnly
        ? " Working sets were saved by a newer FileCat, so changes last only until FileCat exits."
        : string.Empty;

    /// <summary>Opens the list of working sets in the active panel.</summary>
    private void OpenWorkingSets()
    {
        if (ActiveTab is not { } tab) return;
        tab.Navigate(ResultSetProvider.WorkingSetList);
        View.FocusActivePanel();
    }

    /// <summary>Add to working set: the marked or focused items become references in a chosen or new set.</summary>
    private async Task AddToWorkingSetAsync()
    {
        if (ResultSetProvider.IsWorkingSetList(ActiveTab?.Location))
        {
            Notify("These are working sets, not items: open a folder and mark what to add.");
            return;
        }
        var sel = SourceSelection();
        if (sel is null) return;
        try
        {
            var set = await ChooseWorkingSetAsync($"Add {sel.Value.Summary} to a working set");
            if (set is not null) AddMembers(set, sel.Value.Items);
        }
        finally
        {
            ItemSources.Release(sel.Value.Items);
        }
    }

    /// <summary>F5 or F6 toward a panel that shows a working set (or their list): references are added, nothing is copied.</summary>
    private async Task AddToTargetWorkingSetAsync(JobKind kind, IReadOnlyList<ItemRef> items, Location destination, string summary)
    {
        var set = Services.WorkingSets.Get(destination);
        if (ResultSetProvider.IsWorkingSet(destination) && set is null)
        {
            Notify("The working set in the target panel was deleted.", true);
            return;
        }
        if (set is null)
        {
            set = await ChooseWorkingSetAsync($"Add {summary} to a working set");
            if (set is null) return;
        }
        else if (!await Dialogs.ConfirmAsync("Add to working set",
                     $"Add {summary} to the working set \"{set.Title}\"? Only references are added: nothing is {(kind == JobKind.Move ? "moved" : "copied")}, " +
                     "and removing them from the set later never deletes them.", "Add to set"))
        {
            return;
        }
        AddMembers(set, items);
    }

    private void AddMembers(ResultSet set, IReadOnlyList<ItemRef> items)
    {
        var candidates = items.Where(i => i.Parent.Scheme != Schemes.ResultSet && i.Kind != EntryKind.Parent).ToList();
        if (candidates.Count == 0)
        {
            Notify("A working set holds files, folders, and Registry items, not other sets.", true);
            return;
        }
        int already = candidates.Count(i => set.Contains(new ItemRef(i.Parent, i.Name, i.Kind) { Ordinal = i.Ordinal }));
        int added = Services.WorkingSets.Add(set, candidates);
        int full = candidates.Count - already - added;
        bool unsavedConnection = candidates.Any(i => i.Parent.Scheme == Schemes.Sftp && Services.FindRemoteProfile(i.Parent.Session ?? string.Empty) is { Temporary: true });
        RefreshWorkingSetViews();
        Notify($"Added {Formatters.Plural(added, "item", "items")} to the working set \"{set.Title}\"; nothing was copied." +
               (already > 0 ? $" {Formatters.Plural(already, "item was", "items were")} already in it." : string.Empty) +
               (full > 0 ? $" The set is full: it holds at most {WorkingSets.MaxMembers:N0} items, so {full:N0} were left out." : string.Empty) +
               (unsavedConnection ? " Items on a connection you have not saved open only while it is known this session." : string.Empty) +
               WorkingSetsReadOnlyNote, full > 0);
    }

    /// <summary>A type-to-filter list of the working sets plus "New working set…"; null when canceled.</summary>
    private async Task<ResultSet?> ChooseWorkingSetAsync(string title)
    {
        var sets = Services.WorkingSets.All;
        var items = sets.Select(s => new ChoiceItem(s.Title, $"{Formatters.Plural(s.Count, "item", "items")} · changed {s.ModifiedUtc.ToLocalTime():g}")).ToList();
        int create = items.Count;
        items.Add(new ChoiceItem("New working set…", "Name a new set for these items"));
        var r = await Dialogs.ChooseAsync(new ChoiceOptions(title, items)
        {
            Hint = "Type to filter · Enter chooses · the items stay where they are",
            SelectedIndex = sets.Count == 0 ? create : 0,
        });
        if (r.Index < 0) return null;
        return r.Index == create ? await CreateWorkingSetAsync() : sets[r.Index];
    }

    private async Task<ResultSet?> CreateWorkingSetAsync()
    {
        var r = await Dialogs.PromptAsync(new PromptOptions("New working set", "Name of the working set:")
        {
            Text = Services.WorkingSets.SuggestName(),
            Validate = n => Services.WorkingSets.ValidateName(n),
            ConfirmText = "Create",
            Hint = "A working set keeps references to items anywhere; it never holds copies.",
        });
        if (r is null) return null;
        var set = Services.WorkingSets.Create(r.Text);
        RefreshWorkingSetViews();
        return set;
    }

    /// <summary>
    /// File commands in the list of working sets act on the sets: F7 creates one, F2 renames, F8 deletes the set only.
    /// Returns false for commands that need no special meaning here.
    /// </summary>
    private async Task<bool> WorkingSetListCommandAsync(string id)
    {
        var tab = ActiveTab!;
        switch (id)
        {
            case CommandIds.MakeDirectory:
                if (await CreateWorkingSetAsync() is { } created)
                {
                    Notify($"Created the working set \"{created.Title}\". Add items with F5 from another panel or {GestureOf(CommandIds.AddToWorkingSet)}." + WorkingSetsReadOnlyNote);
                    FocusWhenPresent(tab, created.Title);
                }
                return true;
            case CommandIds.Rename:
                if (FocusedWorkingSet(tab) is not { } set) return true;
                var name = await View.RenameInlineAsync(new PromptOptions("Rename working set", $"New name for \"{set.Title}\":")
                {
                    Text = set.Title,
                    Validate = n => Services.WorkingSets.ValidateName(n, set),
                    ConfirmText = "Rename",
                });
                if (name is null) return true;
                Services.WorkingSets.Rename(set, name);
                RefreshWorkingSetViews();
                FocusWhenPresent(tab, set.Title);
                return true;
            case CommandIds.Delete:
            case CommandIds.DeletePermanent:
                await DeleteWorkingSetsAsync(tab);
                return true;
            case CommandIds.Copy:
            case CommandIds.Move:
            case CommandIds.Duplicate:
            case CommandIds.EditNew:
            case CommandIds.AddToWorkingSet:
                Notify("These are working sets, not items: Enter opens a set, whose items can then be copied or moved.");
                return true;
            case CommandIds.RemoveFromSet:
                Notify("Remove from set works inside a set; to forget whole sets, use F8 here (their items are not touched).");
                return true;
        }
        return false;
    }

    private ResultSet? FocusedWorkingSet(TabViewModel tab) =>
        tab.Listing.TryGetFocused(out var e) && e.Tag is WorkingSetTag t ? Services.WorkingSets.Get(new Location(Schemes.ResultSet, ResultSetProvider.WorkingSetPath, session: t.Id)) : null;

    private async Task DeleteWorkingSetsAsync(TabViewModel tab)
    {
        var chosen = tab.Listing.GetSelection().Select(i => Services.WorkingSets.Find(i.Name)).OfType<ResultSet>().Distinct().ToList();
        if (chosen.Count == 0) return;
        int members = chosen.Sum(s => s.Count);
        string what = chosen.Count == 1 ? $"the working set \"{chosen[0].Title}\"" : $"{chosen.Count} working sets";
        if (!await Dialogs.ConfirmAsync("Delete working set" + (chosen.Count == 1 ? string.Empty : "s"),
                $"Delete {what}? Only the set{(chosen.Count == 1 ? " is" : "s are")} forgotten: the {Formatters.Plural(members, "item", "items")} " +
                $"{(chosen.Count == 1 ? "it refers" : "they refer")} to stay where they are.", "Delete set" + (chosen.Count == 1 ? string.Empty : "s"), danger: true))
            return;
        int n = Services.WorkingSets.Delete(chosen);
        RefreshWorkingSetViews();
        Notify($"Deleted {Formatters.Plural(n, "working set", "working sets")}. No files were touched." + WorkingSetsReadOnlyNote);
    }

    /// <summary>Refreshes tabs that show the list of working sets or a working set.</summary>
    private void RefreshWorkingSetViews()
    {
        foreach (var p in Workspace.Panels)
        {
            foreach (var t in p.Tabs)
            {
                if (ResultSetProvider.IsPersistent(t.Location)) t.Refresh();
            }
        }
    }

    /// <summary>
    /// Keeps a result or working set pointing at its items after a rename, move, or delete made through it: renamed
    /// and moved items are followed and deleted ones leave the set. Items that failed or were skipped keep their
    /// references.
    /// </summary>
    private void FollowSetChanges(Job job, Location origin)
    {
        if (Services.ResultSets.Get(origin) is not { } set) return;
        var request = job.Request;
        try
        {
            var sources = request.Sources;
            var done = job.CompletedRootIndices.Where(i => i >= 0 && i < sources.Count).ToList();
            if (done.Count == 0) return;
            switch (job.Kind)
            {
                case JobKind.Recycle or JobKind.Delete:
                    set.Remove(done.Select(i => sources[i]).ToList());
                    break;
                case JobKind.Rename:
                    foreach (int i in done)
                    {
                        var name = request.NewNames is { } names && i < names.Count ? names[i] : request.NewName;
                        var old = sources[i];
                        if (name is not null) set.Replace(old, new ItemRef(old.Parent, name, old.Kind, old.Size, old.Modified) { Ordinal = old.Ordinal, Flags = old.Flags });
                    }
                    break;
                case JobKind.Move when request.Destination is { } destination:
                    foreach (int i in done)
                    {
                        var old = sources[i];
                        var parent = !request.Options.Flatten && old.RelativeFolder is { Length: > 0 } rel && destination.IsFileSystem
                            ? destination.WithPath(Path.Join(destination.Path, rel))
                            : destination;
                        var name = sources.Count == 1 && request.NewName is { } renamed ? renamed : old.Name;
                        set.Replace(old, new ItemRef(parent, name, old.Kind, old.Size, old.Modified) { Flags = old.Flags });
                    }
                    break;
            }
        }
        catch (ObjectDisposedException) { } // the captured selection was already released
    }

    private string GestureOf(string commandId) => Services.Keymap.GetGestureText(commandId) is { Length: > 0 } g ? g : "File → " + Services.Commands.Get(commandId)?.Title;
}
