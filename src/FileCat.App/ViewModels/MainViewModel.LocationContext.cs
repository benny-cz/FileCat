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
                    if (location.Scheme is not (Schemes.FileSystem or Schemes.Zip or Schemes.Sftp or Schemes.Mtp) ||
                        (caps & LocationCapabilities.CreateDirectory) == 0)
                    {
                        Notify(provider.ExplainUnavailable(location, LocationCapabilities.CreateDirectory), true);
                        return;
                    }
                    await MakeDirectoryAsync(tab);
                    break;
                case CommandIds.Refresh:
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
