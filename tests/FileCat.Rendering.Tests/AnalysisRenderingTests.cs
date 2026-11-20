using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
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
using FileCat.Core.Commands;
using System.Collections.Immutable;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task ScopeSummaryKeepsLocalCountsUnitsUncertaintyAndIncompatibleBases()
    {
        using var model = new DiskSpaceViewModel(Fixture()); await model.RebuildAsync(true);
        model.Selection.Navigate("n:3");
        Assert.Contains(DiskSpaceViewModel.Bytes(32768), model.ScopeSummary); Assert.Contains("1 observed file path", model.ScopeSummary);
        Assert.DoesNotContain("3 observed file paths", model.ScopeSummary);
        var s = Fixture(); s = s with { Facts = s.Facts.SetItem(2, s.Facts[2] with { Measurement = AnalysisMeasurement.Known(100) }) };
        using var unverified = new DiskSpaceViewModel(s); await unverified.RebuildAsync(true);
        Assert.Contains("100 B reported for identity-unverified paths", unverified.ScopeSummary);
        s = s with { Roots = s.Roots.Add(new("r2", "file", "Other", "/other", "other-device", IdentityAssurance.Verified, "Other")),
            Nodes = s.Nodes.SetItem(2, s.Nodes[2] with { RootId = "r2", ParentId = 0 }).SetItem(4, s.Nodes[4] with { RootId = "r2" }),
            Facts = s.Facts.SetItem(1, s.Facts[1] with { BasisId = "another-basis" }) };
        using var incompatible = new DiskSpaceViewModel(s); await incompatible.RebuildAsync(true);
        Assert.Contains("no combined total", incompatible.ScopeSummary); Assert.DoesNotContain("0 B", incompatible.ScopeSummary);
    }

    [AvaloniaFact]
    public async Task FinishedTemporaryAnalysesDefaultToKeepOpenAndTabNamesNeverRepeat()
    {
        using var styles = new ProductStyles(); using var temporary = new DiskSpaceViewModel(Fixture());
        await temporary.RebuildAsync(true); Assert.False(temporary.Running); Assert.True(temporary.HasTemporaryWork);
        var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(temporary); window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.Close(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(window.IsVisible);
            var keep = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content as string == "Keep open");
            Assert.True(keep.IsDefault); Assert.True(keep.IsCancel);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(window.IsVisible); Assert.Equal(1, window.SessionCount);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "Keep open");
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        var tabsWindow = new DiskSpaceWindow();
        try
        {
            for (int i = 0; i < 3; i++) tabsWindow.AddSession(new DiskSpaceViewModel(Fixture() with { Origin = AnalysisOrigin.ImportedInventory }));
            tabsWindow.Show(); Dispatcher.UIThread.RunJobs(); tabsWindow.UpdateLayout();
            var tabs = tabsWindow.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex = 1;
            tabsWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Close analysis").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); Assert.Equal(2, tabsWindow.SessionCount);
            tabsWindow.AddSession(new DiskSpaceViewModel(Fixture() with { Origin = AnalysisOrigin.ImportedInventory }));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Analysis 1", "Analysis 3", "Analysis 4" }, tabs.Items.OfType<TabItem>().Select(t => t.Header as string));
        }
        finally { tabsWindow.ForceClose(); await tabsWindow.WaitForIndexedCloseAsync(); }
    }
    [AvaloniaFact]
    public async Task UncertainPredicateResultsAreAnExplicitCohortAndDateOnlyUsesTheCapturedZone()
    {
        var s = Fixture();
        using var model = new DiskSpaceViewModel(s) { Query = "sparse:true" };
        await model.RebuildAsync(true);
        Assert.Equal(0ul, model.Tree!.Projection.Totals.MatchingPaths);
        Assert.Equal(3, model.Tree.Projection.UnknownFilterNodes.Count);
        model.ShowUnknownFilterResults = true; await model.RebuildAsync(true);
        Assert.True(model.Tree.Projection.IncludesUnknownFilterResults); Assert.Equal(3ul, model.Tree.Projection.Totals.MatchingPaths);
        model.Query = "  "; await model.RebuildAsync(true);
        Assert.False(model.ShowUnknownFilterResults); Assert.False(model.Tree.Projection.IncludesUnknownFilterResults);
        Assert.Equal(3ul, model.Tree.Projection.Totals.MatchingPaths);
        using var unavailable = new DiskSpaceViewModel(s with { Identity = s.Identity with { TimeZoneId = "FileCat/Missing-Zone-For-Fixture" } }) { Query = "modified<2026-01-01" };
        await unavailable.RebuildAsync(true); Assert.Contains("explicit offset", unavailable.FilterError);
        unavailable.Query = "modified<2026-01-01T00:00:00Z"; await unavailable.RebuildAsync(true); Assert.Null(unavailable.FilterError);
    }

    [AvaloniaFact]
    public async Task InspectorSeparatesKnownZeroLogicalSizeFromMissingAllocationAndPreservesRawNameEvidence()
    {
        using var styles = new ProductStyles(); var s = Fixture(); const string rawName = "bad\ud800. ";
        s = s with
        {
            Nodes = s.Nodes.SetItem(5, s.Nodes[5] with { Name = rawName, NameEncoding = "utf16le", RawName = AnalysisRawNames.Encode("utf16le", rawName).ToImmutableArray() }),
            Objects = s.Objects.SetItem(2, s.Objects[2] with { LogicalLength = AnalysisMeasurement.Known(0) })
        };
        using var zero = new DiskSpaceViewModel(s); await zero.RebuildAsync(true); zero.Focus("n:6");
        var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(zero); window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var item = zero.Tree!.Items["n:6"];
            Assert.True(item.Unknown); Assert.False(item.LogicalUnknown); Assert.Equal("0 bytes", DiskSpaceViewModel.LogicalText(item, true));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "On disk — reported: Unknown");
            string inspector = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
                .Select(t => t.Text?.Replace('\u00a0', ' ')));
            Assert.Contains("File size: 0 B", inspector);
            var exact = window.GetVisualDescendants().OfType<Expander>().Single(control => Equals(control.Header, "Measurement details"));
            Assert.False(exact.IsExpanded);
            exact.IsExpanded = true;
            Frame(window);
            Assert.Contains(exact.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("File size: 0 bytes", StringComparison.Ordinal) == true);
            Assert.Contains("bad\\uD800\\u002E\\u0020", inspector); Assert.Contains("Raw name (utf16le):", inspector);
            Assert.Contains("no lossless clipboard path", inspector); Assert.Null(zero.FocusedPath());
            using var missing = new DiskSpaceViewModel(s with { Objects = s.Objects.SetItem(2, s.Objects[2] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Failed) }) });
            await missing.RebuildAsync(true); missing.Focus("n:6"); window.AddSession(missing); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal("Unknown", DiskSpaceViewModel.LogicalText(missing.Tree!.Items["n:6"], true));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("File size: Unknown", StringComparison.Ordinal) == true);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IncompatibleAllocationRootsOfferASeparateLocationView()
    {
        using var styles = new ProductStyles(); var s = Fixture();
        s = s with
        {
            Roots = s.Roots.Add(new("second", "file", "Other", "/other", "other-device", IdentityAssurance.Verified, "Other")),
            Nodes = s.Nodes.Add(new AnalysisNode(7, "second", 0, "Other", AnalysisKind.Directory) { ObservedIdentity = "other-device:dir" }).Add(new(8, "second", 7, "other.bin", AnalysisKind.File, "d")),
            Objects = s.Objects.Add(new("d", "other-device:d", AnalysisMeasurement.Known(99), AnalysisMeasurement.Known(1))),
            Facts = s.Facts.Add(new("d", "d", null, "primary", "other-basis", AnalysisMeasurement.Known(4096), "fixture", DateTimeOffset.UnixEpoch, []))
                .Add(new("dir", null, 7, "directory-self", "other-basis", AnalysisMeasurement.Known(17), "fixture", DateTimeOffset.UnixEpoch, []))
        };
        using var model = new DiskSpaceViewModel(s); await model.RebuildAsync(true); var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(model); window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.False(model.Tree!.Projection.Totals.CompatibleBasis);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("These locations report different allocation components", StringComparison.Ordinal) == true);
            var root = window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationLabel(c) == "Filter captured location");
            root.SelectedIndex = 2;
            for (int i = 0; i < 100 && model.Tree.ScopeValue(AnalysisTree.RootKey) != 4113; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal((UInt128)4113, model.Tree.ScopeValue(AnalysisTree.RootKey)); Assert.True(model.Tree.Projection.Totals.CompatibleBasis);
            root.SelectedIndex = 0; model.Metric = AnalysisMetric.FileSize; await model.RebuildAsync(true);
            for (int i = 0; i < 100 && model.Tree.Projection.Metric != AnalysisMetric.FileSize; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal((UInt128)98503, model.Tree.ScopeValue(AnalysisTree.RootKey));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
    [AvaloniaFact]
    public async Task ReviewCancelBindingRoutesFromOverlayAndEnterOnAFileOpensDetails()
    {
        using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(Fixture()); await model.RebuildAsync(true);
        model.Focus("n:2", open: true);
        for (int i = 0; i < 100 && model.Rows.Length != 2; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        var map = new Keymap(CommandRegistry.CreateDefault(), new Dictionary<string, string[]> { [CommandIds.DiskSpaceReviewCancel] = ["Ctrl+R"] });
        var window = new DiskSpaceWindow(keymap: () => map) { Width = 600, Height = 600 };
        try
        {
            window.AddSession(model); window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedItem = model.Rows.Single(r => r.Key == "n:4"); Assert.True(list.Focus());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Close details" && b.IsEffectivelyVisible);
            window.KeyPress(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
            window.KeyRelease(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Close details" && b.IsEffectivelyVisible);
            var review = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Review cleanup");
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Cancel / Keep files" && b.IsEffectivelyVisible);
            window.KeyPress(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
            window.KeyRelease(Key.R, RawInputModifiers.Control, PhysicalKey.R, "r");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Cancel / Keep files" && b.IsEffectivelyVisible);
            Assert.True(window.IsVisible); Assert.Equal("n:4", model.Selection.Focus);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task ShortLargeTextPageScrollsAndTheRankedTableRemainsVirtualized()
    {
        using var styles = new ProductStyles();
        var s = Fixture();
        s = s with
        {
            Nodes = new[] { s.Nodes[0] }.Concat(Enumerable.Range(2, 4000).Select(i => new AnalysisNode((ulong)i, "r", 1, $"file-{i:D5}.dat", AnalysisKind.File, i.ToString()))).ToImmutableArray(),
            Objects = Enumerable.Range(2, 4000).Select(i => new AnalysisObject(i.ToString(), "fixture:" + i, AnalysisMeasurement.Known(16), AnalysisMeasurement.Known(1))).ToImmutableArray(), Facts = []
        };
        using var model = new DiskSpaceViewModel(s) { Metric = AnalysisMetric.FileSize };
        await model.RebuildAsync(true); model.Focus("n:1", open: true);
        for (int i = 0; i < 100 && model.Rows.Length != 4000; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        var window = new DiskSpaceWindow { Width = 380, Height = 430, FontSize = 20 };
        try
        {
            window.AddSession(model); window.Show();
            foreach (string theme in new[] { "Classic", "ClassicDark", "HighContrast" })
            {
                ThemeManager.Apply(theme); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var page = window.GetVisualDescendants().OfType<ScrollViewer>().Single(c => c.Name == "AnalysisPageScroll");
                var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                Assert.True(page.Extent.Height > page.Viewport.Height + 100);
                Assert.InRange(list.Bounds.Height, 100, 2000);
                Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 100);
                page.Offset = new Vector(0, page.Extent.Height); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var review = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Review cleanup");
                var point = review.TranslatePoint(default, window)!.Value;
                Assert.True(point.Y >= 0 && point.Y + review.Bounds.Height <= window.ClientSize.Height + 1, $"{theme}: review at {point}, size {review.Bounds.Size}, client {window.ClientSize}.");
                using var frame = new RenderTargetBitmap(new PixelSize(380, 430)); frame.Render(window);
                string output = Path.Combine(AppContext.BaseDirectory, "analysis-renders"); Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, $"{theme}-380-short-large-text.png"), new PngBitmapEncoderOptions());
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    private sealed class ProductStyles : IDisposable
    {
        private readonly string _theme = ThemeManager.RequestedName;
        private readonly FluentTheme _fluent = new();
        private readonly StyleInclude _styles = new(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") };
        public ProductStyles() { Application.Current!.Styles.Add(_fluent); Application.Current.Styles.Add(_styles); }
        public void Dispose() { Application.Current!.Styles.Remove(_styles); Application.Current.Styles.Remove(_fluent); ThemeManager.Apply(_theme); }
    }
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
                            Assert.Contains($"{98304:N0} bytes", AutomationLabel(chart));
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
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); model.Dispose(); Application.Current.Styles.Remove(styles); Application.Current.Styles.Remove(fluent); ThemeManager.Apply(theme); }
    }
}
