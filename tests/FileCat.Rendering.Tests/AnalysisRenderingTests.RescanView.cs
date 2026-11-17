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
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRescanViewKeepsOldMarksBasketAndSavedRevisionWhileOpeningAReusableSuccessor(bool fallback)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        if (fallback) await CorruptProfileForMemoryAsync(fixture);
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "old.bin"), "old");
        using var model = new DiskSpaceViewModel(fixture.Services); DiskSpaceViewModel? next = null, third = null;
        try
        {
            await model.StartAsync([input]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true); await Until(() => model.CanRescan);
            var old = model.Tree!; var focused = old.Snapshot.Nodes.Single(n => n.Name == "old.bin");
            model.Focus(AnalysisTree.Key(focused.Id), toggleMark: true); model.StageSelection();
            var marks = model.Selection.Marks; var basket = model.Selection.Basket;
            using var retained = model.CaptureDisplayedRevision();
            File.WriteAllText(Path.Combine(input, "new.bin"), "new");
            Assert.True(model.CanRescan); next = await model.CreateRescannedAsync(); Assert.NotNull(next);
            Assert.Same(old, model.Tree); Assert.Same(marks, model.Selection.Marks); Assert.Same(basket, model.Selection.Basket);
            Assert.Equal(old.Snapshot.Identity, retained.Snapshot.Identity); Assert.DoesNotContain(old.Snapshot.Nodes, n => n.Name == "new.bin");
            Assert.Equal(old.Snapshot.Identity.SessionId, next.Tree!.Snapshot.Identity.SessionId);
            Assert.Equal(old.Snapshot.Identity.Epoch + 1, next.Tree.Snapshot.Identity.Epoch);
            Assert.Contains(next.Tree.Snapshot.Nodes, n => n.Name == "new.bin"); Assert.Empty(next.Selection.Marks); Assert.Empty(next.Selection.Basket);
            Assert.False(model.CanRescan); Assert.True(next.CanRescan);
            if (focused.Times is { CreatedUtc: not null, CreatedPrecisionTicks: not null })
                Assert.Equal("old.bin", next.Tree.Items[next.Selection.Focus!].Name);
            else Assert.Null(next.Selection.Focus);
            model.Dispose(); await model.CloseCompletion;
            Assert.Equal(old.Snapshot.Identity, retained.Snapshot.Identity);
            third = await next.CreateRescannedAsync(); Assert.NotNull(third);
            Assert.Equal(old.Snapshot.Identity.Epoch + 2, third.Tree!.Snapshot.Identity.Epoch);
            Assert.Equal("old", File.ReadAllText(Path.Combine(input, "old.bin")));
        }
        finally
        {
            third?.Dispose(); if (third is not null) await third.CloseCompletion;
            next?.Dispose(); if (next is not null) await next.CloseCompletion;
            model.Dispose(); await model.CloseCompletion;
        }
    }

    [AvaloniaFact]
    public async Task NativeRescanViewRefusesAReplacedRootAndCanRetryAfterRestoringTheOriginalObject()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"), moved = Path.Combine(fixture.Root, "original"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "keep.bin"), "keep");
        using var model = new DiskSpaceViewModel(fixture.Services); DiskSpaceViewModel? next = null;
        try
        {
            await model.StartAsync([input]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true); await Until(() => model.CanRescan);
            var before = model.Tree;
            Directory.Move(input, moved); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "replacement.bin"), "replacement");
            Assert.Null(await model.CreateRescannedAsync()); Assert.Same(before, model.Tree); Assert.True(model.CanRescan);
            Assert.Contains("changed", model.Status, StringComparison.OrdinalIgnoreCase);
            File.Delete(Path.Combine(input, "replacement.bin")); Directory.Delete(input); Directory.Move(moved, input);
            next = await model.CreateRescannedAsync(); Assert.NotNull(next); Assert.Contains(next.Tree!.Snapshot.Nodes, n => n.Name == "keep.bin");
        }
        finally { next?.Dispose(); if (next is not null) await next.CloseCompletion; model.Dispose(); await model.CloseCompletion; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRescanViewStopOrCloseDuringLateBindingOwnsAndClosesTheReturnedSource(bool close)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        using var gate = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IAnalysisBackend? late = null; int calls = 0;
        using var model = new DiskSpaceViewModel(fixture.Services, path =>
        {
            if (Interlocked.Increment(ref calls) == 1) return NativeRoot(path);
            late = NativeRoot(path); entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(15)); return late;
        });
        await model.StartAsync([input]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true); await Until(() => model.CanRescan);
        var old = model.Tree; var run = model.CreateRescannedAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (close) model.Dispose(); else model.Stop();
            Assert.False(run.IsCompleted); if (close) Assert.False(model.CloseCompletion.IsCompleted);
        }
        finally { gate.Set(); }
        Assert.Null(await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotNull(late); Assert.Throws<ObjectDisposedException>(() => late.ValidateReadScope(late.RootEntry));
        if (!close) { Assert.Same(old, model.Tree); Assert.True(model.CanRescan); }
        model.Dispose(); await model.CloseCompletion;
    }

    [AvaloniaFact]
    public async Task NativeRescanViewReservesItsPlaceBeforeAnySourceIo()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "one.bin"), "one");
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        using var model = new DiskSpaceViewModel(fixture.Services); var inert = new List<DiskSpaceViewModel>();
        try
        {
            window.AddSession(model); window.Show(); await model.StartAsync([input]);
            await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true); await Until(() => model.CanRescan);
            for (int n = 0; n < AnalysisLimits.Sessions - 1; n++) { var offline = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); inert.Add(offline); window.AddSession(offline); }
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 0;
            window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rescan all" && b.IsVisible && b.IsEffectivelyVisible);
            Assert.False(button.IsEnabled); Assert.Equal(8, window.SessionCount);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(30);
            Assert.Equal(8, window.SessionCount); Assert.True(model.CanRescan); Assert.Contains("Eight analyses", model.Status);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; foreach (var offline in inert) await offline.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task NativeRescanViewButtonOpensAnotherTabAndKeepsTheEarlierEpochUntouched()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input); File.WriteAllText(Path.Combine(input, "one.bin"), "one");
        var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission());
        using var model = new DiskSpaceViewModel(fixture.Services);
        try
        {
            window.AddSession(model); window.Show(); await model.StartAsync([input]);
            await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true); await Until(() => model.CanRescan);
            var before = model.Tree; File.WriteAllText(Path.Combine(input, "two.bin"), "two"); window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rescan all");
            Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.SessionCount == 2);
            Assert.Same(before, model.Tree); Assert.DoesNotContain(before!.Snapshot.Nodes, n => n.Name == "two.bin");
            Assert.Equal(1, window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex);
            Assert.False(model.CanRescan);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task NativeRescanViewReentrantWindowCloseWaitsForTheUnpublishedSuccessorAndItsAdmissionPlace()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        using var model = new DiskSpaceViewModel(fixture.Services);
        try
        {
            window.AddSession(model); window.Show(); await model.StartAsync([input]);
            await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true); await Until(() => model.CanRescan);
            model.Changed += () => { if (model.Status.StartsWith("Rescan finished.", StringComparison.Ordinal)) window.ForceClose(); };
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rescan all");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !window.IsVisible); await window.WaitForIndexedCloseAsync();
            var places = new List<AnalysisSessionAdmission.Slot>();
            try { for (int n = 0; n < AnalysisLimits.Sessions; n++) places.Add(pool.Acquire()); }
            finally { foreach (var place in places) await place.CloseAfterAsync(Task.CompletedTask); }
            Assert.Equal(AnalysisLimits.Sessions, places.Count);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task NativeRescanViewOfflineInventoriesNeverPreparePrivateStateOrBindLocators()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        Assert.False(model.CanRescan); Assert.Null(await model.CreateRescannedAsync());
        Assert.False(File.Exists(fixture.Services.Paths.AnalysisPrivateStateFile));
        Assert.Contains("offline observations", model.Status); model.Dispose(); await model.CloseCompletion;
    }
}
