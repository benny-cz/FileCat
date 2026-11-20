using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task GroupedTreemapAndSunburstUseTheSameBoundedNativeMemberAmountsAcrossThemesAndColorModes()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        await using var groups = await fixture.Lease!.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.FileType, cancellation: TestContext.Current.CancellationToken);
        var root = await groups.ReadGroupsAsync(cancellation: TestContext.Current.CancellationToken); var group = Assert.Single(root.Rows);
        var members = await groups.ReadMemberRowsAsync(group.Definition.Key, maximum: 32, cancellation: TestContext.Current.CancellationToken);
        Assert.True(members.HasMore);
        var overview = AnalysisGroupedChart.Overview(groups.Descriptor.Identity, groups.Dimension, groups.Metric, groups.Totals, groups.LogicalTotals, root);
        var detail = AnalysisGroupedChart.Members(groups.Descriptor.Identity, groups.Dimension, groups.Metric, group, members);
        var treemap = new TreemapControl(); var sunburst = new SunburstControl { IsVisible = false };
        var host = new Grid(); host.Children.Add(treemap); host.Children.Add(sunburst);
        var window = new Window { Width = 820, Height = 580, FontSize = 13, Content = host }; window.Show(); Frame(window);
        string? activated = null; treemap.Activated += (key, _, _) => activated = key; sunburst.Activated += (key, _, _) => activated = key;
        try
        {
            foreach (var theme in ThemePalette.All)
            foreach (var color in Enum.GetValues<AnalysisColorDimension>())
            {
                ThemeManager.Apply(theme.Name);
                foreach (var data in new[] { overview, detail })
                foreach (var chart in new AnalysisChartControl[] { treemap, sunburst })
                {
                    treemap.IsVisible = ReferenceEquals(chart, treemap); sunburst.IsVisible = ReferenceEquals(chart, sunburst);
                    chart.SetView(data, data.Scope, null, ImmutableHashSet<ulong>.Empty, color); Frame(window);
                    Assert.Contains(AnalysisChartColors.Title(color), AutomationLabel(chart));
                    Assert.Equal(data.ScopeValue(data.Scope), data.ChildrenOf(data.Scope).Aggregate((UInt128)0, (sum, item) => sum + item.Value));
                    using var bitmap = new RenderTargetBitmap(new PixelSize(820, 580)); bitmap.Render(window);
                    chart.Focus(); activated = null;
                    window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                    window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null); Frame(window);
                    Assert.NotNull(activated); Assert.Contains(data.ChildrenOf(data.Scope), item => item.Key == activated);
                }
            }
            treemap.IsVisible = true; sunburst.IsVisible = false; treemap.SetView(detail, detail.Scope, null, [], AnalysisColorDimension.FileType); Frame(window);
            window.MouseMove(new Point(24, 24)); window.MouseDown(new Point(24, 24), MouseButton.Left);
            treemap.SetView(overview, overview.Scope, null, [], AnalysisColorDimension.ModifiedAge); Frame(window);
            Assert.Contains(AnalysisChartColors.Title(AnalysisColorDimension.FileType), AutomationLabel(treemap));
            window.MouseUp(new Point(24, 24), MouseButton.Left); Frame(window);
            Assert.Contains(AnalysisChartColors.Title(AnalysisColorDimension.ModifiedAge), AutomationLabel(treemap));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.Close(); }
    }
}
