using System.Buffers.Binary;
using System.Globalization;
using Avalonia.Input.Platform;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
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
    internal bool ShowFileRecordForPath(string path)
    {
        var records = FileRecordsForTests ?? Services.Platform.FileRecords;
        if (!records.IsSupported) return false;
        var subject = Location.FileSystem(path);
        string device = Services.Providers.For(subject).GetDeviceKey(subject);
        var reader = new FileDetailsReader(records, Services.Io, device, path);
        var destination = View;
        // The optional compression link (I-DETAILS-C), only where this build offers NTFS compression at all.
        var compression = Services.Jobs.CompressionUnavailable is null ? new DetailsCompressionLink(DetailsCompressionUnavailable, async (directory, report) =>
        {
            if (await ReviewDetailsCompressionAsync(path, directory, report)) destination = _view;
        }) : null;
        var window = new ReportWindow(path, reader, Services.Platform.HiddenData.IsSupported ? () =>
        {
            OpenHiddenDataForPath(path, newWorkspaceTab: true);
            destination = _view;
        } : null, OpenTargetAsync, compression, PermissionsUnavailable, OpenPermissionsAsync);
        string? PermissionsUnavailable(FileDetailsSnapshot? snapshot)
        {
            if (PermissionProcessRefusal() is { } refusal) return refusal;
            if (Services.Permissions is NoPermissionService) return "A permissions reader is unavailable on this platform.";
            if (_view?.TopLevel is not Avalonia.Controls.Window { IsVisible: true } || Services.Io.IsStopped)
                return "The owning workspace is unavailable.";
            return snapshot is not null && PermissionRecordIdentity(snapshot) is not null ? null
                : "A known ordinary file or directory identity is required. Refresh these details first.";
        }
        async Task OpenPermissionsAsync(FileDetailsSnapshot captured, Func<bool> current, CancellationToken ct)
        {
            if (PermissionsUnavailable(captured) is { } unavailable) throw new NotSupportedException(unavailable);
            if (_view.TopLevel is not Avalonia.Controls.Window main || _dialogs.IsOpen)
                throw new NotSupportedException("Close the active workspace dialog before opening permissions.");
            var identity = PermissionRecordIdentity(captured) ?? throw new NotSupportedException("The original object identity is unavailable.");
            var kind = captured.Kind == FileObjectKind.Directory ? PermissionObjectKind.Directory : PermissionObjectKind.File;
            var original = new PermissionSubject(subject, path, path, LocalPermissionAuthority, kind, PermissionLinkPolicy.NoFollow,
                ExpectedObject: identity);
            using var inspection = await Services.Io.RunOwned(device, IoPriority.Interactive,
                token => Services.Permissions.Inspect(original, PermissionReadSections, token), ct);
            ct.ThrowIfCancellationRequested();
            if (!current() || !main.IsVisible || _view.TopLevel != main || _dialogs.IsOpen)
                throw new NotSupportedException("The original details or the owning workspace changed during inspection.");
            if (inspection.Snapshot.Binding is null)
                throw new NotSupportedException(inspection.Snapshot.BindingProblem ?? "The original object could not be verified.");
            destination = _view;
            var clipboard = _view.Clipboard;
            main.Activate();
            var source = ItemRef.ForFileSystemPath(path, kind == PermissionObjectKind.Directory ? EntryKind.Directory : EntryKind.File);
            await ShowPermissionInspectorAsync(_dialogs, [new(path, source, inspection)], clipboard is null ? null : clipboard.SetTextAsync);
        }
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

    private static FileIdentity? PermissionRecordIdentity(FileDetailsSnapshot snapshot)
    {
        if (snapshot.Kind is not (FileObjectKind.File or FileObjectKind.Directory) || snapshot.Identity.State != FactState.Known ||
            snapshot.Identity.Value is not { } id) return null;
        if (OperatingSystem.IsWindows() && snapshot.Identity.Source == "FileIdInfo" &&
            ulong.TryParse(id.Device, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong serial) &&
            UInt128.TryParse(id.Object, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out UInt128 fileId))
        {
            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteUInt128LittleEndian(bytes, fileId);
            return new(serial, Convert.ToHexString(bytes));
        }
        if (!OperatingSystem.IsLinux()) return null;
        string[] device = id.Device.Split(':');
        if (device.Length != 2 || !uint.TryParse(device[0], out uint major) || !uint.TryParse(device[1], out uint minor) ||
            !ulong.TryParse(id.Object, out ulong inode)) return null;
        return new(((ulong)major << 32) | minor, $"{inode:X16}0000000000000000");
    }

    /// <summary>Why the main window cannot host a compression review for a details report now, or null when it can.</summary>
    private string? DetailsCompressionUnavailable()
    {
        if (Services.Io.IsStopped) return "FileCat is closing.";
        if (Services.Jobs.CompressionUnavailable is { } problem) return problem.Message;
        if (_view?.TopLevel is not Avalonia.Controls.Window { IsVisible: true }) return "The main window is unavailable.";
        if (_dialogs.IsOpen) return "Close the dialog in the main window first.";
        return null;
    }

    /// <summary>
    /// I-DETAILS-C: one NTFS compression review for the item a details report was opened for. The item is captured once, from
    /// the report's own path; no panel, selection, Find or listing generation is consulted, so a report that outlives the Find
    /// or panel it came from still reviews exactly its item. The main window hosts the review with its own dialog service (not
    /// a Find window's while one runs a command there) and reads the item afresh, as for a panel selection. Until the review is
    /// actionable, closing the report, canceling the review or shutting down retires it; from then on the review, and the job
    /// it submits, belong to the main window. The job has no listing origin: no panel's marks are cleared, and completion
    /// refreshes the affected folders as for every job. True when a job was submitted.
    /// </summary>
    private async Task<bool> ReviewDetailsCompressionAsync(string path, bool directory, CancellationToken report)
    {
        if (DetailsCompressionUnavailable() is { } unavailable) { Notify(unavailable, true); return false; }
        if (_view.TopLevel is not Avalonia.Controls.Window main) return false;
        if (Path.GetDirectoryName(path) is not { Length: > 0 } parent)
        {
            Notify("NTFS compression is reviewed for files and folders, not a whole drive.", true);
            return false;
        }
        var item = new ItemRef(Location.FileSystem(parent), Path.GetFileName(path), directory ? EntryKind.Directory : EntryKind.File);
        var dialogs = _dialogs;
        using var preparation = new DetachedPreparation(report, Services.Io.StoppingToken);
        // The review opens in the main window: bring it forward, as navigation from the report does.
        if (main.WindowState == Avalonia.Controls.WindowState.Minimized) main.WindowState = Avalonia.Controls.WindowState.Normal;
        main.Activate();
        try
        {
            if (await StorageRejectedSelectionDialog.ShowIfRejectedAsync(dialogs, [item], [false])) return false;
            preparation.Token.ThrowIfCancellationRequested();
            using var review = Services.Jobs.CaptureStorageSelection([item]);
            string device = Services.Providers.For(item.Parent).GetDeviceKey(item.Parent);
            using var form = new StorageChangeDialog(review, Services.Io, device, [false], preparation.Token, preparation.Detach, detailsItem: true);
            using var choice = await form.ShowAsync(dialogs);
            if (choice is null)
            {
                if (form.RequestedOperation is { } blocker && main is MainWindow owner) owner.RevealStorageBlocker(blocker);
                return false;
            }
            _ = Operations; // Subscribe to terminal events before the manager can schedule this job.
            // No listing origin to register: completion unmarks nothing, and RefreshAffected still refreshes its folders.
            Services.Jobs.SubmitStorage(choice.Roots, choice.Approval, static _ => { }, choice.Mode);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Notify("Could not prepare NTFS compression. " + error.Message, true);
            return false;
        }
    }

    /// <summary>
    /// The preparation of one detached review: canceled by its report's closing or by shutdown until the review is actionable
    /// (<see cref="Detach"/>, which the review calls then), and only by shutdown afterwards.
    /// </summary>
    private sealed class DetachedPreparation : IDisposable
    {
        private readonly CancellationTokenSource _stop;
        private CancellationTokenRegistration _report;
        public DetachedPreparation(CancellationToken report, CancellationToken shutdown)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            _report = report.Register(() => { try { _stop.Cancel(); } catch (ObjectDisposedException) { } });
        }
        public CancellationToken Token => _stop.Token;
        public void Detach() => _report.Dispose();
        public void Dispose()
        {
            _report.Dispose();
            _stop.Dispose();
        }
    }
}
