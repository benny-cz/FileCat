using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task StartingAgainCannotRetagExistingSelectionsOrReplaceTheirAcquisition()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); int prepared = 0;
        using var model = new DiskSpaceViewModel(fixture.Services, path => { prepared++; return new PreparedSource(path); });
        await model.StartAsync([fixture.Root]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true);
        var identity = model.Tree!.Snapshot.Identity; model.Focus("n:1", toggleMark: true); model.StageSelection();
        var marks = model.Selection.Marks; var basket = model.Selection.Basket;
        using var captured = model.CaptureDisplayedRevision();
        await model.StartAsync([fixture.Root + "-different"]); await Until(() => !model.Running); await model.RebuildAsync(true);
        Assert.Equal(identity.SessionId, model.Tree!.Snapshot.Identity.SessionId); Assert.Equal(identity.Epoch, model.Tree.Snapshot.Identity.Epoch);
        Assert.Equal(fixture.Root, Assert.Single(model.Tree.Snapshot.Roots).CanonicalLocator); Assert.Equal(1, prepared);
        Assert.Same(marks, model.Selection.Marks); Assert.Same(basket, model.Selection.Basket); Assert.Equal("n:1", model.Selection.Focus);
        Assert.Equal(identity.SessionId, captured.Snapshot.Identity.SessionId); Assert.Contains("New analysis", model.Status);
    }

    [AvaloniaFact]
    public async Task ImportedViewCannotBecomeANewLiveAcquisitionThroughStart()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await model.RebuildAsync(true);
        var identity = model.Tree!.Snapshot.Identity; model.Focus("n:4", toggleMark: true); model.StageSelection();
        var marks = model.Selection.Marks; var basket = model.Selection.Basket;
        Assert.False(File.Exists(fixture.Services.Paths.AnalysisPrivateStateFile));
        await model.StartAsync([fixture.Root]); await Until(() => !model.Running); await model.RebuildAsync(true);
        Assert.Equal(identity.SessionId, model.Tree!.Snapshot.Identity.SessionId); Assert.Equal(AnalysisOrigin.ImportedInventory, model.Tree.Snapshot.Origin);
        Assert.True(model.Tree.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified)); Assert.False(File.Exists(fixture.Services.Paths.AnalysisPrivateStateFile));
        Assert.Same(marks, model.Selection.Marks); Assert.Same(basket, model.Selection.Basket); Assert.Contains("New analysis", model.Status);
    }

    [AvaloniaFact]
    public async Task OfflineComparisonCannotStartAHiddenLiveScan()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()); await source.RebuildAsync(true); await source.SaveNewAsync("previous.fcdsa");
        using var model = await source.CompareSavedAsync("previous.fcdsa"); var result = model.Comparison;
        var previous = model.ComparisonPreviousIdentity; var current = model.ComparisonCurrentIdentity;
        Assert.False(File.Exists(fixture.Services.Paths.AnalysisPrivateStateFile));
        await model.StartAsync([fixture.Root]); await Until(() => !model.Running);
        Assert.Same(result, model.Comparison); Assert.Equal(previous, model.ComparisonPreviousIdentity); Assert.Equal(current, model.ComparisonCurrentIdentity);
        Assert.Null(model.Tree); Assert.False(File.Exists(fixture.Services.Paths.AnalysisPrivateStateFile)); Assert.Contains("New analysis", model.Status);
    }
}
