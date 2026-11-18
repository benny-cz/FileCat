using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static string? IndexedComparisonNameError(string? name)
    {
        try { AnalysisPrivateInventoryStore.ValidateComparisonName(name ?? ""); return null; }
        catch (ArgumentException) { return "Use a portable filename ending in .fcdsc (up to 120 characters)."; }
    }

    private async Task<T> IndexedComparisonProgressAsync<T>(string title, Func<CancellationToken, Task<T>> run, CancellationToken lifetime,
        string detail = "Working with captured observations. Cancel requests a stop and waits for outstanding work to finish.")
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var work = run(cancel.Token); if (work.IsCompleted) return await work;
        var closer = new DialogCloser();
        using var close = cancel.Token.Register(() => Dispatcher.UIThread.Post(() => closer.Close(false)));
        var dismissed = _dialogs.ShowCustomAsync(title,
            new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap },
            [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true)], closer: closer);
        try { if (await Task.WhenAny(work, dismissed) == dismissed && await dismissed is false) cancel.Cancel(); return await work; }
        finally { closer.Close(null); }
    }

    private async Task OpenIndexedComparisonAsync(AppServices services, CancellationToken lifetime, AnalysisViewWork owner)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime, _indexedLifetime.Token);
        var browsing = new AnalysisViewWork(); bool closed = false, busy = false;
        var closer = new DialogCloser();
        using var close = cancel.Token.Register(() => Dispatcher.UIThread.Post(() => closer.Close(false)));
        try
        {
            var name = new TextBox { PlaceholderText = "Comparison.fcdsc", MaxLength = 120, MinWidth = 220 };
            AutomationProperties.SetName(name, "Saved comparison filename");
            var choices = new ComboBox { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 600 };
            AutomationProperties.SetName(choices, "Saved comparisons");
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            choices.SelectionChanged += (_, _) => { if (choices.SelectedItem is string selected) name.Text = selected; };
            name.TextChanged += (_, _) => error.Text = string.IsNullOrEmpty(name.Text) ? "" : IndexedComparisonNameError(name.Text);
            var browse = new Button { Content = "Browse saved comparisons" };
            browse.Click += async (_, _) =>
            {
                if (closed || busy) return; using var actual = browsing.Enter(); busy = true; browse.IsEnabled = false;
                try
                {
                    await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, cancel.Token, indexedStorage: false);
                    var names = await prepared.ReadComparisonNamesAsync(cancel.Token);
                    if (closed) return;
                    choices.ItemsSource = names; choices.IsVisible = true; status.Text = names.Length.ToString("N0") + " saved comparisons.";
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) when (IndexedExpectedError(exception))
                { browsing.RecordUnconfirmedClose(exception); if (!closed) status.Text = "Comparison names unavailable: " + AnalysisNames.Display(exception.Message, 400); }
                finally { busy = false; if (!closed) browse.IsEnabled = true; }
            };
            var body = new StackPanel { Spacing = 8, Children =
            {
                new TextBlock { Text = "Open both complete captured inventories, root mapping and the applied comparison view from this profile. Recorded locations stay offline. Corrupt or unsupported bundles are kept and refused.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "One indexed comparison can be open at a time. Close the open comparison and wait for its work to finish before opening another. Browsing and deleting saved copies remain available.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Saved comparisons folder: " + AnalysisNames.Display(services.Paths.AnalysisSnapshotsDirectory, 1000), TextWrapping = TextWrapping.Wrap },
                browse, status, choices, name, error
            } };
            var accepted = await _dialogs.ShowCustomAsync("Open saved comparison", body,
                [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Open comparison observations", true)
                    { HelpText = "Close the open indexed comparison before opening another; saved-copy maintenance remains available.",
                      IsAvailable = () => !busy && !browsing.HasUnconfirmedClose && CanAddSession && AnalysisIndexedComparison.IndexCapacityRefusal is null && IndexedComparisonNameError(name.Text) is null },
                 new DialogButton("Delete selected comparison…", SavedAnalysisMaintenanceAction.Delete)
                    { HelpText = "Review removal of the selected private comparison copy. Open observations and source files are kept.",
                      IsAvailable = () => !busy && !browsing.HasUnconfirmedClose && choices.SelectedItem is string selected && selected == name.Text }], name, closer: closer);
            closed = true; cancel.Token.ThrowIfCancellationRequested();
            if (accepted is SavedAnalysisMaintenanceAction.Delete && choices.SelectedItem is string selectedName && selectedName == name.Text)
            {
                await browsing.CloseAsync();
                await DeleteIndexedComparisonAsync(services, selectedName, owner, cancel.Token); return;
            }
            if (accepted is not true) return;
            await IndexedComparisonProgressAsync("Opening comparison", async token =>
            { await AddSavedIndexedComparisonSessionAsync(services, name.Text!, token); return true; }, cancel.Token);
        }
        finally { closed = true; cancel.Cancel(); await browsing.CloseAsync(); }
    }

    private async Task DeleteIndexedComparisonAsync(AppServices services, string name, AnalysisViewWork owner, CancellationToken cancellation)
    {
        try
        {
            var captured = await IndexedComparisonProgressAsync("Checking saved comparison",
                token => AnalysisOwnedStoreAccess.PrepareComparisonAsync(services, name, owner, token), cancellation);
            OwnedAnalysisComparisonDeletionOutcome outcome;
            try
            {
                if (!await SavedComparisonMaintenanceDialog.ConfirmAsync(_dialogs, captured.Review, services.Paths.AnalysisSnapshotsDirectory, cancellation)) return;
                cancellation.ThrowIfCancellationRequested();
                outcome = await IndexedComparisonProgressAsync("Deleting saved comparison", captured.DeleteAsync, cancellation);
            }
            finally { await captured.DisposeAsync(); }
            cancellation.ThrowIfCancellationRequested();
            await ShowComparisonMaintenanceOutcomeAsync("Saved comparison", outcome.Detail, cancellation);
        }
        catch (OwnedAnalysisComparisonDeletionException error)
        {
            owner.RecordUnconfirmedClose(error);
            if (!cancellation.IsCancellationRequested) await ShowComparisonMaintenanceOutcomeAsync("Saved comparison removal", error.Outcome.Detail, cancellation);
            throw;
        }
    }
    private async Task ShowComparisonMaintenanceOutcomeAsync(string title, string detail, CancellationToken cancellation)
    {
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Dispatcher.UIThread.Post(() => closer.Close(false)));
        await _dialogs.ShowCustomAsync(title, new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap },
            [new DialogButton("OK", true, IsDefault: true, IsCancel: true)], closer: closer);
    }

    private sealed partial class IndexedComparisonView : IAnalysisSavedReferenceTarget
    {
        private AnalysisComparisonView? _savedComparisonView;
        private Guid? _savedComparisonId;
        private string? _savedComparisonName;
        internal void RememberSavedComparison(Guid id, string name, AnalysisComparisonView view)
        { _savedComparisonId = id; _savedComparisonName = name; _savedComparisonView = view; }
        void IAnalysisSavedReferenceTarget.ForgetSavedReference(Guid id, string name, bool comparison)
        {
            if (_disposed || !comparison || _savedComparisonId != id || !string.Equals(_savedComparisonName, name, StringComparison.OrdinalIgnoreCase)) return;
            _savedComparisonId = null; _savedComparisonName = null; _savedComparisonView = null;
            _status.Text = "These comparison observations no longer have a confirmed saved copy. Save a new copy to keep them.";
        }
        private bool ComparisonDraftChanged() => (_queryText.Text ?? "") != _applied.Query || _querySide.SelectedIndex != (int)_applied.QuerySide ||
            _cohort.SelectedIndex != (int)_applied.Cohort || _order.SelectedIndex != (int)_applied.Order ||
            (_case.IsChecked == true) != _applied.CaseSensitive || (_uncertain.IsChecked == true) != _applied.ShowUnknown ||
            _chartChoice.SelectedIndex != _chartMode || _chartMode != 0 && _chartSide.SelectedIndex != (_chartOwner?.Previous == true ? 1 : 0);
        private Button _saveBundle = null!, _exportBundle = null!, _openBundle = null!;
        private void AddBundleControls()
        {
            _services?.AnalysisSavedReferences.Track(this);
            _saveBundle = Button("Save named comparison…", () => _ = SaveBundleAsync(), "Save both complete captured inventories, their root mapping and the applied comparison view. Names and metadata are sensitive. Existing saved files are kept.");
            _exportBundle = Button("Export comparison bundle…", () => _ = ExportBundleAsync(), "Export both complete captured inventories and the applied comparison view to a new .fcdsc file. The current filter does not reduce the saved data.");
            _openBundle = Button("Open comparison…", () => _ = RunAsync("Opening saved comparison…", async token =>
            { if (_services is { } services) await _window.OpenIndexedComparisonAsync(services, token, _work); }), "Open or manage a saved comparison from this profile. Both inputs stay offline.");
            Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { _saveBundle, _exportBundle, _openBundle } });
        }
        private AnalysisComparisonView CaptureAppliedComparisonView()
        {
            return _applied with { Chart = (AnalysisComparisonChartMode)_chartMode, PreviousChart = _chartOwner?.Previous == true,
                ChartScope = _chartProjection?.Scope ?? AnalysisTree.RootKey, FocusNode = _focusedPath, PreviousFocus = _focusedPath != 0 && _previousFocus };
        }
        private Task SaveBundleAsync() => RunAsync("Saving captured comparison…", async token =>
        {
            if (_result is null || _services is not { } services) return;
            var savedView = CaptureAppliedComparisonView();
            await using var captured = await _result.CaptureBundleAsync(savedView, _previousLabel, _previousRootIds, _currentRootIds, token);
            var name = new TextBox { Text = "Comparison " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture) + ".fcdsc", MaxLength = 120 };
            AutomationProperties.SetName(name, "Comparison filename"); var error = Text("");
            name.TextChanged += (_, _) => error.Text = IndexedComparisonNameError(name.Text) ?? "";
            var body = new StackPanel { Spacing = 8, Children =
            {
                Text("Save both entire captured inventories, including exact names, locations, identities and metadata. The applied filter and current table page do not limit this save. File contents, marks, source connections and approvals are excluded."),
                Text("Saved comparisons folder: " + AnalysisNames.Display(services.Paths.AnalysisSnapshotsDirectory, 1000)), name, error
            } };
            _reviewCloser = new DialogCloser();
            try
            {
                var accepted = await _window._dialogs.ShowCustomAsync("Save named comparison", body,
                    [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Save new comparison", true)
                        { IsAvailable = () => IndexedComparisonNameError(name.Text) is null }], name, closer: _reviewCloser);
                token.ThrowIfCancellationRequested(); if (accepted is not true) return;
                var receipt = await _window.IndexedComparisonProgressAsync("Saving comparison", async stop =>
                {
                    AnalysisComparisonReceipt? saved = null;
                    try
                    {
                        await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, stop, indexedStorage: false);
                        saved = await prepared.SaveComparisonAsync(captured, name.Text!, stop); return saved;
                    }
                    catch (Exception exception) when (saved is not null) { throw new AnalysisPublicationUncertainException(saved.Name, exception); }
                }, token);
                if (!_disposed)
                {
                    RememberSavedComparison(receipt.ComparisonId, receipt.Name, savedView);
                    _status.Text = "Saved " + AnalysisNames.Display(receipt.Name, 240) + ". Both complete captured inventories and the applied comparison view were saved.";
                }
            }
            catch (AnalysisExportTargetExistsException) { if (!_disposed) _status.Text = "That saved comparison already exists and was kept. Choose another name to save a new copy."; }
            catch (AnalysisPublicationUncertainException exception)
            { _work.RecordUnconfirmedClose(exception); if (!_disposed) _status.Text = "The comparison name was published, but durability is uncertain. Keep that file and inspect it before retrying."; }
            finally { _reviewCloser = null; }
        });
        private Task ExportBundleAsync() => RunAsync("Exporting captured comparison…", async token =>
        {
            if (_result is null || _services is not { } services) return;
            await using var captured = await _result.CaptureBundleAsync(CaptureAppliedComparisonView(), _previousLabel, _previousRootIds, _currentRootIds, token);
            var status = await _window.ExportExternalInventoryAsync(services,
                "Previous: " + ExportObservation(_previousSource.Descriptor.Identity, _previousSource.Descriptor.Coverage, _result.Taint) +
                "\nCurrent: " + ExportObservation(_currentSource.Descriptor.Identity, _currentSource.Descriptor.Coverage, _result.Taint), token,
                (prepared, folder, name, stop) => prepared.ExportComparisonAsync(captured, folder, name, stop), _work.RecordUnconfirmedClose,
                new ReportOptions(AnalysisReportFormat.Markdown, AnalysisReportScope.EntireResult, false) { ComparisonBundle = true });
            if (!_disposed && status is not null) _status.Text = status;
        });
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _openIndexedComparison = new() { Content = "Open comparison…" };
        private async Task OpenIndexedComparisonSavedAsync()
        {
            if (_disposed || _persistenceBusy || !HasInventoryStore) return;
            using var actual = _actualWork.Enter(); _persistenceBusy = true; RefreshIndexedPersistence();
            try { await _window.OpenIndexedComparisonAsync(_services!, _selectionLifetime.Token, _actualWork); }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (Expected(exception))
            { RememberViewCloseFailure(exception); if (!_disposed) _status.Text = "Comparison was not opened: " + AnalysisNames.Display(exception.Message, 400); }
            finally { _persistenceBusy = false; if (!_disposed) RefreshIndexedPersistence(); }
        }
    }
}
