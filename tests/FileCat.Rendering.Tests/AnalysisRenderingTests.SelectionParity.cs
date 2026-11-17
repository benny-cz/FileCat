using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static string MemorySelectionText(Window window) => window.GetVisualDescendants().OfType<TextBlock>()
        .Single(text => AutomationLabel(text) == "Captured selection summary").Text!;

    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FolderHierarchy)]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task SelectionParityMemoryCountsAndCaptureUseDisplayedScopeAndFilterWithoutFocusNarrowing(AnalysisGroupingDimension grouping)
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture()) { Grouping = grouping, Metric = AnalysisMetric.FileSize };
        await model.RebuildAsync(true); var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            model.Focus("n:4"); Click(window, "Mark matching files in this scope");
            await Until(() => !model.IsCapturingMatches && model.Selection.Marks.Count == 3);
            Assert.Equal(new ulong[] { 4, 5, 6 }, model.Selection.Marks.Order());
            model.Selection.StageCaptured([4, 5, 6]); model.Selection.ToggleMark(999); model.Query = "name:source.cs"; await model.RebuildAsync(true); Frame(window);
            await Until(() => model.MarkVisibility is { HiddenByFilter: 2, Missing: 1 });
            Assert.Contains("1 in this scope", MemorySelectionText(window)); Assert.Contains("2 marked items hidden by filters", MemorySelectionText(window));
            var summary = window.GetVisualDescendants().OfType<TextBlock>().Single(text => AutomationLabel(text) == "Captured selection summary");
            int announcements = 0; summary.PropertyChanged += (_, args) => { if (args.Property == TextBlock.TextProperty) announcements++; };
            for (int count = 0; count < 5; count++) model.SetStatus("Unrelated status " + count);
            Assert.Equal(0, announcements);
            Click(window, "Clear marks"); Click(window, "Mark matching files in this scope"); await Until(() => !model.IsCapturingMatches && model.Selection.Marks.Count == 1);
            Assert.Equal(new ulong[] { 4 }, model.Selection.Marks.Order()); Assert.Equal(3, model.Selection.Basket.Count);
            Assert.Contains("future entries are not selected", model.Status);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }
    [AvaloniaFact]
    public async Task SelectionParityMemoryFolderScopeIncludesDescendantMatchesAndCountsMarksElsewhere()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture()); await model.RebuildAsync(true);
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            model.Selection.MarkCaptured([4, 5, 999]); model.Focus("n:2", open: true); await Until(() => model.Selection.Scope == "n:2");
            await Until(() => model.MarkVisibility is { InScope: 1, Elsewhere: 1, Missing: 1 });
            model.Focus("n:4"); Click(window, "Mark matching files in this scope");
            await Until(() => !model.IsCapturingMatches && model.Selection.Marks.Count == 4);
            Assert.Equal(new ulong[] { 4, 5, 6, 999 }, model.Selection.Marks.Order());
            for (int count = 0; count < 101; count++) model.Focus("n:4", toggleMark: true);
            await Until(() => model.MarkVisibility is { Total: 3, InScope: 1, Elsewhere: 1, Missing: 1 });
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }
    [AvaloniaFact]
    public async Task SelectionParityMemoryReviewPagesCapturedMarksAndUnmarkKeepsBasketIndependent()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture()) { Query = "name:source.cs" };
        model.Selection.MarkCaptured(Enumerable.Range(1, 270).Select(id => (ulong)id)); model.Selection.StageCaptured([5]); await model.RebuildAsync(true);
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            await Until(() => model.MarkVisibility is not null); Click(window, "Review marked entries");
            await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(list => AutomationLabel(list) == "Captured marked entries")); Frame(window);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationLabel(list) == "Captured marked entries"); Assert.Equal(128, list.ItemCount);
            list.SelectedItem = list.Items.Cast<object>().Single(row => row.ToString()!.Contains("picture.png", StringComparison.Ordinal)); Click(window, "Unmark selected entry");
            Assert.Equal(269, model.Selection.Marks.Count); Assert.Contains(5UL, model.Selection.Basket); Assert.True(list.SelectedIndex >= 0);
            Click(window, "Next marked entries"); Assert.Equal(128, list.ItemCount); Click(window, "Next marked entries"); Assert.Equal(14, list.ItemCount);
            list.SelectedIndex = 0;
            Assert.Contains("absent from the captured revision", window.GetVisualDescendants().OfType<TextBlock>().Single(text => AutomationLabel(text) == "Captured marked entry details").Text);
            Click(window, "Unmark selected entry"); Assert.Equal(268, model.Selection.Marks.Count); Assert.Contains(5UL, model.Selection.Basket);
            Click(window, "Keep files / Close review"); await Until(() => model.MarkVisibility is { Total: 268 });
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }
    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FolderHierarchy)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    public async Task SelectionParityIndexedMatchesIncludeAllCapturedPagesWithNoFutureSourceEntries(AnalysisGroupingDimension grouping)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            if (grouping != AnalysisGroupingDimension.FolderHierarchy) await SelectGrouping(window, grouping);
            var list = IndexedList(window); list.SelectedIndex = 0;
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(fixture.PayloadPath)!, "future-matching.bin"), [1, 2, 3]);
            Click(window, "Mark matching files in this scope"); await Until(() => IndexedSelectionCount(window, 303, 0));
            Click(window, "Add to review basket"); Click(window, "Clear marks"); Assert.True(IndexedSelectionCount(window, 0, 303));
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationLabel(text) == "Filter indexed observations");
            query.Text = "name:match-target.bin"; Click(window, "Apply filter");
            await Until(() => grouping == AnalysisGroupingDimension.FolderHierarchy ? list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Totals.MatchingPaths == 1) : GroupReady(window) && list.Items.OfType<AnalysisDatasetGroup>().Single().Members == 1);
            Click(window, "Mark matching files in this scope"); await Until(() => IndexedSelectionCount(window, 1, 303));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.PrivatePath));
    }
    [AvaloniaFact]
    public async Task SelectionParityMemoryNonmatchingAliasIsExplainedAsContextWithoutExtraMatches()
    {
        using var styles = new ProductStyles(); var snapshot = Fixture();
        snapshot = snapshot with { Nodes = snapshot.Nodes.SetItem(4, snapshot.Nodes[4] with { ObjectId = "a" }) };
        using var model = new DiskSpaceViewModel(snapshot) { Metric = AnalysisMetric.OnDisk, Query = "name:source.cs" };
        model.Selection.MarkCaptured([5, 6]); await model.RebuildAsync(true); var window = new DiskSpaceWindow(); window.AddSession(model); window.Show(); Frame(window);
        try
        {
            await Until(() => model.MarkVisibility is { InScope: 1, HiddenByFilter: 1 });
            model.Focus("n:5"); Frame(window);
            var item = model.Tree!.Items["n:5"]; Assert.True(item.IsFilterContext); Assert.Equal("Alias context", DiskSpaceViewModel.StatusText(item));
            Assert.Equal("Not a match", DiskSpaceViewModel.LogicalText(item));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Nonmatching hard-link alias context", StringComparison.Ordinal) == true);
            Click(window, "Clear marks"); Click(window, "Mark matching files in this scope"); await Until(() => !model.IsCapturingMatches && model.Selection.Marks.Count == 1);
            Assert.Equal(new ulong[] { 4 }, model.Selection.Marks.Order());
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }
    }
    [AvaloniaFact]
    public async Task SelectionParityIndexedNonmatchingAliasHasTheSameContextExplanationAndIsExcludedFromMatches()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync(sharedAlias: true);
        var window = new DiskSpaceWindow(); var explorer = await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.UniqueFileSize,
            new("name:match-target.bin"), TestContext.Current.CancellationToken); await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>()); Click(window, "Explore selected folder");
            await Until(() => IndexedReady(window, explorer, 1)); Frame(window);
            var alias = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "alias.jpg"); Assert.False(alias.IsMatch); list.SelectedItem = alias;
            await Until(() => IndexedDetails(window).Text?.Contains("Nonmatching hard-link alias context", StringComparison.Ordinal) == true);
            Assert.Contains("File size: Not a match", IndexedDetails(window).Text);
            Click(window, "Mark matching files in this scope"); await Until(() => IndexedSelectionCount(window, 1, 0));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
