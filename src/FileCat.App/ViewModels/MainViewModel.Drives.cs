using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>Drives coming and going while FileCat runs (plan §8.2).</summary>
public sealed partial class MainViewModel
{
    private readonly IDisposable _drives;

    /// <summary>
    /// A drive came or went, or a card or disc in one: every This PC tab lists the drives anew, and a tab that showed a
    /// drive that is gone (or an archive or disk image on it) shows This PC instead of an error.
    /// </summary>
    private void OnDrivesChanged(DriveChange change)
    {
        var thisPc = new Location(Schemes.Computer, string.Empty);
        int moved = 0;
        foreach (var tab in Workspace.Panels.SelectMany(p => p.Tabs).ToList())
        {
            if (tab.Location is not { } location) continue;
            if (location.Scheme == Schemes.Computer)
            {
                if (!tab.Listing.IsRefreshing) tab.Listing.Refresh();
                continue;
            }
            if (change.Removed.Count == 0 || OutermostPath(location) is not { } path) continue;
            if (!change.Removed.Any(root => PathUtil.IsSameOrUnder(path, root))) continue;
            tab.Navigate(thisPc);
            moved++;
        }
        if (moved == 0) return;
        string names = string.Join(", ", change.Removed.Select(r => r.Length > 1 ? r.TrimEnd('\\', '/') : r));
        Notify($"{(change.Removed.Count == 1 ? "Drive " + names + " was" : "Drives " + names + " were")} removed: " +
               $"{Formatters.Plural(moved, "tab", "tabs")} that showed {(change.Removed.Count == 1 ? "it" : "them")} now {(moved == 1 ? "shows" : "show")} {Services.Providers.Display(thisPc)}.");
    }

    /// <summary>Where a location is on disk: its own path, or the path of the file an archive or disk image view is in.</summary>
    private static string? OutermostPath(Location location)
    {
        string? path = null;
        for (var at = location; at is not null; at = at.Container)
            if (at.IsFileSystem) path = at.Path;
        return path;
    }
}
