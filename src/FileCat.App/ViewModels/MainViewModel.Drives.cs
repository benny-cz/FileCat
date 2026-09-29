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
        _ = RefreshDriveButtonsAsync();
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

    /// <summary>The drives the panels' place buttons show (D-52), in This PC's order; replaced whole when drives change.</summary>
    public IReadOnlyList<DriveTag> DriveButtons { get; private set; } = [];

    /// <summary>
    /// The drives, bookmarks, or saved servers may have changed: the place buttons are listed anew (and made anew only
    /// when something they show did change).
    /// </summary>
    public event Action? PlacesChanged;

    /// <summary>What the place buttons above each panel show: everything the location menu offers (D-53).</summary>
    public List<Place> BarPlaces() => Places(DriveButtons);

    private void OnStateSaved()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) PlacesChanged?.Invoke();
        else Avalonia.Threading.Dispatcher.UIThread.Post(() => PlacesChanged?.Invoke());
    }

    /// <summary>Lists the drives off the UI thread, each within a moment: a hung network drive does not hold the buttons up.</summary>
    public async Task RefreshDriveButtonsAsync()
    {
        if (Services.Providers.For(new Location(Schemes.Computer, string.Empty)) is not ComputerProvider computer) return;
        IReadOnlyList<DriveTag> drives;
        try { drives = await computer.QueryDrivesAsync(TimeSpan.FromMilliseconds(700), CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        DriveButtons = drives;
        PlacesChanged?.Invoke();
    }

    /// <summary>The root of the drive a location is on ("C:\", or the mount point on Linux and macOS), or null.</summary>
    public static string? DriveRootOf(Location? location)
    {
        if (location is null || OutermostPath(location) is not { } path) return null;
        return PathUtil.IsWindows ? Path.GetPathRoot(path) : UnixFiles.MountOf(path)?.Name;
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
