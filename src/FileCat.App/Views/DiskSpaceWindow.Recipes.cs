using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Controls;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static Button CandidateRecipeButton(WrapPanel toolbar)
    {
        const string description = "Choose captured locations and a versioned rule. Review candidates using recorded metadata.";
        var button = new Button { Content = "Candidate recipes…" };
        ToolTip.SetTip(button, new RichTip("Preview candidate recipes", detail: description));
        AutomationProperties.SetHelpText(button, description);
        toolbar.Children.Add(button);
        return button;
    }

    private sealed partial class SessionView
    {
        private void AddRecipeControls(WrapPanel toolbar)
        {
            var button = CandidateRecipeButton(toolbar);
            _dataControls.Add(button);
            button.Click += async (_, _) =>
            {
                if (_disposed || _window._dialogs.IsOpen || Model.Tree is not { } tree) return;
                using var work = _actualWork.Enter();
                var lifetime = Model.HistoryLifetime;
                try
                {
                    using var source = Model.CaptureDisplayedRevision();
                    var roots = source.Snapshot.Roots;
                    var choice = await AnalysisRecipeDialog.ChooseAsync(_window._dialogs, roots, tree.Metric, lifetime);
                    if (choice is null || _disposed) return;
                    var result = await _window.IndexedComparisonProgressAsync("Reading captured recipe candidates",
                        token => AnalysisMemoryRecipes.PreviewAsync(source, choice, tree.Metric, token), lifetime);
                    if (!_disposed) await AnalysisRecipeDialog.ShowAsync(_window._dialogs, result, roots,
                        (id, token) => ReadRecipeMemoryPathAsync(source.Snapshot, id, token), lifetime);
                }
                catch (OperationCanceledException) { }
                catch (Exception error) when (IndexedExpectedError(error) || error is FormatException or OverflowException)
                {
                    if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                    if (!_disposed) Model.SetStatus("Candidate preview unavailable. " + AnalysisNames.Display(error.Message, 400));
                }
            };
        }

        private static Task<AnalysisPathMetadata> ReadRecipeMemoryPathAsync(AnalysisSnapshot source, ulong id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (id > 0 && id <= (ulong)source.Nodes.Length && source.Nodes[(int)id - 1] is { } direct && direct.Id == id)
                return Task.FromResult(AnalysisPathMetadata.From(direct));
            return Task.Run(() =>
            {
                foreach (var node in source.Nodes)
                {
                    token.ThrowIfCancellationRequested();
                    if (node.Id == id) return AnalysisPathMetadata.From(node);
                }
                throw new InvalidDataException("The candidate path is absent from this observation.");
            }, token);
        }
    }

    private sealed partial class IndexedSessionView
    {
        private void AddRecipeControls(WrapPanel toolbar)
        {
            var button = CandidateRecipeButton(toolbar);
            button.Click += async (_, _) =>
            {
                if (_disposed || _window._dialogs.IsOpen) return;
                using var work = _actualWork.Enter();
                var lifetime = _selectionLifetime.Token;
                try
                {
                    var roots = _source.Descriptor.Roots;
                    var metric = _explorer.Metric;
                    var choice = await AnalysisRecipeDialog.ChooseAsync(_window._dialogs, roots, metric, lifetime);
                    if (choice is null || _disposed) return;
                    var result = await _window.IndexedComparisonProgressAsync("Reading captured recipe candidates",
                        token => _source.PreviewRecipeAsync(choice, metric, token), lifetime);
                    if (!_disposed) await AnalysisRecipeDialog.ShowAsync(_window._dialogs, result, roots, _source.ReadPathAsync, lifetime);
                }
                catch (OperationCanceledException) { }
                catch (Exception error) when (Expected(error) || error is FormatException or OverflowException)
                {
                    RememberViewCloseFailure(error);
                    if (!_disposed) _status.Text = "Candidate preview unavailable. " + AnalysisNames.Display(error.Message, 400);
                }
            };
        }
    }
}
