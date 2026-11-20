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
    public async Task HierarchyLogicalColumnCountsSelectedPathsIndependentlyOfSharedChargePlacement(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisAccountingTests.Golden(), io);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var all = await lease.BuildHierarchyAsync(metric, Cancellation);
        Assert.Equal((UInt128)220, all.LogicalTotals.KnownBytes); Assert.Equal(3UL, all.LogicalTotals.Files); Assert.Empty(all.LogicalTotals.Missing);
        Assert.Equal(all.LogicalTotals, (await all.ReadAsync(1, Cancellation)).Logical);
        Assert.Equal((UInt128)120, (await all.ReadAsync(2, Cancellation)).Logical.KnownBytes);
        Assert.Equal((UInt128)100, (await all.ReadAsync(3, Cancellation)).Logical.KnownBytes);
        Assert.Equal((UInt128)100, (await all.ReadAsync(4, Cancellation)).Logical.KnownBytes);
        await using var filtered = await lease.BuildHierarchyAsync(metric, new("name:x.bin"), Cancellation);
        Assert.Equal((UInt128)100, filtered.LogicalTotals.KnownBytes); Assert.Equal(1UL, filtered.LogicalTotals.Files);
        Assert.Equal((UInt128)0, (await filtered.ReadAsync(5, Cancellation)).Logical.KnownBytes);
        Assert.Equal(0UL, (await filtered.ReadAsync(5, Cancellation)).Logical.Files);
        var matching = Assert.Single((await filtered.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows);
        Assert.Equal((UInt128)100, matching.Logical.KnownBytes); Assert.Equal(1UL, matching.Logical.Files);
    }

    [Fact]
    public async Task LogicalColumnUsesTheActiveMetricPredicateWithoutReevaluatingStateAgainstFileSize()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Pending) }) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.OnDisk, new("state:known"), Cancellation);
        Assert.Equal((UInt128)80, explorer.Totals.Known); Assert.Equal(2UL, explorer.Totals.MatchingPaths);
        Assert.Equal((UInt128)0, explorer.LogicalTotals.KnownBytes); Assert.Equal(2UL, explorer.LogicalTotals.Files);
        Assert.Equal(2UL, explorer.LogicalTotals.Missing[MeasurementState.Pending]);
        Assert.True(await explorer.LoadPageAsync(2, cancellation: Cancellation));
        var row = Assert.Single(explorer.Page!.Children.Rows); Assert.Equal(4UL, row.Path.Id);
        Assert.Equal(1UL, row.Logical.UnknownFiles); Assert.Equal(1UL, row.Logical.Files);
        await using var unknown = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("state:known", UnknownOnly: true), Cancellation);
        Assert.Equal(0UL, unknown.LogicalTotals.Files);
        await using var other = await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, new("state:known"), Cancellation);
        Assert.Equal((UInt128)20, other.LogicalTotals.KnownBytes); Assert.Equal(1UL, other.LogicalTotals.Files);
    }

    [Theory]
    [InlineData(MeasurementState.Pending)]
    [InlineData(MeasurementState.Unsupported)]
    [InlineData(MeasurementState.Excluded)]
    [InlineData(MeasurementState.Failed)]
    [InlineData(MeasurementState.Changed)]
    public async Task LogicalMissingStatesStaySeparateFromKnownZeroAndAllocation(MeasurementState missing)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(missing) }, snapshot.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.Files, Cancellation);
        Assert.Equal((UInt128)3, hierarchy.Totals.Known); Assert.Equal((UInt128)0, hierarchy.LogicalTotals.KnownBytes);
        Assert.Equal(3UL, hierarchy.LogicalTotals.Files); Assert.Equal(2UL, hierarchy.LogicalTotals.Missing[missing]);
        Assert.Equal(2UL, hierarchy.LogicalTotals.UnknownFiles);
        var zero = await hierarchy.ReadAsync(6, Cancellation); Assert.Empty(zero.Logical.Missing); Assert.Equal(1UL, zero.Logical.Files);
        Assert.Equal(1UL, (await hierarchy.ReadAsync(4, Cancellation)).Logical.Missing[missing]);
    }

    [Fact]
    public async Task LogicalColumnPreserves128BitValuesAndIgnoresHiddenOverflow()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden(); UInt128 large = (UInt128)1 << 100;
        snapshot = snapshot with { Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(large) }) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.Files, Cancellation);
        Assert.Equal(large * 2 + 20, hierarchy.LogicalTotals.KnownBytes);
        Assert.Equal(large + 20, (await hierarchy.ReadAsync(2, Cancellation)).Logical.KnownBytes);
        snapshot = snapshot with { Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) }) };
        await using var huge = await Build(snapshot, io); await using var hugeLease = await huge.AcquireAsync(Cancellation);
        await Assert.ThrowsAsync<OverflowException>(() => hugeLease.BuildHierarchyAsync(AnalysisMetric.Files, Cancellation));
        await using var selected = await hugeLease.BuildHierarchyAsync(AnalysisMetric.Files, new("name:z.bin"), Cancellation);
        Assert.Equal((UInt128)20, selected.LogicalTotals.KnownBytes); Assert.Equal(1UL, selected.LogicalTotals.Files);
    }

    [Fact]
    public async Task LogicalColumnRemainsAdditiveWhenOnDiskBasesDisableTheCombinedArea()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Roots = snapshot.Roots.Add(snapshot.Roots[0] with { Id = "s" }),
            Nodes = snapshot.Nodes.Add(new(7, "s", 0, "Other", AnalysisKind.Directory)).Add(new(8, "s", 7, "other.bin", AnalysisKind.File, "Q")),
            Objects = snapshot.Objects.Add(new("Q", "other:Q", AnalysisMeasurement.Known(9), AnalysisMeasurement.Known(1))),
            Facts = snapshot.Facts.Add(new("other", "Q", null, "primary", "other-basis", AnalysisMeasurement.Known(512), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, Cancellation);
        Assert.False(hierarchy.Totals.CompatibleBasis); Assert.Equal((UInt128)0, hierarchy.Totals.Known);
        Assert.Equal((UInt128)229, hierarchy.LogicalTotals.KnownBytes); Assert.Equal(4UL, hierarchy.LogicalTotals.Files);
        Assert.Equal((UInt128)9, (await hierarchy.ReadAsync(7, Cancellation)).Logical.KnownBytes);
    }
}
