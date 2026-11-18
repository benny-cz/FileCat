using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private const string RevealHelp = "Read the verified current folder and show its metadata in a read-only workspace tab.";

    private static Button RevealButton()
    {
        var button = new Button { Content = "Reveal in panel", IsEnabled = false };
        ToolTip.SetShowOnDisabled(button, true);
        return button;
    }

    private static string? PanelRevealRefusal(AppServices? services, AnalysisIndexedAcquisition? owner, ulong? directory)
    {
        if (services?.ShowAnalysisPanelFolder is null) return "The main FileCat workspace is unavailable.";
        if (owner?.HasRuntimeBranchContext != true) return "Reveal needs a finished live analysis. Bind a current source to start a fresh scan from a saved analysis.";
        return directory is null ? "Select a real folder in the analysis." : null;
    }

    private static void UpdateRevealButton(Button button, string? refusal)
    {
        button.IsEnabled = refusal is null;
        string help = refusal ?? RevealHelp;
        if (Equals(button.Tag, help)) return;
        button.Tag = help;
        AutomationProperties.SetHelpText(button, help);
        ToolTip.SetTip(button, new RichTip("Reveal in panel", detail: help));
    }

    private async Task RevealFolderAsync(AppServices services, AnalysisIndexedAcquisition owner, SessionIdentity revision,
        ulong directory, CancellationToken cancellation)
    {
        var folder = await IndexedComparisonProgressAsync("Reading current folder metadata",
            token => owner.ReadPanelFolderAsync(revision, directory, token), cancellation,
            "FileCat verifies the folder identity and shows a bounded read-only listing in the workspace.");
        cancellation.ThrowIfCancellationRequested();
        if (_closed || services.ShowAnalysisPanelFolder?.Invoke(folder) != true)
            throw new InvalidOperationException("The main FileCat workspace is unavailable.");
    }

    private sealed partial class SessionView
    {
        private readonly Button _revealFolder = RevealButton();
        private ulong? FocusedRevealFolder => Model.Tree?.Items.GetValueOrDefault(Model.Selection.Focus ?? "") is
            { Kind: AnalysisKind.Directory, Synthetic: false, NodeId: { } id } ? id : null;

        private async Task RevealFocusedFolderAsync(ulong? directory, SessionIdentity? revision)
        {
            if (_disposed || _window._dialogs.IsOpen || PanelRevealRefusal(Model.HistoryServices, Model.BranchRuntime, directory) is not null) return;
            if (revision is null || revision != Model.Tree?.Snapshot.Identity)
            {
                Model.SetStatus("The displayed revision changed. Select the folder again.");
                return;
            }
            using var work = _actualWork.Enter();
            Model.Interaction(true);
            try
            {
                await _window.RevealFolderAsync(Model.HistoryServices!, Model.BranchRuntime!, revision, directory!.Value, Model.HistoryLifetime);
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
        private readonly Button _revealFolder = RevealButton();
        private ulong? FocusedRevealFolder => _focus != 0 && _shownInspection?.Row.Path is
            { Kind: AnalysisKind.Directory } path && path.Id == _focus ? _focus : null;

        private async Task RevealFocusedFolderAsync()
        {
            var directory = FocusedRevealFolder;
            if (_disposed || _window._dialogs.IsOpen || PanelRevealRefusal(_services, _runtimeAcquisition, directory) is not null) return;
            using var work = _actualWork.Enter();
            Interaction(true);
            try
            {
                await _window.RevealFolderAsync(_services!, _runtimeAcquisition!, _source.Descriptor.Identity, directory!.Value, _selectionLifetime.Token);
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
