using Avalonia.Input.Platform;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.App.Services;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>A background write must not use a folder whose current listing has not established availability.</summary>
    internal static string? LocationWriteBlockReason(TabViewModel tab, Location location)
    {
        if (tab.Location != location) return "This location changed. Open its menu again.";
        if (tab.Listing.State == ListingState.Failed || tab.Listing.LastRefreshError is not null)
            return "This location is not available. Reread it (Ctrl+R) to retry before changing it.";
        return null;
    }

    /// <summary>The name under a listing's cursor, as a menu's caller records it before the menu opens.</summary>
    internal static string? CursorName(ListingModel listing) => listing.TryGetFocused(out var focused) ? focused.Name : null;

    /// <summary>
    /// After the Shell's New command in a folder's own menu made an item (New → Folder, Text Document, …): once the
    /// folder's listing has it, it is focused and its name opened for editing with the stem selected, as Explorer does.
    /// Only while the user has done nothing else since the menu opened: the cursor on the item it was on then
    /// (<paramref name="cursor"/>) and as many marks (<paramref name="marks"/>), the same folder and active tab, no dialog
    /// open, no name already being edited, the view's own conditions met (<paramref name="undisturbed"/>: the keyboard still
    /// in that list, no other menu opened), and within five seconds of the command. Anything they did meanwhile wins, and
    /// a late or unrelated reread never starts an edit.
    /// </summary>
    internal void RenameCreatedItem(PanelViewModel panel, TabViewModel tab, Location location, string name, string? cursor, int marks,
        Func<bool>? undisturbed = null)
    {
        if (!panel.Tabs.Contains(tab) || tab.Listing.IsDisposed || tab.Location != location) return;
        var listing = tab.Listing;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool reread = false;
        void Handler(object? sender, ListingChange change)
        {
            bool current = !listing.IsDisposed && tab.Location == location && panel.Tabs.Contains(tab) &&
                           ReferenceEquals(ActiveTab, tab) && !Dialogs.IsOpen && _inlineRenames == 0 &&
                           System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5) &&
                           undisturbed?.Invoke() != false;
            if (!current)
            {
                listing.Changed -= Handler;
                return;
            }
            // A load or reread replaces the rows it lists, moving the cursor and marks back by name: the item is looked
            // for once one has finished, not while rows still arrive, and only if the cursor and marks are as they were.
            if (listing.State != ListingState.Complete || listing.IsRefreshing ||
                (change & (ListingChange.RefreshComplete | ListingChange.State)) == 0) return;
            if (listing.MarkedCount != marks || cursor is not null && CursorName(listing) != cursor)
            {
                listing.Changed -= Handler;
                return;
            }
            if (listing.FocusName(name))
            {
                listing.Changed -= Handler;
                // The ordinary Rename command, with its checks, as F2 would run it on the new item.
                Execute(CommandIds.Rename);
                return;
            }
            // Not listed yet (that load or reread ran before the Shell made it): one reread is asked for.
            if (!reread)
            {
                reread = true;
                listing.Refresh();
            }
        }
        listing.Changed += Handler;
        // A folder still loading is not restarted (its marks and progress would go): its load's end is waited for. A
        // reread is the listing's own, as the folder watcher asks for one, without a tab's other refresh work.
        if (listing.State == ListingState.Complete)
        {
            reread = true;
            listing.Refresh();
        }
    }

    /// <summary>Runs a background-menu action against the captured folder, never the current item selection.</summary>
    internal async Task ExecuteLocationContextAsync(string id, PanelViewModel panel, TabViewModel tab, Location location)
    {
        if (!panel.Tabs.Contains(tab) || tab.Listing.IsDisposed || tab.Location != location)
        {
            Notify("This location changed before the menu action ran. Open its menu again.");
            return;
        }
        try
        {
            if ((id is CommandIds.PasteFromClipboard or CommandIds.MakeDirectory) &&
                LocationWriteBlockReason(tab, location) is { } unavailable)
            {
                Notify(unavailable, true);
                return;
            }
            var provider = Services.Providers.For(location);
            var caps = provider.GetCapabilities(location);
            switch (id)
            {
                case CommandIds.PasteFromClipboard:
                    if (!location.IsFileSystem || (caps & LocationCapabilities.TransferTarget) == 0)
                    {
                        Notify("Paste items into a writable file-system folder.", true);
                        return;
                    }
                    await PasteAsync(tab);
                    break;
                case CommandIds.MakeDirectory:
                    if (location.Scheme is not (Schemes.FileSystem or Schemes.Zip or Schemes.Archive or Schemes.Sftp or Schemes.Mtp) ||
                        (caps & LocationCapabilities.CreateDirectory) == 0)
                    {
                        Notify(provider.ExplainUnavailable(location, LocationCapabilities.CreateDirectory), true);
                        return;
                    }
                    await MakeDirectoryAsync(tab);
                    break;
                case CommandIds.Refresh:
                    Services.PrincipalNames.Invalidate();
                    Services.Metadata.Invalidate();
                    if (location.Scheme == Schemes.Recovery) Services.Recovery.Forget(location);
                    tab.Refresh();
                    break;
                case CommandIds.OpenTerminal when location.IsFileSystem:
                    Services.Shell.OpenTerminal(location.Path, Services.Settings.Terminal.Shell);
                    break;
                case CommandIds.OpenTerminal:
                    Notify("Open terminal here requires a file-system folder.", true);
                    break;
                case "location.copyPath":
                    if (View.Clipboard is not { } clipboard)
                    {
                        Notify("The clipboard is unavailable.", true);
                        return;
                    }
                    await clipboard.SetTextAsync(location.IsFileSystem ? location.Path : provider.GetDisplayPath(location));
                    break;
                default:
                    Notify("This location action is unavailable.", true);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error($"Location context action {id} failed", ex);
            Notify("This location action could not finish: " + Formatters.SafeName(ex.Message), true);
        }
    }
}
