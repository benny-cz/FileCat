using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedFileSizeOrderUsesTheSameLogicalColumnAcrossMetricsAndPagedDirections(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, metric, cancellation: Cancellation);
        Assert.True(await explorer.LoadPageAsync(1, AnalysisHierarchySort.FileSize, maximum: 1, cancellation: Cancellation));
        Assert.True(explorer.Page!.Descending); Assert.Equal(2UL, Assert.Single(explorer.Page.Children.Rows).Path.Id);
        Assert.True(await explorer.LoadPageAsync(1, AnalysisHierarchySort.FileSize, maximum: 1, cancellation: Cancellation, descending: false));
        Assert.False(explorer.Page!.Descending); Assert.Equal(3UL, Assert.Single(explorer.Page.Children.Rows).Path.Id);
        Assert.Equal((UInt128)100, explorer.Page.Children.Rows[0].Logical.KnownBytes);
        Assert.True(await explorer.LoadPageAsync(1, AnalysisHierarchySort.FileSize, offset: 1, maximum: 1, cancellation: Cancellation, descending: false));
        Assert.Equal(2UL, Assert.Single(explorer.Page!.Children.Rows).Path.Id);
        Assert.Equal((UInt128)220, explorer.LogicalTotals.KnownBytes);
    }

    [Theory]
    [InlineData(AnalysisHierarchySort.Metric, true)]
    [InlineData(AnalysisHierarchySort.Metric, false)]
    [InlineData(AnalysisHierarchySort.FileSize, true)]
    [InlineData(AnalysisHierarchySort.FileSize, false)]
    public async Task IndexedNumericOrdersKeepKnownZeroBeforeMissingAndNamespaceLeaves(AnalysisHierarchySort sort, bool descending)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Unsupported) }, snapshot.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }],
            Nodes = snapshot.Nodes.Add(new(7, "r", 2, "link", AnalysisKind.Link))
        };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, Cancellation);
        var rows = await hierarchy.ReadChildrenAsync(2, sort, cancellation: Cancellation, descending: descending);
        Assert.Equal(new ulong[] { 6, 4, 7 }, rows.Rows.Select(row => row.Path.Id));
        var tail = await hierarchy.ReadChildrenAsync(2, sort, offset: 1, maximum: 1, cancellation: Cancellation, descending: descending);
        Assert.Equal(4UL, Assert.Single(tail.Rows).Path.Id);
    }

    [Fact]
    public async Task IndexedLogicalOrderRetainsFull128BitPrecisionAndFilteredMembership()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known((UInt128)1 << 100) }) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("name:x.bin"), Cancellation);
        var rows = await hierarchy.ReadChildrenAsync(1, AnalysisHierarchySort.FileSize, cancellation: Cancellation, descending: false);
        Assert.Equal(new ulong[] { 3, 2 }, rows.Rows.Select(row => row.Path.Id));
        Assert.Equal((UInt128)0, rows.Rows[0].Logical.KnownBytes); Assert.Equal((UInt128)1 << 100, rows.Rows[1].Logical.KnownBytes);
        Assert.False(rows.Rows[0].IsMatch); Assert.Equal(1UL, hierarchy.LogicalTotals.Files);
    }
}
