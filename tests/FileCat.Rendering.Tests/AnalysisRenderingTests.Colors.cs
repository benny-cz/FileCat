using Avalonia;
using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public void EveryChartCategoryHasAaTextAndVisibleBoundariesAcrossAllProductThemes()
    {
        foreach (var theme in ThemePalette.All)
        foreach (var dimension in Enum.GetValues<AnalysisColorDimension>())
        foreach (var category in AnalysisChartColors.Legend(dimension))
        {
            var paint = AnalysisChartPalette.For(category.Key, theme); var panel = AnalysisChartPalette.Panel(theme);
            Assert.True(ThemeManager.Contrast(paint.Text, paint.Fill) >= 4.5, theme.Name + "/" + category.Key);
            foreach (var outline in new[] { paint.Border, paint.Focus, paint.Mark })
            {
                Assert.True(ThemeManager.Contrast(outline, paint.Fill) >= 3, theme.Name + "/" + category.Key + "/fill");
                Assert.True(ThemeManager.Contrast(outline, panel) >= 3, theme.Name + "/" + category.Key + "/panel");
            }
        }
    }
    [AvaloniaFact]
    public async Task ColorChoiceUpdatesBothChartsAndLegendWithoutChangingScopeMarksOrTheCapturedProjection()
    {
        using var styles = new ProductStyles();
        var captured = DateTimeOffset.Parse("2026-11-04T12:00:00Z"); var snapshot = Fixture();
        snapshot = snapshot with { Origin = AnalysisOrigin.ImportedInventory, Identity = snapshot.Identity with { StartedUtc = captured, ObservedUtc = captured },
            Nodes = snapshot.Nodes.Select(node => node with { Times = new(captured, captured.AddDays(node.Id == 4 ? -3 : node.Id == 5 ? -45 : -400), 1) }).ToImmutableArray(),
            Facts = snapshot.Facts.Select(fact => fact with { ObservedUtc = captured }).ToImmutableArray() };
        using var model = new DiskSpaceViewModel(snapshot);
        await model.RebuildAsync(true); model.Selection.ToggleMark(4); model.Focus("n:4");
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            var tree = model.Tree!; var color = ColorSelector(window); Assert.Equal(3, color.ItemCount);
            var mode = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Chart view");
            string output = Path.Combine(AppContext.BaseDirectory, "analysis-renders"); Directory.CreateDirectory(output);
            foreach (var theme in ThemePalette.All)
            foreach (var dimension in new[] { AnalysisColorDimension.FileType, AnalysisColorDimension.HierarchyDepth, AnalysisColorDimension.ModifiedAge })
            {
                ThemeManager.Apply(theme.Name); color.SelectedIndex = (int)dimension;
                foreach (int chartMode in new[] { 0, 1 })
                {
                    mode.SelectedIndex = chartMode; Frame(window);
                    Assert.Same(tree, model.Tree); Assert.Equal("n:4", model.Selection.Focus); Assert.Contains(4UL, model.Selection.Marks);
                    Assert.Equal(dimension, model.ColorDimension);
                    var legend = window.GetVisualDescendants().OfType<AnalysisColorLegend>().Single();
                    Assert.True(legend.IsVisible); Assert.Contains(AnalysisChartColors.Title(dimension), AutomationLabel(legend));
                    Assert.Contains(legend.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Colors: ", StringComparison.Ordinal) == true && text.IsEffectivelyVisible);
                    var labels = legend.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                    foreach (var category in AnalysisChartColors.Legend(dimension)) Assert.Contains(category.Label, labels);
                    var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(c => c.IsVisible);
                    Assert.Contains(AnalysisChartColors.Title(dimension), AutomationLabel(chart));
                    Assert.True(chart.Bounds.Height > 100);
                    if (chartMode == 0)
                    {
                        using var image = new RenderTargetBitmap(new PixelSize(1180, 780)); image.Render(window);
                        image.Save(Path.Combine(output, $"colors-{theme.Name}-{dimension}.png"), new PngBitmapEncoderOptions());
                    }
                }
            }
            var shownLegend = window.GetVisualDescendants().OfType<AnalysisColorLegend>().Single();
            shownLegend.IsExpanded = false; Frame(window);
            Assert.DoesNotContain(shownLegend.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Shared files" && text.IsEffectivelyVisible);
            shownLegend.IsExpanded = true; Frame(window);
            Assert.Contains(shownLegend.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Shared files" && text.IsEffectivelyVisible);
            mode.SelectedIndex = 2; Frame(window); Assert.False(shownLegend.IsVisible);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
    [AvaloniaFact]
    public async Task ChartColorAndItsAccessibleMeaningFreezeUntilActualPointerRelease()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture()); await model.RebuildAsync(true);
        var chart = new TreemapControl(); chart.SetView(model.Tree!, AnalysisTree.RootKey, null, [], AnalysisColorDimension.FileType);
        var window = new Window { Width = 820, Height = 580, Content = chart }; window.Show(); Frame(window);
        try
        {
            window.MouseMove(new Point(24, 24)); window.MouseDown(new Point(24, 24), MouseButton.Left);
            chart.SetView(model.Tree!, AnalysisTree.RootKey, "n:1", [], AnalysisColorDimension.ModifiedAge); Frame(window);
            Assert.Contains(AnalysisChartColors.Title(AnalysisColorDimension.FileType), AutomationLabel(chart));
            window.MouseUp(new Point(24, 24), MouseButton.Left); Frame(window);
            Assert.Contains(AnalysisChartColors.Title(AnalysisColorDimension.ModifiedAge), AutomationLabel(chart));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task IndexedSavedColorRestoresAndSwitchesOfflineWithoutReplacingTheExplorerOrChartData()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var saved = AnalysisSavedView.Default with { Metric = AnalysisMetric.Files, ColorDimension = AnalysisColorDimension.ModifiedAge };
        var window = new DiskSpaceWindow(); var explorer = await window.AddPreparedIndexedSessionAsync(token => fixture.Lease!.RetainAsync(token), AnalysisMetric.OnDisk, null, saved, TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            var selector = ColorSelector(window); Assert.Equal(3, selector.ItemCount); Assert.Equal(2, selector.SelectedIndex); var chart = explorer.Chart!;
            foreach (var dimension in new[] { AnalysisColorDimension.FileType, AnalysisColorDimension.HierarchyDepth, AnalysisColorDimension.ModifiedAge })
            {
                selector.SelectedIndex = (int)dimension; Frame(window); Assert.Same(chart, explorer.Chart);
                Assert.Contains(AnalysisChartColors.Title(dimension), AutomationLabel(window.GetVisualDescendants().OfType<AnalysisColorLegend>().Single()));
                foreach (var control in window.GetVisualDescendants().OfType<AnalysisChartControl>())
                    Assert.Contains(AnalysisChartColors.Title(dimension), AutomationLabel(control));
            }
            Assert.Equal(AnalysisMetric.Files, explorer.Metric); Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
    private static ComboBox ColorSelector(Window window) => window.GetVisualDescendants().OfType<ComboBox>()
        .Single(combo => AutomationLabel(combo) == "Chart color dimension");

    [AvaloniaFact]
    public async Task FocusedChartDetailsTrackTheCurrentColorAndEntryWithoutPointerMotion()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture()); await model.RebuildAsync(true);
        var chart = new TreemapControl(); chart.SetView(model.Tree!, AnalysisTree.RootKey, "n:4", []);
        var window = new Window { Width = 820, Height = 580, Content = chart }; window.Show(); Frame(window);
        try
        {
            Assert.True(chart.Focus()); Frame(window);
            Assert.Contains("File-type family: Code", Assert.IsType<FileCat.App.Controls.RichTip>(ToolTip.GetTip(chart)).Text);
            chart.SetView(model.Tree!, AnalysisTree.RootKey, "n:1", [], AnalysisColorDimension.ModifiedAge); Frame(window);
            string text = Assert.IsType<FileCat.App.Controls.RichTip>(ToolTip.GetTip(chart)).Text;
            Assert.Contains("Modification age at capture: Unknown / uncertain modification age", text);
            Assert.DoesNotContain("File-type family: Code", text);
        }
        finally { window.Close(); }
    }
}
