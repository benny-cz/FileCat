using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public async Task GroupSubsetKeepsCanonicalGeometryEveryTableOrderExactRangesMatchingPathsAndMarks(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation), dimension, false, Cancellation);
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(metric, dimension, cancellation: Cancellation);
        foreach (string? parent in new string?[] { null }.Concat(tree.Groups.Keys))
        {
            string key = parent ?? AnalysisTree.RootKey; int count = tree.ChildrenOf(key).Length;
            for (int skip = 1; skip <= count; skip++)
            {
                await using var subset = await groups.CreateSubsetAsync(parent, skip, Cancellation); string other = AnalysisTree.OtherKey(key, skip);
                var expected = tree.ChildrenOf(other); var geometry = await subset.ReadGeometryAsync(cancellation: Cancellation);
                Assert.Equal(expected.Select(item => item.Key), geometry.Rows.Select(item => item.Key)); Assert.Equal((ulong)expected.Length, geometry.Total);
                Assert.Equal(tree.ScopeValue(other), AnalysisGroupedChart.Area(subset.Totals, metric));
                Assert.Equal(tree.CaptureMembers(other).Order(), (await subset.CaptureMembersAsync(cancellation: Cancellation)).NodeIds.Order());
                Assert.Equal(tree.CaptureMatchingFiles(other, Cancellation).NodeIds, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
                if (parent is null)
                {
                    foreach (var selected in expected)
                        Assert.Equal(tree.CaptureMembers(selected.Key).Order(), (await subset.CaptureGroupMembersAsync(selected.Key, cancellation: Cancellation)).NodeIds.Order());
                    var absent = tree.Groups.Keys.FirstOrDefault(candidate => !expected.Any(item => item.Key == candidate));
                    if (absent is not null) await Assert.ThrowsAsync<InvalidOperationException>(() => subset.CaptureGroupMembersAsync(absent, cancellation: Cancellation));
                }
                else Assert.Equal((await subset.CaptureMembersAsync(cancellation: Cancellation)).NodeIds,
                    (await subset.CaptureGroupMembersAsync(parent, cancellation: Cancellation)).NodeIds);
                var marks = snapshot.Nodes.Select(node => node.Id).Append(999UL).ToImmutableHashSet();
                Assert.Equal(tree.ClassifyMarks(marks, other, Cancellation), await subset.ClassifyMarksAsync(marks, Cancellation));
                foreach (var sort in Enum.GetValues<AnalysisTableSort>()) foreach (bool descending in new[] { false, true })
                {
                    if (parent is null)
                    {
                        var page = await subset.ReadGroupsAsync(sort: sort, descending: descending, cancellation: Cancellation);
                        Assert.Equal(expected.Select(item => item.Key).Order(), page.Rows.Select(row => row.Definition.Key).Order());
                        var sourceOrder = await groups.ReadGroupsAsync(sort: sort, descending: descending, cancellation: Cancellation);
                        var selected = expected.Select(item => item.Key).ToHashSet();
                        Assert.Equal(sourceOrder.Rows.Where(row => selected.Contains(row.Definition.Key)).Select(row => row.Definition.Key), page.Rows.Select(row => row.Definition.Key));
                        if (page.Total > 0)
                        {
                            var range = await subset.CaptureTableRangeAsync(0, page.Total - 1, sort, descending, Cancellation);
                            Assert.Empty(range.Selection.NodeIds); Assert.Equal(page.Total, range.SyntheticRows);
                        }
                    }
                    else
                    {
                        var page = await subset.ReadMemberRowsAsync(sort: sort, descending: descending, cancellation: Cancellation);
                        Assert.Equal(expected.Where(item => item.NodeId.HasValue).Select(item => item.NodeId!.Value).Order(), page.Rows.Select(row => row.Member.Path.Id).Order());
                        var sourceOrder = await groups.ReadMemberRowsAsync(parent, sort: sort, descending: descending, cancellation: Cancellation);
                        var selected = expected.Where(item => item.NodeId.HasValue).Select(item => item.NodeId!.Value).ToHashSet();
                        Assert.Equal(sourceOrder.Rows.Where(row => selected.Contains(row.Member.Path.Id)).Select(row => row.Member.Path.Id), page.Rows.Select(row => row.Member.Path.Id));
                        if (page.Total > 0) Assert.Equal(page.Rows.Select(row => row.Member.Path.Id),
                            (await subset.CaptureTableRangeAsync(0, page.Total - 1, sort, descending, Cancellation)).Selection.NodeIds);
                    }
                }
                var charges = await subset.ReadChargesAsync(cancellation: Cancellation); Assert.Equal(subset.ChargeCount, charges.Total);
                Assert.Equal(subset.Totals.Known, charges.Rows.Where(charge => charge.Measurement.IsKnown && !charge.IdentityUnverified).Aggregate((UInt128)0, (sum, charge) => checked(sum + charge.Measurement.Value!.Value)));
                Assert.Equal(subset.Totals.IdentityUnverified, charges.Rows.Where(charge => charge.Measurement.IsKnown && charge.IdentityUnverified).Aggregate((UInt128)0, (sum, charge) => checked(sum + charge.Measurement.Value!.Value)));
            }
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => groups.CreateSubsetAsync(parent, count + 1, Cancellation));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GroupSubsetSharedBucketKeepsOnlyCapturedMatchingAliasesAcrossVerifiedObjectIds(bool missing, bool distinctIds)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Nodes = snapshot.Nodes.SetItem(4, snapshot.Nodes[4] with { Name = "y.jpg" }),
            Facts = snapshot.Facts.SetItem(0, snapshot.Facts[0] with { Measurement = missing ? AnalysisMeasurement.Missing(MeasurementState.Unsupported) : AnalysisMeasurement.Known(80) }) };
        if (distinctIds) snapshot = snapshot with { Nodes = snapshot.Nodes.SetItem(4, snapshot.Nodes[4] with { ObjectId = "X2" }), Objects = snapshot.Objects.Add(snapshot.Objects[0] with { Id = "X2" }) };
        var parsed = AnalysisFilter.Parse("name:x.bin", TimeZoneInfo.Utc);
        bool Match(AnalysisNode node, AnalysisObject? obj) => parsed.Evaluate(new(node, obj, node.Name, AnalysisMeasurement.Missing(MeasurementState.Unsupported))) == AnalysisTruth.True;
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, Match, Cancellation), AnalysisGroupingDimension.Extension, true, Cancellation);
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, new("name:x.bin"), Cancellation);
        string parent = Assert.Single(tree.Groups.Values, group => group.Kind == AnalysisGroupKind.SharedAcrossGroups).Definition.Key;
        var source = tree.ChildrenOf(parent); int bucket = source.Select((item, at) => (item, at)).Single(row => row.item.Family == "shared").at;
        // Missing shared accounting sorts after known-zero references; the suffix can be
        // synthetic-only while its captured matching aliases are still reviewable.
        if (bucket > 0)
        {
            await using var subset = await groups.CreateSubsetAsync(parent, bucket, Cancellation);
            Assert.Empty((await subset.ReadMemberRowsAsync(cancellation: Cancellation)).Rows);
            Assert.Equal(new ulong[] { 4 }, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
            Assert.Equal(tree.ClassifyMarks(ImmutableHashSet.Create(4UL, 5UL), AnalysisTree.OtherKey(parent, bucket), Cancellation), await subset.ClassifyMarksAsync(ImmutableHashSet.Create(4UL, 5UL), Cancellation));
        }
        else
        {
            await using var subset = await groups.CreateSubsetAsync(parent, 1, Cancellation);
            Assert.Equal(new ulong[] { 4 }, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
            Assert.Equal(0UL, (await subset.ReadChargesAsync(cancellation: Cancellation)).Total);
        }
    }

    [Fact]
    public async Task GroupSubsetAndSiblingRemainReadableAfterEveryProducerCloses()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory(); var snapshot = AnalysisGroupingTests.Fixture();
        var dataset = await Build(snapshot, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        var other = await groups.CreateSubsetAsync(null, 1, Cancellation); var sibling = await other.CreateSiblingAsync(null, 2, Cancellation);
        await groups.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); await other.DisposeAsync(); Assert.False(directory.Closed);
        try
        {
            var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, cancellation: Cancellation), AnalysisGroupingDimension.Extension, false, Cancellation);
            Assert.Equal(tree.ChildrenOf(AnalysisTree.OtherKey(AnalysisTree.RootKey, 2)).Select(item => item.Key), (await sibling.ReadGeometryAsync(cancellation: Cancellation)).Rows.Select(item => item.Key));
            await using var parent = await sibling.OpenSnapshotAsync(Cancellation); await sibling.DisposeAsync(); Assert.False(directory.Closed);
            Assert.NotEmpty((await parent.ReadGroupsAsync(cancellation: Cancellation)).Rows);
        }
        finally { await sibling.DisposeAsync(); await other.DisposeAsync(); await groups.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupSubsetOrdinalTiesAndArbitraryPathIdsKeepEveryPagedSortInsideTheGeometrySuffix(bool highIds)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        ulong root = highIds ? ulong.MaxValue : 1;
        snapshot = snapshot with { Nodes = new[] { snapshot.Nodes[0] with { Id = root } }.Concat(Enumerable.Range(2, 271).Select(id =>
            new AnalysisNode(highIds ? id == 2 ? ulong.MaxValue - 1 : (ulong)id * 7919 : (ulong)id, "r", root, $"file-{999 - id:D3}.bin", AnalysisKind.File, "X"))).ToImmutableArray(),
            Objects = [snapshot.Objects[0]], Facts = [snapshot.Facts[0]] };
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.Files, cancellation: Cancellation), AnalysisGroupingDimension.Extension, false, Cancellation);
        string parent = Assert.Single(tree.Groups).Key; string scope = AnalysisTree.OtherKey(parent, 128);
        var expected = tree.ChildrenOf(scope).Select(item => item.NodeId!.Value).ToHashSet(); Assert.Equal(143, expected.Count);
        await using var dataset = await Build(snapshot, io, arbitraryPathIds: highIds); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        await using var subset = await groups.CreateSubsetAsync(parent, 128, Cancellation);
        foreach (var sort in Enum.GetValues<AnalysisTableSort>()) foreach (bool descending in new[] { false, true })
        {
            var actual = new HashSet<ulong>(); ulong offset = 0;
            while (offset < subset.Rows)
            {
                var page = await subset.ReadMemberRowsAsync(offset, 37, sort, descending, Cancellation); Assert.NotEmpty(page.Rows);
                foreach (var row in page.Rows) Assert.True(actual.Add(row.Member.Path.Id)); offset = page.NextOffset;
            }
            Assert.True(expected.SetEquals(actual));
        }
        Assert.Equal(expected.Order(), (await subset.CaptureMembersAsync(cancellation: Cancellation)).NodeIds.Order());
    }

    [Fact]
    public async Task GroupSubsetCancellationWaitsForItsActualClonedReadAndAllSourceOwners()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisGroupingTests.Fixture(), io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); using var release = new ManualResetEventSlim(); int expired = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.BeforeNextRead = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(15))) Interlocked.Increment(ref expired); };
        var creating = groups.CreateSubsetAsync(null, 1, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); stop.Cancel(); var closing = groups.DisposeAsync().AsTask();
            var leaseClose = lease.DisposeAsync().AsTask(); var datasetClose = dataset.DisposeAsync().AsTask();
            Assert.False(creating.IsCompleted); Assert.False(closing.IsCompleted); Assert.False(directory.Closed); Assert.Equal(0, Volatile.Read(ref expired));
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);
            await Task.WhenAll(closing, leaseClose, datasetClose).WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.True(directory.Closed); Assert.Equal(0, Volatile.Read(ref expired));
        }
        finally { release.Set(); try { await creating; } catch (OperationCanceledException) { } await groups.DisposeAsync(); }
    }

    [Theory]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    public async Task GroupSubsetPreservesKnownZeroSharedAccountingAndItsEvidenceInTheGeometrySuffix(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(0) }),
            Facts = snapshot.Facts.SetItem(0, snapshot.Facts[0] with { Measurement = AnalysisMeasurement.Known(0) })
                .SetItem(1, snapshot.Facts[1] with { Measurement = AnalysisMeasurement.Known(60) }) };
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation), AnalysisGroupingDimension.Extension, false, Cancellation);
        string parent = Assert.Single(tree.Groups).Key;
        var bucket = Assert.Single(tree.ChildrenOf(parent), item => item.Family == "shared"); Assert.Equal((UInt128)0, bucket.Value); Assert.False(bucket.Unknown);
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(metric, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        var group = await groups.ReadGroupAsync(parent, Cancellation); Assert.True(group.HasSharedAccounting); Assert.Equal(1UL, group.SharedCharges);
        Assert.Equal(tree.ChildrenOf(parent).Select(item => item.Key), (await groups.ReadGeometryAsync(parent, cancellation: Cancellation)).Rows.Select(item => item.Key));
        await using var subset = await groups.CreateSubsetAsync(parent, 1, Cancellation);
        Assert.Contains((await subset.ReadGeometryAsync(cancellation: Cancellation)).Rows, item => item.Key == bucket.Key);
        Assert.Equal((UInt128)0, subset.Totals.Known); Assert.Equal(1UL, subset.ScopeGroup!.SharedCharges);
        var charge = Assert.Single((await subset.ReadChargesAsync(cancellation: Cancellation)).Rows); Assert.True(charge.Measurement.IsKnown); Assert.Equal((UInt128)0, charge.Measurement.Value);
        Assert.Equal(new ulong[] { 4, 5 }, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
    }
}
