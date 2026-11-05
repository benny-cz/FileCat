using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private readonly Button _save, _openSaved;
        private bool _persistenceBusy;

        private async Task SaveAsync()
        {
            if (_disposed || _persistenceBusy || !Model.CanSave) return;
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
                var receipt = await WithInventoryProgress("Saving inventory", token => Model.SaveNewAsync(name.Text!, revision, token));
                if (!_disposed) Model.SetStatus("Saved " + AnalysisNames.Display(receipt.Name, 240) + ". " + (revision.Snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Captured traversal complete under policy." : "Partial observations remain partial."));
            }
            catch (OperationCanceledException) { Model.SetStatus("Inventory save canceled. No successful save is claimed."); }
            catch (AnalysisPublicationUncertainException) { Model.SetStatus("The inventory name was published, but durability is uncertain. Keep that file and inspect it before trying another name."); }
            catch (Exception ex) when (InventoryError(ex)) { Model.SetStatus("Inventory was not saved: " + AnalysisNames.Display(ex.Message, 400)); }
            finally { _persistenceBusy = false; Model.Interaction(false); Refresh(); }
        }

        private async Task OpenSavedAsync()
        {
            if (_disposed || _persistenceBusy || !Model.HasPrivateStorage) return;
            if (_window.SessionCount >= AnalysisLimits.Sessions) { _window.ExplainSessionLimit(); return; }
            _persistenceBusy = true; Refresh();
            try
            {
                var name = new TextBox { Text = Model.SavedInventoryName ?? "", PlaceholderText = "Saved analysis.fcdsa", MaxLength = 120 };
                AutomationProperties.SetName(name, "Saved inventory filename");
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                AutomationProperties.SetLiveSetting(error, Avalonia.Automation.AutomationLiveSetting.Assertive);
                name.TextChanged += (_, _) => error.Text = string.IsNullOrEmpty(name.Text) ? "" : InventoryNameError(name.Text);
                var body = new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = "Enter a saved .fcdsa filename. Opening it shows offline observations without reconnecting sources or granting file actions.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Saved analyses folder: " + AnalysisNames.Display(Model.SavedDirectory ?? ""), TextWrapping = TextWrapping.Wrap },
                    name, error
                } };
                var result = await _window._dialogs.ShowCustomAsync("Open saved analysis", body,
                    [new DialogButton("Cancel", false, IsCancel: true), new DialogButton("Open observations", true, IsDefault: true) { IsAvailable = () => InventoryNameError(name.Text) is null }], name);
                if (result is not true || _disposed) return;
                var loaded = await WithInventoryProgress("Opening inventory", token => Model.OpenSavedAsync(name.Text!, token));
                if (_disposed || !_window.IsVisible) { loaded.Dispose(); return; }
                if (_window.SessionCount >= AnalysisLimits.Sessions) { loaded.Dispose(); _window.ExplainSessionLimit(); return; }
                try { _window.AddSession(loaded); } catch { loaded.Dispose(); throw; }
            }
            catch (OperationCanceledException) { Model.SetStatus("Opening the inventory was canceled."); }
            catch (Exception ex) when (InventoryError(ex)) { Model.SetStatus("Inventory was not opened: " + AnalysisNames.Display(ex.Message, 400)); }
            finally { _persistenceBusy = false; Refresh(); }
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

        private static bool InventoryError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException;
        private static string? InventoryNameError(string? text)
        {
            try { AnalysisPrivateInventoryStore.ValidateName(text ?? ""); return null; }
            catch (ArgumentException) { return "Use a portable filename ending in .fcdsa (up to 120 characters)."; }
        }
    }
}
