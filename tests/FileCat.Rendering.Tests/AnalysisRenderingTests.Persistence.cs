using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task CatalogSelectionRestoresPrivateViewWithoutConnectingOrRetainingMarks()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        model.Metric = AnalysisMetric.Files; model.Chart = "Table"; model.Query = "name:*.bin"; model.CaseSensitive = true;
        model.RootFilter = model.Tree!.Snapshot.Roots[0].Id; model.Selection.Navigate("n:1"); model.Selection.ToggleMark(1);
        await model.RebuildAsync(true); var receipt = await model.SaveNewAsync("view-settings.fcdsa");
        using (var catalog = await model.ReadSavedCatalogAsync())
        {
            var entry = Assert.Single(catalog.Entries); Assert.Equal(receipt.InventoryId, entry.InventoryId);
            using var offline = await model.OpenSavedAsync(entry.Name, entry); await offline.RebuildAsync(true);
            Assert.Equal(model.Metric, offline.Metric); Assert.Equal(model.Chart, offline.Chart); Assert.Equal(model.Query, offline.Query);
            Assert.Equal("1", offline.RootFilter); Assert.Equal("n:1", offline.Selection.Scope); Assert.True(offline.CaseSensitive);
            Assert.Empty(offline.Selection.Marks); Assert.Empty(offline.Selection.Basket); Assert.False(offline.Running);
        }
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open saved…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Browse saved analyses").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(10); ComboBox? list = null;
            while (DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                list = window.GetVisualDescendants().OfType<ComboBox>().Single(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Saved analyses");
                if (list.ItemCount > 0) break; await Task.Delay(10);
            }
            Assert.NotNull(list); Assert.Equal(1, list.ItemCount); list.SelectedIndex = 0;
            Assert.Equal(receipt.Name, window.GetVisualDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Saved inventory filename").Text);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open observations").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            deadline = DateTime.UtcNow.AddSeconds(10);
            while (window.SessionCount != 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(2, window.SessionCount); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal("name:*.bin", window.GetVisualDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Filter observations" && t.IsEffectivelyVisible).Text);
            Assert.Contains(window.GetVisualDescendants().OfType<ComboBox>(), c => c.IsEffectivelyVisible && c.SelectedItem as string == "Table");
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task PrivateIoNonresponseRequestsCancellationButRetainsItsActualWork()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var release = new ManualResetEventSlim();
        using var model = new DiskSpaceViewModel(fixture.Services);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int observedCancellation = 0;
        var work = model.RunPrivateIoAsync(fixture.Root, token =>
        {
            entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(25));
            if (token.IsCancellationRequested) Interlocked.Increment(ref observedCancellation);
            token.ThrowIfCancellationRequested(); return 17;
        }, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!model.Status.Contains("device is not responding", StringComparison.Ordinal) && DateTime.UtcNow < deadline) await Task.Delay(25);
            Assert.Contains("device is not responding", model.Status); Assert.False(work.IsCompleted);
        }
        finally { release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(10))); }
        Assert.Equal(1, observedCancellation);
    }

    [AvaloniaFact]
    public async Task OpenSavedFolderWrapsAtShortNarrowSizesAndCancelIsReachable()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        var window = new DiskSpaceWindow { Width = 380, Height = 430 }; window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open saved…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var folder = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("Saved analyses folder:", StringComparison.Ordinal) == true);
            Assert.Equal(TextWrapping.Wrap, folder.TextWrapping); Assert.True(folder.Bounds.Width <= 350); Assert.True(folder.Bounds.Height > 20);
            var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel" && b.IsEffectivelyVisible);
            Assert.True(cancel.Bounds.Width > 0 && cancel.Bounds.Height > 0);
            var open = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open observations");
            Assert.True(open.IsDefault); Assert.False(open.IsEnabled);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Parameter", StringComparison.Ordinal) == true);
            window.GetVisualDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Saved inventory filename").Text = "../bad.fcdsa";
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Use a portable filename", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Parameter", StringComparison.Ordinal) == true);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Yield();
            Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisSnapshotsDirectory));
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task OpenSavedDialogUsesTheReviewedFilenameAndCreatesAnOfflineTab()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        var receipt = await model.SaveNewAsync("dialog-workflow.fcdsa");
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open saved…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var field = window.GetVisualDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Saved inventory filename");
            Assert.Equal(receipt.Name, field.Text);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open observations").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (window.SessionCount != 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(2, window.SessionCount); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Observation view.", StringComparison.Ordinal) == true);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Pause").IsEnabled);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Stop").IsEnabled);
        }
        finally { window.ForceClose(); }
    }

    private static AnalysisInventory ImportedFixture(AnalysisSnapshot? snapshot = null)
    {
        using var stream = new MemoryStream();
        AnalysisInventoryFormat.Write(stream, snapshot ?? Fixture(), Guid.NewGuid()); stream.Position = 0;
        return AnalysisInventoryFormat.Read(stream);
    }

    [AvaloniaFact]
    public async Task HistoricalInProgressPhaseRemainsPartialDataWithoutAnActiveScan()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture(Fixture() with { Phase = AnalysisPhase.Scanning, Coverage = NamespaceCoverage.Partial }));
        Assert.False(model.Running); Assert.False(model.HasTemporaryWork); Assert.Equal(AnalysisPhase.Stopped, model.Phase);
        await model.RebuildAsync(true); var saved = await model.SaveNewAsync("partial.fcdsa");
        using var loaded = await model.OpenSavedAsync(saved.Name); await loaded.RebuildAsync(true);
        Assert.False(loaded.Running); Assert.Equal(AnalysisPhase.Stopped, loaded.Phase);
        Assert.Equal(AnalysisPhase.Scanning, loaded.Tree!.Snapshot.Phase);
        Assert.Equal(NamespaceCoverage.Partial, loaded.Tree.Snapshot.Coverage);
    }

    [AvaloniaFact]
    public async Task CanceledSaveCreatesNoPrivatePayloadOrSuccessfulReceipt()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.SaveNewAsync("canceled.fcdsa", cancel.Token));
        Assert.False(model.IsSaving); Assert.Null(model.SavedInventoryName);
        Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisSnapshotsDirectory));
        Assert.Contains("canceled", model.Status);
    }

    [AvaloniaFact]
    public async Task NamedSaveRoundTripsThroughNativePrivateStorageAndNeverReplacesAConflict()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture());
        await model.RebuildAsync(true); Assert.True(model.CanSave);
        var identity = model.Tree!.Snapshot.Identity;
        var receipt = await model.SaveNewAsync("Analysis café 😀.fcdsa");
        string path = Path.Combine(fixture.Services.Paths.AnalysisSnapshotsDirectory, receipt.Name);
        byte[] original = await File.ReadAllBytesAsync(path);
        Assert.Equal(original.Length, receipt.Bytes); Assert.True(receipt.InventoryId != Guid.Empty);
        using var loaded = await model.OpenSavedAsync(receipt.Name); Assert.False(loaded.Running);
        await loaded.RebuildAsync(true);
        Assert.Equal(identity, loaded.Tree!.Snapshot.Identity);
        Assert.Equal(AnalysisOrigin.ImportedInventory, loaded.Tree.Snapshot.Origin);
        Assert.True(loaded.Tree.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        Assert.Equal(model.Tree.Projection.Totals.Known, loaded.Tree.Projection.Totals.Known);
        Assert.Equal(model.Tree.Projection.Totals.MatchingPaths, loaded.Tree.Projection.Totals.MatchingPaths);
        Assert.Equal(model.Tree.Projection.Totals.Missing.OrderBy(p => p.Key), loaded.Tree.Projection.Totals.Missing.OrderBy(p => p.Key));
        await Assert.ThrowsAsync<IOException>(() => model.SaveNewAsync(receipt.Name));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [AvaloniaFact]
    public async Task QueuedSaveRetainsThePublishedRevisionAfterItsViewCloses()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var gate = new ManualResetEventSlim();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        var identity = model.Tree!.Snapshot.Identity;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(fixture.Services.Paths.AnalysisSnapshotsDirectory), _ =>
        { entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(15)); return 0; });
        Task<AnalysisInventoryReceipt>? saving = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); saving = model.SaveNewAsync("retained.fcdsa");
            model.Dispose(); Assert.False(saving.IsCompleted);
        }
        finally { gate.Set(); await blocker.WaitAsync(TimeSpan.FromSeconds(10)); }
        var receipt = await saving!.WaitAsync(TimeSpan.FromSeconds(10));
        using var opener = new DiskSpaceViewModel(fixture.Services);
        using var loaded = await opener.OpenSavedAsync(receipt.Name); await loaded.RebuildAsync(true);
        Assert.Equal(identity, loaded.Tree!.Snapshot.Identity);
    }

    [AvaloniaFact]
    public async Task SaveReviewNamesTheEntireRevisionAndCancelCreatesNoInventory()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save…");
            Assert.True(save.IsEnabled); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("display filter does not limit", StringComparison.Ordinal) == true);
            var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel" && b.IsEffectivelyVisible);
            Assert.True(cancel.IsDefault);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save new inventory").IsDefault);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Yield();
            Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisSnapshotsDirectory)); Assert.False(model.IsSaving);
        }
        finally { window.ForceClose(); }
    }
}
