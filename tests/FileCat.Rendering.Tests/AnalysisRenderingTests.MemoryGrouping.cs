using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static ComboBox MemoryGroupingSelector(Window window) => window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Analysis grouping");
    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task MemoryGroupingPreservesMarksBasketFiltersHistoryAndLiteralCapturedFolderNavigation(AnalysisGroupingDimension dimension)
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture()) { Metric = AnalysisMetric.FileSize };
        await model.RebuildAsync(true); model.Focus("n:4", true); model.StageSelection();
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            MemoryGroupingSelector(window).SelectedIndex = (int)dimension;
            await Until(() => model.Tree?.GroupingDimension == dimension); Frame(window);
            Assert.Equal(AnalysisTree.RootKey, model.Selection.Scope);
            var group = Assert.Single(model.Tree!.Groups.Values, group => group.Kind == AnalysisGroupKind.Entries && group.Members.Contains(4));
            model.Focus(group.Definition.Key, open: true); await Until(() => model.Selection.Scope == group.Definition.Key); Frame(window);
            Assert.Equal(group.Members.Order(), model.Rows.Where(row => row.NodeId.HasValue).Select(row => row.NodeId!.Value).Order());
            model.Query = "name:source.cs"; await model.RebuildAsync(true); Frame(window);
            Assert.Equal(new ulong[] { 4 }, model.Tree!.CaptureMembers(model.Selection.Scope));
            Assert.Equal(new ulong[] { 4 }, model.Selection.Marks); Assert.Equal(new ulong[] { 4 }, model.Selection.Basket);
            var previous = model.Tree; model.Query = "size:nope"; await model.RebuildAsync(true);
            Assert.Same(previous, model.Tree); Assert.NotNull(model.FilterError);
            Click(window, "Clear filter"); await Until(() => model.Tree!.Projection.FilterText == "" && model.FilterError is null);
            model.Parent(); await Until(() => model.Selection.Scope == AnalysisTree.RootKey);
            model.Back(); await Until(() => model.Selection.Scope == group.Definition.Key);
            model.Forward(); await Until(() => model.Selection.Scope == AnalysisTree.RootKey);
            model.Focus(group.Definition.Key, open: true); await Until(() => model.Selection.Scope == group.Definition.Key);
            model.Focus("n:4"); Assert.Equal("/fixture/Projects/source.cs", model.FocusedPath());
            await model.ShowFocusedInHierarchyAsync(); Frame(window);
            Assert.Equal(AnalysisGroupingDimension.FolderHierarchy, model.Tree!.GroupingDimension); Assert.Equal("n:2", model.Selection.Scope);
            Assert.Equal("n:4", model.Selection.Focus); Assert.Contains(model.Rows, row => row.NodeId == 4);
            Assert.Equal(0, MemoryGroupingSelector(window).SelectedIndex);
            Assert.Equal(new ulong[] { 4 }, model.Selection.Marks); Assert.Equal(new ulong[] { 4 }, model.Selection.Basket);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task MemoryGroupingNativeFallbackCapturesOnlyTheSharedAliasesAndRetainsItsPublishedOwnerAfterClose()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture(); await CorruptProfileForMemoryAsync(fixture);
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        string original = Path.Combine(input, "original.bin"); File.WriteAllBytes(original, [1, 2, 3]);
        CreateGroupedNativeAlias(original, Path.Combine(input, "alias.jpg")); File.WriteAllBytes(Path.Combine(input, "zero.txt"), []);
        using var model = new DiskSpaceViewModel(fixture.Services) { Grouping = AnalysisGroupingDimension.FileType, Metric = AnalysisMetric.UniqueFileSize };
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        AnalysisSnapshotLease? retained = null;
        try
        {
            await model.StartAsync([input]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true); Frame(window);
            Assert.Contains("bounded memory", model.AcquisitionNotice); Assert.False(model.HasPrivateStorage);
            var tree = model.Tree!; var group = Assert.Single(tree.Groups.Values, group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
            Assert.Equal((UInt128)3, group.Totals.Known);
            model.Focus(group.Definition.Key, open: true); await Until(() => model.Selection.Scope == group.Definition.Key); Frame(window);
            var members = model.Rows.Where(row => row.NodeId.HasValue).ToArray(); Assert.Equal(2, members.Length);
            Assert.All(members, row => { Assert.True(row.SharedElsewhere); Assert.Equal((UInt128)0, row.Value); Assert.Equal((UInt128)3, row.LogicalSize); });
            var bucket = Assert.Single(model.Rows, row => row.Synthetic);
            model.Focus(bucket.Key); model.MarkCurrentMembers(); model.StageSelection();
            Assert.Equal(group.Members, model.Selection.Marks.Order()); Assert.Equal(group.Members, model.Selection.Basket.Order());
            File.WriteAllText(Path.Combine(input, "later.bin"), "not in captured membership"); await model.RebuildAsync(true);
            Assert.Equal(group.Members, model.Selection.Marks.Order()); Assert.Equal(group.Members, model.Selection.Basket.Order());
            Assert.Equal(3UL, model.Tree!.Projection.Totals.MatchingPaths);
            var reference = members.Single(row => row.Name == "alias.jpg"); model.Focus(reference.Key);
            string focused = model.FocusedPath()!;
            if (OperatingSystem.IsWindows() && focused.StartsWith("\\\\?\\", StringComparison.Ordinal)) focused = focused[4..];
            Assert.Equal(Path.Combine(input, "alias.jpg"), focused);
            await model.ShowFocusedInHierarchyAsync(); Assert.Contains(model.Rows, row => row.Name == "alias.jpg");
            Assert.DoesNotContain(model.Tree!.Snapshot.Nodes, node => node.Name == "later.bin");
            retained = model.CaptureDisplayedRevision(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion;
            Assert.Contains(retained.Snapshot.Nodes, node => node.Name == "alias.jpg"); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(original));
        }
        finally { retained?.Dispose(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); model.Dispose(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task MemoryGroupingLatestPendingChoiceWinsWhenAChartGestureEndsBeforeItsWorker()
    {
        using var styles = new ProductStyles(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int block = 0;
        using var model = new DiskSpaceViewModel(Fixture(), (rows, sort, descending, token) =>
        {
            if (Interlocked.CompareExchange(ref block, 2, 1) == 1) { entered.Set(); release.Wait(); token.ThrowIfCancellationRequested(); }
            return AnalysisTableOrder.Apply(rows, sort, descending, token);
        });
        await model.RebuildAsync(true); var beforeWindow = model.Tree; var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            await Until(() => !ReferenceEquals(beforeWindow, model.Tree)); var original = model.Tree;
            model.Focus("n:4", true); model.StageSelection(); model.Interaction(true);
            model.Grouping = AnalysisGroupingDimension.Extension; await model.RebuildAsync(true); Assert.Same(original, model.Tree);
            block = 1; model.Grouping = AnalysisGroupingDimension.FileType; model.Query = "name:picture.png";
            var pending = model.RebuildAsync(true); await Until(() => entered.IsSet);
            model.Interaction(false); Assert.Same(original, model.Tree);
            release.Set(); await pending;
            await Until(() => model.Tree?.GroupingDimension == AnalysisGroupingDimension.FileType && model.Tree.Projection.FilterText == "name:picture.png");
            Assert.Equal(new ulong[] { 5 }, model.Tree!.CaptureMembers(AnalysisTree.RootKey));
            Assert.Equal(new ulong[] { 4 }, model.Selection.Marks); Assert.Equal(new ulong[] { 4 }, model.Selection.Basket);
        }
        finally { release.Set(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task MemoryGroupingUniqueSummaryAndTooltipExcludeUnverifiedPathEvidenceWithoutUnsignedUnderflow()
    {
        using var styles = new ProductStyles(); var snapshot = Fixture();
        snapshot = snapshot with { Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { VerifiedIdentity = null }) };
        using var model = new DiskSpaceViewModel(snapshot) { Grouping = AnalysisGroupingDimension.CapturedLocation, Metric = AnalysisMetric.UniqueFileSize };
        await model.RebuildAsync(true); var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            Assert.Contains(DiskSpaceViewModel.Bytes(32768) + " known with verified identities", model.ScopeSummary);
            Assert.Contains("identity-unverified path bytes excluded from unique chart area", model.ScopeSummary);
            Assert.DoesNotContain("YiB", model.ScopeSummary);
            var chart = window.GetVisualDescendants().OfType<TreemapControl>().Single();
            var point = chart.TranslatePoint(new Point(20, 18), window)!.Value; window.MouseMove(point); Frame(window);
            var tip = Assert.IsType<RichTip>(ToolTip.GetTip(chart));
            Assert.Contains("65636 identity-unverified path bytes are excluded", tip.Text);
            Assert.DoesNotContain("Includes 65636", tip.Text);
            var group = Assert.Single(model.Tree!.ChildrenOf(AnalysisTree.RootKey));
            Assert.Equal((UInt128)32768, group.Value); Assert.Equal((UInt128)65636, group.IdentityUnverified);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }

    [AvaloniaFact]
    public async Task MemoryGroupingBothChartsTableAndNarrowInspectorPreserveTheSameMemberEvidenceAcrossThemes()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture()) { Grouping = AnalysisGroupingDimension.FileType };
        await model.RebuildAsync(true); var window = new DiskSpaceWindow { Height = 900 }; window.AddSession(model); window.Show(); Frame(window);
        try
        {
            foreach (var theme in ThemePalette.All)
            foreach (int width in new[] { 1180, 380 })
            foreach (int mode in new[] { 0, 1, 2 })
            {
                ThemeManager.Apply(theme.Name); window.Width = width;
                window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Chart view").SelectedIndex = mode;
                Frame(window); Assert.Equal((UInt128)98304, model.Tree!.ScopeValue(AnalysisTree.RootKey));
                Assert.Equal(new ulong[] { 4, 5, 6 }, model.Tree.CaptureMembers(AnalysisTree.RootKey));
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(); Assert.True(list.Bounds.Width > 100 && list.Bounds.Height > 40);
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, 900)); bitmap.Render(window);
            }
            var group = Assert.Single(model.Tree!.Groups.Values, group => group.Members.Contains(4)); model.Focus(group.Definition.Key, open: true);
            await Until(() => model.Selection.Scope == group.Definition.Key); model.Focus("n:4"); Frame(window);
            Click(window, "Inspect selection"); Frame(window);
            var reveal = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Content as string == "Show in captured folder");
            reveal.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Until(() => model.Tree!.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy && model.Selection.Scope == "n:2"); Frame(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Content as string == "Close details");
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }
}
