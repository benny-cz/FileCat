using FileCat.App.Views;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

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

    /// <summary>
    /// Headless dispatch tests observe this route with a fake reader on every OS, including the Unix Properties
    /// fallback, without native I/O. The application never sets it and always uses the platform adapter.
    /// </summary>
    internal IFileRecords? FileRecordsForTests { get; set; }

    /// <summary>The selected panel item and context-menu paths share one exact-path report route.</summary>
    private bool ShowFileRecordForPath(string path)
    {
        var records = FileRecordsForTests ?? Services.Platform.FileRecords;
        if (!records.IsSupported) return false;
        var subject = Location.FileSystem(path);
        string device = Services.Providers.For(subject).GetDeviceKey(subject);
        var reader = new FileDetailsReader(records, Services.Io, device, path);
        var destination = View;
        var window = new ReportWindow(path, reader, Services.Platform.HiddenData.IsSupported ? () =>
        {
            OpenHiddenDataForPath(path, newWorkspaceTab: true);
            destination = _view;
        } : null, OpenTargetAsync);
        async Task<FileDetailsTargetCheck> OpenTargetAsync(string targetPath, CancellationToken ct)
        {
            if (_view?.TopLevel is not Avalonia.Controls.Window { IsVisible: true } main || Services.Io.IsStopped)
                throw new NotSupportedException("The main workspace is unavailable.");

            const string source = "Explicit target metadata check";
            string? resolvedPath = null;
            FileDetailsSnapshot summary;
            Location target;
            try
            {
                bool requiresDirectory = false;
                if (!OperatingSystem.IsWindows())
                    (targetPath, requiresDirectory) = await FileObservationRequests.RunOwned(Services.Io, device, IoPriority.Interactive,
                        token => FileDetailsTargets.ResolveUnixNavigationPath(targetPath, token), ct);
                resolvedPath = targetPath;
                target = Location.FileSystem(resolvedPath);
                using var targetReader = new FileDetailsReader(records, Services.Io, Services.Providers.For(target).GetDeviceKey(target), resolvedPath);
                summary = (await targetReader.ReadAsync(FileDetailsTier.Summary, ct)).Summary;
                ct.ThrowIfCancellationRequested();
                if (!FileDetailsTargets.IsOrdinaryNavigationTarget(summary, OperatingSystem.IsWindows()))
                    throw new NotSupportedException("The target could not be confirmed as an ordinary file or folder.");
                if (requiresDirectory && summary.Kind != FileObjectKind.Directory)
                    throw new NotSupportedException("The stored target requires a directory.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                var state = ex switch
                {
                    FileNotFoundException or DirectoryNotFoundException => FactState.Missing,
                    UnauthorizedAccessException => FactState.Denied,
                    NotSupportedException => FactState.Unsupported,
                    _ => FactState.Failed,
                };
                return new(resolvedPath, DateTimeOffset.UtcNow, ObservedFact<FileObjectKind>.Unavailable(state, FactScope.Entry, source, ex.Message));
            }

            if (!main.IsVisible || _view?.TopLevel != main || Workspace.ActivePanel is not { } panel)
                throw new NotSupportedException("The main workspace is unavailable.");
            var location = summary.Kind == FileObjectKind.Directory ? target : Location.FileSystem(Path.GetDirectoryName(targetPath) ?? targetPath);
            panel.OpenTab(location, summary.Kind == FileObjectKind.File ? Path.GetFileName(targetPath) : null);
            destination = _view;
            main.Activate();
            _view.FocusActivePanel();
            return new(resolvedPath, summary.ObservedAt, ObservedFact<FileObjectKind>.Known(summary.Kind, FactScope.Entry, source));
        }
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
