using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task SavedGroupingMemoryRestoresScopeAndControlsAtomicallyWithoutMarksOrBasket(AnalysisGroupingDimension dimension)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture())
            { Grouping = dimension, Metric = AnalysisMetric.FileSize, Chart = "Sunburst", ColorDimension = AnalysisColorDimension.HierarchyDepth,
                Sort = AnalysisTableSort.Name, SortDescending = false };
        await model.RebuildAsync(true); model.RootFilter = model.Tree!.Snapshot.Roots[0].Id; await model.RebuildAsync(true);
        var group = Assert.Single(model.Tree!.Groups.Values, group => group.Kind == AnalysisGroupKind.Entries && group.Members.Contains(4));
        model.Focus(group.Definition.Key, open: true); await Until(() => model.Selection.Scope == group.Definition.Key);
        model.Focus("n:4", true); model.StageSelection();
        ulong[] members = model.Tree.CaptureMembers(group.Definition.Key).Order().ToArray();
        await model.SaveNewAsync("grouped.fcdsa"); using var catalog = await model.ReadSavedCatalogAsync(); var entry = Assert.Single(catalog.Entries);
        Assert.Equal(dimension, entry.View.Grouping); Assert.Equal(AnalysisGroupingRules.RuleVersion, entry.View.GroupRuleVersion); Assert.Equal("1", entry.View.Root);
        Assert.Equal(dimension == AnalysisGroupingDimension.CapturedLocation && model.Tree.Snapshot.Roots[0].Id != "1", entry.View.Scope != group.Definition.Key);
        using var restored = await model.OpenSavedAsync(entry.Name, entry);
        Assert.NotNull(restored.Tree); Assert.Equal(dimension, restored.Tree.GroupingDimension);
        Assert.Equal(entry.View.Scope, restored.Selection.Scope); Assert.Equal(members, restored.Tree.CaptureMembers(restored.Selection.Scope).Order());
        Assert.Equal(AnalysisTableSort.Name, restored.Sort); Assert.False(restored.SortDescending); Assert.Equal("Sunburst", restored.Chart);
        Assert.Equal(AnalysisColorDimension.HierarchyDepth, restored.ColorDimension); Assert.Equal("1", restored.RootFilter);
        Assert.Empty(restored.Selection.Marks); Assert.Empty(restored.Selection.Basket); Assert.False(restored.Running);
        Assert.Equal(AnalysisOrigin.ImportedInventory, restored.Tree.Snapshot.Origin);
        var window = new DiskSpaceWindow(); window.AddSession(restored); window.Show(); Frame(window);
        try { Assert.Equal((int)dimension, MemoryGroupingSelector(window).SelectedIndex); Assert.Equal(entry.View.Scope, restored.Selection.Scope); }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await restored.CloseCompletion; }
        using var byName = await model.OpenSavedAsync(entry.Name);
        Assert.Equal(AnalysisGroupingDimension.FolderHierarchy, byName.Tree!.GroupingDimension); Assert.Equal(AnalysisTree.RootKey, byName.Selection.Scope);
    }

    private static string SavedIndexedGroupScope(AnalysisGroupingDimension dimension)
    {
        var snapshot = Fixture(); var node = new AnalysisNode(100, "1", 0, "alpha.bin", AnalysisKind.File);
        return AnalysisGroupingRules.Classify(dimension, node, null, snapshot.Identity, [snapshot.Roots[0] with { Id = "1" }]).Key;
    }
    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FileType, false)]
    [InlineData(AnalysisGroupingDimension.Extension, true)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge, false)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation, true)]
    public async Task SavedGroupingIndexedPreparesItsGroupBeforeTheWindowIsPublished(AnalysisGroupingDimension dimension, bool descending)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        var saved = new AnalysisSavedView(AnalysisMetric.FileSize, "Sunburst", "name:.bin", "1", SavedIndexedGroupScope(dimension), true, false, AnalysisTableSort.Name, descending)
            { Grouping = dimension, ColorDimension = AnalysisColorDimension.ModifiedAge };
        var entry = await SaveIndexedInventoryFixture(fixture, saved); string path = Path.Combine(fixture.Services.Paths.AnalysisSnapshotsDirectory, entry.Name);
        byte[] before = File.ReadAllBytes(path); var window = new DiskSpaceWindow();
        try
        {
            var explorer = await window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, entry, TestContext.Current.CancellationToken);
            window.Show(); Frame(window); Assert.True(GroupReady(window));
            Assert.Equal((int)dimension, GroupingSelector(window).SelectedIndex);
            var rows = IndexedList(window).Items.OfType<AnalysisDatasetGroupRow>().ToArray(); Assert.Equal(2, rows.Length);
            Assert.Equal(descending ? new[] { "beta.bin", "alpha.bin" } : ["alpha.bin", "beta.bin"], rows.Select(row => row.Member.Path.Name));
            Assert.Equal(AnalysisOrigin.ImportedInventory, explorer.Descriptor.Origin); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "source-must-not-be-created")));
            var combos = window.GetVisualDescendants().OfType<ComboBox>().ToArray();
            Assert.Equal("Sunburst", combos.Single(combo => AutomationProperties.GetName(combo) == "Indexed chart mode").SelectedItem);
            Assert.Equal((int)AnalysisColorDimension.ModifiedAge, combos.Single(combo => AutomationProperties.GetName(combo) == "Chart color dimension").SelectedIndex);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(fixture.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedGroupingMissingScopeExplainsItsFallbackForBothStoragePaths(bool indexed)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        string missing = "group:1:" + new string('0', 64); var view = AnalysisSavedView.Default with { Grouping = AnalysisGroupingDimension.FileType, Scope = missing };
        if (indexed)
        {
            var entry = await SaveIndexedInventoryFixture(fixture, view); var window = new DiskSpaceWindow();
            try
            {
                await window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, entry, TestContext.Current.CancellationToken); window.Show(); Frame(window);
                Assert.True(GroupReady(window)); Assert.NotEmpty(IndexedList(window).Items.OfType<AnalysisDatasetGroup>());
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("saved group is absent", StringComparison.Ordinal) == true);
            }
            finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        }
        else
        {
            using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true); await model.SaveNewAsync("absent.fcdsa");
            using var catalog = await model.ReadSavedCatalogAsync(); var entry = Assert.Single(catalog.Entries) with { View = view };
            using var restored = await model.OpenSavedAsync(entry.Name, entry);
            Assert.Equal(AnalysisGroupingDimension.FileType, restored.Tree!.GroupingDimension); Assert.Equal(AnalysisTree.RootKey, restored.Selection.Scope);
            Assert.Contains("saved group is absent", restored.Status);
        }
    }

    [AvaloniaFact]
    public async Task SavedGroupingOtherScopePreservesItsCapturedLocationAndCurrentSubset()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Grouping = AnalysisGroupingDimension.CapturedLocation, Metric = AnalysisMetric.FileSize };
        await model.RebuildAsync(true); var group = Assert.Single(model.Tree!.Groups.Values); string other = AnalysisTree.OtherKey(group.Definition.Key, 1);
        model.Focus(other, open: true); await Until(() => model.Selection.Scope == other);
        var members = model.Tree.CaptureMembers(other).Order().ToArray(); UInt128 area = model.Tree.ScopeValue(other);
        await model.SaveNewAsync("other.fcdsa"); using var catalog = await model.ReadSavedCatalogAsync(); var entry = Assert.Single(catalog.Entries);
        Assert.True(AnalysisSavedCatalog.IsGroupScope(AnalysisSavedCatalog.SplitAggregationScope(entry.View.Scope).Scope));
        Assert.Equal(1, AnalysisSavedCatalog.SplitAggregationScope(entry.View.Scope).Skipped);
        using var restored = await model.OpenSavedAsync(entry.Name, entry);
        Assert.Equal(entry.View.Scope, restored.Selection.Scope); Assert.Equal(members, restored.Tree!.CaptureMembers(restored.Selection.Scope).Order());
        Assert.Equal(area, restored.Tree.ScopeValue(restored.Selection.Scope));
    }

    [AvaloniaFact]
    public async Task SavedGroupingIndexedOtherScopeRestoresABoundedTablePageAndQualifiesItsChartScope()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        var view = new AnalysisSavedView(AnalysisMetric.FileSize, "Table", "name:.bin", "1", AnalysisTree.OtherKey(SavedIndexedGroupScope(AnalysisGroupingDimension.Extension), 1), false, false, AnalysisTableSort.Name, false)
            { Grouping = AnalysisGroupingDimension.Extension };
        var entry = await SaveIndexedInventoryFixture(fixture, view); var window = new DiskSpaceWindow();
        try
        {
            await window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, entry, TestContext.Current.CancellationToken); window.Show(); Frame(window);
            var row = Assert.Single(IndexedList(window).Items.OfType<AnalysisDatasetGroupRow>()); Assert.Equal("beta.bin", row.Member.Path.Name);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("table page within its captured group", StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Entries 2", StringComparison.Ordinal) == true);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task SavedGroupingIndexedMissingGroupPageReturnsToFirstPage(bool grouped, int offset)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture();
        var dimension = grouped ? AnalysisGroupingDimension.Extension : AnalysisGroupingDimension.FolderHierarchy;
        string scope = grouped ? SavedIndexedGroupScope(dimension) : "n:400";
        var view = new AnalysisSavedView(AnalysisMetric.FileSize, "Table", "name:.bin", "1", AnalysisTree.OtherKey(scope, offset), false, false, AnalysisTableSort.Name, false)
            { Grouping = dimension };
        var entry = await SaveIndexedInventoryFixture(fixture, view); var window = new DiskSpaceWindow();
        try
        {
            var explorer = await window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, entry, TestContext.Current.CancellationToken); window.Show(); Frame(window);
            string[] names = grouped ? IndexedList(window).Items.OfType<AnalysisDatasetGroupRow>().Select(row => row.Member.Path.Name).ToArray()
                : explorer.Page!.Children.Rows.Select(row => row.Path.Name).ToArray();
            Assert.Equal(new[] { "alpha.bin", "beta.bin" }, names);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("saved table page is absent. Showing the first page", StringComparison.Ordinal) == true);
            if (!grouped) { Assert.Equal(400UL, explorer.Page!.ParentId); Assert.Equal(0UL, explorer.Page.Children.Offset); }
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "source-must-not-be-created")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(fixture.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaFact]
    public async Task SavedGroupingCapturesThePublishedMetricFilterAndRuleWhileANewerRequestIsPending()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture())
            { Grouping = AnalysisGroupingDimension.FileType, Metric = AnalysisMetric.FileSize, Sort = AnalysisTableSort.Name, SortDescending = false };
        await model.RebuildAsync(true); var displayed = model.Tree; model.Interaction(true);
        try
        {
            model.Grouping = AnalysisGroupingDimension.Extension; model.Metric = AnalysisMetric.Files; model.Query = "name:picture.png";
            model.Sort = AnalysisTableSort.ActiveMetric; model.SortDescending = true; model.CaseSensitive = true;
            await model.RebuildAsync(true); Assert.Same(displayed, model.Tree);
            await model.SaveNewAsync("displayed.fcdsa"); using var catalog = await model.ReadSavedCatalogAsync(); var entry = Assert.Single(catalog.Entries);
            Assert.Equal(AnalysisGroupingDimension.FileType, entry.View.Grouping); Assert.Equal(AnalysisMetric.FileSize, entry.View.Metric);
            Assert.Equal("", entry.View.Query); Assert.False(entry.View.CaseSensitive); Assert.Equal(AnalysisTableSort.Name, entry.View.Sort); Assert.False(entry.View.SortDescending);
            using var restored = await model.OpenSavedAsync(entry.Name, entry);
            Assert.Equal(AnalysisGroupingDimension.FileType, restored.Tree!.GroupingDimension); Assert.Equal(3UL, restored.Tree.Projection.Totals.MatchingPaths);
        }
        finally { model.Interaction(false); }
        await Until(() => model.Tree?.GroupingDimension == AnalysisGroupingDimension.Extension);
        Assert.Equal(1UL, model.Tree!.Projection.Totals.MatchingPaths);
    }
}
