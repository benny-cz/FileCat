using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task NativeIndexedComparisonChartsKeepWholeCapturedSidesAcrossQueryNavigationAndMetricRetirement(AnalysisMetric metric)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var a = await IndexedFixture.CreateAsync(sharedAlias: true);
        await using var b = await IndexedFixture.CreateAsync(sharedAlias: true); var token = TestContext.Current.CancellationToken;
        var mapping = new Dictionary<string, string> { [a.Lease!.Descriptor.Roots.Single().Id] = b.Lease!.Descriptor.Roots.Single().Id };
        var window = new DiskSpaceWindow(); await window.AddIndexedComparisonSessionAsync(a.Lease, b.Lease, metric, mapping, cancellation: token);
        await a.ReleaseInitialLeaseAsync(); await b.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            var choice = ComparisonControl<ComboBox>(window, "Comparison chart")!;
            var side = ComparisonControl<ComboBox>(window, "Comparison chart side")!;
            var apply = window.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, "Apply comparison chart"));
            choice.SelectedIndex = 1; Click(window, "Apply comparison chart");
            await Until(() => apply.IsEnabled && ComparisonControl<TextBlock>(window, "Comparison chart scope")!.Text!.StartsWith("Current recorded amounts", StringComparison.Ordinal), () => ComparisonText(window));
            Frame(window);
            var tree = window.GetVisualDescendants().OfType<TreemapControl>().Single(control => control.IsEffectivelyVisible);
            string initial = AutomationProperties.GetName(tree)!;
            Assert.Contains("Current", initial); Assert.Contains("Observed growth", initial);
            Assert.Contains("filters apply to the table", ComparisonControl<TextBlock>(window, "Comparison chart scope")!.Text!);
            ComparisonControl<TextBox>(window, "Comparison query")!.Text = "name:never-a-match"; Click(window, "Apply comparison view");
            await Until(() => apply.IsEnabled && ComparisonControl<ListBox>(window, "Captured comparison rows")!.ItemCount == 0);
            Assert.Equal(initial, AutomationProperties.GetName(tree));
            // Exercise the same routed Enter used by the native control. Geometry is
            // rendered first, so keyboard targets are its bounded visible fragments.
            using (var bitmap = new RenderTargetBitmap(new PixelSize(1180, 780))) bitmap.Render(window);
            tree.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Until(() => apply.IsEnabled && window.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, "Up comparison chart")).IsEnabled, () => ComparisonText(window));
            Click(window, "All comparison locations"); await Until(() => apply.IsEnabled && AutomationProperties.GetName(tree) == initial);
            foreach (int mode in new[] { 2, 1 })
            {
                choice.SelectedIndex = mode; side.SelectedIndex = 1; Click(window, "Apply comparison chart");
                await Until(() => apply.IsEnabled && ComparisonControl<TextBlock>(window, "Comparison chart scope")!.Text!.StartsWith("Previous recorded amounts", StringComparison.Ordinal));
                Frame(window); var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(control => control.IsEffectivelyVisible);
                Assert.Contains("Previous", AutomationProperties.GetName(chart)!);
                Assert.Contains("Removed area is shown on Previous only", ComparisonControl<TextBlock>(window, "Comparison chart scope")!.Text!);
            }
            Click(window, "Reset comparison view"); await Until(() => apply.IsEnabled && ComparisonControl<ListBox>(window, "Captured comparison rows")!.ItemCount > 0);
            var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!; rows.SelectedIndex = 0;
            await Until(() => apply.IsEnabled && ComparisonControl<TextBox>(window, "Current comparison details")!.Text!.Contains("Recorded location:", StringComparison.Ordinal));
            if (metric == AnalysisMetric.FileSize)
                foreach (string theme in new[] { "Classic", "ClassicDark", "HighContrast", "Cyberpunk", "Psychedelic", "Steampunk", "DosCommander" })
                    foreach (int mode in new[] { 1, 2 })
                    {
                        ThemeManager.Apply(theme); choice.SelectedIndex = mode; Click(window, "Apply comparison chart"); await Until(() => apply.IsEnabled);
                        foreach (int width in new[] { 1180, 380 })
                        {
                            window.Width = width; window.Height = width == 380 ? 430 : 780; window.FontSize = width == 380 ? 20 : 13; Frame(window);
                            var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(control => control.IsEffectivelyVisible);
                            chart.BringIntoView(); Frame(window); Assert.True(chart.Bounds.Width > 100); Assert.Equal(280, chart.Bounds.Height);
                            using var bitmap = new RenderTargetBitmap(new PixelSize(width, width == 380 ? 430 : 780)); bitmap.Render(window);
                            string output = Path.Combine(AppContext.BaseDirectory, "indexed-comparison-growth-renders"); Directory.CreateDirectory(output);
                            bitmap.Save(Path.Combine(output, $"{theme}-{width}-{mode}.png"), new PngBitmapEncoderOptions());
                        }
                    }
            var metricChoice = ComparisonControl<ComboBox>(window, "Comparison metric")!;
            metricChoice.SelectedIndex = (int)(metric == AnalysisMetric.Files ? AnalysisMetric.FileSize : AnalysisMetric.Files);
            await Until(() => metricChoice.IsEnabled && ComparisonControl<ListBox>(window, "Captured comparison rows")!.ItemCount > 0, () => ComparisonText(window));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<AnalysisChartControl>(), control => control.IsEffectivelyVisible);
            choice.SelectedIndex = 2; side.SelectedIndex = 0; Click(window, "Apply comparison chart");
            await Until(() => apply.IsEnabled && window.GetVisualDescendants().OfType<SunburstControl>().Any(control => control.IsEffectivelyVisible), () => ComparisonText(window));
            choice.SelectedIndex = 0; Click(window, "Apply comparison chart"); await Until(() => apply.IsEnabled);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<AnalysisChartControl>(), control => control.IsEffectivelyVisible);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); ThemeManager.Apply("Classic"); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(a.PrivatePath)); Assert.Empty(Directory.EnumerateFileSystemEntries(b.PrivatePath));
        Assert.Equal(a.Payload, File.ReadAllBytes(a.PayloadPath)); Assert.Equal(b.Payload, File.ReadAllBytes(b.PayloadPath));
    }
}
