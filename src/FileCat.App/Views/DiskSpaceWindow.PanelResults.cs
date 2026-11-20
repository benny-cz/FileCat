using Avalonia.Controls;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static Button ResultsButton()
    {
        var button = new Button { Content = "Show selection in results", IsEnabled = false };
        ToolTip.SetShowOnDisabled(button, true);
        return button;
    }

    private static string? PanelResultsRefusal(AppServices? services, int selected)
    {
        if (services?.ShowAnalysisPanelFolder is null) return "The main FileCat workspace is unavailable.";
        if (selected == 0) return "Mark entries or focus one observed path first.";
        return selected > AnalysisPanelFolder.MaximumEntries ? AnalysisPanelResults.LimitMessage : null;
    }

    private static void UpdateResultsButton(Button button, AppServices? services, int selected) =>
        UpdatePanelCommand(button, PanelResultsRefusal(services, selected),
            "Open the marked entries, including those hidden by filters, or the focused entry in a read-only results tab.");

    private async Task ShowPanelResultsAsync(AppServices services, CleanupSelection selection, IReadOnlyList<RootBinding> roots, AnalysisTaint taint,
        Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> read, CancellationToken cancellation)
    {
        var listing = await IndexedComparisonProgressAsync("Reading selected observations",
            token => AnalysisPanelResults.ReadAsync(selection.Paths, selection.Label, roots, taint, read, token), cancellation,
            "The results tab shows the exact selected recorded paths and keeps their locations offline.");
        cancellation.ThrowIfCancellationRequested();
        if (_closed || services.ShowAnalysisPanelFolder?.Invoke(listing) != true)
            throw new InvalidOperationException("The main FileCat workspace is unavailable.");
    }

    private sealed partial class SessionView
    {
        private readonly Button _openPanelResults = ResultsButton();

        private async Task OpenPanelResultsAsync()
        {
            if (_disposed || _window._dialogs.IsOpen || Model.Tree is not { } tree || Model.HistoryServices is not { } services) return;
            var selected = CleanupSelection.Capture(Model.Selection, FocusedCleanupPath, basket: false);
            if (PanelResultsRefusal(services, selected.Paths.Count) is not null) return;
            using var work = _actualWork.Enter();
            using var owner = Model.RetainDisplayedOwner();
            var snapshot = tree.Snapshot;
            Model.Interaction(true);
            try
            {
                await _window.ShowPanelResultsAsync(services, selected, snapshot.Roots, snapshot.Taint,
                    (id, token) => ReadRecipeMemoryPathAsync(snapshot, id, token), Model.HistoryLifetime);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (IndexedExpectedError(error))
            {
                _actualWork.RecordUnconfirmedClose(error);
                if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                if (!_disposed) Model.SetStatus("Could not open results. " + AnalysisNames.Display(error.Message, 380));
            }
            finally { Model.Interaction(false); }
        }
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _openPanelResults = ResultsButton();

        private async Task OpenPanelResultsAsync()
        {
            if (_disposed || _window._dialogs.IsOpen || _actualWork.HasUnconfirmedClose || _services is not { } services) return;
            var selected = CleanupSelection.Capture(_selection, _focus == 0 ? null : _focus, basket: false);
            if (PanelResultsRefusal(services, selected.Paths.Count) is not null) return;
            using var work = _actualWork.Enter();
            Interaction(true);
            try
            {
                var descriptor = _source.Descriptor;
                await _window.ShowPanelResultsAsync(services, selected, descriptor.Roots, descriptor.Taint, _source.ReadPathAsync, _selectionLifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (Expected(error))
            {
                RememberViewCloseFailure(error);
                if (!_disposed) _status.Text = "Could not open results. " + AnalysisNames.Display(error.Message, 380);
            }
            finally { Interaction(false); }
        }
    }
}
