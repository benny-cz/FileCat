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
    public async Task OtherExplorerRetainsExactPagesMatchesMarksAndSiblingScopesAfterAllProducersClose(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Facts = snapshot.Facts.SetItem(1, snapshot.Facts[1] with { Measurement = AnalysisMeasurement.Known(60) })
            .Add(new("directory", null, 1, "directory-self", "fixture-v1", AnalysisMeasurement.Known(20), "fixture", DateTimeOffset.UnixEpoch, [])) };
        var tree = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation), false, Cancellation);
        var directory = new MemoryDirectory(); var dataset = await Build(snapshot, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var original = await AnalysisDatasetExplorer.CreateAsync(lease, metric, cancellation: Cancellation);
        var explorer = await original.CreateScopeAsync(1, 1, Cancellation);
        await original.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
        try
        {
            string scope = AnalysisTree.OtherKey("n:1", 1); Assert.Equal(scope, explorer.OtherScopeKey);
            var expected = tree.ChildrenOf(scope).Where(item => item.NodeId.HasValue).Select(item => item.NodeId!.Value).Order().ToArray();
            foreach (var sort in Enum.GetValues<AnalysisHierarchySort>()) foreach (bool descending in new[] { false, true })
            {
                Assert.True(await explorer.LoadPageAsync(1, sort, cancellation: Cancellation, descending: descending));
                var page = explorer.Page!; Assert.Equal(1, page.GeometrySkipped); Assert.Equal(0UL, page.Children.Offset);
                Assert.Equal(expected, page.Children.Rows.Select(row => row.Path.Id).Order());
                Assert.Equal(tree.ScopeValue(scope), AnalysisGroupedChart.Area(page.Scope!.Totals, metric));
                for (int index = 0; index < page.Children.Rows.Length; index++)
                    Assert.Equal((ulong)index, (await explorer.LocateChildAsync(page.Children.Rows[index].Path.Id, sort, descending, Cancellation)).Offset);
                if (page.Children.Rows.Length != 0)
                    Assert.Equal(page.Children.Rows.Select(row => row.Path.Id), (await explorer.CaptureTableRangeAsync(1, 0, (ulong)page.Children.Rows.Length - 1, sort, descending, Cancellation)).Selection.NodeIds);
            }
            Assert.Equal(tree.CaptureMatchingFiles(scope, Cancellation).NodeIds, (await explorer.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
            var marks = snapshot.Nodes.Select(node => node.Id).Append(999UL).ToImmutableHashSet();
            Assert.Equal(tree.ClassifyMarks(marks, scope, Cancellation), await explorer.ClassifyMarksAsync(marks, cancellation: Cancellation));
            Assert.True(await explorer.LoadChartAsync(1, Cancellation)); Assert.Equal(scope, explorer.Chart!.Scope);
            Assert.Equal(tree.ScopeValue(scope), explorer.Chart.ScopeValue(scope));
            Assert.True(await explorer.InspectAsync(4, cancellation: Cancellation)); Assert.Equal(4UL, explorer.Inspection!.Row.Path.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => explorer.LoadPageAsync(2, cancellation: Cancellation));
            var previous = explorer.Page;
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => explorer.CreateScopeAsync(1, 999, Cancellation));
            Assert.Same(previous, explorer.Page);
            int count = tree.ChildrenOf("n:1").Length;
            await using var empty = await explorer.CreateScopeAsync(1, count, Cancellation);
            Assert.True(await empty.LoadPageAsync(1, cancellation: Cancellation)); Assert.Empty(empty.Page!.Children.Rows); Assert.Empty(empty.Page.Buckets);
            Assert.Equal((UInt128)0, AnalysisGroupedChart.Area(empty.Totals, metric));
            await using var parent = await empty.CreateScopeAsync(1, cancellation: Cancellation);
            await explorer.DisposeAsync(); Assert.False(directory.Closed);
            Assert.Null(parent.OtherScopeKey); Assert.True(await parent.LoadPageAsync(1, cancellation: Cancellation));
            Assert.Equal(tree.ChildrenOf("n:1").Count(item => item.NodeId.HasValue), parent.Page!.Children.Rows.Length);
        }
        finally { await explorer.DisposeAsync(); await original.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
    }

    [Fact]
    public async Task OtherExplorerCloseCancelsScopeConstructionAndWaitsForItsActualHeldRead()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.FileSize, cancellation: Cancellation);
        using var release = new ManualResetEventSlim(); int expired = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.BeforeNextRead = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(15))) Interlocked.Increment(ref expired); };
        var creating = explorer.CreateScopeAsync(1, 1, Cancellation);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); var closing = explorer.DisposeAsync().AsTask();
            var leaseClose = lease.DisposeAsync().AsTask(); var datasetClose = dataset.DisposeAsync().AsTask();
            Assert.False(creating.IsCompleted); Assert.False(closing.IsCompleted); Assert.False(directory.Closed); Assert.Equal(1, explorer.PendingRequests);
            Assert.Equal(0, Volatile.Read(ref expired)); release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);
            await Task.WhenAll(closing, leaseClose, datasetClose).WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.Equal(0, explorer.PendingRequests); Assert.True(directory.Closed); Assert.Equal(0, Volatile.Read(ref expired));
        }
        finally { release.Set(); try { await creating; } catch (OperationCanceledException) { } await explorer.DisposeAsync(); }
    }
}
