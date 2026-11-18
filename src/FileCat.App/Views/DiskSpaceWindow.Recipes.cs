using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private async Task ShowCandidateRecipesAsync(SessionIdentity identity, ImmutableArray<RootBinding> roots, AnalysisMetric metric, string seedQuery,
        AppServices? services, Func<AnalysisRecipeRequest, CancellationToken, Task<AnalysisRecipePreview>> builtIn,
        Func<AnalysisSavedRecipe, CancellationToken, Task<AnalysisRecipePreview>> saved,
        Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> readPath, CancellationToken lifetime)
    {
        var choice = await AnalysisRecipeDialog.ChooseAsync(_dialogs, roots, metric, services is { Io.IsStopped: false }, lifetime);
        lifetime.ThrowIfCancellationRequested();
        AnalysisRecipePreview result;
        if (choice is AnalysisRecipeDialog.Choice.BuiltIn selected)
        {
            result = await IndexedComparisonProgressAsync("Reading captured recipe candidates", token => builtIn(selected.Request, token), lifetime);
        }
        else if (choice is AnalysisRecipeDialog.Choice.Saved && services is not null)
        {
            var recipe = await ChooseSavedRecipeAsync(services, identity, roots, metric, seedQuery, lifetime);
            if (recipe is null) return;
            result = await IndexedComparisonProgressAsync("Reading saved recipe candidates", token => saved(recipe, token), lifetime);
        }
        else return;
        await AnalysisRecipeDialog.ShowAsync(_dialogs, result, roots, readPath, lifetime);
    }

    private async Task<AnalysisSavedRecipe?> ChooseSavedRecipeAsync(AppServices services, SessionIdentity identity, ImmutableArray<RootBinding> roots,
        AnalysisMetric metric, string seedQuery, CancellationToken lifetime)
    {
        var saved = await IndexedComparisonProgressAsync("Reading saved recipes", async token =>
        {
            await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, token, indexedStorage: false);
            return await prepared.ReadRecipesAsync(identity, roots, token);
        }, lifetime);
        var chosen = await AnalysisSavedRecipeDialog.ChooseAsync(_dialogs, saved, lifetime);
        if (chosen is null || !chosen.Create) return chosen?.Recipe;
        var recipe = await AnalysisSavedRecipeDialog.CreateAsync(_dialogs, identity, roots, metric, seedQuery, lifetime);
        if (recipe is null) return null;
        return await IndexedComparisonProgressAsync("Saving private recipe", async token =>
        {
            await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, token, indexedStorage: false);
            await prepared.SaveRecipeAsync(recipe, identity, roots, token);
            return recipe;
        }, lifetime);
    }

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
                    var snapshot = source.Snapshot;
                    await _window.ShowCandidateRecipesAsync(snapshot.Identity, snapshot.Roots, tree.Metric, Model.PublishedReportView.Query,
                        Model.HistoryServices, (choice, token) => choice.Recipe == AnalysisMetadataRecipe.LargestFolders
                            ? AnalysisMemoryRecipes.PreviewFoldersAsync(source, tree, choice, token) : AnalysisMemoryRecipes.PreviewAsync(source, choice, tree.Metric, token),
                        (recipe, token) => AnalysisMemoryRecipes.PreviewSavedAsync(source, recipe, token),
                        (id, token) => ReadRecipeMemoryPathAsync(snapshot, id, token), lifetime);
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
                    var descriptor = _source.Descriptor;
                    var metric = _explorer.Metric;
                    await _window.ShowCandidateRecipesAsync(descriptor.Identity, descriptor.Roots, metric, _explorer.Filter?.Request.Text ?? "", _services,
                        (choice, token) => choice.Recipe == AnalysisMetadataRecipe.LargestFolders &&
                            _explorer is { ReportIsSubset: false, Filter: null } explorer && explorer.Metric == metric
                                ? explorer.PreviewLargestFoldersAsync(choice, token) : _source.PreviewRecipeAsync(choice, metric, token),
                        _source.PreviewSavedRecipeAsync, _source.ReadPathAsync, lifetime);
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
