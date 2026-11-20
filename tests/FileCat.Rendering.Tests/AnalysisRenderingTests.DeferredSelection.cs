using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static CancellationToken DeferredCancellation => TestContext.Current.CancellationToken;
    private sealed class DeferredGate : IDisposable
    {
        internal readonly ManualResetEventSlim Release = new();
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() { Release.Set(); Release.Dispose(); }
    }
    private sealed class DeferredFixture : IAsyncDisposable
    {
        private readonly PreparationFixture _fixture = new();
        internal readonly DeferredGate Gate = new();
        private readonly List<IAnalysisBackend> _sources = [];
        internal bool SourcesDisposed => _sources.Count != 0 && _sources.All(source => source is WindowsAnalysisBackend windows ? windows.IsDisposed : ((UnixAnalysisBackend)source).IsDisposed);
        internal DiskSpaceViewModel Model { get; private set; } = null!;
        internal FileCat.App.Services.AppServices Services => _fixture.Services;
        internal string Input => Path.Combine(_fixture.Root, "input");
        internal static async Task<DeferredFixture> CreateAsync(int future = 2, bool partial = false, bool unknownAllocation = false, bool indexedStorage = false, bool mixedExtensions = false)
        {
            OrdinaryPreparation(); var fixture = new DeferredFixture(); var fixtureCancellation = DeferredCancellation;
            try
            {
                Directory.CreateDirectory(Path.Combine(fixture.Input, "waiting")); File.WriteAllBytes(Path.Combine(fixture.Input, "current.txt"), [1]);
                for (int index = 0; index < future; index++) File.WriteAllBytes(Path.Combine(fixture.Input, "waiting", $"next-{index:D4}" + (mixedExtensions && index % 2 != 0 ? ".bin" : ".txt")), new byte[16]);
                if (partial) await CorruptProfileForMemoryAsync(fixture._fixture);
                fixture.Model = new DiskSpaceViewModel(fixture._fixture.Services, path =>
                {
                    void BeforeChildren(AnalysisEntry directory, CancellationToken cancellation)
                    {
                        if (directory.Name != "waiting") return;
                        fixture.Gate.Entered.TrySetResult();
                        if (!fixture.Gate.Release.Wait(TimeSpan.FromSeconds(30), fixtureCancellation)) throw new IOException("Native deferred fixture gate timed out.");
                        cancellation.ThrowIfCancellationRequested();
                    }
                    AnalysisEntry Observe(AnalysisEntry entry) => unknownAllocation && entry.Kind == AnalysisKind.File
                        ? entry with { Allocation = AnalysisMeasurement.Missing(MeasurementState.Unsupported) } : entry;
                    IAnalysisBackend source = OperatingSystem.IsWindows() ? new WindowsAnalysisBackend(path, null, BeforeChildren, Observe) : new UnixAnalysisBackend(path, null, BeforeChildren, Observe);
                    fixture._sources.Add(source); return source;
                }, indexedStorage);
                string[] roots = partial ? [fixture.Input, Path.GetDirectoryName(fixture._fixture.Services.Paths.AnalysisScratchDirectory)!] : [fixture.Input];
                await fixture.Model.StartAsync(roots);
                try { await fixture.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), fixtureCancellation); }
                catch (TimeoutException error)
                {
                    await fixture.Model.RebuildAsync(true);
                    throw new InvalidOperationException("Native deferred fixture did not reach enumeration: " + fixture.Model.Phase + " · " + fixture.Model.Status + " · " +
                        string.Join(", ", fixture.Model.Tree?.Snapshot.Roots.Select(root => root.Disposition.ToString()) ?? []), error);
                }
                await fixture.Model.RebuildAsync(true);
                Assert.True(fixture.Model.Running); Assert.NotNull(fixture.Model.Tree);
                Assert.Contains(fixture.Model.Tree.Snapshot.Nodes, node => node.Name == "current.txt");
                Assert.DoesNotContain(fixture.Model.Tree.Snapshot.Nodes, node => node.Name.StartsWith("next-", StringComparison.Ordinal));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        internal async Task FinishAsync()
        {
            Gate.Release.Set(); await Until(() => Model.Phase is AnalysisPhase.Finished or AnalysisPhase.Stopped or AnalysisPhase.Failed);
            await Model.RebuildAsync(true);
        }
        public async ValueTask DisposeAsync()
        {
            Gate.Release.Set();
            if (Model is not null) { Model.Dispose(); await Model.CloseCompletion; }
            Gate.Dispose(); _fixture.Dispose();
        }
    }
    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FolderHierarchy)]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task DeferredSelectionKeepsItsDisplayedQueryAndGroupThenRequiresExplicitReview(AnalysisGroupingDimension grouping)
    {
        using var styles = new ProductStyles(); await using var fixture = await DeferredFixture.CreateAsync(); var model = fixture.Model;
        model.Query = "name:.txt"; model.Metric = AnalysisMetric.FileSize; model.Grouping = grouping; await model.RebuildAsync(true);
        if (grouping != AnalysisGroupingDimension.FolderHierarchy)
        {
            string scope = Assert.Single(model.Tree!.Groups.Keys); model.Focus(scope, open: true); await Until(() => model.Selection.Scope == scope);
        }
        ulong current = model.Tree!.Snapshot.Nodes.Single(node => node.Name == "current.txt").Id;
        model.Selection.MarkCaptured([current]); model.Selection.StageCaptured([current]);
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            Click(window, "Select all after scan completes"); Assert.Equal(AnalysisDeferredSelectionState.WaitingForScan, model.DeferredSelectionState);
            model.Query = "name:no-matches"; model.Grouping = AnalysisGroupingDimension.FolderHierarchy; model.CaseSensitive = true; await model.RebuildAsync(true);
            await fixture.FinishAsync(); await Until(() => model.CanReviewDeferredSelection); Assert.Equal(3, model.DeferredSelectionCount);
            Assert.Equal(new[] { current }, model.Selection.Marks.Order()); Assert.Equal(new[] { current }, model.Selection.Basket.Order());
            Click(window, "Review deferred matches"); await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(list => AutomationLabel(list) == "Deferred captured matches")); Frame(window);
            Assert.Equal(3, window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationLabel(list) == "Deferred captured matches").ItemCount);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Until(() => !window.GetVisualDescendants().OfType<ListBox>().Any(list => AutomationLabel(list) == "Deferred captured matches"));
            Assert.Single(model.Selection.Marks); Assert.True(model.CanReviewDeferredSelection);
            Click(window, "Review deferred matches"); await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Add reviewed matches to marks"));
            Click(window, "Add reviewed matches to marks"); await Until(() => !model.HasDeferredSelection);
            Assert.Equal(3, model.Selection.Marks.Count); Assert.Single(model.Selection.Basket);
            Assert.Equal("name:no-matches", model.Query); Assert.True(fixture.SourcesDisposed); Assert.Equal(0UL, model.SourceCloseFailures);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
    [AvaloniaFact]
    public async Task DeferredSelectionFolderScopeDoesNotBroadenWhenNavigationChanges()
    {
        await using var fixture = await DeferredFixture.CreateAsync(); var model = fixture.Model;
        ulong folder = model.Tree!.Snapshot.Nodes.Single(node => node.Name == "waiting").Id;
        model.Focus(AnalysisTree.Key(folder), open: true); await Until(() => model.Selection.Scope == AnalysisTree.Key(folder));
        Assert.True(model.RequestSelectionAfterScan()); model.Parent(); await Until(() => model.Selection.Scope != AnalysisTree.Key(folder));
        await fixture.FinishAsync(); await Until(() => model.CanReviewDeferredSelection);
        using var review = model.CaptureDeferredSelectionReview(); Assert.Equal(2, review.Selection.NodeIds.Length);
        Assert.All(review.Selection.NodeIds, id => Assert.StartsWith("next-", review.Node(id).Name));
        Assert.Empty(model.Selection.Marks); model.ApplyReviewedDeferredSelection(review); Assert.Equal(2, model.Selection.Marks.Count); Assert.Empty(model.Selection.Basket);
        Assert.Throws<InvalidOperationException>(() => model.ApplyReviewedDeferredSelection(review));
    }
    [AvaloniaFact]
    public async Task DeferredSelectionOtherScopeExplicitlyUsesTheFinalObservedRanking()
    {
        await using var fixture = await DeferredFixture.CreateAsync(); var model = fixture.Model; model.Metric = AnalysisMetric.FileSize; await model.RebuildAsync(true);
        string scope = AnalysisTree.OtherKey("n:1", 1); model.Selection.Navigate(scope); await model.RebuildAsync(true);
        Assert.True(model.RequestSelectionAfterScan()); Assert.Contains("final observed ranking", model.DeferredSelectionText);
        await fixture.FinishAsync(); await Until(() => model.CanReviewDeferredSelection); using var review = model.CaptureDeferredSelectionReview();
        Assert.Equal("current.txt", review.Node(Assert.Single(review.Selection.NodeIds)).Name); Assert.Empty(model.Selection.Marks);
    }
    [AvaloniaFact]
    public async Task DeferredSelectionCanBeRequestedWhilePausedAndWaitsForActualCompletion()
    {
        await using var fixture = await DeferredFixture.CreateAsync(); var model = fixture.Model; model.PauseOrResume();
        await Until(() => model.Phase is AnalysisPhase.Pausing or AnalysisPhase.Paused); Assert.True(model.RequestSelectionAfterScan());
        Assert.False(model.CanReviewDeferredSelection); model.PauseOrResume(); await fixture.FinishAsync();
        await Until(() => model.CanReviewDeferredSelection); Assert.Equal(3, model.DeferredSelectionCount); Assert.Empty(model.Selection.Marks);
    }
    [AvaloniaFact]
    public async Task DeferredSelectionRapidReplacementAndCancellationNeverAddsMarks()
    {
        await using var fixture = await DeferredFixture.CreateAsync(); var model = fixture.Model;
        for (int index = 0; index < 30; index++) Assert.True(model.RequestSelectionAfterScan());
        model.CancelDeferredSelection(); Assert.False(model.HasDeferredSelection); await fixture.FinishAsync();
        Assert.False(model.CanReviewDeferredSelection); Assert.Empty(model.Selection.Marks); Assert.Empty(model.Selection.Basket);
        Assert.Throws<InvalidOperationException>(() => model.CaptureDeferredSelectionReview());
    }
    [AvaloniaFact]
    public async Task DeferredSelectionCanceledReviewCannotReplayAndRetainsOnlyCapturedDataAfterClose()
    {
        await using var fixture = await DeferredFixture.CreateAsync(); var model = fixture.Model; Assert.True(model.RequestSelectionAfterScan());
        await fixture.FinishAsync(); await Until(() => model.CanReviewDeferredSelection);
        using var review = model.CaptureDeferredSelectionReview(); model.CancelDeferredSelection();
        Assert.Throws<InvalidOperationException>(() => model.ApplyReviewedDeferredSelection(review)); Assert.Empty(model.Selection.Marks);
        model.Dispose(); await model.CloseCompletion; Assert.Equal(AnalysisPhase.Finished, review.Snapshot.Phase); Assert.Equal(3, review.Selection.NodeIds.Length);
        Assert.All(review.Selection.NodeIds, id => Assert.Equal(AnalysisKind.File, review.Node(id).Kind));
    }
    [AvaloniaFact]
    public async Task DeferredSelectionStoppedScanDoesNotPretendToHaveCompleted()
    {
        await using var fixture = await DeferredFixture.CreateAsync(); var model = fixture.Model; Assert.True(model.RequestSelectionAfterScan()); model.Stop();
        Assert.False(model.CanReviewDeferredSelection); await fixture.FinishAsync();
        await Until(() => model.DeferredSelectionState == AnalysisDeferredSelectionState.Unavailable);
        Assert.Contains("scan did not complete", model.DeferredSelectionText); Assert.Empty(model.Selection.Marks); Assert.Empty(model.Selection.Basket);
        Assert.Throws<InvalidOperationException>(() => model.CaptureDeferredSelectionReview());
    }
    [AvaloniaFact]
    public async Task DeferredSelectionCloseCancelsTheIntentButKeepsActualNativeWorkOwned()
    {
        await using var fixture = await DeferredFixture.CreateAsync(); var model = fixture.Model; Assert.True(model.RequestSelectionAfterScan()); model.Dispose();
        Assert.False(model.CloseCompletion.IsCompleted); Assert.False(fixture.SourcesDisposed); Assert.False(model.HasDeferredSelection);
        fixture.Gate.Release.Set(); await model.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(10), DeferredCancellation);
        Assert.True(fixture.SourcesDisposed); Assert.True(fixture.SourcesDisposed); Assert.Equal(0UL, model.SourceCloseFailures);
    }
    [AvaloniaTheory]
    [InlineData(AnalysisPhase.Scanning)]
    [InlineData(AnalysisPhase.Paused)]
    [InlineData(AnalysisPhase.Finished)]
    [InlineData(AnalysisPhase.Stopped)]
    [InlineData(AnalysisPhase.Failed)]
    public async Task DeferredSelectionHistoricalPhasesCannotCreateRuntimeIntent(AnalysisPhase phase)
    {
        using var model = new DiskSpaceViewModel(Fixture() with { Phase = phase, Origin = AnalysisOrigin.ImportedInventory }); await model.RebuildAsync(true);
        Assert.False(model.CanRequestDeferredSelection); Assert.False(model.RequestSelectionAfterScan()); Assert.False(model.HasDeferredSelection);
        Assert.Empty(model.Selection.Marks); Assert.False(model.CanReviewDeferredSelection);
    }
    [AvaloniaFact]
    public async Task DeferredSelectionFinalPartialResultRemainsExplicitAndRequiresReview()
    {
        await using var fixture = await DeferredFixture.CreateAsync(partial: true); var model = fixture.Model;
        Assert.True(model.RequestSelectionAfterScan()); await fixture.FinishAsync(); await Until(() => model.CanReviewDeferredSelection);
        Assert.Contains("partial results", model.DeferredSelectionText);
        using var review = model.CaptureDeferredSelectionReview(); Assert.Equal(NamespaceCoverage.Partial, review.Snapshot.Coverage);
        Assert.Equal(3, review.Selection.NodeIds.Length); Assert.Empty(model.Selection.Marks); Assert.Empty(model.Selection.Basket);
        Assert.Contains(review.Snapshot.Roots, root => root.Disposition == AnalysisRootDisposition.UnavailablePrivateBoundary);
    }
    [AvaloniaFact]
    public async Task DeferredSelectionCapturesTheExplicitUncertainCohortRatherThanLaterEditorState()
    {
        await using var fixture = await DeferredFixture.CreateAsync(unknownAllocation: true); var model = fixture.Model;
        model.Query = "allocated>0"; model.ShowUnknownFilterResults = true; await model.RebuildAsync(true);
        Assert.True(model.Tree!.Projection.IncludesUnknownFilterResults); Assert.True(model.RequestSelectionAfterScan());
        model.ShowUnknownFilterResults = false; await model.RebuildAsync(true); await fixture.FinishAsync();
        await Until(() => model.CanReviewDeferredSelection); using var review = model.CaptureDeferredSelectionReview();
        Assert.True(review.View.ShowUnknown); Assert.Contains("uncertain results", review.Selection.FilterSummary); Assert.Equal(3, review.Selection.NodeIds.Length);
        Assert.Empty(model.Selection.Marks); Assert.Empty(model.Tree!.Projection.MatchingNodes);
    }
    [AvaloniaFact]
    public async Task DeferredSelectionReviewPagesItsExactSetAndKeepsTheDefaultPreserving()
    {
        using var styles = new ProductStyles(); await using var fixture = await DeferredFixture.CreateAsync(270); var model = fixture.Model;
        Assert.True(model.RequestSelectionAfterScan()); await fixture.FinishAsync(); await Until(() => model.CanReviewDeferredSelection);
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            Click(window, "Review deferred matches"); await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(list => AutomationLabel(list) == "Deferred captured matches")); Frame(window);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationLabel(list) == "Deferred captured matches");
            Assert.Equal(128, list.ItemCount); Click(window, "Next deferred matches"); Assert.Equal(128, list.ItemCount); Click(window, "Next deferred matches"); Assert.Equal(15, list.ItemCount);
            Assert.Contains(list.Items.Cast<string>(), row => row.Contains("waiting/next-", StringComparison.Ordinal));
            Assert.Empty(model.Selection.Marks); Click(window, "Keep marks unchanged / Close review"); Assert.True(model.CanReviewDeferredSelection);
            Click(window, "Cancel deferred selection"); Assert.False(model.HasDeferredSelection); Assert.Empty(model.Selection.Marks); Assert.Empty(model.Selection.Basket);
            Assert.Equal(new byte[16], File.ReadAllBytes(Path.Combine(fixture.Input, "waiting", "next-0000.txt")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
