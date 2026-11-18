using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private const string RevealHelp = "Verify the selected entry and show current folder metadata in a read-only workspace tab.";

    private static Button RevealButton()
    {
        var button = new Button { Content = "Reveal in panel", IsEnabled = false };
        ToolTip.SetShowOnDisabled(button, true);
        return button;
    }

    private static string? PanelRevealRefusal(AppServices? services, AnalysisIndexedAcquisition? owner, ulong? path)
    {
        if (services?.ShowAnalysisPanelFolder is null) return "The main FileCat workspace is unavailable.";
        if (owner?.HasRuntimeBranchContext != true) return "Reveal needs a finished live analysis. Bind a current source to start a fresh scan from a saved analysis.";
        return path is null ? "Select an observed folder or a file within an observed folder." : null;
    }

    private static void UpdateRevealButton(Button button, string? refusal) => UpdatePanelCommand(button, refusal, RevealHelp);

    private static void UpdatePanelCommand(Button button, string? refusal, string enabledHelp)
    {
        button.IsEnabled = refusal is null;
        string help = refusal ?? enabledHelp;
        if (Equals(button.Tag, help)) return;
        button.Tag = help;
        AutomationProperties.SetHelpText(button, help);
        ToolTip.SetTip(button, new RichTip((string)button.Content!, detail: help));
    }

    private async Task RevealPathAsync(AppServices services, AnalysisIndexedAcquisition owner, SessionIdentity revision,
        ulong path, CancellationToken cancellation)
    {
        var folder = await IndexedComparisonProgressAsync("Reading current folder metadata",
            token => owner.ReadPanelListingAsync(revision, path, token), cancellation,
            "FileCat verifies the selected entry and shows a bounded read-only listing in the workspace.");
        cancellation.ThrowIfCancellationRequested();
        if (_closed || services.ShowAnalysisPanelFolder?.Invoke(folder) != true)
            throw new InvalidOperationException("The main FileCat workspace is unavailable.");
    }

    private sealed partial class SessionView
    {
        private readonly Button _revealInPanel = RevealButton();
        private ulong? FocusedRevealPath => Model.Tree?.Items.GetValueOrDefault(Model.Selection.Focus ?? "")?.CapturedNode is
            { IncludedByPolicy: true, MetadataState: MeasurementState.Known, Cloud: not true } node &&
            node is { Kind: AnalysisKind.Directory } or { Kind: AnalysisKind.File, ParentId: > 0 } ? node.Id : null;

        private async Task RevealFocusedPathAsync(ulong? path, SessionIdentity? revision)
        {
            if (_disposed || _window._dialogs.IsOpen || PanelRevealRefusal(Model.HistoryServices, Model.BranchRuntime, path) is not null) return;
            if (revision is null || revision != Model.Tree?.Snapshot.Identity)
            {
                Model.SetStatus("The displayed revision changed. Select the entry again.");
                return;
            }
            using var work = _actualWork.Enter();
            Model.Interaction(true);
            try
            {
                await _window.RevealPathAsync(Model.HistoryServices!, Model.BranchRuntime!, revision, path!.Value, Model.HistoryLifetime);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (IndexedExpectedError(error))
            {
                _actualWork.RecordUnconfirmedClose(error);
                if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                if (!_disposed) Model.SetStatus("Reveal unavailable. " + AnalysisNames.Display(error.Message, 380));
            }
            finally { Model.Interaction(false); }
        }
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _revealInPanel = RevealButton();
        private ulong? FocusedRevealPath => _focus != 0 && _shownInspection?.Row.Path is
            { IncludedByPolicy: true, MetadataState: MeasurementState.Known, Cloud: not true } path && path.Id == _focus &&
            path is { Kind: AnalysisKind.Directory } or { Kind: AnalysisKind.File, ParentId: > 0 } ? _focus : null;

        private async Task RevealFocusedPathAsync()
        {
            var path = FocusedRevealPath;
            if (_disposed || _window._dialogs.IsOpen || PanelRevealRefusal(_services, _runtimeAcquisition, path) is not null) return;
            using var work = _actualWork.Enter();
            Interaction(true);
            try
            {
                await _window.RevealPathAsync(_services!, _runtimeAcquisition!, _source.Descriptor.Identity, path!.Value, _selectionLifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (Expected(error))
            {
                RememberViewCloseFailure(error);
                if (!_disposed) _inspectorStatus.Text = "Reveal unavailable. " + AnalysisNames.Display(error.Message, 380);
            }
            finally { Interaction(false); }
        }
    }
}
