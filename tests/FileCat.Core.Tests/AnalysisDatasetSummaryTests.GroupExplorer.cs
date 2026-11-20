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
    public async Task GroupExplorerKeepsExactScopeChartsSelectionAndChildNavigationAfterAllProducersClose(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation), AnalysisGroupingDimension.Extension, false, Cancellation);
        var directory = new MemoryDirectory(); var dataset = await Build(snapshot, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var original = await AnalysisDatasetGroupExplorer.CreateAsync(lease, metric, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        var other = await original.CreateScopeAsync(null, 1, Cancellation); await original.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync();
        Assert.False(directory.Closed);
        try
        {
            string scope = AnalysisTree.OtherKey(AnalysisTree.RootKey, 1); Assert.Equal(scope, other.OtherScopeKey);
            Assert.Equal(tree.ChildrenOf(scope).Select(item => item.Key), (await other.ReadGeometryAsync(cancellation: Cancellation)).Rows.Select(item => item.Key));
            Assert.Equal(tree.CaptureMatchingFiles(scope, Cancellation).NodeIds, (await other.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
            var marks = snapshot.Nodes.Select(node => node.Id).Append(999UL).ToImmutableHashSet();
            Assert.Equal(tree.ClassifyMarks(marks, scope, Cancellation), await other.ClassifyMarksAsync(marks, cancellation: Cancellation));
            var chart = await AnalysisGroupedChart.BuildAsync(other, cancellation: Cancellation); Assert.Equal(scope, chart.Scope); Assert.True(chart.GeometryProjection);
            Assert.Equal(tree.ScopeValue(scope), chart.ScopeValue(scope));
            foreach (var category in tree.ChildrenOf(scope))
            {
                Assert.Equal(tree.ChildrenOf(category.Key).Select(item => item.Key), (await other.ReadGeometryAsync(category.Key, cancellation: Cancellation)).Rows.Select(item => item.Key));
                var group = await other.ReadGroupAsync(category.Key, Cancellation);
                if (group.Kind == AnalysisGroupKind.SharedAcrossGroups)
                {
                    var expected = tree.Items[category.Key].ReferenceLogical!;
                    Assert.Equal(expected.KnownBytes, group.MemberLogical.KnownBytes); Assert.Equal(expected.Files, group.MemberLogical.Files);
                }
                if (tree.ChildrenOf(category.Key).Length == 0) continue;
                await using var child = await other.CreateScopeAsync(category.Key, 1, Cancellation);
                string childScope = AnalysisTree.OtherKey(category.Key, 1);
                Assert.Equal(tree.CaptureMatchingFiles(childScope, Cancellation).NodeIds, (await child.CaptureMatchingFilesAsync(category.Key, cancellation: Cancellation)).NodeIds);
                Assert.Equal(tree.ClassifyMarks(marks, childScope, Cancellation), await child.ClassifyMarksAsync(marks, cancellation: Cancellation));
                var childChart = await AnalysisGroupedChart.BuildAsync(child, category.Key, Cancellation); Assert.Equal(childScope, childChart.Scope);
                Assert.Equal(tree.ScopeValue(childScope), childChart.ScopeValue(childScope));
                await using var parent = await child.CreateScopeAsync(category.Key, cancellation: Cancellation);
                Assert.Null(parent.OtherScopeKey); Assert.NotNull(await parent.ReadGroupAsync(category.Key, Cancellation));
            }
        }
        finally { await other.DisposeAsync(); await original.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
    }

    [Fact]
    public async Task GroupExplorerCloseCancelsPendingScopeConstructionAndJoinsTheHeldNativeRead()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisGroupingTests.Fixture(), io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        var explorer = await AnalysisDatasetGroupExplorer.CreateAsync(lease, AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        using var release = new ManualResetEventSlim(); int expired = 0; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.BeforeNextRead = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(15))) Interlocked.Increment(ref expired); };
        var creating = explorer.CreateScopeAsync(null, 1, Cancellation);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); var closing = explorer.DisposeAsync().AsTask();
            var leaseClose = lease.DisposeAsync().AsTask(); var datasetClose = dataset.DisposeAsync().AsTask();
            Assert.False(creating.IsCompleted); Assert.False(closing.IsCompleted); Assert.False(directory.Closed); Assert.Equal(1, explorer.PendingScopeRequests); Assert.Equal(0, Volatile.Read(ref expired));
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);
            await Task.WhenAll(closing, leaseClose, datasetClose).WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.Equal(0, explorer.PendingScopeRequests); Assert.True(directory.Closed); Assert.Equal(0, Volatile.Read(ref expired));
        }
        finally { release.Set(); try { await creating; } catch (OperationCanceledException) { } await explorer.DisposeAsync(); }
    }
}
