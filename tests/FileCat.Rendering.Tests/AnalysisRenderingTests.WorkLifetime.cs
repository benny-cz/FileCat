using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task AQueuedCatalogClickAfterWindowCloseCannotStartWorkOrEscapeTheClosedOwner()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(model); window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open saved…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var browse = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Browse saved analyses");
            window.ForceClose(); browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.WaitForIndexedCloseAsync(); Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisSnapshotsDirectory));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task OpenSavedReviewReservesItsDestinationBeforeReadingAndReleasesItOnCancel()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        var window = new DiskSpaceWindow(); var other = new DiskSpaceWindow();
        try
        {
            window.AddSession(model); for (int i = 0; i < 6; i++) window.AddSession(new DiskSpaceViewModel(Fixture()));
            window.Show(); other.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Assert.True(other.CanAddSession);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open saved…" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Assert.False(other.CanAddSession);
            Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisSnapshotsDirectory));
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => other.CanAddSession); Assert.Equal(7, window.SessionCount);
        }
        finally { window.ForceClose(); other.ForceClose(); await Task.WhenAll(window.WaitForIndexedCloseAsync(), other.WaitForIndexedCloseAsync()); }
    }

    [AvaloniaFact]
    public async Task EightAnalysisAdmissionIsSharedAcrossWindowsAndReleasedAfterActualClosure()
    {
        var first = new DiskSpaceWindow(); var second = new DiskSpaceWindow(); var next = new DiskSpaceWindow();
        using var rejected = new DiskSpaceViewModel(Fixture());
        try
        {
            first.Show(); second.Show(); next.Show();
            for (int i = 0; i < 4; i++) { first.AddSession(new DiskSpaceViewModel(Fixture())); second.AddSession(new DiskSpaceViewModel(Fixture())); }
            Assert.False(next.CanAddSession); Assert.Throws<InvalidOperationException>(() => next.AddSession(rejected));
            first.ForceClose(); await first.WaitForIndexedCloseAsync();
            Assert.True(next.CanAddSession); next.AddSession(rejected); Assert.Equal(1, next.SessionCount);
        }
        finally
        {
            first.ForceClose(); second.ForceClose(); next.ForceClose();
            await Task.WhenAll(first.WaitForIndexedCloseAsync(), second.WaitForIndexedCloseAsync(), next.WaitForIndexedCloseAsync());
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedWindowKeepsItsSlotUntilTheActualProjectionOrNavigationReturns(bool navigation)
    {
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0; AnalysisOrderedRows? lateOrder = null;
        using var model = new DiskSpaceViewModel(Fixture(), (rows, field, descending, token) =>
        {
            if (Interlocked.Increment(ref calls) == (navigation ? 2 : 1))
            {
                entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15));
                // A native/noncooperative worker may return a real resource after cancellation.
                return lateOrder = AnalysisTableOrder.Apply(rows, field, descending, CancellationToken.None);
            }
            return AnalysisTableOrder.Apply(rows, field, descending, token);
        });
        var old = new DiskSpaceWindow(); var replacement = new DiskSpaceWindow(); Task? projection = null;
        using var refused = new DiskSpaceViewModel(Fixture());
        try
        {
            old.AddSession(model); old.Show(); replacement.Show();
            for (int i = 0; i < 7; i++) replacement.AddSession(new DiskSpaceViewModel(Fixture()));
            if (navigation) { await model.RebuildAsync(true); model.Focus("n:1", open: true); }
            else projection = model.RebuildAsync(true);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); old.ForceClose();
            var closing = old.WaitForIndexedCloseAsync(); Assert.False(closing.IsCompleted); Assert.False(model.CloseCompletion.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => replacement.AddSession(refused));
            release.Set(); if (projection is not null) await projection;
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(lateOrder); Assert.Throws<ObjectDisposedException>(() => lateOrder.Rows);
            replacement.AddSession(refused); Assert.Equal(8, replacement.SessionCount);
        }
        finally
        {
            release.Set(); if (projection is not null) await projection;
            old.ForceClose(); replacement.ForceClose();
            await Task.WhenAll(old.WaitForIndexedCloseAsync(), replacement.WaitForIndexedCloseAsync());
        }
    }

    [AvaloniaFact]
    public async Task FailedCloseCannotReturnItsAdmissionOrBeHiddenByAReplacementWindow()
    {
        // Use a separate pool for the permanently failed close; production windows all use Shared.
        var pool = new AnalysisSessionAdmission(); var failed = pool.Acquire();
        var slots = Enumerable.Range(0, 7).Select(_ => pool.Acquire()).ToArray();
        await Assert.ThrowsAsync<IOException>(() => failed.CloseAfterAsync(Task.FromException(new IOException("injected unverified close"))));
        Assert.False(pool.HasCapacity); Assert.Throws<InvalidOperationException>(() => pool.Acquire());
        Assert.Contains("restart FileCat", pool.Refusal);
        await slots[0].CloseAfterAsync(Task.CompletedTask); Assert.True(pool.HasCapacity);
        var replacement = pool.Acquire(); Assert.False(pool.HasCapacity);
        await replacement.CloseAfterAsync(Task.CompletedTask);
        foreach (var slot in slots.Skip(1)) await slot.CloseAfterAsync(Task.CompletedTask);
    }
}
