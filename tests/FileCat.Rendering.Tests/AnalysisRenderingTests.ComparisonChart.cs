using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task ComparisonMetricWaitsActualReadersAndRecomputesAllCapturedMetrics()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        var observed = Fixture();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture(observed)) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("metrics.fcdsa");
        using var comparison = await source.CompareSavedAsync("metrics.fcdsa");
        source.Dispose(); // Every switch is offline and owns the captured inputs.
        var old = comparison.Comparison!; using var reader = old.Retain();
        var switching = comparison.ChangeComparisonMetricAsync(AnalysisMetric.Files);
        Assert.False(switching.IsCompleted); Assert.False(old.CloseCompletion.IsCompleted); Assert.NotEmpty(reader.Rows);
        reader.Dispose(); await switching.WaitAsync(TimeSpan.FromSeconds(15));
        foreach (var metric in Enum.GetValues<AnalysisMetric>())
        {
            await comparison.ChangeComparisonMetricAsync(metric);
            var expected = AnalysisAggregate.Project(observed, metric);
            Assert.Equal(metric, comparison.Comparison!.Metric);
            Assert.Equal(expected.Totals.Known, comparison.Comparison.CurrentTotals.Known);
            Assert.Equal(expected.Totals.IdentityUnverified, comparison.Comparison.CurrentTotals.IdentityUnverified);
        }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => comparison.ChangeComparisonMetricAsync((AnalysisMetric)999));
        Assert.NotEmpty(comparison.Comparison!.Rows);
    }

    [AvaloniaFact]
    public async Task ComparisonMetricCancellationRetainsInputsAndAllowsExplicitRecovery()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture());
        await source.RebuildAsync(true); await source.SaveNewAsync("cancel-metric.fcdsa");
        using var comparison = await source.CompareSavedAsync("cancel-metric.fcdsa");
        var old = comparison.Comparison!; using var reader = old.Retain(); using var cancel = new CancellationTokenSource();
        var switching = comparison.ChangeComparisonMetricAsync(AnalysisMetric.Files, cancel.Token); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => switching);
        Assert.False(old.CloseCompletion.IsCompleted); reader.Dispose(); await old.CloseCompletion;
        await comparison.ChangeComparisonMetricAsync(AnalysisMetric.FileSize);
        Assert.NotEmpty(comparison.Comparison!.Rows);
    }

    [AvaloniaTheory]
    [InlineData(AnalysisMetric.FileSize)] [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)] [InlineData(AnalysisMetric.Files)]
    public async Task ComparisonChartsUseRecordedPositiveAreaAndRetainSharedAccounting(AnalysisMetric metric)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var observed = Fixture();
        observed = observed with { Nodes = observed.Nodes.Add(new(7, "r", 3, "alias-source.cs", AnalysisKind.File, "a")),
            Objects = observed.Objects.SetItem(0, observed.Objects[0] with { LinkCount = AnalysisMeasurement.Known(2) }) };
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture(observed)) { Metric = metric };
        await source.RebuildAsync(true); await source.SaveNewAsync("chart.fcdsa");
        using var comparison = await source.CompareSavedAsync("chart.fcdsa");
        using (var captured = source.CaptureDisplayedRevision())
        using (var rows = comparison.Comparison!.Retain())
            Assert.Throws<InvalidOperationException>(() => AnalysisComparisonChart.Build(captured, rows,
                metric == AnalysisMetric.Files ? AnalysisMetric.FileSize : AnalysisMetric.Files, false));
        foreach (bool previous in new[] { false, true })
        {
            using var chart = await comparison.BuildComparisonChartAsync(previous);
            var projection = AnalysisAggregate.Project(observed, metric);
            var expected = AnalysisTree.Build(observed, projection, false);
            Assert.Equal(previous, chart.Previous);
            Assert.Equal(expected.ScopeValue(AnalysisTree.RootKey), chart.ScopeValue(AnalysisTree.RootKey));
            foreach (var item in expected.Items.Values)
            {
                Assert.Equal(item.Value, chart.Items[item.Key].Value);
                Assert.Equal(item.IdentityUnverified, chart.Items[item.Key].IdentityUnverified);
                Assert.Equal(item.Members, chart.Items[item.Key].Members);
                Assert.NotNull(chart.Items[item.Key].ComparisonChange);
                var category = AnalysisChartColors.Category(chart, chart.Items[item.Key], AnalysisColorDimension.Growth);
                Assert.Contains(category, AnalysisChartColors.Legend(AnalysisColorDimension.Growth));
            }
            if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
                Assert.Contains(chart.Items.Values, item => item.Family == "shared" && item.Value > 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() => comparison.BuildComparisonChartAsync(!previous));
        }
        // A displayed chart is an actual reader and must retire before metric replacement.
        var held = await comparison.BuildComparisonChartAsync(false);
        var replacement = comparison.ChangeComparisonMetricAsync(AnalysisMetric.Files);
        Assert.False(replacement.IsCompleted); held.Dispose(); await replacement;
    }

    [AvaloniaFact]
    public async Task ComparisonChartSeparatesGrowthFromAreaAndKeepsRemovedOnlyInPrevious()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var before = Fixture();
        using var previous = new DiskSpaceViewModel(fixture.Services, ImportedFixture(before)) { Metric = AnalysisMetric.FileSize };
        await previous.RebuildAsync(true); await previous.SaveNewAsync("growth.fcdsa");
        var now = before with
        {
            Nodes = before.Nodes.RemoveAt(4).Add(new(7, "r", 3, "new.bin", AnalysisKind.File, "d")),
            Objects = before.Objects.RemoveAt(1).SetItem(0, before.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(131072) })
                .Add(new("d", "device:d", AnalysisMeasurement.Known(256), AnalysisMeasurement.Known(1))),
            Facts = before.Facts.RemoveAt(1)
        };
        using var current = new DiskSpaceViewModel(fixture.Services, ImportedFixture(now)) { Metric = AnalysisMetric.FileSize };
        await current.RebuildAsync(true); using var comparison = await current.CompareSavedAsync("growth.fcdsa");
        using (var chart = await comparison.BuildComparisonChartAsync(false))
        {
            Assert.Equal((UInt128)(131072 + 100 + 256), chart.ScopeValue(AnalysisTree.RootKey));
            Assert.False(chart.Items.ContainsKey("n:5"));
            Assert.Equal(AnalysisChange.Growing, chart.Items["n:4"].ComparisonChange);
            Assert.Equal(AnalysisChange.Added, chart.Items["n:7"].ComparisonChange);
            Assert.Equal("growth-up", AnalysisChartColors.Category(chart, chart.Items["n:4"], AnalysisColorDimension.Growth).Key);
            Assert.Equal(AnalysisChange.Growing, chart.RowFor("n:4")!.Change);
        }
        using (var chart = await comparison.BuildComparisonChartAsync(true))
        {
            Assert.Equal((UInt128)(65536 + 32768 + 100), chart.ScopeValue(AnalysisTree.RootKey));
            Assert.False(chart.Items.ContainsKey("n:7"));
            Assert.Equal(AnalysisChange.Removed, chart.Items["n:5"].ComparisonChange);
            Assert.Equal("growth-removed", AnalysisChartColors.Category(chart, chart.Items["n:5"], AnalysisColorDimension.Growth).Key);
        }
    }

    [AvaloniaFact]
    public async Task ComparisonChartAndMetricControlsRenderAndSwitchAfterSourceClosure()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("ui-chart.fcdsa");
        using var comparison = await source.CompareSavedAsync("ui-chart.fcdsa"); source.Dispose();
        var window = new DiskSpaceWindow(); window.AddSession(comparison); window.Show();
        try
        {
            ComboBox Choice(string name) => window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationLabel(c) == name);
            async Task Until(Func<bool> condition)
            {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!condition() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                Assert.True(condition()); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            }
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Choice("Comparison chart").SelectedIndex = 1;
            await Until(() => window.GetVisualDescendants().OfType<TreemapControl>().Any(c => c.IsEffectivelyVisible && (AutomationLabel(c) ?? "").Contains("Observed growth", StringComparison.OrdinalIgnoreCase)));
            Choice("Comparison chart").SelectedIndex = 2;
            Choice("Comparison chart side").SelectedIndex = 1;
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Previous recorded amounts", StringComparison.Ordinal) == true));
            foreach (string theme in new[] { "Classic", "ClassicDark", "HighContrast", "Cyberpunk", "Psychedelic", "Steampunk", "DosCommander" })
            {
                FileCat.App.Services.ThemeManager.Apply(theme);
                foreach (int mode in new[] { 1, 2 })
                {
                Choice("Comparison chart").SelectedIndex = mode;
                foreach (int width in new[] { 1180, 380 })
                {
                    window.Width = width; window.Height = width == 380 ? 430 : 780; window.FontSize = width == 380 ? 20 : 13;
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(c => c.IsEffectivelyVisible);
                    Assert.True(chart.Bounds.Width > 100 && chart.Bounds.Height == 260);
                    chart.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    var origin = chart.TranslatePoint(default, window);
                    Assert.NotNull(origin); Assert.InRange(origin.Value.Y, 0, window.Bounds.Height - chart.Bounds.Height + 1);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(width, width == 380 ? 430 : 780)); bitmap.Render(window);
                    string output = Path.Combine(AppContext.BaseDirectory, "comparison-growth-renders"); Directory.CreateDirectory(output);
                    bitmap.Save(Path.Combine(output, $"{theme}-{width}-{mode}.png"), new PngBitmapEncoderOptions());
                }
                }
            }
            Choice("Comparison metric").SelectedIndex = (int)AnalysisMetric.Files;
            await Until(() => comparison.Comparison?.Metric == AnalysisMetric.Files && window.GetVisualDescendants().OfType<ListBox>().Any(c => AutomationLabel(c) == "Comparison rows"));
            Assert.Equal(0, Choice("Comparison chart").SelectedIndex);
            Assert.Equal((int)AnalysisMetric.Files, Choice("Comparison metric").SelectedIndex);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
