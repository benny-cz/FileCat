using Avalonia.Automation;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task StructuralInputsSurviveSourceClosureAndReleaseTheirOwnersDuringActualWork()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        var old = Fixture(); old = old with { Roots = [old.Roots[0] with { CanonicalLocator = "/previous", SourceIdentity = "previous-source" }] };
        using var previous = new DiskSpaceViewModel(fixture.Services, ImportedFixture(old)); await previous.RebuildAsync(true); await previous.SaveNewAsync("mapped.fcdsa");
        var s = Fixture(); s = s with { Roots = [s.Roots[0] with { CanonicalLocator = "/current", SourceIdentity = "current-source" }],
            Objects = s.Objects.SetItem(0, s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(131072) }) };
        using var current = new DiskSpaceViewModel(fixture.Services, ImportedFixture(s)) { Metric = AnalysisMetric.FileSize }; await current.RebuildAsync(true);
        using var captured = current.CaptureDisplayedRevision();
        using var inputs = await current.PrepareComparisonAsync("mapped.fcdsa", null, captured, current.Metric, CancellationToken.None);
        Assert.False(inputs.HasAutomaticRoots);
        await Assert.ThrowsAsync<InvalidOperationException>(() => inputs.BuildAsync(null, CancellationToken.None));
        var pairs = new Dictionary<string, string> { [inputs.PreviousRoots[0].Id] = inputs.CurrentRoots[0].Id };
        var building = inputs.BuildAsync(pairs, CancellationToken.None);
        inputs.Dispose(); current.Dispose(); previous.Dispose();
        using var result = await building;
        Assert.Equal(new AnalysisDelta(false, 65536), result.Comparison!.NetChange);
        Assert.False(result.Comparison.Compatibility.VerifiedIdentityContext);
        Assert.All(result.Comparison.Rows, r => Assert.Equal(AnalysisComparisonEvidence.Structural, r.Evidence));
        Assert.Throws<ObjectDisposedException>(() => inputs.CurrentRoots);
    }

    [AvaloniaFact]
    public async Task MissingAutomaticRootsRequireExplicitMappingAndDefaultToCancel()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        var old = Fixture(); old = old with { Roots = [old.Roots[0] with { CanonicalLocator = "/previous", SourceIdentity = "previous-source" }] };
        using var previous = new DiskSpaceViewModel(fixture.Services, ImportedFixture(old)); await previous.RebuildAsync(true); await previous.SaveNewAsync("mapped.fcdsa");
        var s = Fixture(); s = s with { Roots = [s.Roots[0] with { CanonicalLocator = "/current", SourceIdentity = "current-source" }] };
        using var current = new DiskSpaceViewModel(fixture.Services, ImportedFixture(s)) { Metric = AnalysisMetric.FileSize }; await current.RebuildAsync(true);
        var window = new DiskSpaceWindow(); window.AddSession(current); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare…" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Previous inventory filename").Text = "mapped.fcdsa";
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare observations" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(10); Button? compare = null;
            while (DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                compare = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Content as string == "Compare mapped observations" && b.IsEffectivelyVisible);
                if (compare is not null) break; await Task.Delay(10);
            }
            Assert.NotNull(compare); Assert.False(compare.IsEnabled);
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel" && b.IsEffectivelyVisible).IsDefault);
            var root = window.GetVisualDescendants().OfType<ComboBox>().Single(c => (AutomationProperties.GetName(c) ?? "").StartsWith("Previous root for current ", StringComparison.Ordinal));
            Assert.Equal(0, root.SelectedIndex); root.SelectedIndex = 1;
            deadline = DateTime.UtcNow.AddSeconds(5); while (!compare.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.True(compare.IsEnabled); compare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            deadline = DateTime.UtcNow.AddSeconds(5); while (window.SessionCount != 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(2, window.SessionCount); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("Explicit structural comparison", StringComparison.Ordinal) == true);
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task ComparisonPresentationRendersEveryThemeWithFiniteNarrowAndWideRows()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("before.fcdsa");
        string output = Path.Combine(AppContext.BaseDirectory, "comparison-renders"); Directory.CreateDirectory(output);
        foreach (string theme in new[] { "Classic", "ClassicDark", "HighContrast", "Cyberpunk", "Psychedelic", "Steampunk", "DosCommander" })
        {
            using var comparison = await source.CompareSavedAsync("before.fcdsa"); var window = new DiskSpaceWindow(); window.AddSession(comparison); window.Show();
            try
            {
                ThemeManager.Apply(theme);
                foreach (int width in new[] { 1180, 380 })
                {
                    window.Width = width; window.Height = width == 380 ? 430 : 780; window.FontSize = width == 380 ? 20 : 13;
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    var rows = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Comparison rows");
                    Assert.True(rows.Bounds.Width > 100 && double.IsFinite(rows.Bounds.Height)); Assert.Equal(3, rows.ItemCount);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(width, width == 380 ? 430 : 780)); bitmap.Render(window);
                    bitmap.Save(Path.Combine(output, $"{theme}-{width}.png"), new PngBitmapEncoderOptions());
                }
            }
            finally { window.ForceClose(); }
        }
    }

    [AvaloniaFact]
    public async Task ReviewedComparisonDialogCreatesAnOfflineTabAndExplicitDiscardReleasesItsBudget()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var previous = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await previous.RebuildAsync(true); await previous.SaveNewAsync("before.fcdsa");
        var s = Fixture(); s = s with { Objects = s.Objects.SetItem(0, s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(131072) }) };
        using var current = new DiskSpaceViewModel(fixture.Services, ImportedFixture(s)) { Metric = AnalysisMetric.FileSize }; await current.RebuildAsync(true);
        var window = new DiskSpaceWindow(); window.AddSession(current); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare…" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Previous inventory filename").Text = "before.fcdsa";
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare observations" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(10); while (window.SessionCount != 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(2, window.SessionCount); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Compare observations · File size", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "Start analysis");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Close analysis").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Keep open" && b.IsEffectivelyVisible).IsDefault);
            var discard = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Discard comparison" && b.IsEffectivelyVisible);
            Assert.False(discard.IsDefault); discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            deadline = DateTime.UtcNow.AddSeconds(5); while (window.SessionCount != 1 && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(1, window.SessionCount);
            using var next = await current.CompareSavedAsync("before.fcdsa"); Assert.NotNull(next.Comparison);
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task QueuedComparisonRetainsItsCapturedRevisionAfterTheSourceViewCloses()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var release = new ManualResetEventSlim();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await source.RebuildAsync(true); await source.SaveNewAsync("before.fcdsa");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(fixture.Services.Paths.AnalysisSnapshotsDirectory), _ =>
        { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); return 0; });
        Task<DiskSpaceViewModel>? comparing = null;
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); comparing = source.CompareSavedAsync("before.fcdsa"); source.Dispose(); Assert.False(comparing.IsCompleted); }
        finally { release.Set(); await blocking.WaitAsync(TimeSpan.FromSeconds(10)); }
        using var result = await comparing!.WaitAsync(TimeSpan.FromSeconds(10)); Assert.NotNull(result.Comparison); Assert.False(result.Running);
    }

    [AvaloniaFact]
    public async Task ComparisonOwnsBothRevisionsAfterItsSourceViewsCloseAndNeverBecomesALiveScan()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var previous = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await previous.RebuildAsync(true); await previous.SaveNewAsync("before.fcdsa");
        var s = Fixture(); s = s with { Objects = s.Objects.SetItem(0, s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(131072) }) };
        using var current = new DiskSpaceViewModel(fixture.Services, ImportedFixture(s)) { Metric = AnalysisMetric.FileSize }; await current.RebuildAsync(true);
        using var comparison = await current.CompareSavedAsync("before.fcdsa"); current.Dispose(); previous.Dispose();
        Assert.NotNull(comparison.Comparison); Assert.Equal(new AnalysisDelta(false, 65536), comparison.Comparison.NetChange);
        Assert.False(comparison.Running); Assert.True(comparison.HasTemporaryWork); Assert.False(comparison.CanCompare); Assert.False(comparison.CanSave);
        Assert.True(comparison.Comparison.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        var grown = Assert.Single(comparison.Comparison.Rows, r => r.Change == AnalysisChange.Growing);
        Assert.Equal(["primary · fixture · 1 Known"], comparison.ComparisonBases(true, grown.Previous));
        Assert.Equal(["primary · fixture · 1 Known"], comparison.ComparisonBases(false, grown.Current));
        await comparison.RebuildAsync(true); Assert.Null(comparison.Tree);
        Assert.Empty(comparison.Selection.Marks); Assert.Empty(comparison.Selection.Basket);
    }

    [AvaloniaFact]
    public async Task ComparisonPaneFiltersGrowthInspectsBothSidesAndKeepsItsCloseDefault()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var previous = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await previous.RebuildAsync(true); await previous.SaveNewAsync("before.fcdsa");
        var s = Fixture(); s = s with { Objects = s.Objects.SetItem(0, s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(131072) }) };
        using var current = new DiskSpaceViewModel(fixture.Services, ImportedFixture(s)) { Metric = AnalysisMetric.FileSize }; await current.RebuildAsync(true);
        var comparison = await current.CompareSavedAsync("before.fcdsa"); var window = new DiskSpaceWindow { Width = 380, Height = 430, FontSize = 20 };
        window.AddSession(comparison); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Comparison rows");
            Assert.Equal(3, rows.ItemCount); Assert.True(double.IsFinite(rows.Bounds.Height)); Assert.InRange(rows.Bounds.Height, 100, 1600);
            var status = window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Comparison status");
            status.SelectedIndex = 1; Dispatcher.UIThread.RunJobs(); Assert.Equal(1, rows.ItemCount); rows.SelectedIndex = 0;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Inspect comparison entry" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("No current source or content proof", StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<ListBox>(), l => AutomationProperties.GetName(l) == "Previous observed paths");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Close" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.Close(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var keep = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Keep open" && b.IsEffectivelyVisible);
            Assert.True(keep.IsDefault); Assert.True(keep.IsCancel); Assert.True(window.IsVisible);
            keep.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task CompareDialogReviewsTheFullCapturedRevisionAndCancelCreatesNoComparison()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare…" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("display filter and folder scope do not limit", StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Current metric:", StringComparison.Ordinal) == true);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel" && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); Assert.Equal(1, window.SessionCount); Assert.False(Directory.Exists(fixture.Services.Paths.AnalysisSnapshotsDirectory));
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task CanceledComparisonRetainsTheSourceViewAndReleasesNoPartialResult()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true); await model.SaveNewAsync("before.fcdsa");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.CompareSavedAsync("before.fcdsa", cancel.Token));
        Assert.True(model.CanCompare); using var valid = await model.CompareSavedAsync("before.fcdsa"); Assert.NotNull(valid.Comparison);
    }
}
