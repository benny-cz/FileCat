using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData(AnalysisMetric.FileSize, 220UL, 120UL, 100UL)]
    [InlineData(AnalysisMetric.Files, 3UL, 2UL, 1UL)]
    [InlineData(AnalysisMetric.UniqueFileSize, 120UL, 20UL, 0UL)]
    [InlineData(AnalysisMetric.OnDisk, 80UL, 0UL, 0UL)]
    public async Task AllMetricHierarchyMatchesGoldenTotalsWithoutChargingAliasRowsTwice(AnalysisMetric metric, ulong total, ulong a, ulong b)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisAccountingTests.Golden(), io); await using var lease = await dataset.AcquireAsync(Cancellation);
        var expected = await lease.SummarizeAsync(metric, Cancellation); await using var hierarchy = await lease.BuildHierarchyAsync(metric, Cancellation);
        Equal(expected.Totals, hierarchy.Totals); Equal(expected.Totals, (await hierarchy.ReadAsync(1, Cancellation)).Totals);
        Assert.Equal((UInt128)total, hierarchy.Totals.Known); Assert.Equal(3UL, hierarchy.Totals.MatchingPaths);
        var first = await hierarchy.ReadChildrenAsync(1, maximum: 1, cancellation: Cancellation); Assert.True(first.HasMore); Assert.Equal(2UL, first.Total);
        Assert.Equal(2UL, Assert.Single(first.Rows).Path.Id); Assert.Equal((UInt128)a, first.Rows[0].Totals.Known);
        var second = await hierarchy.ReadChildrenAsync(1, offset: first.NextOffset, cancellation: Cancellation); Assert.Equal((UInt128)b, Assert.Single(second.Rows).Totals.Known);
        var children = await hierarchy.ReadChildrenAsync(1, AnalysisHierarchySort.Files, cancellation: Cancellation); Assert.Equal(new ulong[] { 2, 3 }, children.Rows.Select(r => r.Path.Id));
        var buckets = await hierarchy.ReadBucketsAsync(1, Cancellation);
        UInt128 area = children.Rows.Aggregate((UInt128)0, (sum, row) => checked(sum + hierarchy.ChartArea(row.Totals)));
        area = buckets.Aggregate(area, (sum, bucket) => checked(sum + hierarchy.ChartArea(bucket.Totals)));
        Assert.Equal(hierarchy.ChartArea(hierarchy.Totals), area);
        bool sharedMetric = metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk;
        var alias = await hierarchy.ReadAsync(4, Cancellation); Assert.Equal(sharedMetric, alias.HasChargesElsewhere); Assert.Equal(1UL, alias.Totals.MatchingPaths);
        if (sharedMetric)
        {
            var shared = Assert.Single(buckets); Assert.Equal(AnalysisHierarchyBucketKind.SharedFiles, shared.Kind);
            Assert.Equal(1UL, shared.Objects); Assert.Equal(2UL, shared.ObservedAliases); Assert.Equal(1UL, shared.Charges); Assert.Equal(0UL, shared.Totals.MatchingPaths);
            Assert.Equal((UInt128)0, alias.Totals.Known); Assert.Equal((UInt128)(metric == AnalysisMetric.OnDisk ? 80 : 100), shared.Totals.Known);
        }
        else Assert.Empty(buckets);
        Assert.Empty(await hierarchy.ReadBucketsAsync(2, Cancellation));
    }

    [Fact]
    public async Task HierarchyDistinguishesMeasuredZeroFromAnAliasChargedElsewhere()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0], snapshot.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.UniqueFileSize, Cancellation);
        var rows = await hierarchy.ReadChildrenAsync(2, cancellation: Cancellation); Assert.Equal(new ulong[] { 6, 4 }, rows.Rows.Select(r => r.Path.Id));
        Assert.False(rows.Rows[0].HasChargesElsewhere); Assert.Empty(rows.Rows[0].Totals.Missing); Assert.Equal((UInt128)0, rows.Rows[0].Totals.Known);
        Assert.True(rows.Rows[1].HasChargesElsewhere); Assert.Equal((UInt128)0, rows.Rows[1].Totals.Known); Assert.True((await hierarchy.ReadAsync(1, Cancellation)).HasChargesElsewhere);
    }

    [Theory]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task HierarchyKeepsUnverifiedPathAmountsSeparateAndRetainsTheirMetricAreaPolicy(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden(); snapshot = snapshot with { Objects = [snapshot.Objects[0] with { VerifiedIdentity = null }, snapshot.Objects[1]] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildHierarchyAsync(metric, Cancellation);
        Equal((await lease.SummarizeAsync(metric, Cancellation)).Totals, hierarchy.Totals); Assert.Empty(await hierarchy.ReadBucketsAsync(1, Cancellation));
        var alias = await hierarchy.ReadAsync(4, Cancellation); Assert.False(alias.HasChargesElsewhere); Assert.Equal((UInt128)0, alias.Totals.Known);
        Assert.Equal((UInt128)(metric == AnalysisMetric.OnDisk ? 80 : 100), alias.Totals.IdentityUnverified);
        Assert.Equal((UInt128)(metric == AnalysisMetric.OnDisk ? 80 : 0), hierarchy.ChartArea(alias.Totals));
    }

    [Fact]
    public async Task DirectorySelfBucketDoesNotBecomeFileCountOrDuplicateChildArea()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        UInt128 large = (UInt128)1 << 100;
        snapshot = snapshot with { Facts = snapshot.Facts.Add(new("directory", null, 2, "self", "fixture-v1", AnalysisMeasurement.Known(large), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, Cancellation);
        Equal((await lease.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation)).Totals, hierarchy.Totals);
        var a = await hierarchy.ReadAsync(2, Cancellation); Assert.Equal(2UL, a.Totals.MatchingPaths); Assert.Equal(large, a.Totals.IdentityUnverified);
        var bucket = Assert.Single(await hierarchy.ReadBucketsAsync(2, Cancellation)); Assert.Equal(AnalysisHierarchyBucketKind.DirectoryAllocation, bucket.Kind);
        Assert.Equal(1UL, bucket.Objects); Assert.Equal(0UL, bucket.ObservedAliases); Assert.Equal(0UL, bucket.Totals.MatchingPaths); Assert.Equal(1UL, bucket.Charges);
        Assert.Equal(large, bucket.Totals.IdentityUnverified);
        var children = await hierarchy.ReadChildrenAsync(2, cancellation: Cancellation);
        Assert.Equal(hierarchy.ChartArea(a.Totals), children.Rows.Aggregate(hierarchy.ChartArea(bucket.Totals), (sum, row) => checked(sum + hierarchy.ChartArea(row.Totals))));
    }

    [Fact]
    public async Task CrossRootSharedChargesRemainAtVirtualParentInsteadOfEnteringEachRoot()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Roots = snapshot.Roots.Add(snapshot.Roots[0] with { Id = "other", Label = "Other", DisplayLocator = "Other", CanonicalLocator = "Other" }),
            Nodes = snapshot.Nodes.Add(new(7, "other", 0, "Other", AnalysisKind.Directory)).Add(new(8, "other", 7, "x2", AnalysisKind.File, "X")) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.UniqueFileSize, Cancellation);
        Assert.Equal((UInt128)120, hierarchy.Totals.Known); Assert.Equal(4UL, hierarchy.Totals.MatchingPaths);
        Assert.Equal((UInt128)20, (await hierarchy.ReadAsync(1, Cancellation)).Totals.Known); Assert.Equal((UInt128)0, (await hierarchy.ReadAsync(7, Cancellation)).Totals.Known);
        Assert.Empty(await hierarchy.ReadBucketsAsync(1, Cancellation)); Assert.Empty(await hierarchy.ReadBucketsAsync(7, Cancellation));
        var bucket = Assert.Single(await hierarchy.ReadBucketsAsync(0, Cancellation)); Assert.Equal((UInt128)100, bucket.Totals.Known); Assert.Equal(3UL, bucket.ObservedAliases);
    }

    [Fact]
    public async Task IncompatibleRootsDisableCombinedAreaButPreserveRootSeparatedAmounts()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Roots = snapshot.Roots.Add(snapshot.Roots[0] with { Id = "other", Label = "Other", DisplayLocator = "Other", CanonicalLocator = "Other" }),
            Nodes = snapshot.Nodes.Add(new(7, "other", 0, "Other", AnalysisKind.Directory)).Add(new(8, "other", 7, "w", AnalysisKind.File, "W")),
            Objects = snapshot.Objects.Add(new("W", "other:W", AnalysisMeasurement.Known(70), AnalysisMeasurement.Known(1))),
            Facts = snapshot.Facts.Add(new("other-fact", "W", null, "primary", "different-basis", AnalysisMeasurement.Known(50), "fixture", DateTimeOffset.UnixEpoch, []))
        };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, Cancellation);
        Assert.False(hierarchy.Totals.CompatibleBasis); Assert.Equal((UInt128)0, hierarchy.ChartArea(hierarchy.Totals)); Assert.Empty(await hierarchy.ReadBucketsAsync(0, Cancellation));
        var roots = await hierarchy.ReadChildrenAsync(0, cancellation: Cancellation); Assert.Equal(new ulong[] { 1, 7 }, roots.Rows.Select(r => r.Path.Id));
        Assert.All(roots.Rows, row => Assert.True(row.Totals.CompatibleBasis)); Assert.Equal((UInt128)80, roots.Rows[0].Totals.Known); Assert.Equal((UInt128)50, roots.Rows[1].Totals.Known);
    }

    [Fact]
    public async Task HierarchyBuildRetainsChargeRowsThroughActualIoWhileTheirOwnerIsClosing()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var charges = await lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        directory.BeforeNextRead = () => { entered.Set(); release.Wait(Cancellation); };
        var building = charges.BuildHierarchyAsync(Cancellation); Task? closing = null;
        try { Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation)); closing = charges.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted); }
        finally { release.Set(); }
        await using var hierarchy = await building; Assert.NotNull(closing); await closing;
        Assert.Equal((UInt128)80, (await hierarchy.ReadAsync(1, Cancellation)).Totals.Known); Assert.Single(await hierarchy.ReadBucketsAsync(1, Cancellation));
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task AllMetricHierarchyRetainsAmountsAbove64BitsInRowsBucketsAndRanking(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); UInt128 large = (UInt128)1 << 100; var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(large) }, snapshot.Objects[1]],
            Facts = [snapshot.Facts[0] with { Measurement = AnalysisMeasurement.Known(large) }, snapshot.Facts[1]] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildHierarchyAsync(metric, Cancellation);
        Equal((await lease.SummarizeAsync(metric, Cancellation)).Totals, hierarchy.Totals); Equal(hierarchy.Totals, (await hierarchy.ReadAsync(1, Cancellation)).Totals);
        if (metric == AnalysisMetric.FileSize)
        {
            var rows = await hierarchy.ReadChildrenAsync(1, cancellation: Cancellation); Assert.Equal(2UL, rows.Rows[0].Path.Id); Assert.Equal(large + 20, rows.Rows[0].Totals.Known);
            Assert.Equal(large, (await hierarchy.ReadAsync(4, Cancellation)).Totals.Known);
        }
        else Assert.Equal(large, Assert.Single(await hierarchy.ReadBucketsAsync(1, Cancellation)).Totals.Known);
    }

    [Fact]
    public async Task AllMetricHierarchyUsesTheOriginalExhaustedSessionBudgetAfterFactoryClosure()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); AnalysisScratchArena? filler = null;
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, sessionLimit: 28 * 1024, afterSeal: workspace =>
        {
            filler = workspace.CreateArena(); filler.Append(new byte[checked((int)(28 * 1024 - workspace.ReservedBytes - 48))], Cancellation);
            Assert.Equal(28 * 1024, workspace.ReservedBytes);
        });
        using var reserved = filler!; await using var lease = await dataset.AcquireAsync(Cancellation);
        var error = await Assert.ThrowsAsync<AnalysisScratchLimitException>(() => lease.BuildHierarchyAsync(AnalysisMetric.FileSize, Cancellation));
        Assert.Equal(AnalysisScratchLimit.Budget, error.Limit);
        Assert.Equal(6UL, (await lease.ReadPathsAsync(0, cancellation: Cancellation)).Total);
        Assert.Equal((UInt128)220, (await lease.SummarizeAsync(AnalysisMetric.FileSize, Cancellation)).Totals.Known);
    }

    [Fact]
    public async Task HierarchyOwnsItsRowsAfterDatasetLeaseAndChargeIndexClose()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var charges = await lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation); await using var hierarchy = await charges.BuildHierarchyAsync(Cancellation);
        await charges.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
        Assert.Equal((UInt128)80, (await hierarchy.ReadAsync(1, Cancellation)).Totals.Known); Assert.Single(await hierarchy.ReadBucketsAsync(1, Cancellation));
        await hierarchy.DisposeAsync(); Assert.True(directory.Closed); await Assert.ThrowsAsync<ObjectDisposedException>(() => hierarchy.ReadAsync(1, Cancellation));
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task CanceledOrOverflowedAllMetricHierarchyLeavesSourceReadable(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) }, snapshot.Objects[1]],
            Facts = [snapshot.Facts[0] with { Measurement = AnalysisMeasurement.Known(UInt128.MaxValue) }, snapshot.Facts[1] with { Measurement = AnalysisMeasurement.Known(20) }] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.BuildHierarchyAsync(metric, stop.Token));
        await Assert.ThrowsAsync<OverflowException>(() => lease.BuildHierarchyAsync(metric, Cancellation));
        Assert.Equal(6UL, (await lease.ReadPathsAsync(0, cancellation: Cancellation)).Total);
        Assert.Equal((UInt128)3, (await lease.SummarizeAsync(AnalysisMetric.Files, Cancellation)).Totals.Known);
    }
}
