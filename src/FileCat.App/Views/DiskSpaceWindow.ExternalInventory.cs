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
    internal async Task<DiskSpaceViewModel> AddExternalComparisonSessionAsync(AppServices services, string path, CancellationToken cancellation)
    {
        Avalonia.Threading.Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_closed, this);
        AnalysisInventoryInput.ValidateChosenPath(path, comparison: true);
        AnalysisSessionAdmission.Slot? admission = _admissionPool.Acquire(); DiskSpaceViewModel? model = null; Exception? unconfirmed = null;
        if (_pendingIndexed++ == 0) _indexedIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _indexedLifetime.Token);
        try
        {
            model = await DiskSpaceViewModel.ImportExternalComparisonAsync(services, path, linked.Token);
            linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
            var slot = admission!; admission = null; AddReservedSession(model, [], slot);
            var success = model; model = null; return success;
        }
        catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { unconfirmed = error; RecordIndexedCloseFailure(error); throw; }
        finally
        {
            try
            {
                Task close = CloseImportedAsync();
                try { if (admission is not null) await admission.CloseAfterAsync(close); else await close; }
                catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { RecordIndexedCloseFailure(error); throw; }
            }
            finally { if (--_pendingIndexed == 0) { _indexedIdle.TrySetResult(); if (_closed) _indexedLifetime.Dispose(); } }
        }
        async Task CloseImportedAsync()
        {
            Exception? failure = unconfirmed;
            try { model?.Dispose(); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            try { if (model is not null) await model.CloseCompletion; } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null) throw AnalysisCloseUnconfirmedException.From(failure);
        }
    }
    internal Task<AnalysisDatasetExplorer> AddExternalIndexedSessionAsync(AppServices services, string path, CancellationToken cancellation)
    {
        AnalysisInventoryInput.ValidateChosenPath(path);
        return AddPreparedIndexedSessionAsync(Load, AnalysisMetric.OnDisk, null, null, cancellation, services,
            historyNotice: "Imported inventory · offline observations. Recorded locations stay disconnected; source actions require a separate current-source binding and review.");
        async Task<AnalysisDatasetLease> Load(CancellationToken token)
        {
            AnalysisDatasetLease? retained = null;
            try
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, token);
                await using var imported = await prepared.LoadExternalInventoryAsync(path, token);
                retained = await imported.AcquireAsync(token); return retained;
            }
            catch { if (retained is not null) await retained.DisposeAsync(); throw; }
        }
    }
    internal static async Task<bool> ConfirmExternalInventoryAsync(IDialogService dialogs, string path, CancellationToken cancellation, bool comparison = false)
    {
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
        return await dialogs.ShowCustomAsync(comparison ? "Import comparison" : "Import inventory", new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = (comparison ? "Open both captured inventories and the saved comparison view as offline observations:\n" : "Open this inventory as offline observations:\n") + AnalysisNames.Display(path, 4096) +
                "\nRecorded locations are not opened, and imported claims cannot grant file actions. The complete record graph and accounting must validate before a tab opens. Maximum input size: 8 GiB." +
                (comparison ? " Private scratch and profile capacity limits also apply; an input that exceeds them is refused without opening a partial comparison." : "")
        }, [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Import observations", true)], closer: closer) is true;
    }
    private async Task ImportExternalInventoryAsync(AppServices services, CancellationToken lifetime, Action<Exception> rememberFailure)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime, _indexedLifetime.Token);
        try
        {
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            var items = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Import offline analysis", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("FileCat inventory or comparison") { Patterns = ["*.fcdsa", "*.fcdsc"] }] });
            using var picked = new PickedStorageItems(items);
            cancel.Token.ThrowIfCancellationRequested();
            if (items.Count == 0) return;
            string path = items[0].TryGetLocalPath() ?? throw new NotSupportedException("Choose an inventory on a directly accessible local drive.");
            bool comparison = path.EndsWith(".fcdsc", StringComparison.Ordinal);
            AnalysisInventoryInput.ValidateChosenPath(path, comparison);
            if (!await ConfirmExternalInventoryAsync(_dialogs, path, cancel.Token, comparison)) return;
            cancel.Token.ThrowIfCancellationRequested();
            Task task = comparison ? AddExternalIndexedComparisonSessionAsync(services, path, cancel.Token) : AddExternalIndexedSessionAsync(services, path, cancel.Token);
            if (task.IsCompleted) { await task; return; }
            var closer = new DialogCloser();
            using var close = cancel.Token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => closer.Close(false)));
            var dismissed = _dialogs.ShowCustomAsync("Opening imported observations", new TextBlock
                { Text = "Validating the captured observations. Cancel requests a stop and waits for outstanding work to finish.", TextWrapping = TextWrapping.Wrap },
                [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true)], closer: closer);
            try { if (await Task.WhenAny(task, dismissed) == dismissed) cancel.Cancel(); await task; }
            finally { closer.Close(null); }
        }
        catch (OperationCanceledException) { }
        catch (AggregateException error)
        {
            rememberFailure(new AnalysisCloseUnconfirmedException(error));
            if (!cancel.IsCancellationRequested) await _dialogs.AlertAsync("Inventory picker cleanup is unconfirmed", "Any imported tab remains open. A selected picker item could not confirm release; close this analysis before further file operations.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException)
        {
            rememberFailure(error);
            if (!cancel.IsCancellationRequested) await _dialogs.AlertAsync("Analysis was not imported", AnalysisNames.Display(error.Message, 600));
        }
    }
    internal sealed class PickedStorageItems(IReadOnlyList<IDisposable> items) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; var failures = new List<Exception>();
            foreach (var item in items) try { item.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("The inventory picker could not release every selected item.", failures);
        }
    }
    private sealed partial class SessionView
    {
        private readonly Button _importExternal = new() { Content = "Import analysis…" };
        private async Task ImportExternalAsync()
        {
            if (_disposed || _persistenceBusy || Model.HistoryServices is not { } services) return;
            using var actual = _actualWork.Enter(); _persistenceBusy = true; Refresh();
            try { await _window.ImportExternalInventoryAsync(services, Model.HistoryLifetime, _actualWork.RecordUnconfirmedClose); }
            finally { _persistenceBusy = false; if (!_disposed) Refresh(); }
        }
    }
    private sealed partial class IndexedSessionView
    {
        private readonly Button _importExternal = new() { Content = "Import analysis…" };
        private async Task ImportExternalAsync()
        {
            if (_disposed || _persistenceBusy || !HasInventoryStore) return;
            using var actual = _actualWork.Enter(); _persistenceBusy = true; RefreshIndexedPersistence();
            try { await _window.ImportExternalInventoryAsync(_services!, _selectionLifetime.Token, RememberViewCloseFailure); }
            finally { _persistenceBusy = false; if (!_disposed) RefreshIndexedPersistence(); }
        }
    }
}
