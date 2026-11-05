using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task RefusedNavigationDoesNotChangeDisplayedScopeRowsOrHistory()
    {
        bool refuse = false;
        using var model = new DiskSpaceViewModel(Fixture(), (rows, field, descending, token) =>
            refuse ? throw new InvalidOperationException("injected ordering refusal") : AnalysisTableOrder.Apply(rows, field, descending, token));
        await model.RebuildAsync(true);
        var original = model.Rows;
        refuse = true; model.Focus("n:1", open: true);
        await Until(() => model.Status.Contains("injected ordering refusal", StringComparison.Ordinal));
        Assert.Equal(AnalysisTree.RootKey, model.Selection.Scope); Assert.Equal(original, model.Rows);
        Assert.Null(model.Selection.BackScope); Assert.Null(model.Selection.ForwardScope);

        refuse = false; model.Focus("n:1", open: true);
        await Until(() => model.Selection.Scope == "n:1");
        original = model.Rows; Assert.Equal(AnalysisTree.RootKey, model.Selection.BackScope);
        refuse = true; model.SetStatus("Trying Back"); model.Back();
        await Until(() => model.Status.Contains("injected ordering refusal", StringComparison.Ordinal));
        Assert.Equal("n:1", model.Selection.Scope); Assert.Equal(original, model.Rows);
        Assert.Equal(AnalysisTree.RootKey, model.Selection.BackScope); Assert.Null(model.Selection.ForwardScope);

        refuse = false; model.Back(); await Until(() => model.Selection.Scope == AnalysisTree.RootKey);
        original = model.Rows;
        refuse = true; model.SetStatus("Trying Forward"); model.Forward();
        await Until(() => model.Status.Contains("injected ordering refusal", StringComparison.Ordinal));
        Assert.Equal(AnalysisTree.RootKey, model.Selection.Scope); Assert.Equal(original, model.Rows);
        Assert.Null(model.Selection.BackScope); Assert.Equal("n:1", model.Selection.ForwardScope);
    }

    [AvaloniaFact]
    public async Task PendingNavigationKeepsThePublishedScopeUntilCommitAndRevalidatesIncomingProjection()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0, reorderedTarget = 0;
        using var model = new DiskSpaceViewModel(Fixture(), (rows, field, descending, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2) { entered.TrySetResult(); release.Wait(); }
            if (rows.Length == 2 && field == AnalysisTableSort.Name) Volatile.Write(ref reorderedTarget, 1);
            return AnalysisTableOrder.Apply(rows, field, descending, token);
        });
        try
        {
            await model.RebuildAsync(true); var original = model.Rows;
            model.Focus("n:1", open: true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(AnalysisTree.RootKey, model.Selection.Scope); Assert.Equal(original, model.Rows); Assert.Null(model.Selection.BackScope);
            model.Query = ".png"; model.Sort = AnalysisTableSort.Name; model.SortDescending = false; await model.RebuildAsync(true);
            Assert.Equal((UInt128)98304, model.Tree!.ScopeValue(AnalysisTree.RootKey));
            Assert.Equal(AnalysisTree.RootKey, model.Selection.Scope);
            release.Set();
            await Until(() => model.Selection.Scope == "n:1" && model.Tree?.Projection.FilterText == ".png");
            Assert.Equal("n:3", Assert.Single(model.Rows).Key); Assert.Equal((UInt128)32768, model.Tree!.ScopeValue(AnalysisTree.RootKey));
            Assert.Equal(AnalysisTree.RootKey, model.Selection.BackScope);
            Assert.Equal(1, Volatile.Read(ref reorderedTarget));
        }
        finally { release.Set(); }
    }

    [AvaloniaFact]
    public void NavigationHistoryRetainsOnlyItsBoundedRecentLocationsWithoutChangingReferences()
    {
        var selection = new AnalysisSelectionState(); selection.ToggleMark(17); selection.StageCaptured([17]);
        for (int i = 1; i <= 1000; i++) selection.Navigate("n:" + i);
        int count = 0;
        while (selection.BackScope is not null) { selection.Back(); count++; Assert.True(count <= AnalysisLimits.NavigationHistory); }
        Assert.Equal(AnalysisLimits.NavigationHistory, count); Assert.Equal("n:872", selection.Scope);
        count = 0;
        while (selection.ForwardScope is not null) { selection.Forward(); count++; Assert.True(count <= AnalysisLimits.NavigationHistory); }
        Assert.Equal(AnalysisLimits.NavigationHistory, count); Assert.Equal("n:1000", selection.Scope);
        Assert.Equal(new ulong[] { 17 }, selection.Marks); Assert.Equal(new ulong[] { 17 }, selection.Basket);
    }
}
