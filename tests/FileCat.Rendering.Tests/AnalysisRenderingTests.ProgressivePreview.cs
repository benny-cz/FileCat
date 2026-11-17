using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static async Task StartReadyPreviewAsync(DiskSpaceViewModel model, string input)
    {
        await model.StartAsync([input]); await Until(() => model.Phase == AnalysisPhase.Finished);
        await model.RebuildAsync(true); await Until(() => model.CanOpenRecordedResult);
    }

    [AvaloniaFact]
    public async Task NativeProgressivePreviewPublishesBeforeProducerReturnsThenKeepsReviewReferencesDuringFullHandoff()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "keep.bin"), "keep");
        using var gate = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void BeforeChildren(AnalysisEntry _, CancellationToken token) { entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(6)); }
        IAnalysisBackend Prepare(string path) => OperatingSystem.IsWindows()
            ? new WindowsAnalysisBackend(path, null, BeforeChildren, null) : new UnixAnalysisBackend(path, null, BeforeChildren, null);
        using var model = new DiskSpaceViewModel(fixture.Services, Prepare, indexedStorage: true);
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool); int handles = AnalysisNativeHandles.Count;
        try
        {
            window.AddSession(model); window.Show(); var clock = Stopwatch.StartNew(); await model.StartAsync([input]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Until(() => model.Tree?.Snapshot.Nodes.Length > 0);
            Assert.True(model.Running); Assert.False(model.CanOpenRecordedResult); Assert.True(model.CanRequestDeferredSelection);
            Assert.Contains("bounded", model.RuntimePreviewNotice); Assert.DoesNotContain("stops with partial", model.AcquisitionNotice ?? "", StringComparison.Ordinal);
            TestContext.Current.TestOutputHelper?.WriteLine("First visible bounded preview: " + clock.ElapsedMilliseconds + " ms; producer still blocked.");
            gate.Set(); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true); await Until(() => model.CanOpenRecordedResult);
            var old = model.Tree!; var root = old.Snapshot.Nodes.Single(n => n.ParentId == 0); var rootKey = AnalysisTree.Key(root.Id);
            model.Focus(rootKey, open: true); await Until(() => model.Selection.Scope == rootKey);
            var file = old.Snapshot.Nodes.Single(n => n.Name == "keep.bin"); model.Focus(AnalysisTree.Key(file.Id), toggleMark: true); model.StageSelection();
            var marks = model.Selection.Marks; var basket = model.Selection.Basket; using var retained = model.CaptureDisplayedRevision();
            Click(window, "Open recorded result"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30)); Frame(window);
            Assert.Same(old, model.Tree); Assert.Same(marks, model.Selection.Marks); Assert.Same(basket, model.Selection.Basket);
            Assert.Equal(old.Snapshot.Identity, retained.Snapshot.Identity); Assert.False(model.CanRescan); Assert.False(model.CanOpenRecordedResult);
            Assert.Contains("Captured preview", model.RuntimePreviewNotice); Assert.Contains("keep.bin", IndexedDetails(window).Text); Assert.True(IndexedSelectionCount(window, 0, 0));
            model.Dispose(); await model.CloseCompletion;
            Click(window, "Rescan all"); await Until(() => window.SessionCount == 3, () => ComparisonText(window), TimeSpan.FromSeconds(30));
            Assert.Equal(old.Snapshot.Identity, retained.Snapshot.Identity); Assert.Equal("keep", File.ReadAllText(Path.Combine(input, "keep.bin")));
        }
        finally { gate.Set(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); model.Dispose(); await model.CloseCompletion; }
        await AssertAllRuntimePlacesAvailableAsync(pool); Assert.Equal(handles, AnalysisNativeHandles.Count);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeProgressivePreviewCanceledPublicationRestoresTheOwnerOrClosesItWithoutResurrectingAClosedModel(bool close)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "keep.bin"), "keep");
        using var model = new DiskSpaceViewModel(fixture.Services, indexedStorage: true); var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        using var cancel = new CancellationTokenSource(); AnalysisIndexedAcquisition? moved = null;
        try
        {
            window.AddSession(model); window.Show(); await StartReadyPreviewAsync(model, input); var old = model.Tree; using var retained = model.CaptureDisplayedRevision();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.AddPreparedIndexedSessionAsync(model.AcquireRecordedDatasetAsync,
                model.Metric, null, model.PublishedReportView, cancel.Token, fixture.Services, runtimeHandoff: () =>
                {
                    var transfer = model.DetachRecordedResultOwner(); moved = transfer.Acquisition;
                    if (close) model.Dispose(); cancel.Cancel(); return transfer;
                }));
            await window.WaitForIndexedCloseAsync(); Assert.NotNull(moved); Assert.Equal(1, window.SessionCount);
            Assert.Contains(retained.Snapshot.Nodes, node => node.Name == "keep.bin");
            if (close) { await model.CloseCompletion; Assert.Throws<ObjectDisposedException>(() => moved.RuntimeSession); }
            else
            {
                Assert.Same(old, model.Tree); Assert.True(model.CanOpenRecordedResult); Assert.True(moved.HasRuntimeRescanContext);
                Click(window, "Open recorded result"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30));
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); model.Dispose(); await model.CloseCompletion; }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }

    [AvaloniaFact]
    public async Task NativeProgressivePreviewCapacityRefusalDoesNotQueryOrDetachItsRecordedResult()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        using var model = new DiskSpaceViewModel(fixture.Services, indexedStorage: true); var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            window.AddSession(model); window.Show(); await StartReadyPreviewAsync(model, input);
            for (int n = 1; n < AnalysisLimits.Sessions; n++) window.AddSession(new DiskSpaceViewModel(fixture.Services, ImportedFixture()));
            int queries = 0, transfers = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => window.AddPreparedIndexedSessionAsync(token => { queries++; return model.AcquireRecordedDatasetAsync(token); },
                model.Metric, null, model.PublishedReportView, default, fixture.Services, runtimeHandoff: () => { transfers++; return model.DetachRecordedResultOwner(); }));
            Assert.Equal(0, queries); Assert.Equal(0, transfers);
            // Adding seven tabs can let the preview's periodic projection begin. A temporary
            // view update does not mean capacity refusal lost its recorded acquisition.
            await Until(() => model.CanOpenRecordedResult, () => model.RecordedResultUnavailableReason ?? model.Status);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); model.Dispose(); await model.CloseCompletion; }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }

    [AvaloniaFact]
    public async Task NativeProgressivePreviewUnavailablePrivateStorageUsesMemoryWithoutPretendingToHaveAFullDataset()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); await CorruptProfileForMemoryAsync(fixture);
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); byte[] state = File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile);
        using var model = new DiskSpaceViewModel(fixture.Services, indexedStorage: true);
        try
        {
            await model.StartAsync([input]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true);
            Assert.False(model.IsIndexedRuntimePreview); Assert.False(model.CanOpenRecordedResult); Assert.Null(model.RuntimePreviewNotice);
            Assert.Contains("bounded memory", model.AcquisitionNotice); Assert.Equal(state, File.ReadAllBytes(fixture.Services.Paths.AnalysisPrivateStateFile));
        }
        finally { model.Dispose(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task NativeProgressivePreviewCapturedInventoryCannotCreateARuntimeHandoff()
    {
        using var fixture = new PreparationFixture(); using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        Assert.False(model.CanOpenRecordedResult); Assert.False(model.IsIndexedRuntimePreview); Assert.Null(model.RuntimePreviewNotice);
        Assert.Throws<InvalidOperationException>(() => model.DetachRecordedResultOwner());
        model.Dispose(); await model.CloseCompletion;
    }

    [AvaloniaFact]
    public async Task NativeProgressivePreviewStopOpeningKeepsItsOwnerAndCanRetryAfterQueuedWorkIsReleased()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        using var model = new DiskSpaceViewModel(fixture.Services, indexedStorage: true); var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission());
        using var gate = new ManualResetEventSlim(); using var entered = new CountdownEvent(fixture.Services.Io.ThreadsPerDevice); var blocked = new List<Task>();
        try
        {
            window.AddSession(model); window.Show(); await StartReadyPreviewAsync(model, input); var old = model.Tree;
            for (int n = 0; n < fixture.Services.Io.ThreadsPerDevice; n++)
                blocked.Add(fixture.Services.Io.RunOwned(PathUtil.GetDeviceKey(input), IoPriority.Background, _ => { entered.Signal(); gate.Wait(TimeSpan.FromSeconds(6)); return true; }));
            await Task.Run(() => Assert.True(entered.Wait(TimeSpan.FromSeconds(3))));
            Click(window, "Open recorded result");
            var stop = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Stop opening"); Assert.True(stop.IsVisible);
            Click(window, "Stop opening"); gate.Set(); await Task.WhenAll(blocked);
            await Until(() => !stop.IsVisible); Assert.Same(old, model.Tree); Assert.True(model.CanOpenRecordedResult); Assert.Equal(1, window.SessionCount);
            Click(window, "Open recorded result"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30));
        }
        finally { gate.Set(); await Task.WhenAll(blocked); window.ForceClose(); await window.WaitForIndexedCloseAsync(); model.Dispose(); await model.CloseCompletion; }
    }

    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FolderHierarchy, true)]
    [InlineData(AnalysisGroupingDimension.FileType, false)]
    [InlineData(AnalysisGroupingDimension.FileType, true)]
    public async Task NativeProgressivePreviewKeepsAnExistingGroupAndExplainsWhyOtherReturnsToItsParent(AnalysisGroupingDimension dimension, bool other)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        for (int n = 0; n < 24; n++) File.WriteAllText(Path.Combine(input, $"entry-{n:D2}.bin"), "keep");
        using var model = new DiskSpaceViewModel(fixture.Services, indexedStorage: true) { Grouping = dimension, Metric = AnalysisMetric.FileSize };
        var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(model); window.Show(); await StartReadyPreviewAsync(model, input); var before = model.Tree!;
            string parent = dimension == AnalysisGroupingDimension.FolderHierarchy
                ? AnalysisTree.Key(before.Snapshot.Nodes.Single(node => node.ParentId == 0).Id)
                : Assert.Single(before.Groups.Values, group => group.Kind == AnalysisGroupKind.Entries && group.Members.Length == 24).Definition.Key;
            string scope = other ? AnalysisTree.OtherKey(parent, 10) : parent;
            model.Focus(scope, open: true); await Until(() => model.Selection.Scope == scope && model.CanOpenRecordedResult);
            Click(window, "Open recorded result"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30)); Frame(window);
            Assert.Equal(scope, model.Selection.Scope); Assert.Same(before, model.Tree);
            var list = IndexedList(window);
            Assert.Equal(24, dimension == AnalysisGroupingDimension.FolderHierarchy
                ? list.Items.OfType<AnalysisHierarchyRow>().Count() : list.Items.OfType<AnalysisDatasetGroupRow>().Count());
            Assert.Equal(other, ComparisonText(window).Contains("Other subset was cleared", StringComparison.Ordinal));
            Assert.True(IndexedSelectionCount(window, 0, 0));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); model.Dispose(); await model.CloseCompletion; }
    }
}
