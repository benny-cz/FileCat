using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private readonly Button _save, _openSaved, _compare;
        private bool _persistenceBusy;
        private sealed record SavedChoice(string Name, AnalysisSavedEntry? Entry, AnalysisPendingDeletion? Pending = null, AnalysisPendingReplacement? Replacement = null)
        {
            public override string ToString() => AnalysisNames.Display(Name, 240) + (Replacement is not null ? " · replacement pending · review remaining versions" : Pending is not null ? " · pending removal · " + (Pending.PayloadPresent ? "saved file present" : "saved file absent") : Entry is null ? " · unindexed" : " · " + Entry.SavedUtc.ToLocalTime().ToString("g") + " · " + Entry.Bytes.ToString("N0") + " bytes" + (Entry.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "" : " · partial"));
        }

        private async Task SaveAsync()
        {
            if (_disposed || _persistenceBusy || !Model.CanSave) return;
            using var actualWork = _actualWork.Enter();
            _persistenceBusy = true; Model.Interaction(true); Refresh();
            try
            {
                using var revision = Model.CaptureDisplayedRevision();
                var name = new TextBox { Text = "Analysis " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".fcdsa", PlaceholderText = "Analysis name.fcdsa", MaxLength = 120 };
                AutomationProperties.SetName(name, "Inventory filename");
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var body = new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = "Save the entire captured inventory, including paths, identities and observed metadata. File contents are excluded. The display filter does not limit this save.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Saved analyses folder: " + AnalysisNames.Display(Model.SavedDirectory ?? "") + "\nObserved " + revision.Snapshot.Identity.StartedUtc.ToString("u") + " – " + revision.Snapshot.Identity.ObservedUtc.ToString("u") + ". " + (revision.Snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Traversal complete under the captured policy." : "Partial observations remain partial.") + " The live scan can continue.", TextWrapping = TextWrapping.Wrap },
                    name, error
                } };
                name.TextChanged += (_, _) => error.Text = string.IsNullOrEmpty(name.Text) ? "" : InventoryNameError(name.Text);
                AutomationProperties.SetLiveSetting(error, Avalonia.Automation.AutomationLiveSetting.Assertive);
                var result = await _window._dialogs.ShowCustomAsync("Save named analysis", body,
                    [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Save new inventory", true) { IsAvailable = () => InventoryNameError(name.Text) is null }], name);
                if (result is not true || _disposed) return;
                var existing = await WithInventoryProgress("Checking inventory name", token => Model.FindSavedReplacementAsync(name.Text!, token));
                if (_disposed) return;
                if (existing is not null) { await ReplaceSavedAsync(existing.InventoryId, revision); return; }
                var receipt = await WithInventoryProgress("Saving inventory", token => Model.SaveNewAsync(name.Text!, revision, token));
                if (!_disposed) Model.SetStatus("Saved " + AnalysisNames.Display(receipt.Name, 240) + ". " + (revision.Snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Captured traversal complete under policy." : "Partial observations remain partial."));
            }
            catch (OperationCanceledException) { Model.SetStatus("Inventory save canceled. No successful save is claimed."); }
            catch (AnalysisPublicationUncertainException) { Model.SetStatus("The inventory name was published, but durability is uncertain. Keep that file and inspect it before trying another name."); }
            catch (AnalysisCatalogUpdateException ex) { Model.SetStatus("Saved " + AnalysisNames.Display(ex.Receipt.Name, 240) + ", but its catalog entry is incomplete. Keep the file and open it by filename."); }
            catch (Exception ex) when (InventoryError(ex)) { Model.SetStatus("Inventory was not saved: " + AnalysisNames.Display(ex.Message, 400)); }
            finally { _persistenceBusy = false; Model.Interaction(false); Refresh(); }
        }

        private Task CompareAsync() => OpenSavedAsync(comparing: true);
        private async Task OpenSavedAsync(bool comparing = false)
        {
            if (_disposed || _persistenceBusy || !Model.HasPrivateStorage) return;
            if (comparing && !Model.CanCompare) return;
            using var actualWork = _actualWork.Enter();
            if (comparing && !_window.CanAddSession) { _window.ExplainSessionLimit(); return; }
            AnalysisSessionAdmission.Slot? destination = comparing ? _window._admissionPool.Acquire() : null;
            var openingWork = new AnalysisViewWork();
            _persistenceBusy = true; Refresh();
            AnalysisCatalogSnapshot? catalogLease = null; bool closed = false;
            AnalysisSnapshotLease? captured = null; bool interaction = false; var metric = Model.Metric;
            using var browseCancel = new CancellationTokenSource();
            SavedAnalysisInspectionPanel? inspection = null;
            try
            {
                if (comparing) { Model.Interaction(true); interaction = true; captured = Model.CaptureDisplayedRevision(); }
                var name = new TextBox { Text = Model.SavedInventoryName ?? "", PlaceholderText = "Saved analysis.fcdsa", MaxLength = 120 };
                var mapRoots = new CheckBox { Content = "Map recorded roots for structural comparison", IsVisible = comparing };
                AutomationProperties.SetName(mapRoots, "Map recorded roots");
                AutomationProperties.SetName(name, comparing ? "Previous inventory filename" : "Saved inventory filename");
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var catalogStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var choices = new ComboBox { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 600 };
                AutomationProperties.SetName(choices, "Saved analyses"); AutomationProperties.SetLiveSetting(catalogStatus, AutomationLiveSetting.Polite);
                var browse = new Button { Content = "Browse saved analyses" };
                inspection = new(Model.SavedDirectory ?? "", () => choices.SelectedItem is SavedChoice selected && selected.Name == name.Text && selected.Replacement is null ? selected.Entry : null,
                    (id, token) => Model.InspectSavedAsync(id, token), browseCancel.Token, _actualWork.RecordUnconfirmedClose);
                bool browsing = false;
                choices.SelectionChanged += (_, _) => { if (choices.SelectedItem is SavedChoice choice) name.Text = choice.Name; inspection.SelectionChanged(); };
                browse.Click += async (_, _) =>
                {
                    if (_disposed || browsing || closed) return;
                    using var actualBrowse = _actualWork.Enter();
                    using var actualOpening = openingWork.Enter();
                    browsing = true; browse.IsEnabled = false; catalogStatus.Text = "Reading this profile's private catalog…";
                    try
                    {
                        var catalog = await Model.ReadSavedCatalogAsync(browseCancel.Token);
                        if (closed || _disposed) { catalog.Dispose(); return; }
                        catalogLease?.Dispose(); catalogLease = catalog;
                        choices.ItemsSource = catalog.Entries.Where(e => !catalog.PendingReplacements.Any(p => p.IncomingId == e.InventoryId)).Select(e => new SavedChoice(e.Name, e))
                            .Concat(comparing ? Enumerable.Empty<SavedChoice>() : catalog.PendingDeletions.Select(p => new SavedChoice(p.Entry.Name, p.Entry, p)))
                            .Concat(comparing ? Enumerable.Empty<SavedChoice>() : catalog.PendingReplacements.Select(p => new SavedChoice(p.Previous.Name, null, Replacement: p)))
                            .Concat(catalog.UnindexedNames.Select(n => new SavedChoice(n, null))).ToArray();
                        choices.IsVisible = true;
                        catalogStatus.Text = $"{catalog.Entries.Length:N0} named analyses · {catalog.PendingDeletions.Length:N0} pending removals · {catalog.PendingReplacements.Length:N0} pending replacements · {catalog.UnindexedNames.Length:N0} unindexed files. " +
                            (comparing ? "Select the previous inventory. Comparison uses the captured metric and full inventories." : "Select an analysis to restore its saved view.");
                    }
                    catch (OperationCanceledException) { if (!closed && !_disposed) catalogStatus.Text = "Catalog browsing canceled."; }
                    catch (Exception ex) when (InventoryError(ex)) { if (!closed && !_disposed) catalogStatus.Text = "Catalog unavailable: " + AnalysisNames.Display(ex.Message, 400) + " You can still open a known filename."; }
                    finally { browsing = false; if (!closed && !_disposed) browse.IsEnabled = true; }
                };
                AutomationProperties.SetLiveSetting(error, Avalonia.Automation.AutomationLiveSetting.Assertive);
                name.TextChanged += (_, _) => { error.Text = string.IsNullOrEmpty(name.Text) ? "" : InventoryNameError(name.Text); inspection.SelectionChanged(); };
                var body = new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = comparing ? "Compare the entire captured revision with a saved inventory as Previous. The display filter and folder scope do not limit this comparison. Compatibility and missing coverage qualify every difference." : "Browse saved analyses to restore saved display settings, including table order. Entering a .fcdsa filename alone uses the default view. Opening shows offline observations without reconnecting sources or granting file actions.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Saved analyses folder: " + AnalysisNames.Display(Model.SavedDirectory ?? ""), TextWrapping = TextWrapping.Wrap },
                    browse, catalogStatus, choices, name, inspection, error
                } };
                if (captured is not null)
                {
                    body.Children.Insert(1, new TextBlock { Text = "Current metric: " + ComparisonMetric(metric) + " · observed " + captured.Snapshot.Identity.StartedUtc.ToString("u") + " – " + captured.Snapshot.Identity.ObservedUtc.ToString("u") + ". " + (captured.Snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Captured traversal complete under policy." : "Captured observations are partial."), TextWrapping = TextWrapping.Wrap });
                    body.Children.Insert(2, mapRoots);
                }
                SavedChoice? SelectedChoice() => choices.SelectedItem is SavedChoice selected && selected.Name == name.Text ? selected : null;
                var actions = new List<DialogButton>
                {
                    new("Cancel", false, IsCancel: true), new(comparing ? "Compare observations" : "Open observations", true, IsDefault: true)
                    { IsAvailable = () => InventoryNameError(name.Text) is null && SelectedChoice()?.Replacement is null && (comparing ? destination is not null : _window.CanAddSession) }
                };
                if (!comparing)
                {
                    actions.Add(new("Delete selected analysis…", SavedAnalysisMaintenanceAction.Delete) { IsAvailable = () => !browsing && SelectedChoice()?.Entry is not null });
                    actions.Add(new("Restore pending analysis…", SavedAnalysisMaintenanceAction.Restore) { IsAvailable = () => !browsing && SelectedChoice()?.Pending?.PayloadPresent == true });
                    actions.Add(new("Review pending replacement…", SavedAnalysisMaintenanceAction.Replacement) { IsAvailable = () => !browsing && SelectedChoice()?.Replacement is not null });
                }
                var result = await _window._dialogs.ShowCustomAsync(comparing ? "Compare saved analysis" : "Open saved analysis", body, actions, name);
                closed = true;
                if (_disposed) return;
                if (result is SavedAnalysisMaintenanceAction.Replacement && SelectedChoice()?.Replacement is { } replacement)
                {
                    browseCancel.Cancel(); catalogLease?.Dispose(); catalogLease = null;
                    if (inspection is not null) { await inspection.CloseAsync(); inspection = null; }
                    await RecoverSavedAsync(replacement.OperationId); return;
                }
                if (result is SavedAnalysisMaintenanceAction maintenance && SelectedChoice()?.Entry is { } maintained)
                {
                    browseCancel.Cancel(); catalogLease?.Dispose(); catalogLease = null;
                    if (inspection is not null) { await inspection.CloseAsync(); inspection = null; }
                    await MaintainSavedAsync(maintained.InventoryId, maintenance == SavedAnalysisMaintenanceAction.Restore); return;
                }
                if (result is not true) return;
                if (!comparing)
                {
                    if (!_window.CanAddSession) { _window.ExplainSessionLimit(); return; }
                    destination = _window._admissionPool.Acquire();
                }
                var selected = choices.SelectedItem is SavedChoice choice && choice.Name == name.Text ? choice.Entry : null;
                DiskSpaceViewModel loaded;
                if (comparing)
                {
                    using var inputs = await WithInventoryProgress("Reading comparison inputs", token => Model.PrepareComparisonAsync(name.Text!, selected, captured!, metric, token));
                    if (_disposed || !_window.IsVisible) return;
                    System.Collections.Immutable.ImmutableDictionary<string, string>? mapping = null;
                    if (mapRoots.IsChecked == true || !inputs.HasAutomaticRoots)
                    {
                        mapping = await ReviewRootMappingAsync(inputs);
                        if (mapping is null || _disposed) return;
                    }
                    loaded = await WithInventoryProgress("Comparing observations", token => inputs.BuildAsync(mapping, token));
                }
                else loaded = await WithInventoryProgress("Opening inventory", token => Model.OpenSavedAsync(name.Text!, selected, token));
                if (_disposed || !_window.IsVisible) { loaded.Dispose(); return; }
                var admission = destination!; destination = null;
                try { _window.AddReservedSession(loaded, [], admission); } catch { loaded.Dispose(); throw; }
            }
            catch (OperationCanceledException) { Model.SetStatus(comparing ? "Comparison canceled. No partial comparison was published." : "Opening the inventory was canceled."); }
            catch (Exception ex) when (InventoryError(ex)) { Model.SetStatus((comparing ? "Comparison unavailable: " : "Inventory was not opened: ") + AnalysisNames.Display(ex.Message, 400)); }
            finally
            {
                closed = true; browseCancel.Cancel(); catalogLease?.Dispose(); captured?.Dispose();
                if (inspection is not null) await inspection.CloseAsync();
                if (interaction) Model.Interaction(false); _persistenceBusy = false; Refresh();
                var actualOpening = openingWork.CloseAsync();
                if (destination is not null) await destination.CloseAfterAsync(actualOpening); else await actualOpening;
            }
        }

        private async Task<T> WithInventoryProgress<T>(string title, Func<CancellationToken, Task<T>> run)
        {
            using var cancel = new CancellationTokenSource(); var closer = new DialogCloser();
            var work = run(cancel.Token);
            if (work.IsCompleted) return await work;
            var dismissed = _window._dialogs.ShowCustomAsync(title,
                new TextBlock { Text = "Working in this profile's private inventory store. Cancel requests a stop; an outstanding native call remains owned until it returns.", TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
                [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true)], closer: closer);
            try
            {
                if (await Task.WhenAny(work, dismissed) == dismissed)
                { cancel.Cancel(); Model.SetStatus("Stopping inventory work. Waiting for its native request to return…"); }
                return await work;
            }
            finally { closer.Close(null); }
        }

        private static bool InventoryError(Exception ex) => ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException;
        private static string? InventoryNameError(string? text)
        {
            try { AnalysisPrivateInventoryStore.ValidateName(text ?? ""); return null; }
            catch (ArgumentException) { return "Use a portable filename ending in .fcdsa (up to 120 characters)."; }
        }
    }
}
