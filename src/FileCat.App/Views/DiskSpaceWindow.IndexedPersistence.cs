using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedSessionView : IAnalysisSavedReferenceTarget
    {
        private readonly AppServices? _services;
        private readonly Button _saveInventory = new() { Content = "Save named analysis" }, _openInventory = new() { Content = "Open saved analysis" };
        private bool _persistenceBusy, _revisionSaved;
        private string? _savedInventoryName;
        private Guid? _savedInventoryId;
        private string? _persistenceNotice;
        private string? PersistenceStatus { set { _persistenceNotice = value; _status.Text = value; } }
        private void AppendPersistenceNotice() { if (_persistenceNotice is { } notice) _status.Text += "\n" + notice; }
        internal void RememberOpenedInventory(AnalysisInventoryReceipt receipt)
        { _savedInventoryName = receipt.Name; _savedInventoryId = receipt.InventoryId; _revisionSaved = true; }
        private CancellationTokenSource? _inventoryCancel;
        private DialogCloser? _inventoryCloser;
        private bool HasInventoryStore => _services is not null && !_services.Io.IsStopped && !_actualWork.HasUnconfirmedClose;
        private void AddIndexedPersistenceControls(WrapPanel bar)
        {
            _services?.AnalysisSavedReferences.Track(this);
            bar.Children.Add(_saveInventory); bar.Children.Add(_openInventory);
            _importExternal.IsVisible = _services is not null; bar.Children.Add(_importExternal);
            _importExternal.Click += async (_, _) => await ImportExternalAsync();
            ToolTip.SetTip(_importExternal, new RichTip("Import offline observations", detail: "Choose a local .fcdsa file. Imported paths stay offline and cannot grant current-source or file-action authority."));
            _exportExternal.IsVisible = _services is not null; bar.Children.Add(_exportExternal); _exportExternal.Click += async (_, _) => await ExportExternalAsync();
            ToolTip.SetTip(_exportExternal, new RichTip("Export entire captured inventory", detail: "Choose a folder and unused filename. The sensitivity review explains that filters and chart scope do not reduce the complete inventory."));
            var history = new Button { Content = "History…", IsEnabled = _services is not null };
            history.Click += async (_, _) => await ShowHistoryAsync(); bar.Children.Add(history);
            _saveInventory.Click += async (_, _) => await SaveInventoryAsync(); _openInventory.Click += async (_, _) => await OpenInventoryAsync();
            ToolTip.SetTip(_saveInventory, new RichTip("Save the whole captured inventory", detail: "Save all recorded paths and metadata without file contents. The display filter does not limit the inventory. Current display settings are retained; marks and the basket are not saved."));
            ToolTip.SetTip(_openInventory, new RichTip("Open captured observations", detail: "Open a named inventory from this FileCat profile. Its recorded locations are not reconnected and it grants no file actions."));
            RefreshIndexedPersistence();
        }
        private void RefreshIndexedPersistence()
        {
            if (_disposed) return;
            _saveInventory.IsEnabled = HasInventoryStore && !_persistenceBusy;
            _openInventory.IsEnabled = HasInventoryStore && !_persistenceBusy;
            _importExternal.IsEnabled = HasInventoryStore && !_persistenceBusy;
            _exportExternal.IsEnabled = HasInventoryStore && !_persistenceBusy;
            string reason = HasInventoryStore ? "" : "This view has no available private inventory store. Open it from a FileCat profile to save named analyses.";
            AutomationProperties.SetHelpText(_saveInventory, reason); AutomationProperties.SetHelpText(_openInventory, reason);
        }
        void IAnalysisSavedReferenceTarget.ForgetSavedReference(Guid id, string name, bool comparison)
        {
            if (_disposed || comparison || _savedInventoryId != id || !string.Equals(_savedInventoryName, name, StringComparison.OrdinalIgnoreCase)) return;
            _savedInventoryId = null; _savedInventoryName = null; _revisionSaved = false;
            _status.Text = "These observations no longer have a confirmed saved copy. Save a new copy to keep them.";
            RefreshIndexedPersistence();
        }
        private AnalysisSavedView CapturePublishedView()
        {
            var filter = _grouping?.Filter?.Request ?? _explorer.Filter?.Request ?? new("");
            var metric = _grouping?.Metric ?? _explorer.Metric;
            var dimension = _grouping?.Dimension ?? AnalysisGroupingDimension.FolderHierarchy;
            var sort = _groupPage?.Sort ?? IndexedSavedSort(_shownPage?.Sort ?? AnalysisHierarchySort.Metric);
            bool descending = _groupPage?.Descending ?? _shownPage?.Descending ?? true;
            string scope = _grouping is null ? _explorer.OtherScopeKey ?? (_scope == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(_scope)) : _grouping.OtherScopeKey ?? _groupKey ?? AnalysisTree.RootKey;
            return new(metric, _chartMode.SelectedIndex == 1 ? "Sunburst" : _chartMode.SelectedIndex == 2 ? "Table" : "Treemap", filter.Text, filter.RootId,
                scope, filter.CaseSensitive, filter.UnknownOnly, sort, descending)
            { Grouping = dimension, ColorDimension = (AnalysisColorDimension)_color.SelectedIndex, RootDisplayIds = filter.RootDisplayIds };
        }
        private static AnalysisTableSort IndexedSavedSort(AnalysisHierarchySort sort) => sort switch
        {
            AnalysisHierarchySort.Metric => AnalysisTableSort.ActiveMetric, AnalysisHierarchySort.Files => AnalysisTableSort.FilePaths,
            AnalysisHierarchySort.FileSize => AnalysisTableSort.FileSize, AnalysisHierarchySort.Name => AnalysisTableSort.Name,
            AnalysisHierarchySort.MetadataState => AnalysisTableSort.MeasurementState, _ => throw new InvalidOperationException("Unsupported displayed table order.")
        };
        private static string? InventoryFilenameError(string? name)
        {
            try { AnalysisPrivateInventoryStore.ValidateName(name ?? ""); return null; }
            catch (ArgumentException) { return "Use a portable filename ending in .fcdsa (up to 120 characters)."; }
        }
        private async Task SaveInventoryAsync()
        {
            if (_disposed || _persistenceBusy || !HasInventoryStore) return;
            _persistenceNotice = null;
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token);
            _inventoryCancel = cancel; _persistenceBusy = true; Interaction(true); RefreshIndexedPersistence();
            try
            {
                var view = CapturePublishedView(); await using var captured = await _source.RetainAsync(cancel.Token);
                var name = new TextBox { Text = "Analysis " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture) + ".fcdsa", MaxLength = 120 };
                AutomationProperties.SetName(name, "Inventory filename"); var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var body = new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = "Save the entire captured inventory, including paths, identities and metadata. File contents are excluded. The display filter and current table page do not limit this save. Marks and the review basket are not saved.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Saved analyses folder: " + AnalysisNames.Display(_services!.Paths.AnalysisSnapshotsDirectory) + "\nObserved " + captured.Descriptor.Identity.StartedUtc.ToString("u") + " – " + captured.Descriptor.Identity.ObservedUtc.ToString("u") +
                        (captured.Descriptor.Coverage == NamespaceCoverage.CompleteUnderPolicy ? " · complete under the recorded scan policy." : " · partial observations remain partial."), TextWrapping = TextWrapping.Wrap }, name, error
                } };
                name.TextChanged += (_, _) => error.Text = string.IsNullOrEmpty(name.Text) ? "" : InventoryFilenameError(name.Text);
                var closer = new DialogCloser(); _inventoryCloser = closer;
                var result = await _window._dialogs.ShowCustomAsync("Save named analysis", body,
                    [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Save new inventory", true) { IsAvailable = () => InventoryFilenameError(name.Text) is null }], name, closer: closer);
                if (result is not true || _disposed) return;
                var existing = await InventoryProgressAsync("Checking inventory name", token => AnalysisOwnedStoreAccess.FindReplacementTargetAsync(_services!, name.Text!, token), cancel);
                if (_disposed) return;
                if (existing is not null) { await ReplaceInventoryAsync(existing.InventoryId, captured, view, cancel); return; }
                var receipt = await InventoryProgressAsync("Saving inventory", async token =>
                {
                    AnalysisInventoryReceipt? saved = null;
                    try
                    {
                        await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(_services!, token, indexedStorage: false);
                        saved = await prepared.SaveInventoryAsync(captured, name.Text!, view, token); return saved;
                    }
                    catch (Exception error) when (saved is not null)
                    { throw new AnalysisPublicationUncertainException(saved.Name, error); }
                }, cancel);
                _revisionSaved = true; _savedInventoryName = receipt.Name; _savedInventoryId = receipt.InventoryId;
                if (!_disposed) _status.Text = "Saved " + AnalysisNames.Display(receipt.Name, 240) + ". The whole captured inventory and its displayed settings were saved.";
            }
            catch (AnalysisCatalogUpdateException error)
            {
                RememberViewCloseFailure(error); _revisionSaved = true; _savedInventoryName = error.Receipt.Name; _savedInventoryId = error.Receipt.InventoryId;
                if (!_disposed) _status.Text = "Saved " + AnalysisNames.Display(error.Receipt.Name, 240) + ", but its catalog entry is incomplete. Keep the file and open it by filename.";
            }
            catch (AnalysisPublicationUncertainException error)
            { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "The inventory name was published, but durability is uncertain. Keep that file and inspect it before retrying."; }
            catch (OperationCanceledException) { if (!_disposed) _status.Text = "Inventory save canceled. No successful save is claimed."; }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "Inventory was not saved: " + AnalysisNames.Display(error.Message, 400); }
            finally
            {
                if (ReferenceEquals(_inventoryCancel, cancel)) _inventoryCancel = null;
                _inventoryCloser = null; _persistenceBusy = false; Interaction(false); RefreshIndexedPersistence();
            }
        }
        private sealed record InventoryChoice(string Name, AnalysisSavedEntry? Entry, AnalysisPendingDeletion? Pending = null, AnalysisPendingReplacement? Replacement = null)
        { public override string ToString() => AnalysisNames.Display(Name, 200) + (Replacement is not null ? " · replacement pending · review remaining versions" : Pending is not null ? " · pending removal · " + (Pending.PayloadPresent ? "saved file present" : "saved file absent") : Entry is null ? " · unindexed" : " · " + Entry.SavedUtc.ToLocalTime().ToString("g")); }
        private async Task<AnalysisCatalogSnapshot> ReadInventoryCatalogAsync(CancellationToken cancellation)
        {
            AnalysisCatalogSnapshot? result = null;
            try
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(_services!, cancellation, indexedStorage: false);
                result = await prepared.ReadCatalogAsync(cancellation); return result;
            }
            catch { result?.Dispose(); throw; }
        }
        private async Task OpenInventoryAsync()
        {
            if (_disposed || _persistenceBusy || !HasInventoryStore) return;
            _persistenceNotice = null;
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token);
            _persistenceBusy = true; _inventoryCancel = cancel; RefreshIndexedPersistence();
            AnalysisCatalogSnapshot? catalog = null; bool closed = false, loading = false;
            SavedAnalysisInspectionPanel? inspection = null;
            try
            {
                var name = new TextBox { Text = _savedInventoryName ?? "", MaxLength = 120, PlaceholderText = "Saved analysis.fcdsa" };
                AutomationProperties.SetName(name, "Saved inventory filename");
                var choices = new ComboBox { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 600 };
                AutomationProperties.SetName(choices, "Saved analyses"); var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var browse = new Button { Content = "Browse saved analyses" }; var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                inspection = new(_services!.Paths.AnalysisSnapshotsDirectory, () => choices.SelectedItem is InventoryChoice selected && selected.Name == name.Text && selected.Replacement is null ? selected.Entry : null,
                    (id, token) => AnalysisOwnedStoreAccess.InspectAsync(_services!, id, token), cancel.Token, RememberViewCloseFailure);
                choices.SelectionChanged += (_, _) => { if (choices.SelectedItem is InventoryChoice choice) name.Text = choice.Name; inspection.SelectionChanged(); };
                browse.Click += async (_, _) =>
                {
                    if (loading || closed || _disposed) return; using var page = _actualWork.Enter(); loading = true; browse.IsEnabled = false;
                    try
                    {
                        var data = await ReadInventoryCatalogAsync(cancel.Token);
                        if (closed || _disposed) { data.Dispose(); return; }
                        catalog?.Dispose(); catalog = data;
                        choices.ItemsSource = data.Entries.Where(entry => !data.PendingReplacements.Any(p => p.IncomingId == entry.InventoryId)).Select(entry => new InventoryChoice(entry.Name, entry))
                            .Concat(data.PendingDeletions.Select(p => new InventoryChoice(p.Entry.Name, p.Entry, p)))
                            .Concat(data.PendingReplacements.Select(p => new InventoryChoice(p.Previous.Name, null, Replacement: p)))
                            .Concat(data.UnindexedNames.Select(text => new InventoryChoice(text, null))).ToArray();
                        choices.IsVisible = true; message.Text = $"{data.Entries.Length:N0} named analyses · {data.PendingDeletions.Length:N0} pending removals · {data.PendingReplacements.Length:N0} pending replacements · {data.UnindexedNames.Length:N0} unindexed files.";
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception exception) when (Expected(exception)) { RememberViewCloseFailure(exception); if (!closed && !_disposed) message.Text = "Catalog unavailable: " + AnalysisNames.Display(exception.Message, 400); }
                    finally { loading = false; if (!closed && !_disposed) browse.IsEnabled = true; }
                };
                name.TextChanged += (_, _) => { error.Text = string.IsNullOrEmpty(name.Text) ? "" : InventoryFilenameError(name.Text); inspection.SelectionChanged(); };
                var body = new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = "Select an analysis from the private catalog to restore its display settings. Entering a filename alone uses the default view. Recorded locations are not reconnected and no file actions are granted.", TextWrapping = TextWrapping.Wrap },
                    browse, message, choices, name, inspection, error
                } };
                var closer = new DialogCloser(); _inventoryCloser = closer;
                InventoryChoice? SelectedChoice() => choices.SelectedItem is InventoryChoice selected && selected.Name == name.Text ? selected : null;
                var result = await _window._dialogs.ShowCustomAsync("Open saved analysis", body,
                    [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Open inventory", true) { IsAvailable = () => InventoryFilenameError(name.Text) is null && !loading && SelectedChoice()?.Replacement is null && _window.CanAddSession },
                    new DialogButton("Delete selected analysis…", SavedAnalysisMaintenanceAction.Delete) { IsAvailable = () => !loading && SelectedChoice()?.Entry is not null },
                    new DialogButton("Restore pending analysis…", SavedAnalysisMaintenanceAction.Restore) { IsAvailable = () => !loading && SelectedChoice()?.Pending?.PayloadPresent == true },
                    new DialogButton("Review pending replacement…", SavedAnalysisMaintenanceAction.Replacement) { IsAvailable = () => !loading && SelectedChoice()?.Replacement is not null }], name, closer: closer);
                closed = true; if (_disposed) return;
                if (result is SavedAnalysisMaintenanceAction.Replacement && SelectedChoice()?.Replacement is { } replacement)
                {
                    catalog?.Dispose(); catalog = null;
                    if (inspection is not null) { await inspection.CloseAsync(); inspection = null; }
                    await RecoverSavedAsync(replacement.OperationId, cancel); return;
                }
                if (result is SavedAnalysisMaintenanceAction maintenance && SelectedChoice()?.Entry is { } maintained)
                {
                    catalog?.Dispose(); catalog = null;
                    if (inspection is not null) { await inspection.CloseAsync(); inspection = null; }
                    await MaintainSavedAsync(maintained.InventoryId, maintenance == SavedAnalysisMaintenanceAction.Restore, cancel); return;
                }
                if (result is not true) return;
                string filename = name.Text!; var entry = choices.SelectedItem is InventoryChoice selected && selected.Name == filename ? selected.Entry : null;
                await InventoryProgressAsync("Opening inventory", token => _window.AddSavedIndexedSessionAsync(_services!, filename, entry, token), cancel);
            }
            catch (OperationCanceledException) { if (!_disposed) _status.Text = "Inventory open canceled."; }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "Inventory was not opened: " + AnalysisNames.Display(error.Message, 400); }
            finally
            {
                closed = true; cancel.Cancel(); catalog?.Dispose(); _persistenceBusy = false; _inventoryCloser = null;
                if (inspection is not null) await inspection.CloseAsync();
                if (ReferenceEquals(_inventoryCancel, cancel)) _inventoryCancel = null; RefreshIndexedPersistence();
            }
        }
        private async Task<T> InventoryProgressAsync<T>(string title, Func<CancellationToken, Task<T>> run, CancellationTokenSource cancel)
        {
            var work = run(cancel.Token); if (work.IsCompleted) return await work;
            var closer = new DialogCloser(); _inventoryCloser = closer;
            var dismissed = _window._dialogs.ShowCustomAsync(title, new TextBlock
                { Text = "Working in the private inventory store. Cancel requests a stop; outstanding native work remains owned until it returns.", TextWrapping = TextWrapping.Wrap },
                [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true)], closer: closer);
            try { if (await Task.WhenAny(work, dismissed) == dismissed) cancel.Cancel(); return await work; }
            finally { closer.Close(null); }
        }
    }
}
