using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData("name:x.cs OR name:z")]
    [InlineData("root:r")]
    public async Task DeferredScopeIncompatibleBasesCannotEraseNonmatchingAliasContext(string query)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisGroupingTests.Fixture();
        source = source with { Facts = source.Facts.SetItem(1, source.Facts[1] with { BasisId = "other-basis", Measurement = AnalysisMeasurement.Known(20) }) };
        var roots = new AnalysisRootScopeIndex(source, Cancellation); var filter = AnalysisFilter.Parse(query, TimeZoneInfo.Utc, roots: source.Roots, rootScopes: roots);
        bool Match(AnalysisNode node, AnalysisObject? obj) => filter.Evaluate(new(node, obj, node.Name, AnalysisMeasurement.Missing(MeasurementState.Unsupported))) == AnalysisTruth.True;
        Func<AnalysisNode, bool>? directories = filter.IsNamespaceOnly ? node => Match(node, null) : null;
        var projection = AnalysisAggregate.Project(source, AnalysisMetric.OnDisk, Match, Cancellation, directories);
        Assert.False(projection.Totals.CompatibleBasis); Assert.Empty(projection.Charges); Assert.Contains(5UL, projection.AliasContextNodes);
        var tree = AnalysisTree.Build(source, projection, true, Cancellation, directories);
        await using var dataset = await Build(source, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new(query), Cancellation);
        Assert.False(hierarchy.Totals.CompatibleBasis);
        Assert.Equal(await hierarchy.ClassifyMarksAsync(ScopeMarks, cancellation: Cancellation), tree.ClassifyMarks(ScopeMarks, cancellation: Cancellation));
        Assert.True(tree.Items["n:5"].IsFilterContext); Assert.True(tree.Items["n:5"].Visible); Assert.True(tree.Items["n:3"].Visible);
        Assert.Equal(new ulong[] { 4, 6 }, tree.CaptureMatchingFiles(cancellation: Cancellation).NodeIds);
        Assert.Equal(new ulong[] { 4, 6 }, (await hierarchy.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
    }
    [Fact]
    public void DeferredScopeDefaultHierarchyRowsDoNotImplyAnAliasContextWithoutCapturedEvidence()
    {
        var node = AnalysisGroupingTests.Fixture().Nodes.Single(node => node.Id == 4);
        var totals = new AnalysisTotals(100, 0, 1, System.Collections.Immutable.ImmutableDictionary<MeasurementState, ulong>.Empty, true);
        var row = new AnalysisHierarchyRow(AnalysisPathMetadata.From(node), totals, false, false);
        Assert.False(AnalysisIndexedChart.Row(row, AnalysisMetric.FileSize).IsFilterContext);
        Assert.True(AnalysisIndexedChart.Row(row with { IsFilterContext = true }, AnalysisMetric.OnDisk).IsFilterContext);
    }
    [Fact]
    public void DeferredScopeMatchingFileLimitRefusesBeforeAnyMarksCanBeAdded()
    {
        var source = AnalysisAccountingTests.Golden();
        source = source with
        {
            Nodes = new[] { source.Nodes[0] }.Concat(Enumerable.Range(2, AnalysisLimits.ReviewPaths + 1)
                .Select(id => new AnalysisNode((ulong)id, "r", 1, "file-" + id, AnalysisKind.File, "X"))).ToImmutableArray(),
            Objects = [source.Objects[0]], Facts = []
        };
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, AnalysisMetric.Files, cancellation: Cancellation), false, Cancellation);
        var state = new AnalysisSelectionState(); state.MarkCaptured([4]);
        var error = Assert.Throws<InvalidOperationException>(() => state.MarkCaptured(tree.CaptureMatchingFiles(cancellation: Cancellation).NodeIds));
        Assert.Contains("100,000", error.Message); Assert.Equal(new ulong[] { 4 }, state.Marks.Order());
    }
}
