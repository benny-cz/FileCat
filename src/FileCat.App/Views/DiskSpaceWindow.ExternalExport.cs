using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    internal static async Task<string?> ReviewExternalExportAsync(IDialogService dialogs, string folder, string observed, string initialName, CancellationToken cancellation)
    {
        var name = new TextBox { Text = initialName, MaxLength = 120 };
        AutomationProperties.SetName(name, "External inventory filename");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        string? NameError()
        {
            try { AnalysisPrivateInventoryStore.ValidateName(name.Text ?? ""); return null; }
            catch (ArgumentException) { return "Use a portable filename ending in .fcdsa, up to 120 characters."; }
        }
        name.TextChanged += (_, _) => error.Text = NameError();
        var body = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "Export the entire captured inventory, including exact paths, raw names, identities and observed metadata. Display filters, selected rows and the current chart do not limit this export. File contents and operation approvals are excluded.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Destination folder: " + AnalysisNames.Display(folder, 4096) + "\n" + observed, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "This sensitive file can be copied or shared outside FileCat. Imported claims are not authenticated. Existing files are kept; qualified external replacement is unavailable, so choose a new filename.", TextWrapping = TextWrapping.Wrap },
            name, error
        } };
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
        var choice = await dialogs.ShowCustomAsync("Export entire inventory", body,
            [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Export new inventory", true) { IsAvailable = () => NameError() is null }], name, closer: closer);
        return choice is true ? name.Text : null;
    }
    internal static async Task<bool> ReviewExistingExternalExportAsync(IDialogService dialogs, string name, CancellationToken cancellation)
    {
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
        return await dialogs.ShowCustomAsync("Keep existing output", new TextBlock
        {
            Text = "The destination already contains " + AnalysisNames.Display(name, 240) + ". It was kept. Qualified external replacement is unavailable. Choose a new name to export another copy.", TextWrapping = TextWrapping.Wrap
        }, [new DialogButton("Keep existing", false, IsDefault: true, IsCancel: true), new DialogButton("Choose new name", true)], closer: closer) is true;
    }
    private async Task<string?> ExportExternalInventoryAsync(AppServices services, string observed, CancellationToken lifetime,
        Func<AnalysisIndexedAcquisition, string, string, CancellationToken, Task<AnalysisExportReceipt>> export, Action<Exception> rememberFailure)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime, _indexedLifetime.Token);
        try
        {
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            var items = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose inventory export folder", AllowMultiple = false });
            using var picked = new PickedStorageItems(items);
            cancel.Token.ThrowIfCancellationRequested(); if (items.Count == 0) return null;
            string folder = items[0].TryGetLocalPath() ?? throw new NotSupportedException("Choose a directly accessible local output folder.");
            AnalysisExportDestination.ValidateFolder(folder);
            string name = "Analysis " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".fcdsa";
            while (true)
            {
                string? reviewed = await ReviewExternalExportAsync(_dialogs, folder, observed, name, cancel.Token);
                if (reviewed is null) return null; name = reviewed; cancel.Token.ThrowIfCancellationRequested();
                try
                {
                    var task = WriteAsync(); var closer = new DialogCloser();
                    using var close = cancel.Token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
                    try
                    {
                        if (!task.IsCompleted)
                        {
                            var dismissed = _dialogs.ShowCustomAsync("Exporting captured inventory", new TextBlock
                            { Text = "Writing a complete new inventory. Cancel requests a stop and waits for outstanding work to release its private staging.", TextWrapping = TextWrapping.Wrap },
                                [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true)], closer: closer);
                            if (await Task.WhenAny(task, dismissed) == dismissed) cancel.Cancel();
                        }
                        var receipt = await task;
                        return "Exported entire captured inventory as " + AnalysisNames.Display(receipt.Name, 240) + " · " + receipt.Bytes.ToString("N0") + " bytes. The tab's named-save and history state did not change.";
                    }
                    finally { closer.Close(null); }
                    async Task<AnalysisExportReceipt> WriteAsync()
                    {
                        AnalysisExportReceipt? published = null;
                        try
                        {
                            await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, cancel.Token);
                            published = await export(prepared, folder, name, cancel.Token); return published;
                        }
                        catch (Exception error) when (published is not null && error is not AnalysisExportUncertainException)
                        { throw new AnalysisExportUncertainException(name, error); }
                    }
                }
                catch (AnalysisExportTargetExistsException)
                { if (!await ReviewExistingExternalExportAsync(_dialogs, name, cancel.Token)) return null; }
            }
        }
        catch (OperationCanceledException) { return "Export canceled. No successful full output is claimed."; }
        catch (AnalysisExportUncertainException error)
        { rememberFailure(error); return "Output publication or release is uncertain. Keep existing files and inspect the destination before choosing a new name."; }
        catch (AggregateException error)
        { rememberFailure(new AnalysisCloseUnconfirmedException(error)); return "Output or picker cleanup is unconfirmed. Inspect the destination before starting another export."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException)
        { rememberFailure(error); return "Inventory was not exported: " + AnalysisNames.Display(error.Message, 600); }
    }
    private static string ExportObservation(SessionIdentity identity, NamespaceCoverage coverage, AnalysisTaint taint) =>
        "Observed " + identity.StartedUtc.ToString("u") + " – " + identity.ObservedUtc.ToString("u") + ". " +
        (coverage == NamespaceCoverage.CompleteUnderPolicy ? "Traversal complete under captured policy." : "Partial observations remain partial.") +
        (taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? " Includes privileged-derived observations." : "");
    private sealed partial class SessionView
    {
        private readonly Button _exportExternal = new() { Content = "Export entire inventory…" };
        private async Task ExportExternalAsync()
        {
            if (_disposed || _persistenceBusy || !Model.CanSave || Model.HistoryServices is not { } services) return;
            using var actual = _actualWork.Enter(); _persistenceBusy = true; Model.Interaction(true); Refresh();
            try
            {
                using var revision = Model.CaptureDisplayedRevision(); var source = revision.Snapshot;
                var status = await _window.ExportExternalInventoryAsync(services, ExportObservation(source.Identity, source.Coverage, source.Taint), Model.HistoryLifetime,
                    (prepared, folder, name, token) => prepared.ExportInventoryAsync(source, folder, name, token), _actualWork.RecordUnconfirmedClose);
                if (!_disposed && status is not null) Model.SetStatus(status);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException)
            { _actualWork.RecordUnconfirmedClose(error); if (!_disposed) Model.SetStatus("Export or source release is unconfirmed: " + AnalysisNames.Display(error.Message, 400)); }
            finally { _persistenceBusy = false; Model.Interaction(false); if (!_disposed) Refresh(); }
        }
    }
    private sealed partial class IndexedSessionView
    {
        private readonly Button _exportExternal = new() { Content = "Export entire inventory…" };
        private async Task ExportExternalAsync()
        {
            if (_disposed || _persistenceBusy || !HasInventoryStore) return;
            using var actual = _actualWork.Enter(); _persistenceBusy = true; Interaction(true); RefreshIndexedPersistence();
            try
            {
                await using var source = await _source.RetainAsync(_selectionLifetime.Token); var descriptor = source.Descriptor;
                var status = await _window.ExportExternalInventoryAsync(_services!, ExportObservation(descriptor.Identity, descriptor.Coverage, descriptor.Taint), _selectionLifetime.Token,
                    (prepared, folder, name, token) => prepared.ExportInventoryAsync(source, folder, name, token), RememberViewCloseFailure);
                if (!_disposed && status is not null) PersistenceStatus = status;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException)
            { RememberViewCloseFailure(error); if (!_disposed) PersistenceStatus = "Export or source release is unconfirmed: " + AnalysisNames.Display(error.Message, 400); }
            finally { _persistenceBusy = false; Interaction(false); if (!_disposed) RefreshIndexedPersistence(); }
        }
    }
}
