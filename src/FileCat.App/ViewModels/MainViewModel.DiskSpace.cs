using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    internal const string AnalyzeDiskSpaceTitle = "Analyze disk space…";
    internal const string AnalyzeSelectedFoldersTitle = "Analyze selected folders…";

    private void OpenDiskSpaceFromTools()
    {
        string? folder = ActiveTab?.Location is { IsFileSystem: true } location ? FullDiskSpacePath(location.Path) : null;
        OpenDiskSpace(folder is null ? [] : [folder], fromTools: true);
    }

    internal void OpenDiskSpace(IReadOnlyList<string> roots, bool fromTools = false)
    {
        if (Scope is null && _dialogs is { IsOpen: true }) return;
        if (AnalysisAdmission.Refusal() is { } reason) { Notify(reason); return; }
        string? panelFilter = ActiveTab?.Listing is { IsDisposed: false, Filter: { } mask } ? AnalysisPanelFilter.Capture(mask) ?? "" : null;
        if (fromTools) Services.DiskSpace.OpenFromTools(roots, panelFilter);
        else Services.DiskSpace.Open(roots, panelFilter);
    }

    private bool ShowReadOnlyAnalysisFolder(AnalysisPanelFolder folder)
    {
        if (_view?.TopLevel is not Avalonia.Controls.Window { IsVisible: true } main || Workspace.ActivePanel is not { } panel)
            return false;
        using var pending = Services.AnalysisPanelFolders.Publish(folder);
        panel.OpenTab(pending.Location);
        main.Activate();
        _view.FocusActivePanel();
        return true;
    }

    internal static string? DiskSpaceRoot(TabViewModel tab, int index)
    {
        var listing = tab.Listing;
        if (listing.IsDisposed || (uint)index >= (uint)listing.Store.Count) return null;
        var row = listing.Store[index];
        if (row.Kind == EntryKind.Drive && tab.Location?.Scheme == Schemes.Computer && row.Tag is DriveTag drive)
            return DiskSpaceDriveRoot(drive);
        return row.Kind == EntryKind.Directory && !row.Has(EntryFlags.Unavailable)
            ? FullDiskSpacePath(listing.GetItemRef(index).FileSystemPath) : null;
    }

    internal static string? DiskSpaceDriveRoot(DriveTag drive) => drive.Ready && drive.DriveType != nameof(DriveType.Network)
        ? FullDiskSpacePath(drive.RootPath) : null;

    private static string? FullDiskSpacePath(string? path) => path is not null && Path.IsPathFullyQualified(path) ? path : null;

    internal static IReadOnlyList<string>? MarkedDiskSpaceRoots(TabViewModel tab)
    {
        var listing = tab.Listing;
        if (listing.IsDisposed || listing.MarkedCount is 0 or > AnalysisLimits.Roots) return null;
        Span<int> indices = stackalloc int[AnalysisLimits.Roots];
        int count = listing.CopyMarkedStoreIndices(-1, indices);
        var roots = new string[count];
        for (int i = 0; i < count; i++)
        {
            if (DiskSpaceRoot(tab, indices[i]) is not { } root) return null;
            roots[i] = root;
        }
        return roots;
    }

    private async Task<bool> ConfirmExitWithAnalysesAsync()
    {
        if (!Services.HasTemporaryAnalyses) return true;
        var answer = await Dialogs.ShowCustomAsync("Disk space analyses are open",
            new Avalonia.Controls.TextBlock { Text = "Exiting stops active scans and discards unsaved analysis results.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            [new DialogButton("Keep FileCat open", "keep", IsDefault: true, IsCancel: true),
             new DialogButton("Review and save", "review"), new DialogButton("Stop and exit", "stop", IsDanger: true)]);
        if (answer as string == "review") Services.DiskSpace.ReviewTemporaryWork();
        return answer as string == "stop";
    }
}
