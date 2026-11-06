using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Fact]
    public async Task LogicalHierarchyKeepsExactDirectoryTotalsAndRankedPagesAfterSourceOwnersClose()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory(); var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory);
        var lease = await dataset.AcquireAsync(Cancellation); var hierarchy = await lease.BuildLogicalHierarchyAsync(Cancellation);
        await dataset.DisposeAsync(); await lease.DisposeAsync(); Assert.False(directory.Closed);
        Assert.Equal((UInt128)220, hierarchy.Totals.KnownBytes); Assert.Equal(3UL, hierarchy.Totals.Files);
        var root = await hierarchy.ReadAsync(1, Cancellation); Assert.Equal(hierarchy.Totals, root.Totals);
        var size = await hierarchy.ReadChildrenAsync(1, AnalysisLogicalSort.FileSize, maximum: 1, cancellation: Cancellation);
        Assert.Equal(2UL, size.Total); Assert.Equal("A", Assert.Single(size.Rows).Path.Name); Assert.Equal((UInt128)120, size.Rows[0].Totals.KnownBytes); Assert.True(size.HasMore);
        var second = await hierarchy.ReadChildrenAsync(1, AnalysisLogicalSort.FileSize, size.NextOffset, cancellation: Cancellation); Assert.Equal("B", Assert.Single(second.Rows).Path.Name);
        var files = await hierarchy.ReadChildrenAsync(2, AnalysisLogicalSort.Files, cancellation: Cancellation); Assert.Equal(new ulong[] { 4, 6 }, files.Rows.Select(r => r.Path.Id));
        Assert.Equal((UInt128)100, (await hierarchy.ReadAsync(4, Cancellation)).Totals.KnownBytes);
        await hierarchy.DisposeAsync(); Assert.True(directory.Closed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => hierarchy.ReadAsync(1, Cancellation));
    }

    [Fact]
    public async Task HierarchyPreservesMissingLogicalValuesInsteadOfTurningThemIntoZeroFiles()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Unsupported) }, snapshot.Objects[1]] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildLogicalHierarchyAsync(Cancellation);
        Assert.Equal((UInt128)20, hierarchy.Totals.KnownBytes); Assert.Equal(3UL, hierarchy.Totals.Files); Assert.Equal(2UL, hierarchy.Totals.UnknownFiles);
        var a = await hierarchy.ReadAsync(2, Cancellation); Assert.Equal(2UL, a.Totals.Files); Assert.Equal(1UL, a.Totals.UnknownFiles);
        var b = await hierarchy.ReadAsync(3, Cancellation); Assert.Equal((UInt128)0, b.Totals.KnownBytes); Assert.Equal(1UL, b.Totals.Files); Assert.Equal(1UL, b.Totals.UnknownFiles);
    }

    [Fact]
    public async Task HierarchySortAndTotalsRetainValuesAbove64Bits()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden(); UInt128 large = (UInt128)1 << 100;
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(large) }, snapshot.Objects[1]] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildLogicalHierarchyAsync(Cancellation);
        Assert.Equal(large * 2 + 20, hierarchy.Totals.KnownBytes);
        var rows = await hierarchy.ReadChildrenAsync(1, AnalysisLogicalSort.FileSize, cancellation: Cancellation); Assert.Equal("A", rows.Rows[0].Path.Name); Assert.Equal(large + 20, rows.Rows[0].Totals.KnownBytes);
    }

    [Fact]
    public async Task CanceledOrOverflowedHierarchyBuildLeavesTheOriginalDatasetUsable()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) }, snapshot.Objects[1]] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.BuildLogicalHierarchyAsync(stop.Token));
        await Assert.ThrowsAsync<OverflowException>(() => lease.BuildLogicalHierarchyAsync(Cancellation));
        Assert.Equal(6UL, (await lease.ReadPathsAsync(0, cancellation: Cancellation)).Total);
    }

    [Fact]
    public async Task DerivedHierarchyUsesTheOriginalSessionQuotaAfterTheFactoryOwnerCloses()
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, sessionLimit: 28 * 1024);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        await Assert.ThrowsAsync<IOException>(() => lease.BuildLogicalHierarchyAsync(Cancellation));
        Assert.Equal(6UL, (await lease.ReadPathsAsync(0, cancellation: Cancellation)).Total);
        Assert.Equal((UInt128)3, (await lease.SummarizeAsync(AnalysisMetric.Files, Cancellation)).Totals.Known);
    }
}
