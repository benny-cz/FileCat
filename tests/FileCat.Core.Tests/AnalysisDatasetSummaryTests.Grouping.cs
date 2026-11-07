using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public async Task IndexedGroupingMatchesMemoryAccountingAndPagesExactMembersAndCharges(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        var projected = AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation);
        var expected = AnalysisGrouping.Project(snapshot, projected, dimension, Cancellation);
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(metric, dimension, cancellation: Cancellation);
        Equal(projected.Totals, groups.Totals); Assert.Equal(dimension, groups.Dimension); Assert.Equal(metric, groups.Metric);
        Assert.Equal(AnalysisGroupingRules.RuleVersion, groups.RuleVersion); Assert.Equal((UInt128)220, groups.LogicalTotals.KnownBytes);
        var actual = await AllGroups(groups); Assert.Equal(expected.Groups.Length, actual.Count);
        foreach (var wanted in expected.Groups)
        {
            var found = Assert.Single(actual, row => row.Definition.Key == wanted.Definition.Key);
            Assert.Equal(wanted.Definition, found.Definition); Assert.Equal(wanted.Kind, found.Kind); Equal(wanted.Totals, found.Totals);
            Assert.Equal(wanted.Logical.KnownBytes, found.Logical.KnownBytes); Assert.Equal(wanted.Logical.Files, found.Logical.Files);
            Assert.Equal(wanted.Logical.Missing.OrderBy(p => p.Key), found.Logical.Missing.OrderBy(p => p.Key));
            var members = new List<ulong>(); ulong at = 0;
            while (true)
            {
                var page = await groups.ReadMembersAsync(found.Definition.Key, at, maximum: 1, cancellation: Cancellation);
                Assert.Equal(found.Members, page.Total); members.AddRange(page.Rows.Select(row => row.Path.Id));
                Assert.All(page.Rows, row => Assert.Equal(found.Kind != AnalysisGroupKind.Entries, row.AccountingReference));
                if (!page.HasMore) break; at = page.NextOffset;
            }
            Assert.Equal(wanted.Members, members);
            var charges = new List<AnalysisObjectCharge>(); at = 0;
            while (true)
            {
                var page = await groups.ReadChargesAsync(found.Definition.Key, offset: at, maximum: 1, cancellation: Cancellation);
                Assert.Equal(found.Charges, page.Total); charges.AddRange(page.Rows);
                if (!page.HasMore) break; at = page.NextOffset;
            }
            Assert.Equal(found.Totals.Missing.Values.Aggregate(0UL, (sum, n) => sum + n), (ulong)charges.Count(c => !c.Measurement.IsKnown));
            Assert.Equal(found.Totals.Known, charges.Where(c => c.Measurement.IsKnown && !c.IdentityUnverified).Aggregate((UInt128)0, (sum, c) => sum + c.Measurement.Value!.Value));
            Assert.Equal(found.Totals.IdentityUnverified, charges.Where(c => c.Measurement.IsKnown && c.IdentityUnverified).Aggregate((UInt128)0, (sum, c) => sum + c.Measurement.Value!.Value));
            if (found.Kind == AnalysisGroupKind.SharedAcrossGroups || found.Charges != 0 && charges.All(c => c.Shared))
                Assert.Equal(found.Charges, (await groups.ReadChargesAsync(found.Definition.Key, ownerPath: 0, cancellation: Cancellation)).Total);
        }
    }
    [Theory]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task IndexedFilterCannotPullASharedChargeIntoTheRemainingAliasCategory(AnalysisGroupingDimension dimension)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.UniqueFileSize, dimension, new("name:x.cs"), Cancellation);
        var actual = await AllGroups(groups); Assert.Equal(2, actual.Count); Assert.Equal(1UL, groups.Totals.MatchingPaths);
        var shared = Assert.Single(actual, g => g.Kind == AnalysisGroupKind.SharedAcrossGroups);
        Assert.Equal((UInt128)100, shared.Totals.Known); Assert.Equal(1UL, shared.Members);
        Assert.Equal(4UL, Assert.Single((await groups.ReadMembersAsync(shared.Definition.Key, cancellation: Cancellation)).Rows).Path.Id);
        Assert.Equal(2UL, Assert.Single((await groups.ReadChargesAsync(shared.Definition.Key, cancellation: Cancellation)).Rows).ObservedAliases);
        Assert.Equal((UInt128)0, Assert.Single(actual, g => g.Kind == AnalysisGroupKind.Entries).Totals.Known);
    }
    [Fact]
    public async Task IndexedGroupingRetainsNativeScratchAndMetadataAfterTheOriginalOwnersClose()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        var dataset = await Build(AnalysisGroupingTests.Fixture(), io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
        try
        {
            var all = await AllGroups(groups); var shared = Assert.Single(all, g => g.Kind == AnalysisGroupKind.SharedAcrossGroups);
            Assert.Equal(new ulong[] { 4, 5 }, (await groups.ReadMembersAsync(shared.Definition.Key, cancellation: Cancellation)).Rows.Select(row => row.Path.Id));
            Assert.Equal((UInt128)80, Assert.Single((await groups.ReadChargesAsync(shared.Definition.Key, cancellation: Cancellation)).Rows).Measurement.Value);
        }
        finally { await groups.DisposeAsync(); }
        Assert.True(directory.Closed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => groups.ReadGroupsAsync(cancellation: Cancellation));
    }
    [Fact]
    public async Task IndexedGroupsHaveBoundedPagesAcrossMoreThanOneChartAndBatchOfDistinctExtensions()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisGroupingTests.Fixture(); const int count = AnalysisLimits.ChartLeaves + 17;
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(s.Nodes[0]);
        for (int i = 1; i <= count; i++) nodes.Add(new((ulong)i + 1, "r", 1, "file.extension" + i, AnalysisKind.File, "X"));
        s = s with { Nodes = nodes.ToImmutable(), Facts = [], Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(count) }], Roots = [s.Roots[0]] };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        Assert.Equal((ulong)count, groups.Count); ulong at = 0, seen = 0;
        while (true)
        {
            var page = await groups.ReadGroupsAsync(at, maximum: 113, cancellation: Cancellation); Assert.InRange(page.Rows.Length, 1, 113);
            Assert.All(page.Rows, g => { Assert.Equal((UInt128)1, g.Totals.Known); Assert.Equal(1UL, g.Members); }); seen += (ulong)page.Rows.Length;
            if (!page.HasMore) break; Assert.True(page.NextOffset > at); at = page.NextOffset;
        }
        Assert.Equal((ulong)count, seen);
    }
    [Fact]
    public async Task LongExtensionGroupsUseByteBoundedPagesAndRejectUnknownKeysWithoutRebinding()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisGroupingTests.Fixture();
        s = s with { Nodes = [s.Nodes[0], new(2, "r", 1, "x." + new string('a', 60_000), AnalysisKind.File, "X"),
            new(3, "r", 1, "y." + new string('b', 60_000), AnalysisKind.File, "Z")], Facts = [], Roots = [s.Roots[0]] };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        var first = await groups.ReadGroupsAsync(maximum: 128, cancellation: Cancellation); Assert.Single(first.Rows); Assert.True(first.HasMore);
        Assert.Single((await groups.ReadGroupsAsync(first.NextOffset, 128, Cancellation)).Rows);
        var member = Assert.Single((await groups.ReadMembersAsync(first.Rows[0].Definition.Key, cancellation: Cancellation)).Rows);
        Assert.Equal(60_002, member.Path.Name.Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => groups.ReadMembersAsync("group:2:" + new string('0', 64), cancellation: Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => groups.ReadMembersAsync("group:1:" + new string('0', 64), cancellation: Cancellation));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => groups.ReadGroupsAsync(groups.Count + 1, cancellation: Cancellation));
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => groups.ReadGroupsAsync(cancellation: stopped.Token));
    }
    private static async Task<List<AnalysisDatasetGroup>> AllGroups(AnalysisDatasetGrouping groups)
    {
        var all = new List<AnalysisDatasetGroup>(); ulong at = 0;
        while (true)
        {
            var page = await groups.ReadGroupsAsync(at, maximum: 2, cancellation: Cancellation); all.AddRange(page.Rows);
            if (!page.HasMore) return all; Assert.True(page.NextOffset > at); at = page.NextOffset;
        }
    }
    [Fact]
    public async Task IndexedGroupingPreservesWideAmountsAndMissingLogicalStatesWithoutDuplicatingComponentReferences()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisGroupingTests.Fixture(); UInt128 wide = (UInt128)1 << 100;
        s = s with { Objects = s.Objects.SetItem(0, s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(wide) })
            .SetItem(1, s.Objects[1] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Failed) }),
            Facts = s.Facts.SetItem(0, s.Facts[0] with { Measurement = AnalysisMeasurement.Known(wide) })
                .Add(s.Facts[0] with { Id = "extra", ComponentId = "extra", Measurement = AnalysisMeasurement.Known(16) })
                .Add(s.Facts[2] with { Id = "directory-extra", ComponentId = "directory-extra", Measurement = AnalysisMeasurement.Known(5) }) };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        var all = await AllGroups(groups); var shared = Assert.Single(all, g => g.Kind == AnalysisGroupKind.SharedAcrossGroups);
        Assert.Equal(wide + 16, shared.Totals.Known); Assert.Equal(2UL, shared.Charges); Assert.Equal(2UL, shared.Members);
        var directory = Assert.Single(all, g => g.Kind == AnalysisGroupKind.DirectoryAllocation);
        Assert.Equal((UInt128)37, directory.Totals.IdentityUnverified); Assert.Equal(2UL, directory.Charges); Assert.Equal(1UL, directory.Members);
        Assert.Equal(wide * 2, groups.LogicalTotals.KnownBytes); Assert.Equal(1UL, groups.LogicalTotals.Missing[MeasurementState.Failed]);
        Assert.Equal(2UL, (await groups.ReadMembersAsync(shared.Definition.Key, cancellation: Cancellation)).Total);
        Assert.Equal(1UL, (await groups.ReadMembersAsync(directory.Definition.Key, cancellation: Cancellation)).Total);
    }
    [Fact]
    public async Task GroupPagesShareExplorerAdmissionAndKeepItUntilCanceledQueuedReadsAndCloseActuallyFinish()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        await using var dataset = await Build(AnalysisGroupingTests.Fixture(), io); await using var lease = await dataset.AcquireAsync(Cancellation);
        var first = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        var second = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task? hold = null, closing = null; var requests = new List<Task<AnalysisDatasetPage<AnalysisDatasetGroup>>>();
        try
        {
            hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            for (int i = 0; i < 8; i++) requests.Add((i < 6 ? first : second).ReadGroupsAsync(cancellation: i == 0 ? stop.Token : Cancellation));
            Assert.Equal(8, AnalysisExplorerAdmission.Active);
            await Assert.ThrowsAsync<InvalidOperationException>(() => second.ReadGroupsAsync(cancellation: Cancellation));
            await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.Files, cancellation: Cancellation));
            Assert.Throws<InvalidOperationException>(() => { _ = lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.ModifiedAge, cancellation: Cancellation); });
            stop.Cancel(); closing = first.DisposeAsync().AsTask();
            Assert.False(requests[0].IsCompleted); Assert.False(closing.IsCompleted); Assert.Equal(8, AnalysisExplorerAdmission.Active);
            release.Set(); await hold; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requests[0]); await closing;
            foreach (var result in await Task.WhenAll(requests.Skip(1))) Assert.NotEmpty(result.Rows);
            Assert.Equal(0, AnalysisExplorerAdmission.Active);
            Assert.NotEmpty((await second.ReadGroupsAsync(cancellation: Cancellation)).Rows);
        }
        finally
        {
            release.Set(); if (hold is not null) await hold;
            foreach (var request in requests) try { await request; } catch (OperationCanceledException) { }
            await first.DisposeAsync(); await second.DisposeAsync();
        }
    }
}
