using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedOrderingRestoresTheCapturedDirectionAndScopeWithoutSelectionAuthority(bool descending)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Chart = "Table", Metric = AnalysisMetric.FileSize,
            Sort = AnalysisTableSort.Name, SortDescending = descending };
        await model.RebuildAsync(true); model.Focus("n:1", open: true); await Until(() => model.Selection.Scope == "n:1");
        string[] expected = descending ? ["Projects", "Photos"] : ["Photos", "Projects"];
        Assert.Equal(expected, model.Rows.Select(r => r.Name));
        model.Focus("n:4", toggleMark: true); model.StageSelection();
        await model.SaveNewAsync("ordered.fcdsa");
        using var catalog = await model.ReadSavedCatalogAsync(); var entry = Assert.Single(catalog.Entries);
        model.Sort = AnalysisTableSort.ActiveMetric; model.SortDescending = !descending;
        using var restored = await model.OpenSavedAsync(entry.Name, entry); await restored.RebuildAsync(true);
        Assert.Equal(AnalysisTableSort.Name, restored.Sort); Assert.Equal(descending, restored.SortDescending);
        Assert.Equal("n:1", restored.Selection.Scope); Assert.Equal(expected, restored.Rows.Select(r => r.Name));
        Assert.Empty(restored.Selection.Marks); Assert.Empty(restored.Selection.Basket); Assert.False(restored.Running);
        Assert.True(restored.Tree!.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        using var filenameOnly = await model.OpenSavedAsync(entry.Name); await filenameOnly.RebuildAsync(true);
        Assert.Equal(AnalysisTableSort.ActiveMetric, filenameOnly.Sort); Assert.True(filenameOnly.SortDescending);
        Assert.Equal(AnalysisTree.RootKey, filenameOnly.Selection.Scope);
    }
}
