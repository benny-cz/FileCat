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
    public async Task OtherSubsetGeometryMatchesMemoryAndEveryTableSortRetainsTheExactSuffix(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisAccountingTests.Golden();
        source = source with { Facts = source.Facts.SetItem(1, source.Facts[1] with { Measurement = AnalysisMeasurement.Known(60) })
            .Add(new("directory", null, 1, "directory-self", "fixture-v1", AnalysisMeasurement.Known(200), "fixture", DateTimeOffset.UnixEpoch, [])) };
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, metric, cancellation: Cancellation), false, Cancellation);
        var directory = new MemoryDirectory(); await using var dataset = await Build(source, io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(metric, Cancellation);
        var geometry = await hierarchy.ReadGeometryAsync(1, cancellation: Cancellation); Assert.Equal(tree.ChildrenOf("n:1").Select(item => item.Key), geometry.Rows.Select(item => item.Key));
        for (int skip = 1; skip <= geometry.Rows.Length; skip++)
        {
            await using var subset = await hierarchy.CreateSubsetAsync(1, skip, Cancellation); string scope = AnalysisTree.OtherKey("n:1", skip);
            Assert.Equal(tree.ChildrenOf(scope).Select(item => item.Key), (await subset.ReadGeometryAsync(cancellation: Cancellation)).Rows.Select(item => item.Key));
            Assert.Equal(tree.ScopeValue(scope), AnalysisGroupedChart.Area(subset.Totals, metric));
            var expected = tree.ChildrenOf(scope).Where(item => item.NodeId.HasValue).Select(item => item.NodeId!.Value).Order().ToArray();
            foreach (var sort in Enum.GetValues<AnalysisHierarchySort>()) foreach (bool descending in new[] { false, true })
            {
                var page = await subset.ReadChildrenAsync(sort, cancellation: Cancellation, descending: descending);
                Assert.Equal(expected, page.Rows.Select(row => row.Path.Id).Order()); Assert.Equal((ulong)expected.Length, page.Total);
                for (int row = 0; row < page.Rows.Length; row++)
                    Assert.Equal((ulong)row, (await subset.LocateChildAsync(page.Rows[row].Path.Id, sort, descending, Cancellation)).Offset);
                if (page.Rows.Length != 0)
                    Assert.Equal(page.Rows.Select(row => row.Path.Id), (await subset.CaptureTableRangeAsync(0, (ulong)page.Rows.Length - 1, sort, descending, Cancellation)).Selection.NodeIds);
            }
            Assert.Equal(tree.CaptureMatchingFiles(scope, Cancellation).NodeIds, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
            var marks = source.Nodes.Select(node => node.Id).Append(999UL).ToImmutableHashSet();
            Assert.Equal(tree.ClassifyMarks(marks, scope, Cancellation), await subset.ClassifyMarksAsync(marks, Cancellation));
        }
    }

    [Fact]
    public async Task OtherSubsetOrdinalGeometryTiesAreIndependentOfNumericTableTiesAndSurviveProducerClosure()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisAccountingTests.Golden();
        source = source with { Nodes = new[] { source.Nodes[0] }.Concat(Enumerable.Range(2, 271).Select(id => new AnalysisNode((ulong)id, "r", 1, $"file-{999 - id:D3}", AnalysisKind.File, "X"))).ToImmutableArray(),
            Objects = [source.Objects[0]], Facts = [source.Facts[0]] };
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, AnalysisMetric.Files, cancellation: Cancellation), false, Cancellation);
        var directory = new MemoryDirectory(); var dataset = await Build(source, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.Files, Cancellation); var subset = await hierarchy.CreateSubsetAsync(1, 128, Cancellation);
        await hierarchy.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
        try
        {
            Assert.Equal(143UL, subset.Rows); var expected = tree.ChildrenOf(AnalysisTree.OtherKey("n:1", 128)).Select(row => row.NodeId!.Value).ToHashSet();
            Assert.Equal(143, expected.Count); Assert.NotEqual(Enumerable.Range(130, 143).Select(id => (ulong)id).ToHashSet(), expected);
            foreach (var sort in Enum.GetValues<AnalysisHierarchySort>()) foreach (bool descending in new[] { false, true })
            {
                ulong offset = 0; var actual = new HashSet<ulong>();
                do
                {
                    var page = await subset.ReadChildrenAsync(sort, offset, 37, Cancellation, descending);
                    Assert.NotEmpty(page.Rows); foreach (var row in page.Rows) Assert.True(actual.Add(row.Path.Id)); offset = page.NextOffset;
                } while (offset < subset.Rows);
                Assert.True(expected.SetEquals(actual));
            }
            Assert.True(expected.SetEquals((await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds));
        }
        finally { await subset.DisposeAsync(); await hierarchy.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OtherSubsetSharedBucketRetainsOnlyMatchingAliasesAndMissingBucketsRemainVisible(bool missing, bool distinctIds)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisAccountingTests.Golden();
        source = source with { Facts = source.Facts.SetItem(0, source.Facts[0] with { Measurement = missing ? AnalysisMeasurement.Missing(MeasurementState.Unsupported) : AnalysisMeasurement.Known(80) })
            .SetItem(1, source.Facts[1] with { Measurement = AnalysisMeasurement.Known(300) }) };
        if (distinctIds) source = source with { Nodes = source.Nodes.SetItem(3, source.Nodes[3] with { ObjectId = "X2" }), Objects = source.Objects.Add(source.Objects[0] with { Id = "X2" }) };
        var filter = AnalysisFilter.Parse("name:x.bin OR name:z.bin", TimeZoneInfo.Utc);
        bool Match(AnalysisNode node, AnalysisObject? obj) => filter.Evaluate(new(node, obj, node.Name, AnalysisMeasurement.Missing(MeasurementState.Unsupported))) == AnalysisTruth.True;
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, AnalysisMetric.OnDisk, Match, Cancellation), true, Cancellation);
        await using var dataset = await Build(source, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("name:x.bin OR name:z.bin"), Cancellation);
        var geometry = await hierarchy.ReadGeometryAsync(1, cancellation: Cancellation); Assert.Equal(tree.ChildrenOf("n:1").Select(row => row.Key), geometry.Rows.Select(row => row.Key));
        int bucket = geometry.Rows.Select((row, index) => (row, index)).Single(item => item.row.Family == "shared").index;
        if (bucket == 0)
        {
            await using var without = await hierarchy.CreateSubsetAsync(1, 1, Cancellation);
            Assert.Equal(tree.CaptureMatchingFiles(AnalysisTree.OtherKey("n:1", 1), Cancellation).NodeIds, (await without.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
        }
        else
        {
            await using var subset = await hierarchy.CreateSubsetAsync(1, bucket, Cancellation);
            Assert.Equal(new ulong[] { 4 }, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
            Assert.Equal(tree.ClassifyMarks(ImmutableHashSet.Create(4UL, 5UL, 6UL), AnalysisTree.OtherKey("n:1", bucket), Cancellation),
                await subset.ClassifyMarksAsync(ImmutableHashSet.Create(4UL, 5UL, 6UL), Cancellation));
        }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => hierarchy.CreateSubsetAsync(1, geometry.Rows.Length + 1, Cancellation));
        Assert.Equal(6UL, lease.Descriptor.Paths);
    }

    [Fact]
    public async Task OtherSubsetCancellationKeepsTheActualClonedReadAndItsSourceOwnedUntilItReturns()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, Cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int expired = 0;
        directory.BeforeNextRead = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(15))) Interlocked.Increment(ref expired); };
        var creating = hierarchy.CreateSubsetAsync(1, 1, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); stop.Cancel(); var closing = hierarchy.DisposeAsync().AsTask();
            // Cleanup shares the same actual device admission. Queue every close, but do not await
            // that queue while this test is deliberately holding the native read ahead of it.
            var leaseClose = lease.DisposeAsync().AsTask(); var datasetClose = dataset.DisposeAsync().AsTask();
            Assert.False(creating.IsCompleted, "The held native read completed before release.");
            Assert.False(closing.IsCompleted, "The hierarchy retired while its actual read was held.");
            Assert.False(directory.Closed); Assert.Equal(0, Volatile.Read(ref expired));
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);
            await Task.WhenAll(closing, leaseClose, datasetClose).WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.True(directory.Closed); Assert.Equal(0, Volatile.Read(ref expired));
        }
        finally { release.Set(); try { await creating; } catch (OperationCanceledException) { } await hierarchy.DisposeAsync(); }
    }
}
