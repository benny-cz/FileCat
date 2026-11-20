using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static string CapturedScopeStatus(Window window) => window.GetVisualDescendants().OfType<TextBlock>().Single(text => AutomationLabel(text) == "Indexed analysis status").Text!;
    private static async Task CapturedScopeNameOrder(Window window)
    {
        window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Indexed table order").SelectedIndex = 3;
        window.GetVisualDescendants().OfType<CheckBox>().Single(check => AutomationLabel(check) == "Indexed descending table order").IsChecked = false;
        await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.EndsWith("Name · Ascending", StringComparison.Ordinal) == true)); Frame(window);
    }
    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task CapturedScopeFolderNavigationFindsTheSortedEntryAfterTheLiveSourceMoves(AnalysisGroupingDimension dimension)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, dimension); var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Click(window, "Explore selected group"); await Until(() => MemberReady(window)); await CapturedScopeNameOrder(window);
            for (int page = 0; !list.Items.OfType<AnalysisDatasetGroupRow>().Any(row => row.Member.Path.Name == "file-0299.bin"); page++)
            {
                Assert.True(page < 3); var previous = list.ItemsSource; Click(window, "Next entries"); await Until(() => !ReferenceEquals(previous, list.ItemsSource)); Frame(window);
            }
            var target = list.Items.OfType<AnalysisDatasetGroupRow>().Single(row => row.Member.Path.Name == "file-0299.bin"); list.SelectedItem = target;
            await Until(() => IndexedDetails(window).Text?.Contains("file-0299.bin", StringComparison.Ordinal) == true);
            Click(window, "Toggle focused mark"); Click(window, "Add to review basket");
            string source = Path.Combine(fixture.Root, "source"), moved = Path.Combine(fixture.Root, "source-after-capture"); Directory.Move(source, moved);
            Click(window, "Show in captured folder"); await Until(() => GroupingSelector(window).SelectedIndex == 0 && list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Id == target.Member.Path.Id)); Frame(window);
            var selected = Assert.IsType<AnalysisHierarchyRow>(list.SelectedItem); Assert.Equal(target.Member.Path.Id, selected.Path.Id);
            Assert.True(IndexedSelectionCount(window, 1, 1)); Assert.Contains("No live location was opened", CapturedScopeStatus(window));
            Assert.False(Directory.Exists(source)); Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(moved, "file-0299.bin")));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(Path.Combine(moved, "folder", "match-target.bin")));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.EndsWith("Name · Ascending", StringComparison.Ordinal) == true);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.PrivatePath));
    }

    [AvaloniaFact]
    public async Task CapturedScopeHiddenBasketEntryClearsItsFilterExplicitlyWhenShowingTheRecordedFolder()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            list.SelectedItem = list.Items.OfType<AnalysisDatasetGroupRow>().Single(row => row.Member.Path.Name == "match-target.bin");
            await Until(() => IndexedDetails(window).Text?.Contains("match-target.bin", StringComparison.Ordinal) == true); Click(window, "Toggle focused mark"); Click(window, "Add to review basket");
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationLabel(text) == "Filter indexed observations");
            query.Text = "name:file-0000.bin"; Click(window, "Apply filter"); await Until(() => GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Single().Members == 1);
            await Until(() => IndexedSelectionSummary(window).Contains("1 marked items hidden by filters", StringComparison.Ordinal));
            Click(window, "Review captured basket"); await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(control => AutomationLabel(control) == "Captured basket entries")); Frame(window);
            window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationLabel(control) == "Captured basket entries").SelectedIndex = 0; Click(window, "Inspect captured basket entry");
            await Until(() => IndexedDetails(window).Text?.Contains("Captured file size: 3 bytes", StringComparison.Ordinal) == true); Click(window, "Show in captured folder");
            await Until(() => GroupingSelector(window).SelectedIndex == 0 && list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "match-target.bin")); Frame(window);
            Assert.Equal("", query.Text); Assert.Contains("Cleared the display filters", CapturedScopeStatus(window)); Assert.True(IndexedSelectionCount(window, 1, 1));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedScopeDirectoryAllocationEnterExploresItsRecordedFolderInBothStoragePaths(bool indexed)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); var window = new DiskSpaceWindow();
        if (indexed)
        {
            using var fixture = new PreparationFixture(); var saved = AnalysisSavedView.Default with { Metric = AnalysisMetric.OnDisk, Grouping = AnalysisGroupingDimension.FileType };
            var entry = await SaveIndexedInventoryFixture(fixture, saved, directoryAllocation: true);
            try
            {
                await window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, entry, TestContext.Current.CancellationToken); window.Show(); Frame(window);
                var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>(), group => group.Kind == AnalysisGroupKind.DirectoryAllocation);
                Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
                list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroupRow>()); Assert.True(list.Focus());
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Until(() => GroupingSelector(window).SelectedIndex == 0 && list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "alpha.bin")); Frame(window);
                Assert.False(Directory.Exists(Path.Combine(fixture.Root, "source-must-not-be-created")));
            }
            finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        }
        else
        {
            var snapshot = Fixture(); snapshot = snapshot with { Facts = snapshot.Facts.Add(new("directory", null, 2, "directory-self", "fixture", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, [])) };
            using var model = new DiskSpaceViewModel(snapshot) { Metric = AnalysisMetric.OnDisk, Grouping = AnalysisGroupingDimension.FileType };
            await model.RebuildAsync(true); var directory = Assert.Single(model.Tree!.Groups.Values, group => group.Kind == AnalysisGroupKind.DirectoryAllocation);
            model.Focus(directory.Definition.Key, open: true); await Until(() => model.Selection.Scope == directory.Definition.Key); model.Focus("n:2", toggleMark: true); model.StageSelection();
            model.Focus("n:2", open: true); await Until(() => model.Tree?.GroupingDimension == AnalysisGroupingDimension.FolderHierarchy && model.Selection.Scope == "n:2");
            Assert.Contains(2UL, model.Selection.Marks); Assert.Contains(2UL, model.Selection.Basket); Assert.Equal("n:2", model.Selection.Focus);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedScopeKeyboardRangesCrossPagesAndShrinkWithoutLosingOutsideMarksOrBasket(bool grouped)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow { Height = 960 }; await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); var root = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>()); list.SelectedItem = root;
            await Until(() => IndexedDetails(window).Text?.Contains("captured path ID: " + root.Path.Id, StringComparison.Ordinal) == true); Click(window, "Toggle focused mark");
            if (grouped)
            {
                await SelectGrouping(window, AnalysisGroupingDimension.Extension); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>()); Click(window, "Explore selected group"); await Until(() => MemberReady(window));
            }
            else { Click(window, "Explore selected folder"); await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Count() > 1); }
            await CapturedScopeNameOrder(window); list.SelectedIndex = 0; Assert.True(list.Focus());
            int children = grouped ? 303 : 302;
            window.KeyPress(Key.End, RawInputModifiers.Shift, PhysicalKey.End, null); window.KeyRelease(Key.End, RawInputModifiers.Shift, PhysicalKey.End, null);
            await Until(() => IndexedSelectionCount(window, children + 1, 0)); Click(window, "Add to review basket");
            window.KeyPress(Key.Home, RawInputModifiers.Shift, PhysicalKey.Home, null); window.KeyRelease(Key.Home, RawInputModifiers.Shift, PhysicalKey.Home, null);
            await Until(() => IndexedSelectionCount(window, 2, children + 1)); Frame(window);
            Assert.Equal(0, list.SelectedIndex); Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
            await Until(() => !IndexedSelectionSummary(window).Contains("Counting", StringComparison.Ordinal)); Assert.DoesNotContain("unavailable", IndexedSelectionSummary(window));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task CapturedScopeGroupOverviewRangeCannotImplicitlyExpandAccountingRows()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync(sharedAlias: true);
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.FileType); var list = IndexedList(window); list.SelectedIndex = 0; Assert.True(list.Focus());
            window.KeyPress(Key.End, RawInputModifiers.Shift, PhysicalKey.End, null); window.KeyRelease(Key.End, RawInputModifiers.Shift, PhysicalKey.End, null);
            await Until(() => CapturedScopeStatus(window).Contains("Accounting groups not expanded", StringComparison.Ordinal)); Assert.True(IndexedSelectionCount(window, 0, 0));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task CapturedScopeHiddenMarksCanBeReviewedAndUnmarkedWithoutChangingTheBasket()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync(sharedAlias: true);
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>()); Click(window, "Toggle focused mark");
            await SelectGrouping(window, AnalysisGroupingDimension.FileType); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>(), group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
            Click(window, "Mark current members"); await Until(() => IndexedSelectionCount(window, 3, 0)); Click(window, "Add to review basket");
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationLabel(text) == "Filter indexed observations"); query.Text = "name:match-target.bin"; Click(window, "Apply filter");
            await Until(() => GroupReady(window)); await Until(() => IndexedSelectionSummary(window).Contains("1 marked items hidden by filters", StringComparison.Ordinal));
            Assert.Contains("1 elsewhere", IndexedSelectionSummary(window)); Click(window, "Review marked entries");
            await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(control => AutomationLabel(control) == "Captured marked entries")); Frame(window);
            var marked = window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationLabel(control) == "Captured marked entries"); Assert.Equal(3, marked.ItemCount);
            marked.SelectedItem = marked.Items.Cast<object>().Single(row => row.ToString()!.Contains("alias.jpg", StringComparison.Ordinal)); Click(window, "Unmark selected entry");
            Assert.True(IndexedSelectionCount(window, 2, 3)); Click(window, "Keep files / Close review");
            await Until(() => IndexedSelectionSummary(window).Contains("0 marked items hidden by filters", StringComparison.Ordinal));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task CapturedScopeRapidMarkChangesPublishOnlyTheLatestCountsAndCloseAllWork()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken); await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
            for (int count = 0; count < 101; count++) Click(window, "Toggle focused mark");
            await Until(() => IndexedSelectionSummary(window).Contains("1 in this scope", StringComparison.Ordinal)); Assert.True(IndexedSelectionCount(window, 1, 0)); Assert.DoesNotContain("unavailable", IndexedSelectionSummary(window));
            window.ForceClose(); await window.WaitForIndexedCloseAsync(); Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.PrivatePath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task CapturedScopePendingFolderNavigationRetainsANewerTextDraftButShowsItsActualLocationChoice()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow { Height = 960 }; await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        bool pressed = false; Point point = default;
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>());
            Click(window, "Explore selected group"); await Until(() => MemberReady(window)); Frame(window);
            var target = list.Items.OfType<AnalysisDatasetGroupRow>().Single(row => row.Member.Path.Name == "match-target.bin"); list.SelectedItem = target;
            await Until(() => IndexedDetails(window).Text?.Contains("match-target.bin", StringComparison.Ordinal) == true); list.ScrollIntoView(target); Frame(window);
            var container = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(list.SelectedIndex)); container.BringIntoView(); Frame(window);
            point = container.TranslatePoint(new Point(Math.Min(80, container.Bounds.Width / 2), container.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); pressed = true;
            var roots = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationLabel(combo) == "Indexed captured location"); roots.SelectedIndex = 1;
            await Until(() => CapturedScopeStatus(window) == "Query ready. Finish the current interaction to display it.");
            Click(window, "Show in captured folder");
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationLabel(text) == "Filter indexed observations"); query.Text = "name:file-0000.bin";
            window.MouseUp(point, MouseButton.Left); pressed = false;
            await Until(() => GroupingSelector(window).SelectedIndex == 0 && list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Id == target.Member.Path.Id)); Frame(window);
            Assert.Equal(0, roots.SelectedIndex); Assert.Equal("name:file-0000.bin", query.Text);
            Assert.Contains("newer filter draft is retained", CapturedScopeStatus(window)); Assert.Equal(target.Member.Path.Id, Assert.IsType<AnalysisHierarchyRow>(list.SelectedItem).Path.Id);
        }
        finally { if (pressed) window.MouseUp(point, MouseButton.Left); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
