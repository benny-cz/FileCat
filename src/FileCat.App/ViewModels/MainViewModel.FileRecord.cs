using FileCat.App.Views;
using FileCat.Core.Records;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>An item's file-system record (D-56): what the file system itself keeps about it, in a report window.</summary>
public sealed partial class MainViewModel
{
    private void ShowFileRecord()
    {
        if (FocusedFileSystemPath() is not { } path)
        {
            Notify("File-system records belong to files and folders on disk.", true);
            return;
        }
        if (!ShowFileRecordForPath(path))
            Notify("FileCat does not read file-system records on this system.", true);
    }

    /// <summary>The selected panel item and context-menu paths share one exact-path report route.</summary>
    private bool ShowFileRecordForPath(string path)
    {
        var records = Services.Platform.FileRecords;
        if (!records.IsSupported) return false;
        var subject = Location.FileSystem(path);
        string device = Services.Providers.For(subject).GetDeviceKey(subject);
        var reader = new FileDetailsReader(records, Services.Io, device, path);
        var destination = View;
        var window = new ReportWindow(path, reader, Services.Platform.HiddenData.IsSupported ? () =>
        {
            OpenHiddenDataForPath(path, newWorkspaceTab: true);
            destination = _view;
        } : null, async (targetPath, ct) =>
        {
            if (_view?.TopLevel is not Avalonia.Controls.Window { IsVisible: true } main || Services.Io.IsStopped)
                throw new NotSupportedException("The main workspace is unavailable.");
            var target = Location.FileSystem(targetPath);
            using var targetReader = new FileDetailsReader(records, Services.Io, Services.Providers.For(target).GetDeviceKey(target), targetPath);
            var observed = await targetReader.ReadAsync(FileDetailsTier.Summary, ct);
            ct.ThrowIfCancellationRequested();
            if (observed.Summary.Kind is not (FileObjectKind.File or FileObjectKind.Directory))
                throw new NotSupportedException("The target is another link or special object; inspect it before navigating further.");
            var location = observed.Summary.Kind == FileObjectKind.Directory ? target
                : Location.FileSystem(Path.GetDirectoryName(targetPath) ?? targetPath);
            Workspace.ActivePanel?.OpenTab(location, observed.Summary.Kind == FileObjectKind.File ? Path.GetFileName(targetPath) : null);
            destination = _view;
            main.Activate();
            _view.FocusActivePanel();
        });
        window.Closed += (_, _) =>
        {
            if (destination?.TopLevel is Avalonia.Controls.Window { IsVisible: true } live)
            {
                live.Activate();
                destination.FocusActivePanel();
            }
        };
        window.Show();
        return true;
    }
}
