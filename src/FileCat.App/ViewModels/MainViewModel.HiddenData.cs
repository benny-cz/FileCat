using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>A file's streams and attributes (D-55): opened as a list, deleted, and saved as files.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The download marks: removing one lets the file open without the warning the system gives downloads.</summary>
    private static bool IsDownloadMark(ItemRef item) => HiddenIdentities.Kind(item.Ordinal) is { } kind &&
        DownloadMarks.IsDownloadMark(new(item.Name, kind, item.Size), OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    private void OpenHiddenData()
    {
        if (ActiveTab is not { } tab || FocusedFileSystemPath() is not { } path)
        {
            Notify("Streams and attributes belong to files and folders on disk.", true);
            return;
        }
        OpenHiddenDataForPath(path, newWorkspaceTab: Scope is not null);
    }

    private void OpenHiddenDataForPath(string path, bool newWorkspaceTab)
    {
        var location = HiddenDataProvider.Of(path);
        if (!Services.Providers.IsRegistered(Schemes.HiddenData) || Services.Providers.For(location) is not HiddenDataProvider { Data.IsSupported: true })
        {
            Notify("This system keeps no streams or attributes beside files.", true);
            return;
        }
        if (newWorkspaceTab)
        {
            if (_view?.TopLevel is not Avalonia.Controls.Window { IsVisible: true } main || Services.Io.IsStopped) return;
            Workspace.ActivePanel?.OpenTab(location);
            main.Activate();
            _view.FocusActivePanel();
        }
        else ActiveTab?.Navigate(location);
    }

    private static string HiddenNames(IReadOnlyList<ItemRef> items) =>
        items.Count == 1 ? $"“{items[0].Name}”" : $"{items.Count} streams and attributes";

    private async Task DeleteHiddenDataAsync(TabViewModel tab, IReadOnlyList<ItemRef> items)
    {
        var location = tab.Location;
        var inventory = tab.Listing.SupplementaryInventory;
        if (location?.Container is not { IsFileSystem: true } container || tab.IsLoading || tab.Listing.IsRefreshing ||
            tab.Listing.Provider is not HiddenDataProvider provider ||
            inventory is not { CanDelete: true, Identity: { } reviewed })
        {
            Notify(inventory is { CanDelete: false, Notice: { } reason } ? reason :
                "Deleting supplementary data requires a current inventory of the file. Refresh the inventory and try again.", true);
            return;
        }
        string file = container.Path;
        string text = $"Delete {HiddenNames(items)} of {Path.GetFileName(file)}? This cannot be undone.";
        if (items.Any(IsDownloadMark))
            text += " Without its download mark the file opens without the warning the system gives downloaded files.";
        if (!await Dialogs.ConfirmAsync("Delete", text, "Delete", danger: true)) return;
        var failures = new List<string>();
        string device = Services.Providers.For(Core.Resources.Location.FileSystem(file)).GetDeviceKey(Core.Resources.Location.FileSystem(file));
        await Core.Records.FileObservationRequests.RunOwned(Services.Io, device, Core.Threading.IoPriority.Interactive, ct =>
        {
            foreach (var item in items)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    provider.DeleteReviewedValue(item, reviewed, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    failures.Add($"{item.Name}: {ex.Message}");
                }
            }
            return true;
        });
        // The parent file's Extra data cell is cached by its own revision, which
        // need not change when a side stream is removed. Retire it even after a partial
        // failure so the next visible row reads the provider's actual current count.
        Services.Metadata.Forget("hidden", file);
        if (Equals(tab.Location, location)) tab.Refresh();
        if (failures.Count > 0) Notify("Not deleted: " + string.Join("; ", failures), true);
        else Notify(items.Count == 1 ? $"Deleted “{items[0].Name}”." : $"Deleted {items.Count} streams and attributes.");
    }
}
