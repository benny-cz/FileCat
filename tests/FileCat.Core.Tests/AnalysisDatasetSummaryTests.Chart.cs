using System.Collections.Immutable;
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
    public async Task IndexedChartKeepsExactParentAreaAndSharedBucketsAfterSourceOwnersClose(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, metric, new("name:x.bin"), Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync(); Assert.True(await explorer.LoadChartAsync(1, Cancellation));
        var chart = explorer.Chart!; Assert.Equal(metric, chart.Metric); Assert.Equal("n:1", chart.Scope);
        UInt128 expected = metric switch { AnalysisMetric.OnDisk => 80, AnalysisMetric.Files => 1, _ => 100 };
        Assert.Equal(expected, chart.ScopeValue(chart.Scope)); Assert.Equal(expected, chart.ChildrenOf(chart.Scope).Aggregate((UInt128)0, (sum, row) => checked(sum + row.Value)));
        if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
        { var bucket = Assert.Single(chart.ChildrenOf(chart.Scope)); Assert.True(bucket.Synthetic); Assert.Null(bucket.NodeId); Assert.Equal("Shared hard-linked files", bucket.Name); Assert.Equal(0UL, bucket.Files); }
        foreach (var item in chart.Items.Values)
        { var children = chart.ChildrenOf(item.Key); if (children.Length != 0) Assert.Equal(item.Value, children.Aggregate((UInt128)0, (sum, row) => checked(sum + row.Value))); }
        await explorer.DisposeAsync(); Assert.True(directory.Closed); Assert.Null(explorer.Chart);
    }

    [Fact]
    public async Task IndexedChartRemainderOpensTheCanonicalSuffixRatherThanANumericTableOffset()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(snapshot.Nodes[0]);
        for (ulong id = 2; id <= 602; id++) nodes.Add(new(id, "r", 1, "alias-" + id, AnalysisKind.File, "X"));
        snapshot = snapshot with { Nodes = nodes.ToImmutable(), Objects = [snapshot.Objects[0]], Facts = [snapshot.Facts[0]] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.Files, cancellation: Cancellation);
        Assert.True(await explorer.LoadChartAsync(1, Cancellation)); var chart = explorer.Chart!;
        Assert.True(chart.DetailLimited); Assert.InRange(chart.Items.Count, 1, AnalysisLimits.ChartLeaves);
        var target = Assert.Single(chart.Remainders); Assert.Equal(128UL, target.Value.Offset);
        Assert.Equal((UInt128)473, chart.Items[target.Key].Value); Assert.Equal(473UL, chart.Items[target.Key].Files);
        Assert.Equal((UInt128)601, chart.ChildrenOf("n:1").Aggregate((UInt128)0, (sum, row) => checked(sum + row.Value)));
        Assert.True(target.Value.GeometryScope);
        var reference = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.Files, cancellation: Cancellation), false, Cancellation);
        await using var other = await explorer.CreateScopeAsync(target.Value.Parent, checked((int)target.Value.Offset), Cancellation);
        Assert.True(await other.LoadPageAsync(1, maximum: 1, cancellation: Cancellation));
        Assert.Equal(473UL, other.Page!.Children.Total); Assert.Equal(0UL, other.Page.Children.Offset);
        Assert.Equal(reference.CaptureMatchingFiles(target.Key, Cancellation).NodeIds, (await other.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
        Assert.Empty(chart.ChildrenOf(target.Key));
        Assert.Equal(target.Key, chart.ChildrenOf(chart.Scope)[^1].Key);
        Assert.True(await other.LoadChartAsync(1, Cancellation));
        Assert.Equal(target.Key, other.Chart!.Scope);
        Assert.Equal(256UL, Assert.Single(other.Chart.Remainders).Value.Offset);
        var nested = AnalysisTree.OtherKey(other.Chart.Scope, 1);
        Assert.Equal(other.Chart.ChildrenOf(other.Chart.Scope).Skip(1), other.Chart.ChildrenOf(nested));
        var tail = chart.ChildrenOf(AnalysisTree.OtherKey("n:1", 1)); Assert.Equal(chart.ChildrenOf("n:1").Length - 1, tail.Length);
    }

    [Fact]
    public async Task IndexedChartRejectsSupersededReadsAndCloseWaitsForTheirActualIo()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.Files, cancellation: Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); return true; }, Cancellation);
        Task<bool>? first = null, second = null; Task? closing = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            first = explorer.LoadChartAsync(1, Cancellation); second = explorer.LoadChartAsync(2, Cancellation);
            closing = explorer.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted); Assert.Null(explorer.Chart);
        }
        finally { release.Set(); }
        await hold; Assert.False(await first!); Assert.False(await second!); await closing!; Assert.Null(explorer.Chart);
    }
}
