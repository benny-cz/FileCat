using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private readonly Button _openComparison;
        private Button? _comparisonSave;
        private TextBlock? _comparisonPersistenceStatus;
        private sealed record ComparisonChoice(string Name)
        { public override string ToString() => AnalysisNames.Display(Name, 240); }

        private void AddComparisonPersistence(StackPanel footer)
        {
            _comparisonSave = ActionButton("Save comparison…", () => _ = SaveComparisonAsync(),
                "Save both captured inventories and applied metric, query, ranking, focus and chart settings in one private comparison bundle. Source files are not opened.");
            _comparisonPersistenceStatus = SessionText(Model.SavedComparisonName is { } name ? "Opened " + AnalysisNames.Display(name, 240) + ". Captured inputs remain disconnected." : Model.Status);
            AutomationProperties.SetName(_comparisonPersistenceStatus, "Comparison storage status");
            AutomationProperties.SetLiveSetting(_comparisonPersistenceStatus, AutomationLiveSetting.Polite);
            footer.Children.Add(_comparisonSave); footer.Children.Add(_comparisonPersistenceStatus); RefreshComparisonPersistence();
        }
        private void RefreshComparisonPersistence()
        {
            _openComparison.IsEnabled = Model.HasPrivateStorage && !_persistenceBusy;
            if (_comparisonSave is not null)
            {
                _comparisonSave.IsEnabled = Model.CanSaveComparison && !_persistenceBusy && !_comparisonChartBusy && !_comparisonChartUnapplied;
                ToolTip.SetTip(_comparisonSave, new RichTip("Save comparison", detail: _comparisonChartUnapplied
                    ? "Wait for the selected chart to finish, or choose Table before saving the applied comparison view."
                    : "Save both captured inventories and the applied comparison settings privately. Unapplied query edits are not saved."));
            }
            if (_comparisonPersistenceStatus is not null) _comparisonPersistenceStatus.Text = Model.Status;
        }
        private async Task SaveComparisonAsync()
        {
            if (_disposed || _persistenceBusy || _comparisonChartBusy || _comparisonChartUnapplied || !Model.CanSaveComparison) return;
            using var actualWork = _actualWork.Enter(); using var captured = Model.CaptureComparison();
            _persistenceBusy = true; RefreshComparisonPersistence();
            try
            {
                var name = new TextBox { Text = Model.SavedComparisonName ?? "Comparison.fcdsc", MaxLength = 120, MinWidth = 200 };
                AutomationProperties.SetName(name, "Comparison filename");
                var error = SessionText(""); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
                name.TextChanged += (_, _) => error.Text = ComparisonNameError(name.Text) ?? "";
                var body = new StackPanel
                {
                    Spacing = 8,
                    Children =
                {
                    SessionText("Save both entire captured inventories and the applied comparison settings. Table filters do not reduce the saved data. Unapplied query edits are not saved. The bundle restores offline observations without reconnecting sources or granting file actions."),
                    SessionText("Private comparison folder: "+AnalysisNames.Display(Model.SavedDirectory??"",1000)),
                    SessionText("Captured metric: "+ComparisonMetric(captured.View.Metric)+" · "+(captured.View.Chart==AnalysisComparisonChartMode.Table?"Table":captured.View.PreviousChart?"Previous chart":"Current chart")),name,error
                }
                };
                if (captured.Taint.HasFlag(AnalysisTaint.PrivilegedDerived))
                    body.Children.Insert(1, SessionText("Contains privileged-derived observations. This private bundle retains that classification."));
                var result = await _window._dialogs.ShowCustomAsync("Save named comparison", body,
                    [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Save new comparison", true) { IsAvailable = () => ComparisonNameError(name.Text) is null }], name);
                if (result is not true || _disposed) return;
                await WithInventoryProgress("Saving comparison", token => Model.SaveComparisonNewAsync(name.Text!, captured, token));
            }
            catch (OperationCanceledException) { Model.SetStatus("Comparison save canceled. No successful save is claimed."); }
            catch (AnalysisPublicationUncertainException) { Model.SetStatus("The comparison name was published, but durability is uncertain. Keep the existing file and inspect it before retrying."); }
            catch (Exception error) when (InventoryError(error) || error is FormatException or OverflowException)
            { Model.SetStatus("Comparison was not saved: " + AnalysisNames.Display(error.Message, 400)); }
            finally { _persistenceBusy = false; if (!_disposed) RefreshComparisonPersistence(); }
        }
        private async Task OpenComparisonSavedAsync()
        {
            if (_disposed || _persistenceBusy || !Model.HasPrivateStorage) return;
            using var actualWork = _actualWork.Enter();
            AnalysisSessionAdmission.Slot? destination = null;
            var opening = new AnalysisViewWork(); using var cancel = new CancellationTokenSource(); bool closed = false, browsing = false;
            _persistenceBusy = true; RefreshComparisonPersistence();
            try
            {
                var name = new TextBox { Text = "", PlaceholderText = "Comparison.fcdsc", MaxLength = 120, MinWidth = 200 };
                AutomationProperties.SetName(name, "Saved comparison filename");
                var error = SessionText(""); var status = SessionText("");
                AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
                var choices = new ComboBox { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 600 };
                AutomationProperties.SetName(choices, "Saved comparisons");
                choices.SelectionChanged += (_, _) => { if (choices.SelectedItem is ComparisonChoice choice) name.Text = choice.Name; };
                ComparisonChoice? SelectedChoice() => choices.SelectedItem is ComparisonChoice choice && choice.Name == name.Text ? choice : null;
                var browse = new Button { Content = "Browse saved comparisons" };
                browse.Click += async (_, _) =>
                {
                    if (_disposed || closed || browsing) return;
                    using var actualBrowse = _actualWork.Enter(); using var actualOpening = opening.Enter(); browsing = true; browse.IsEnabled = false;
                    status.Text = "Reading this profile's private comparison names…";
                    try
                    {
                        var names = await Model.ReadSavedComparisonNamesAsync(cancel.Token);
                        if (_disposed || closed) return;
                        choices.ItemsSource = names.Select(value => new ComparisonChoice(value)).ToArray(); choices.IsVisible = true;
                        status.Text = names.Length.ToString("N0") + " saved comparisons. Both captured inputs and the applied view are restored offline.";
                    }
                    catch (OperationCanceledException) { if (!_disposed && !closed) status.Text = "Comparison browsing canceled."; }
                    catch (Exception exception) when (InventoryError(exception))
                    { if (!_disposed && !closed) status.Text = "Comparison names unavailable: " + AnalysisNames.Display(exception.Message, 400); }
                    finally { browsing = false; if (!_disposed && !closed) browse.IsEnabled = true; }
                };
                name.TextChanged += (_, _) => error.Text = string.IsNullOrEmpty(name.Text) ? "" : ComparisonNameError(name.Text) ?? "";
                var body = new StackPanel
                {
                    Spacing = 8,
                    Children =
                {
                    SessionText("Open a saved comparison from this private profile. Both captured inventories, root mapping and applied view are restored; no filesystem source is opened. Unsupported, incomplete or corrupt bundles are preserved and refused."),
                    SessionText("Private comparison folder: "+AnalysisNames.Display(Model.SavedDirectory??"",1000)),browse,status,choices,name,error
                }
                };
                var result = await _window._dialogs.ShowCustomAsync("Open saved comparison", body,
                    [new DialogButton("Cancel", false, IsCancel: true), new DialogButton("Open comparison observations", true, IsDefault: true) { IsAvailable = () => ComparisonNameError(name.Text) is null && _window.CanAddSession },
                    new DialogButton("Delete selected comparison…", SavedAnalysisMaintenanceAction.Delete) { IsAvailable = () => !browsing && SelectedChoice() is not null }], name);
                closed = true; if (_disposed) return;
                if (result is SavedAnalysisMaintenanceAction.Delete && SelectedChoice() is { } selected)
                {
                    cancel.Cancel(); await opening.CloseAsync(); await DeleteSavedComparisonAsync(selected.Name); return;
                }
                if (result is not true) return;
                if (!_window.CanAddSession) { _window.ExplainSessionLimit(); return; }
                destination = _window._admissionPool.Acquire();
                var loaded = await WithInventoryProgress("Opening comparison", token => Model.OpenSavedComparisonAsync(name.Text!, token));
                if (_disposed || !_window.IsVisible) { loaded.Dispose(); return; }
                var admission = destination!; destination = null;
                try { _window.AddReservedSession(loaded, [], admission); } catch { loaded.Dispose(); throw; }
            }
            catch (OperationCanceledException) { Model.SetStatus("Opening the saved comparison was canceled."); }
            catch (Exception error) when (InventoryError(error) || error is FormatException or OverflowException)
            { Model.SetStatus("Comparison was not opened: " + AnalysisNames.Display(error.Message, 400)); }
            finally
            {
                closed = true; cancel.Cancel(); _persistenceBusy = false; if (!_disposed) RefreshComparisonPersistence();
                var actualOpening = opening.CloseAsync(); if (destination is not null) await destination.CloseAfterAsync(actualOpening); else await actualOpening;
            }
        }
        private async Task DeleteSavedComparisonAsync(string name)
        {
            try
            {
                await using var captured = await WithInventoryProgress("Checking saved comparison", token => Model.PrepareSavedComparisonMaintenanceAsync(name, token));
                if (_disposed || !await SavedComparisonMaintenanceDialog.ConfirmAsync(_window._dialogs, captured.Review, Model.SavedDirectory ?? ""))
                { Model.SetStatus("The saved comparison was kept."); return; }
                if (_disposed || !_window.IsVisible) return;
                var outcome = await WithInventoryProgress("Deleting saved comparison", token => captured.DeleteAsync(token));
                Model.SetStatus(outcome.Detail);
            }
            catch (OwnedAnalysisComparisonDeletionException error) { _actualWork.RecordUnconfirmedClose(error); Model.SetStatus(error.Outcome.Detail); }
            catch (OperationCanceledException) { Model.SetStatus("Saved-comparison maintenance canceled. No successful removal is claimed."); }
            catch (Exception error) when (InventoryError(error) || error is FormatException or OverflowException)
            { _actualWork.RecordUnconfirmedClose(error); Model.SetStatus("Saved comparison was not changed: " + AnalysisNames.Display(error.Message, 400)); }
        }
        private static string? ComparisonNameError(string? value)
        {
            try { AnalysisPrivateInventoryStore.ValidateComparisonName(value ?? ""); return null; }
            catch (ArgumentException) { return "Use a portable filename ending in .fcdsc (up to120 characters)."; }
        }
    }
}
