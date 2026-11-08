using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static string IndexedSelectionSummary(Window window) => window.GetVisualDescendants().OfType<TextBlock>().Single(text => AutomationProperties.GetName(text) == "Indexed captured selection").Text!;
    private static bool IndexedSelectionCount(Window window, int marked, int basket) => IndexedSelectionSummary(window).StartsWith($"{marked:N0} marked · {basket:N0} in review basket", StringComparison.Ordinal);

    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task IndexedSelectionCapturesAllGroupPagesAndKeepsMarksAndBasketAcrossQueryAndGroupingChanges(AnalysisGroupingDimension dimension)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, dimension); var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Click(window, "Mark current members"); await Until(() => IndexedSelectionCount(window, 303, 0));
            Click(window, "Add to review basket"); Assert.True(IndexedSelectionCount(window, 303, 303));
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(fixture.PayloadPath)!, "future-only.bin"), [4, 5]);
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Filter indexed observations");
            query.Text = "name:match-target.bin"; Click(window, "Apply filter");
            await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Single().Members == 1); Frame(window);
            Assert.True(IndexedSelectionCount(window, 303, 303));
            Click(window, "Clear marks"); Assert.True(IndexedSelectionCount(window, 0, 303));
            Click(window, "Mark current members"); await Until(() => IndexedSelectionCount(window, 1, 303));
            GroupingSelector(window).SelectedIndex = 0; await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Any()); Frame(window);
            Assert.True(IndexedSelectionCount(window, 1, 303)); Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
            Click(window, "Clear review basket"); Assert.True(IndexedSelectionCount(window, 1, 0));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.PrivatePath));
    }

    [AvaloniaFact]
    public async Task IndexedSelectionSharedGroupCapturesOnlyTheMatchingAliasAndStagesNoFutureSourceEntries()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync(sharedAlias: true);
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken);
        await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.FileType); var list = IndexedList(window);
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Filter indexed observations");
            query.Text = "name:match-target.bin"; Click(window, "Apply filter");
            await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Any(group => group.Kind == AnalysisGroupKind.SharedAcrossGroups));
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>(), group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
            Click(window, "Mark current members"); await Until(() => IndexedSelectionCount(window, 1, 0));
            Click(window, "Add to review basket"); Assert.True(IndexedSelectionCount(window, 1, 1));
            Click(window, "Review captured basket"); await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(control => AutomationProperties.GetName(control) == "Captured basket entries")); Frame(window);
            var basket = window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationProperties.GetName(control) == "Captured basket entries");
            string row = Assert.Single(basket.Items)?.ToString() ?? ""; Assert.Contains("match-target.bin", row); Assert.DoesNotContain("alias.jpg", row);
            basket.SelectedIndex = 0; Click(window, "Inspect captured basket entry");
            await Until(() => IndexedDetails(window).Text?.Contains("Captured file size: 3 bytes", StringComparison.Ordinal) == true);
            Assert.DoesNotContain("Active metric:", IndexedDetails(window).Text); Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.PrivatePath));
    }

    [AvaloniaFact]
    public async Task IndexedSelectionBasketPagesItsCapturedSnapshotAndAllowsRemovalWithoutChangingMarksOrSource()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Click(window, "Mark current members"); await Until(() => IndexedSelectionCount(window, 303, 0)); Click(window, "Add to review basket");
            Click(window, "Review captured basket"); await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(control => AutomationProperties.GetName(control) == "Captured basket entries")); Frame(window);
            var basket = window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationProperties.GetName(control) == "Captured basket entries");
            Assert.Equal(128, basket.ItemCount); var first = basket.Items.Cast<object>().Select(row => row.ToString()).ToArray();
            Click(window, "Next basket entries"); await Until(() => basket.ItemCount == 128 && !first.Contains(basket.Items[0]?.ToString())); Frame(window);
            Assert.DoesNotContain(basket.Items.Cast<object>(), row => first.Contains(row.ToString()));
            Click(window, "Next basket entries"); await Until(() => basket.ItemCount == 47); Frame(window);
            basket.SelectedIndex = 0; Click(window, "Remove selected from basket"); Assert.True(IndexedSelectionCount(window, 303, 302));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Current basket: 302.", StringComparison.Ordinal) == true);
            Assert.Contains("removed from basket", basket.SelectedItem?.ToString());
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Remove selected from basket")).IsEnabled);
            Click(window, "Previous basket entries"); await Until(() => basket.ItemCount == 128); Frame(window);
            Click(window, "Keep files / Close review"); Assert.True(IndexedSelectionCount(window, 303, 302));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedSelectionCheckboxAndSpaceToggleCapturedRealRowsWithoutExpandingAccountingGroups()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Assert.True(list.Focus()); Assert.True(list.IsKeyboardFocusWithin);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.True(IndexedSelectionCount(window, 0, 0));
            Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            var row = list.Items.OfType<AnalysisDatasetGroupRow>().Single(row => row.Member.Path.Name == "match-target.bin"); list.SelectedItem = row;
            await Until(() => IndexedDetails(window).Text?.Contains("match-target.bin", StringComparison.Ordinal) == true); Frame(window);
            var check = window.GetVisualDescendants().OfType<CheckBox>().Single(check => check.Tag is ulong id && id == row.Member.Path.Id);
            check.IsChecked = true; Assert.True(IndexedSelectionCount(window, 1, 0));
            Assert.True(list.Focus()); Assert.True(list.IsKeyboardFocusWithin);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.True(IndexedSelectionCount(window, 0, 0)); Assert.False(check.IsChecked);
            Click(window, "Add to review basket"); Assert.True(IndexedSelectionCount(window, 0, 1));
            Click(window, "Toggle focused mark"); Assert.True(IndexedSelectionCount(window, 1, 1));
            Click(window, "Clear marks"); Assert.True(IndexedSelectionCount(window, 0, 1));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedSelectionTablePressKeepsItsCapturedMarkTargetUntilThePendingQueryCanPublish()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        bool pressed = false; Point point = default;
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window);
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>()); Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            var row = list.Items.OfType<AnalysisDatasetGroupRow>().Single(row => row.Member.Path.Name == "match-target.bin"); list.SelectedItem = row;
            await Until(() => IndexedDetails(window).Text?.Contains("match-target.bin", StringComparison.Ordinal) == true); Frame(window);
            var check = window.GetVisualDescendants().OfType<CheckBox>().Single(check => check.Tag is ulong id && id == row.Member.Path.Id);
            check.BringIntoView(); Frame(window); point = check.TranslatePoint(new Point(check.Bounds.Width / 2, check.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(point) as Visual;
            Assert.True(hit == check || hit?.GetVisualAncestors().Contains(check) == true,
                $"Pointer {point} missed checkbox {check.Bounds}; hit {hit?.GetType().Name}.");
            var before = list.ItemsSource; window.MouseMove(point); Frame(window); window.MouseDown(point, MouseButton.Left); pressed = true;
            Assert.True(check.IsPressed, "The rendered checkbox must own the press before changing the filter.");
            window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Filter indexed observations").Text = "name:file-0000.bin";
            Click(window, "Apply filter");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Query ready. Finish the current interaction to display it."));
            Assert.Same(before, list.ItemsSource);
            var afterPoint = check.TranslatePoint(new Point(check.Bounds.Width / 2, check.Bounds.Height / 2), window);
            bool pressedBeforeRelease = check.IsPressed, overBeforeRelease = check.IsPointerOver;
            window.MouseUp(point, MouseButton.Left); pressed = false;
            await Until(() => GroupReady(window) && !ReferenceEquals(before, list.ItemsSource)); Frame(window);
            Assert.True(IndexedSelectionCount(window, 1, 0), $"Checkbox before={point}, after={afterPoint}, pressed={pressedBeforeRelease}, over={overBeforeRelease}; {IndexedSelectionSummary(window)}"); Click(window, "Add to review basket");
            Click(window, "Review captured basket"); await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(control => AutomationProperties.GetName(control) == "Captured basket entries")); Frame(window);
            var basket = window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationProperties.GetName(control) == "Captured basket entries");
            Assert.Contains("match-target.bin", Assert.Single(basket.Items)?.ToString()); Click(window, "Keep files / Close review");
        }
        finally { if (pressed) window.MouseUp(point, MouseButton.Left); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedSelectionClosingWhileBasketPageLoadsWaitsForItsActualWorkAndReleasesScratch()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Click(window, "Mark current members"); await Until(() => IndexedSelectionCount(window, 303, 0)); Click(window, "Add to review basket");
            Click(window, "Review captured basket"); await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(control => AutomationProperties.GetName(control) == "Captured basket entries")); Frame(window);
            Click(window, "Next basket entries"); window.ForceClose(); await window.WaitForIndexedCloseAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.PrivatePath)); Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedSelectionOfflineMarksAndBasketRequirePreservingCloseReviewUntilCleared()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        var view = AnalysisSavedView.Default with { Metric = AnalysisMetric.FileSize, Scope = "n:400" };
        var entry = await SaveIndexedInventoryFixture(fixture, view); var window = new DiskSpaceWindow();
        try
        {
            await window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, entry, TestContext.Current.CancellationToken); window.Show(); Frame(window);
            var list = IndexedList(window); list.SelectedItem = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "alpha.bin");
            await Until(() => IndexedDetails(window).Text?.Contains("alpha.bin", StringComparison.Ordinal) == true);
            Click(window, "Toggle focused mark"); Assert.True(IndexedSelectionCount(window, 1, 0));
            Click(window, "Close analysis"); await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Keep open"))); Frame(window);
            Assert.Equal("Keep open", Assert.IsType<Button>(window.FocusManager!.GetFocusedElement()).Content); Click(window, "Keep open"); Assert.Equal(1, window.SessionCount);
            Click(window, "Add to review basket"); Click(window, "Clear marks"); Assert.True(IndexedSelectionCount(window, 0, 1));
            Click(window, "Close analysis"); await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Keep open"))); Click(window, "Keep open"); Assert.Equal(1, window.SessionCount);
            Click(window, "Clear review basket"); Assert.True(IndexedSelectionCount(window, 0, 0)); Click(window, "Close analysis"); await Until(() => window.SessionCount == 0);
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "source-must-not-be-created")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(fixture.Services.Paths.AnalysisScratchDirectory));
    }
}
