using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static async Task<AnalysisIndexedAcquisition> PrepareOwnedRuntimeAsync(PreparationFixture fixture, string input, bool indexed = true)
    {
        var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, indexedStorage: indexed);
        try { await StartAcquiredAsync(owner, fixture, input); return owner; }
        catch { await owner.DisposeAsync(); throw; }
    }

    private static async Task AssertAllRuntimePlacesAvailableAsync(AnalysisSessionAdmission pool)
    {
        var places = new List<AnalysisSessionAdmission.Slot>();
        try { for (int n = 0; n < AnalysisLimits.Sessions; n++) places.Add(pool.Acquire()); }
        finally { foreach (var place in places) await place.CloseAfterAsync(Task.CompletedTask); }
        Assert.Equal(AnalysisLimits.Sessions, places.Count);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeOwnedRuntimeViewAdoptsBothRepresentationsAndClosesItsOwner(bool indexed)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "keep.bin"), "keep");
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        int handles = AnalysisNativeHandles.Count; AnalysisIndexedAcquisition? owner = null;
        try
        {
            var view = await window.AddOwnedAcquisitionAsync(fixture.Services, async _ => owner = await PrepareOwnedRuntimeAsync(fixture, input, indexed));
            window.Show(); Frame(window); Assert.Equal(1, window.SessionCount);
            if (indexed) { Assert.NotNull(view.Indexed); Assert.Null(view.Memory); }
            else { Assert.Null(view.Indexed); Assert.NotNull(view.Memory); Assert.True(view.Memory.CanRescan); }
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rescan all").IsEnabled);
            window.ForceClose(); await window.WaitForIndexedCloseAsync();
            Assert.NotNull(owner); Assert.Throws<ObjectDisposedException>(() => owner.RuntimeSession);
            await AssertAllRuntimePlacesAvailableAsync(pool); Assert.Equal("keep", File.ReadAllText(Path.Combine(input, "keep.bin")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); if (owner is not null) await owner.DisposeAsync(); }
        Assert.Equal(handles, AnalysisNativeHandles.Count);
    }

    [AvaloniaFact]
    public async Task NativeOwnedRuntimeViewRescanRestoresScopedCrossPageFocusWithoutRetaggingReviewEntries()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        for (int n = 0; n < 170; n++) File.WriteAllText(Path.Combine(input, $"f{n:000}.bin"), "keep");
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        int handles = AnalysisNativeHandles.Count; AnalysisIndexedAcquisition? original = null;
        try
        {
            var view = await window.AddOwnedAcquisitionAsync(fixture.Services, async _ => original = await PrepareOwnedRuntimeAsync(fixture, input),
                AnalysisSavedView.Default with { Metric = AnalysisMetric.FileSize, Sort = AnalysisTableSort.Name, SortDescending = false, Chart = "Table" });
            window.Show(); Frame(window); var list = IndexedList(window);
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>()); Click(window, "Explore selected folder");
            await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "f000.bin"));
            Click(window, "Next entries"); await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "f169.bin"));
            list.SelectedItem = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "f169.bin");
            await Until(() => IndexedDetails(window).Text?.Contains("f169.bin", StringComparison.Ordinal) == true);
            Click(window, "Toggle focused mark"); Click(window, "Add to review basket"); Assert.True(IndexedSelectionCount(window, 1, 1));
            var identity = view.Indexed!.Descriptor.Identity; File.WriteAllText(Path.Combine(input, "a-new.bin"), "new");
            Click(window, "Rescan all"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30)); Frame(window);
            Assert.Equal(1, window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex);
            Assert.Equal("f169.bin", Assert.IsType<AnalysisHierarchyRow>(IndexedList(window).SelectedItem).Path.Name);
            Assert.Contains("f169.bin", IndexedDetails(window).Text); Assert.True(IndexedSelectionCount(window, 0, 0));
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex = 0; Frame(window);
            Assert.True(IndexedSelectionCount(window, 1, 1)); Assert.Equal(identity, view.Indexed.Descriptor.Identity);
            Assert.DoesNotContain(list.Items.OfType<AnalysisHierarchyRow>(), row => row.Path.Name == "a-new.bin");
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rescan all").IsEnabled);
            Click(window, "Clear marks"); Click(window, "Clear review basket");
            // Closing the entire predecessor also releases its data/index handles. Eight tab
            // places do not override the separate 128-handle global resource ceiling.
            Click(window, "Close analysis"); await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Stop and close"));
            Click(window, "Stop and close"); await Until(() => window.SessionCount == 1); await window.WaitForIndexedCloseAsync(); Frame(window);
            Assert.Throws<ObjectDisposedException>(() => original!.RuntimeSession);
            Click(window, "Rescan all");
            await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(input, "f169.bin")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); if (original is not null) await original.DisposeAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
        Assert.Equal(handles, AnalysisNativeHandles.Count);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeOwnedRuntimeViewRejectsFullOrClosedWindowsBeforeCallingTheFactory(bool close)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool); window.Show();
        try
        {
            if (close) window.ForceClose();
            else for (int n = 0; n < AnalysisLimits.Sessions; n++) window.AddSession(new DiskSpaceViewModel(fixture.Services, ImportedFixture()));
            int calls = 0;
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => window.AddOwnedAcquisitionAsync(fixture.Services, _ =>
            { calls++; throw new InvalidOperationException("Factory must not run"); }));
            Assert.Equal(0, calls);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeOwnedRuntimeViewCancellationOrWindowCloseWaitsForTheLateOwner(bool close)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool); window.Show();
        using var cancel = new CancellationTokenSource(); var returned = new TaskCompletionSource<AnalysisIndexedAcquisition>(TaskCreationOptions.RunContinuationsAsynchronously);
        int handles = AnalysisNativeHandles.Count; var owner = await PrepareOwnedRuntimeAsync(fixture, input);
        var run = window.AddOwnedAcquisitionAsync(fixture.Services, _ => returned.Task, cancellation: cancel.Token);
        try
        {
            if (close) window.ForceClose(); else cancel.Cancel();
            Assert.False(run.IsCompleted); Assert.Equal(0, window.SessionCount);
            var closing = window.WaitForIndexedCloseAsync(); Assert.False(closing.IsCompleted);
            returned.TrySetResult(owner); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            await closing; Assert.Throws<ObjectDisposedException>(() => owner.RuntimeSession);
            await AssertAllRuntimePlacesAvailableAsync(pool);
        }
        finally { returned.TrySetResult(owner); window.ForceClose(); await window.WaitForIndexedCloseAsync(); await owner.DisposeAsync(); }
        Assert.Equal(handles, AnalysisNativeHandles.Count);
    }

    [AvaloniaFact]
    public async Task NativeOwnedRuntimeViewDataOnlyDatasetHasNoRescanAuthority()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        await using var owner = await PrepareOwnedRuntimeAsync(fixture, input);
        await using var lease = await owner.RuntimeSession.AcquireDatasetAsync(); Assert.NotNull(lease);
        var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission());
        try
        {
            await window.AddIndexedSessionAsync(lease, services: fixture.Services); window.Show(); Frame(window);
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rescan all");
            Assert.False(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(30);
            Assert.Equal(1, window.SessionCount);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task NativeOwnedRuntimeViewReplacedRootKeepsOldResultAndAllowsRetryAfterRestoration()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"), moved = Path.Combine(fixture.Root, "original"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "keep.bin"), "keep"); var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission());
        try
        {
            var old = await window.AddOwnedAcquisitionAsync(fixture.Services, _ => PrepareOwnedRuntimeAsync(fixture, input)); window.Show(); Frame(window);
            Directory.Move(input, moved); Directory.CreateDirectory(input); Click(window, "Rescan all");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Rescan unavailable:", StringComparison.Ordinal) == true));
            Assert.Equal(1, window.SessionCount); Assert.NotNull(old.Indexed!.Descriptor);
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rescan all").IsEnabled);
            Directory.Delete(input); Directory.Move(moved, input); Click(window, "Rescan all"); await Until(() => window.SessionCount == 2);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(input, "keep.bin")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task NativeOwnedRuntimeViewNativeHandleRefusalPreservesTheEpochAndCanRetryAfterRelease()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "keep.bin"), "keep");
        int handles = AnalysisNativeHandles.Count;
        var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission()); var reservations = new List<IDisposable>();
        try
        {
            var old = await window.AddOwnedAcquisitionAsync(fixture.Services, _ => PrepareOwnedRuntimeAsync(fixture, input)); window.Show(); Frame(window);
            var identity = old.Indexed!.Descriptor.Identity;
            while (AnalysisNativeHandles.Count < AnalysisLimits.NativeHandles) reservations.Add(AnalysisNativeHandles.Acquire());
            Click(window, "Rescan all");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Rescan unavailable:", StringComparison.Ordinal) == true),
                () => ComparisonText(window), TimeSpan.FromSeconds(30));
            Assert.Equal(1, window.SessionCount); Assert.Equal(identity, old.Indexed.Descriptor.Identity);
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rescan all").IsEnabled);
            foreach (var reservation in reservations) reservation.Dispose(); reservations.Clear();
            Click(window, "Rescan all"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(input, "keep.bin")));
        }
        finally { foreach (var reservation in reservations) reservation.Dispose(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(handles, AnalysisNativeHandles.Count);
    }
}
