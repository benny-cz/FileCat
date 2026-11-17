using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static ComboBox GroupingSelector(Window window) => window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Indexed grouping");
    private static bool GroupReady(Window window) => window.GetVisualDescendants().OfType<TextBlock>().Any(text => AutomationLabel(text) == "Indexed analysis status" && text.Text?.StartsWith("Grouped view ready.", StringComparison.Ordinal) == true);
    private static TextBox IndexedDetails(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationLabel(text) == "Captured metadata inspector");
    private static bool MemberReady(Window window) => GroupReady(window) && IndexedList(window).Items.OfType<AnalysisDatasetGroupRow>().Any();
    private static Task SelectGrouping(Window window, AnalysisGroupingDimension dimension)
    { GroupingSelector(window).SelectedIndex = (int)dimension; return Until(() => GroupReady(window) && IndexedList(window).Items.OfType<AnalysisDatasetGroup>().Any()); }

    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task IndexedGroupingBrowsesCapturedRowsHistoryChartsAndPagedChargesAfterSourceClosure(AnalysisGroupingDimension dimension)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, dimension); Frame(window);
            var list = IndexedList(window); var group = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Assert.Equal((UInt128)305, group.Totals.Known); Assert.Equal(303UL, group.Members);
            list.SelectedItem = group;
            Assert.Contains("no filesystem entry or action authority", IndexedDetails(window).Text);
            Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            Assert.InRange(list.ItemCount, 1, 128);
            var first = list.Items.OfType<AnalysisDatasetGroupRow>().ToArray();
            var target = first.Single(row => row.Member.Path.Name == "match-target.bin"); list.SelectedItem = target;
            await Until(() => IndexedDetails(window).Text?.Contains("amount assigned to this entry", StringComparison.Ordinal) == true);
            Assert.Contains("3 bytes", IndexedDetails(window).Text);
            Click(window, "Accounting charges"); await Until(() => IndexedDetails(window).Text?.Contains("of 1. Captured aliases", StringComparison.Ordinal) == true);
            Click(window, "Next entries"); await Until(() => MemberReady(window) && !list.Items.OfType<AnalysisDatasetGroupRow>().Any(row => row.Member.Path.Id == target.Member.Path.Id)); Frame(window);
            Assert.DoesNotContain(list.Items.OfType<AnalysisDatasetGroupRow>(), row => first.Any(previous => previous.Member.Path.Id == row.Member.Path.Id));
            Click(window, "Previous entries"); await Until(() => MemberReady(window) && list.Items.OfType<AnalysisDatasetGroupRow>().Any(row => row.Member.Path.Id == target.Member.Path.Id)); Frame(window);
            Click(window, "Parent"); await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Any());
            Click(window, "Back"); await Until(() => MemberReady(window));
            Click(window, "Forward"); await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Any()); Frame(window);
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Click(window, "Accounting charges"); await Until(() => IndexedDetails(window).Text?.Contains("of 303. Captured aliases", StringComparison.Ordinal) == true);
            Click(window, "More charges"); await Until(() => IndexedDetails(window).Text?.Contains("Charges 32", StringComparison.Ordinal) == true);
            Click(window, "First charges"); await Until(() => IndexedDetails(window).Text?.Contains("Charges 0", StringComparison.Ordinal) == true);
            foreach (var mode in new[] { "Treemap", "Sunburst" })
            {
                window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Indexed chart mode").SelectedIndex = mode == "Treemap" ? 0 : 1;
                Frame(window); var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(chart => chart.IsVisible);
                chart.Focus(); window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null); window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                await Until(() => IndexedDetails(window).Text?.Contains("Accounting group", StringComparison.Ordinal) == true);
                Assert.Contains("known", IndexedDetails(window).Text);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Until(() => MemberReady(window)); Frame(window);
                // Focus and activation are separate; opening a geometry suffix starts its
                // own sorted table at offset zero.
                chart.Focus(); window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null); window.KeyRelease(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                Assert.DoesNotContain("Exact Other subset.", CapturedScopeStatus(window));
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Until(() => CapturedScopeStatus(window).Contains("Exact Other subset.", StringComparison.Ordinal));
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Entries 1", StringComparison.Ordinal) == true);
                Click(window, "Parent"); await Until(() => MemberReady(window) && !CapturedScopeStatus(window).Contains("Exact Other subset.", StringComparison.Ordinal));
                Click(window, "Parent"); await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Any()); Frame(window);
            }
            GroupingSelector(window).SelectedIndex = 0;
            await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Any()); Frame(window);
            Assert.False(GroupReady(window)); Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.PrivatePath));
    }

    [AvaloniaFact]
    public async Task IndexedGroupedTableAppliesEverySortAndDirectionWhilePreservingCapturedMemberFocus()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window);
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>()); Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            var selected = list.Items.OfType<AnalysisDatasetGroupRow>().Single(row => row.Member.Path.Name == "match-target.bin");
            list.SelectedItem = selected; await Until(() => IndexedDetails(window).Text?.Contains("match-target.bin", StringComparison.Ordinal) == true);
            var order = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Indexed table order");
            var descending = window.GetVisualDescendants().OfType<CheckBox>().Single(check => AutomationLabel(check) == "Indexed descending table order");
            foreach (int sort in new[] { 2, 3, 1, 4, 0 })
            foreach (bool direction in new[] { false, true })
            {
                object? old = list.ItemsSource; order.SelectedIndex = sort; descending.IsChecked = direction;
                string label = (sort switch { 2 => "File size", 3 => "Name", 1 => "File paths", 4 => "Metadata state", _ => "Active metric" }) + (direction ? " · Descending" : " · Ascending");
                await Until(() => MemberReady(window) && !ReferenceEquals(old, list.ItemsSource) && window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.EndsWith(label, StringComparison.Ordinal) == true)); Frame(window);
                var rows = list.Items.OfType<AnalysisDatasetGroupRow>().ToArray(); Assert.InRange(rows.Length, 1, 128);
                for (int index = 1; index < rows.Length; index++)
                {
                    int comparison = sort switch
                    {
                        2 => rows[index - 1].Member.LogicalLength.Value!.Value.CompareTo(rows[index].Member.LogicalLength.Value!.Value),
                        3 => StringComparer.OrdinalIgnoreCase.Compare(rows[index - 1].Member.Path.Name, rows[index].Member.Path.Name),
                        1 => rows[index - 1].Assigned.MatchingPaths.CompareTo(rows[index].Assigned.MatchingPaths),
                        4 => rows[index - 1].Member.Path.MetadataState.CompareTo(rows[index].Member.Path.MetadataState),
                        _ => rows[index - 1].Assigned.Known.CompareTo(rows[index].Assigned.Known)
                    };
                    Assert.True(direction ? comparison >= 0 : comparison <= 0);
                }
                await Until(() => IndexedDetails(window).Text?.Contains("match-target.bin", StringComparison.Ordinal) == true);
                if (rows.Any(row => row.Member.Path.Id == selected.Member.Path.Id)) Assert.Equal(selected.Member.Path.Id, Assert.IsType<AnalysisDatasetGroupRow>(list.SelectedItem).Member.Path.Id);
                else Assert.Null(list.SelectedItem);
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedGroupedSharedNativeAliasesStayZeroAssignedAndExposeTheSingleSharedCharge()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync(sharedAlias: true);
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken);
        await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.FileType); Frame(window); var list = IndexedList(window);
            var shared = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>(), group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
            Assert.Equal((UInt128)3, shared.Totals.Known); Assert.Equal(shared.Totals.Known, shared.Shared.Known);
            list.SelectedItem = shared; Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            var rows = list.Items.OfType<AnalysisDatasetGroupRow>().ToArray(); Assert.Equal(2, rows.Length);
            Assert.All(rows, row => { Assert.Equal((UInt128)0, row.Assigned.Known); Assert.True(row.SharedElsewhere); Assert.Equal(3UL, row.Member.LogicalLength.Value); });
            list.SelectedItem = rows[0]; await Until(() => IndexedDetails(window).Text?.Contains("Shared charge shown in its accounting group", StringComparison.Ordinal) == true);
            Click(window, "Accounting charges"); await Until(() => IndexedDetails(window).Text?.Contains("of 0. Captured aliases", StringComparison.Ordinal) == true);
            var bucket = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString()?.StartsWith("Shared files — charged once ·", StringComparison.Ordinal) == true);
            bucket.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Until(() => IndexedDetails(window).Text?.Contains("of 1. Captured aliases", StringComparison.Ordinal) == true);
            Assert.Contains("shared subtotal", IndexedDetails(window).Text); Assert.Contains("2 observed aliases", IndexedDetails(window).Text);
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.PrivatePath));
    }

    [AvaloniaFact]
    public async Task IndexedGroupingReplacesAllMetricsAndItsQueryWithoutRetainingDuplicateHierarchyReaders()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window);
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationLabel(text) == "Filter indexed observations");
            query.Text = "name:match-target.bin"; Click(window, "Apply filter");
            await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Single().Members == 1); Frame(window);
            var metricSelector = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Indexed metric");
            foreach (var metric in new[] { AnalysisMetric.Files, AnalysisMetric.UniqueFileSize, AnalysisMetric.OnDisk, AnalysisMetric.FileSize })
            {
                var old = list.ItemsSource; metricSelector.SelectedIndex = (int)metric;
                await Until(() => GroupReady(window) && !ReferenceEquals(old, list.ItemsSource)); Frame(window);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("1 definite matches; 0 uncertain results", StringComparison.Ordinal) == true);
                var group = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>()); Assert.Equal(1UL, group.Members);
                if (metric == AnalysisMetric.Files) Assert.Equal((UInt128)1, group.Totals.Known);
                else if (metric != AnalysisMetric.OnDisk) Assert.Equal((UInt128)3, group.Totals.Known);
                list.SelectedItem = group; Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
                var row = Assert.Single(list.Items.OfType<AnalysisDatasetGroupRow>()); Assert.Equal(group.Totals.Known, row.Assigned.Known);
                list.SelectedItem = row;
                string value = row.Assigned.Known.ToString(System.Globalization.CultureInfo.InvariantCulture) + (metric == AnalysisMetric.Files ? "" : " bytes");
                await Until(() => IndexedDetails(window).Text?.Contains("Active metric: " + value + " · File size: 3 bytes", StringComparison.Ordinal) == true);
                Click(window, "Parent"); await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Any()); Frame(window);
            }
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedGroupedParentPublicationDiscardsAnInspectionStartedOnTheOldPage()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window);
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>()); Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            var target = list.Items.OfType<AnalysisDatasetGroupRow>().Single(row => row.Member.Path.Name == "match-target.bin");
            Click(window, "Parent"); list.SelectedItem = target;
            await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Any()); Frame(window);
            await Until(() => !window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Loading captured metadata…"));
            Assert.DoesNotContain("match-target.bin", IndexedDetails(window).Text);
            Assert.Contains("Choose a group or captured member", IndexedDetails(window).Text);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedGroupedWindowsKeepBothChartsAndTableEvidenceInsideNarrowAndLargeTextLayoutsAcrossThemes()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow { Height = 900, FontSize = 20 };
        await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window);
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>()); Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            var target = list.Items.OfType<AnalysisDatasetGroupRow>().Single(row => row.Member.Path.Name == "match-target.bin"); list.SelectedItem = target;
            foreach (var theme in ThemePalette.All)
            foreach (int width in new[] { 1180, 380 })
            foreach (int mode in new[] { 0, 1 })
            {
                ThemeManager.Apply(theme.Name); window.Width = width;
                window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Indexed chart mode").SelectedIndex = mode;
                Frame(window); list.ScrollIntoView(target); Frame(window);
                var grid = window.GetVisualDescendants().OfType<Grid>().Single(grid => AutomationLabel(grid)?.StartsWith("match-target.bin;", StringComparison.Ordinal) == true);
                Assert.True(grid.Bounds.Width <= list.Bounds.Width + 0.5);
                Assert.All(grid.Children.OfType<TextBlock>(), cell => Assert.True(cell.Bounds.Right <= grid.Bounds.Width + 0.5));
                Assert.Contains("3 B", AutomationLabel(grid));
                var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(chart => chart.IsVisible);
                Assert.Contains(AnalysisChartColors.Title(AnalysisColorDimension.FileType), AutomationLabel(chart));
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, 900)); bitmap.Render(window);
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedGroupingWaitsForChartGestureKeepsInvalidRequestsAndClosesPendingReplacement()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window); bool pressed = false;
        try
        {
            var list = IndexedList(window); object? original = list.ItemsSource;
            var chart = window.GetVisualDescendants().OfType<TreemapControl>().Single(); var point = chart.TranslatePoint(new Point(30, 30), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); pressed = true;
            GroupingSelector(window).SelectedIndex = (int)AnalysisGroupingDimension.Extension;
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Query ready. Finish the current interaction to display it."));
            Assert.Same(original, list.ItemsSource);
            window.MouseUp(point, MouseButton.Left); pressed = false; await Until(() => GroupReady(window)); Frame(window);
            original = list.ItemsSource;
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationLabel(text) == "Filter indexed observations");
            query.Text = "size:>"; Click(window, "Apply filter");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith("The previous view remains displayed.", StringComparison.Ordinal) == true));
            Assert.Same(original, list.ItemsSource);
            query.Text = "name:match-target.bin"; Click(window, "Apply filter");
            await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Single().Members == 1,
                () => string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Where(text => AutomationLabel(text) == "Indexed analysis status").Select(text => text.Text)) +
                    " Groups: " + string.Join(", ", list.Items.OfType<AnalysisDatasetGroup>().Select(group => group.Definition.Label + ": " + group.Members))); Frame(window);
            GroupingSelector(window).SelectedIndex = (int)AnalysisGroupingDimension.ModifiedAge;
            window.ForceClose(); await window.WaitForIndexedCloseAsync();
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { if (pressed) window.MouseUp(new Point(20, 20), MouseButton.Left); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    private static void CreateGroupedNativeAlias(string existing, string alias)
    {
        bool linked = OperatingSystem.IsWindows() ? CreateGroupedHardLink(alias, existing, 0) : LinkGroupedFile(existing, alias) == 0;
        Assert.True(linked, "Native hard-link fixture failed: " + Marshal.GetLastPInvokeError());
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateGroupedHardLink(string alias, string existing, nint security);
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkGroupedFile([MarshalAs(UnmanagedType.LPUTF8Str)] string existing, [MarshalAs(UnmanagedType.LPUTF8Str)] string alias);
}
