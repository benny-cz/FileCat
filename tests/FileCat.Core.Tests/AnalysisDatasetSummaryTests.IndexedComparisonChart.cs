using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static IEnumerable<object[]> IndexedComparisonChartSides()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>()) foreach (bool previous in new[] { false, true }) yield return [metric, previous];
    }
    [Theory]
    [MemberData(nameof(IndexedComparisonChartSides))]
    public async Task IndexedComparisonChartUsesTheFullPositiveSideHierarchyAndExactEntityEvidence(AnalysisMetric metric, bool previous)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var (old, current) = QuerySamples();
        await using var a = await Build(old, io, enumeration: ComparisonEnumeration(old), arbitraryPathIds: true);
        await using var b = await Build(current, io, enumeration: ComparisonEnumeration(current), arbitraryPathIds: true);
        await using var before = await a.AcquireAsync(Cancellation); await using var now = await b.AcquireAsync(Cancellation);
        await using var result = await now.CompareIndexedAsync(before, metric, cancellation: Cancellation);
        await using var query = await result.QueryAsync(new() { Metric = metric, Query = "name:never-a-match" }, cancellation: Cancellation);
        Assert.Equal(0UL, query.Count);
        await using var chart = await result.BuildChartAsync(previous, Cancellation);
        var geometry = await chart.LoadAsync(cancellation: Cancellation); var snapshot = previous ? old : current;
        var projection = AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation);
        var expected = AnalysisTree.Build(snapshot, projection, false, Cancellation);
        Assert.Equal(previous, geometry.Previous); Assert.Equal(metric, geometry.Metric);
        Assert.Equal(expected.ScopeValue(AnalysisTree.RootKey), geometry.ScopeValue(geometry.Scope));
        foreach (var item in geometry.Items.Values)
        {
            Assert.Equal(expected.Items[item.Key].Value, item.Value);
            Assert.Equal(expected.Items[item.Key].IdentityUnverified, item.IdentityUnverified);
            Assert.Equal(expected.Items[item.Key].Unknown, item.Unknown);
            Assert.NotNull(item.ComparisonChange);
            Assert.Contains(AnalysisChartColors.Category(geometry, item, AnalysisColorDimension.Growth), AnalysisChartColors.Legend(AnalysisColorDimension.Growth));
            if (item.Kind == AnalysisKind.File || item.Family == "shared")
            {
                var row = await chart.RowForAsync(item, Cancellation); Assert.NotNull(row); Assert.Equal(row.Change, item.ComparisonChange);
                var entity = previous ? row.Previous : row.Current; Assert.NotNull(entity);
                if (item.Family == "shared") Assert.Equal(2UL, entity.ObservedPaths);
            }
            UInt128 sum = 0; foreach (var child in geometry.ChildrenOf(item.Key)) sum = checked(sum + child.Value);
            if (geometry.ChildrenOf(item.Key).Length > 0) Assert.Equal(item.Value, sum);
        }
        Assert.Equal(previous && expected.Items[AnalysisTree.Key(6)].Value > 0, geometry.Items.ContainsKey(AnalysisTree.Key(6)));
        Assert.Equal(!previous && expected.Items[AnalysisTree.Key(7)].Value > 0, geometry.Items.ContainsKey(AnalysisTree.Key(7)));
        if (metric is AnalysisMetric.FileSize or AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
        {
            var growing = geometry.Items.Values.First(item => metric == AnalysisMetric.FileSize ? item.Kind == AnalysisKind.File && item.NodeId == 4 : item.Family == "shared");
            Assert.Equal(AnalysisChange.Growing, growing.ComparisonChange);
            Assert.Equal("growth-up", AnalysisChartColors.Category(geometry, growing, AnalysisColorDimension.Growth).Key);
        }
        var branch = await chart.LoadAsync(AnalysisTree.Key(2), Cancellation);
        Assert.Equal(expected.ScopeValue(AnalysisTree.Key(2)), branch.ScopeValue(branch.Scope));
        Assert.Equal(AnalysisTree.Key(1), branch.ParentOf(branch.Scope));
    }
    [Fact]
    public async Task IndexedComparisonChartRetainsInputsAndActualCloseAfterComparisonOwnerCloses()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = ComparisonSample();
        await using var a = await Build(snapshot, io, enumeration: ComparisonEnumeration(snapshot)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, AnalysisMetric.OnDisk, cancellation: Cancellation);
        await before.DisposeAsync(); await a.DisposeAsync();
        var chart = await result.BuildChartAsync(false, Cancellation);
        try
        {
            var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
            var projection = await chart.LoadAsync(cancellation: Cancellation);
            var bucket = Assert.Single(projection.Items.Values, item => item.Family == "shared");
            Assert.Equal(2UL, (await chart.RowForAsync(bucket, Cancellation))!.Current!.ObservedPaths);
            await chart.DisposeAsync(); await closing;
            await Assert.ThrowsAsync<ObjectDisposedException>(() => chart.LoadAsync(cancellation: Cancellation));
        }
        finally { await chart.DisposeAsync(); await result.DisposeAsync(); await before.DisposeAsync(); await a.DisposeAsync(); }
    }
    private static async Task DiscardUnexpectedChartAsync(Task<AnalysisIndexedComparison.Chart> request) { await using var chart = await request; }
    [Fact]
    public async Task IndexedComparisonChartFailureCancellationAndSharedPlaceRefusalAllowRetry()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); var storage = new MemoryDirectory();
        await using var a = await Build(s, io, storage, enumeration: ComparisonEnumeration(s)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, AnalysisMetric.UniqueFileSize, cancellation: Cancellation);
        storage.BeforeNextRead = () => throw new IOException("chart read failure");
        await Assert.ThrowsAsync<IOException>(() => DiscardUnexpectedChartAsync(result.BuildChartAsync(false, Cancellation)));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); storage.BeforeNextRead = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiscardUnexpectedChartAsync(result.BuildChartAsync(false, cancel.Token)));
        await using (var chart = await result.BuildChartAsync(false, Cancellation))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DiscardUnexpectedChartAsync(result.BuildChartAsync(true, Cancellation)));
            var applied = await chart.LoadAsync(cancellation: Cancellation);
            storage.BeforeNextRead = () => throw new IOException("chart page failure");
            await Assert.ThrowsAsync<IOException>(() => chart.LoadAsync(AnalysisTree.Key(2), Cancellation));
            Assert.Equal((UInt128)120, applied.ScopeValue(applied.Scope));
            Assert.Equal((UInt128)120, (await chart.LoadAsync(cancellation: Cancellation)).ScopeValue(AnalysisTree.RootKey));
        }
        await using var retry = await result.BuildChartAsync(true, Cancellation);
        Assert.True((await retry.LoadAsync(cancellation: Cancellation)).Previous);
    }
    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonChartBoundsLargeGeometryAndOpensExactRemainders(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); const int count = 14000;
        s = s with { Nodes = ImmutableArray.Create(s.Nodes[0]).AddRange(Enumerable.Range(0, count).Select(i => new AnalysisNode((ulong)i + 10, "r", 1, "alias-" + i, AnalysisKind.File, "X"))),
            Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(count) }], Facts = [s.Facts[0]] };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s), arbitraryPathIds: true); await using var input = await a.AcquireAsync(Cancellation);
        await using var result = await input.CompareIndexedAsync(input, metric, cancellation: Cancellation);
        await using var chart = await result.BuildChartAsync(false, Cancellation);
        var root = await chart.LoadAsync(AnalysisTree.Key(1), Cancellation);
        Assert.InRange(root.Items.Count, 1, AnalysisLimits.ChartLeaves);
        if (metric is AnalysisMetric.FileSize or AnalysisMetric.Files)
        {
            var other = Assert.Single(root.ChildrenOf(root.Scope), item => item.Synthetic && item.Family == "other");
            var suffix = await chart.LoadAsync(other.Key, Cancellation);
            Assert.Equal(other.Value, suffix.ScopeValue(suffix.Scope));
            var nested = Assert.Single(suffix.ChildrenOf(suffix.Scope), item => item.Synthetic && item.Family == "other");
            var next = await chart.LoadAsync(nested.Key, Cancellation); Assert.Equal(nested.Value, next.ScopeValue(next.Scope));
            Assert.True(next.ScopeValue(next.Scope) < suffix.ScopeValue(suffix.Scope));
        }
        else
        {
            var shared = Assert.Single(root.ChildrenOf(root.Scope), item => item.Family == "shared");
            Assert.Equal((ulong)count, (await chart.RowForAsync(shared, Cancellation))!.Current!.ObservedPaths);
            Assert.Equal(metric == AnalysisMetric.OnDisk ? (UInt128)80 : 100, shared.Value);
        }
    }
    [Fact]
    public async Task IndexedComparisonChartDirectorySelfKeepsItsSeparateAreaAndComparisonEvidence()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        s = s with { Facts = s.Facts.Add(new("directory", null, 2, "directory-self", "fixture-v1", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var input = await a.AcquireAsync(Cancellation);
        await using var result = await input.CompareIndexedAsync(input, AnalysisMetric.OnDisk, cancellation: Cancellation);
        await using var chart = await result.BuildChartAsync(false, Cancellation); var geometry = await chart.LoadAsync(cancellation: Cancellation);
        var bucket = Assert.Single(geometry.Items.Values, item => item.Family == "directory-allocation");
        Assert.Equal((UInt128)32, bucket.Value); Assert.True((await chart.RowForAsync(bucket, Cancellation))!.Current!.DirectorySelf);
        Assert.Equal(AnalysisChange.NoObservedMetadataChange, bucket.ComparisonChange);
    }
    [Fact]
    public async Task IndexedComparisonChartIncompatibleBasesKeepRecordedRootsSeparateWithoutACombinedArea()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        s = s with { Roots = s.Roots.Add(s.Roots[0] with { Id = "s", Label = "Other", CanonicalLocator = "Other", DisplayLocator = "Other" }),
            Nodes = s.Nodes.Add(new(7, "s", 0, "Other", AnalysisKind.Directory)).Add(new(8, "s", 7, "q", AnalysisKind.File, "Q")),
            Objects = s.Objects.Add(new("Q", "other:Q", AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1))),
            Facts = s.Facts.Add(new("other-fact", "Q", null, "primary", "different-basis", AnalysisMeasurement.Known(512), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var input = await a.AcquireAsync(Cancellation);
        await using var result = await input.CompareIndexedAsync(input, AnalysisMetric.OnDisk, cancellation: Cancellation);
        await using var chart = await result.BuildChartAsync(false, Cancellation);
        Assert.False(chart.CombinedAreaAvailable); var roots = await chart.ReadRootsAsync(Cancellation); Assert.Equal(2, roots.Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chart.LoadAsync(cancellation: Cancellation));
        foreach (var root in roots)
        {
            var geometry = await chart.LoadAsync(AnalysisTree.Key(root.Id), Cancellation);
            Assert.Equal(root.Id == 1 ? (UInt128)80 : 512, geometry.ScopeValue(geometry.Scope));
            Assert.Equal(AnalysisTree.RootKey, geometry.ParentOf(geometry.Scope));
        }
    }
}
