using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private object? _acquisitionProbe;
    private async Task AcquisitionProbeAsync()
    {
        var source = _models[0]; var identity = source.Tree!.Snapshot.Identity;
        var root = source.Tree.Snapshot.Nodes.Single(n => n.ParentId == 0);
        source.Focus(AnalysisTree.Key(root.Id), toggleMark: true); source.StageSelection();
        var marks = source.Selection.Marks; var basket = source.Selection.Basket;
        await source.StartAsync([Path.Combine(_fixture, "large.bin")]); await source.RebuildAsync(true);
        if (source.Tree!.Snapshot.Identity.SessionId != identity.SessionId || source.Tree.Snapshot.Identity.Epoch != identity.Epoch ||
            !ReferenceEquals(marks, source.Selection.Marks) || !ReferenceEquals(basket, source.Selection.Basket) || !source.Status.Contains("New analysis", StringComparison.Ordinal))
            throw new InvalidOperationException("A repeat start replaced the native acquisition or its selections.");
        await source.SaveNewAsync("acquisition.fcdsa");
        using var imported = await source.OpenSavedAsync("acquisition.fcdsa"); await imported.RebuildAsync(true);
        await imported.StartAsync([_fixture]); await imported.RebuildAsync(true);
        if (imported.Tree!.Snapshot.Origin != AnalysisOrigin.ImportedInventory || !imported.Tree.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified) ||
            imported.Tree.Snapshot.Identity.SessionId != identity.SessionId || imported.Running || !imported.Status.Contains("New analysis", StringComparison.Ordinal))
            throw new InvalidOperationException("An imported view became a native acquisition.");
        using var comparison = await source.CompareSavedAsync("acquisition.fcdsa"); var result = comparison.Comparison;
        await comparison.StartAsync([_fixture]);
        if (!ReferenceEquals(result, comparison.Comparison) || comparison.Running || comparison.Tree is not null || !comparison.Status.Contains("New analysis", StringComparison.Ordinal))
            throw new InvalidOperationException("An offline comparison started a hidden native scan.");
        source.Focus(AnalysisTree.Key(root.Id));
        // Keep the acquisition explanation visible for the controller's native capture.
        await source.StartAsync([_fixture]);
        _acquisitionProbe = new { repeatedStartPreservedIdentity = true, marksAndBasketPreserved = ReferenceEquals(marks, source.Selection.Marks) && ReferenceEquals(basket, source.Selection.Basket),
            importedStayedInert = true, comparisonStayedInert = true, sourcePaths = source.Tree.Snapshot.Nodes.Length };
    }
}
