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
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open observations").IsDefault);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Yield();
            Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisSnapshotsDirectory));
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
