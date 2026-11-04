using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task LatestFilterWinsAndFrozenInteractionKeepsItsSelectionAndBasket()
    {
        using var model = new DiskSpaceViewModel(Fixture());
        var initial = model.RebuildAsync(true);
        model.Query = "picture.png";
        await model.RebuildAsync(true); await initial;
        Assert.Equal((UInt128)32768, model.Tree!.ScopeValue(AnalysisTree.RootKey));
        model.Query = ""; await model.RebuildAsync(true);
        model.Focus("n:4", toggleMark: true); model.StageSelection();
        model.Interaction(true); model.Query = "picture.png"; await model.RebuildAsync(true);
        Assert.Equal((UInt128)98304, model.Tree.ScopeValue(AnalysisTree.RootKey));
        model.Focus("n:3", open: true); model.Interaction(false);
        for (int i = 0; i < 200 && model.Tree.ScopeValue(AnalysisTree.RootKey) != 32768; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal((UInt128)32768, model.Tree.ScopeValue(AnalysisTree.RootKey));
        Assert.Equal("n:3", model.Selection.Scope);
        Assert.Equal("picture.png", Assert.Single(model.Rows).Name);
        Assert.Equal(new ulong[] { 4 }, model.Selection.Marks); Assert.Equal(new ulong[] { 4 }, model.Selection.Basket);
        model.Query = "size>bogus-unit"; await model.RebuildAsync(true);
        Assert.NotNull(model.FilterError); Assert.Equal((UInt128)32768, model.Tree.ScopeValue(AnalysisTree.RootKey));
    }

    private static AnalysisSnapshot Fixture() => new(
        new(Guid.NewGuid(), 1, 1, "render-fixture", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        [new("r", "file", "Fixture", "/fixture", "fixture-device", IdentityAssurance.Verified, "Fixture")],
        [new(1, "r", 0, "Fixture", AnalysisKind.Directory), new(2, "r", 1, "Projects", AnalysisKind.Directory),
         new(3, "r", 1, "Photos", AnalysisKind.Directory), new(4, "r", 2, "source.cs", AnalysisKind.File, "a"),
         new(5, "r", 3, "picture.png", AnalysisKind.File, "b"), new(6, "r", 2, "Unknown\u202E.bin", AnalysisKind.File, "c")],
        [new("a", "device:a", AnalysisMeasurement.Known(65536), AnalysisMeasurement.Known(1)),
         new("b", "device:b", AnalysisMeasurement.Known(32768), AnalysisMeasurement.Known(1)),
         new("c", null, AnalysisMeasurement.Known(100), AnalysisMeasurement.Known(1))],
        [new("a", "a", null, "primary", "fixture", AnalysisMeasurement.Known(65536), "fixture", DateTimeOffset.UnixEpoch, []),
         new("b", "b", null, "primary", "fixture", AnalysisMeasurement.Known(32768), "fixture", DateTimeOffset.UnixEpoch, []),
         new("c", "c", null, "primary", "fixture", AnalysisMeasurement.Missing(MeasurementState.Unsupported), "fixture", DateTimeOffset.UnixEpoch, [])],
        [], new(), AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);

    [AvaloniaFact]
    public async Task BothChartsRetainSelectionAcrossThemesAndNarrowInspectorRemainsReachable()
    {
        string theme = ThemeManager.RequestedName;
        var fluent = new FluentTheme();
        var styles = new StyleInclude(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") };
        Application.Current!.Styles.Add(fluent); Application.Current.Styles.Add(styles);
        var model = new DiskSpaceViewModel(Fixture());
        var window = new DiskSpaceWindow();
        string output = Path.Combine(AppContext.BaseDirectory, "analysis-renders"); Directory.CreateDirectory(output);
        try
        {
            await model.RebuildAsync(true); model.Focus("n:4", toggleMark: true);
            window.AddSession(model); window.Show();
            foreach (string currentTheme in new[] { "Classic", "ClassicDark", "HighContrast", "Cyberpunk", "Psychedelic", "Steampunk", "DosCommander" })
            {
                ThemeManager.Apply(currentTheme);
                foreach (int width in new[] { 1180, 600, 380 })
                {
                    window.Width = width; window.Height = 780;
                    var mode = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Items.OfType<string>().SequenceEqual(["Treemap", "Sunburst", "Table"]));
                    for (int index = 0; index < 3; index++)
                    {
                        mode.SelectedIndex = index; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        Assert.Equal("n:4", model.Selection.Focus); Assert.Contains(4ul, model.Selection.Marks);
                        Assert.Equal((UInt128)98304, model.Tree!.ScopeValue(AnalysisTree.RootKey));
                        var table = window.GetVisualDescendants().OfType<ListBox>().Single();
                        Assert.True(table.Bounds.Width > 100 && table.Bounds.Height > 40, $"{currentTheme}/{width}/{index}: table has {table.Bounds}.");
                        if (width >= 900 && index < 2)
                        {
                            var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(c => c.IsVisible);
                            Assert.True(chart.Bounds.Height > 100);
                            Assert.Contains("98304", AutomationProperties.GetName(chart));
                        }
                        using var bitmap = new RenderTargetBitmap(new PixelSize(width, 780)); bitmap.Render(window);
                        using var file = File.Create(Path.Combine(output, $"{currentTheme}-{width}-{index}.png")); bitmap.Save(file, new PngBitmapEncoderOptions());
                    }
                }
            }
            var inspect = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Inspect selection");
            Assert.True(inspect.IsVisible && inspect.IsEnabled);
            inspect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("source.cs", StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Close details" && b.IsEffectivelyVisible);
        }
        finally { window.ForceClose(); model.Dispose(); Application.Current.Styles.Remove(styles); Application.Current.Styles.Remove(fluent); ThemeManager.Apply(theme); }
    }
}
