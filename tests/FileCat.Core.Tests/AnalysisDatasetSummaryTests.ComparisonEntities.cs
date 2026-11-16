using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    private sealed class ComparisonFactoryOwner(Func<ValueTask> close) : IAsyncDisposable
    {
        internal int Closes;
        public ValueTask DisposeAsync() { Closes++; return close(); }
    }

    [Fact]
    public async Task IndexedComparisonEntitiesFactoryPublishesOnlyAfterActualProducerCloseAndTransfersResultOwnership()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = new ComparisonFactoryOwner(async () => { entered.SetResult(); await release.Task; });
        var result = new ComparisonFactoryOwner(() => ValueTask.CompletedTask);
        var pending = AnalysisComparisonEntities.BuildAfterProducerCloseAsync(producer, () => Task.FromResult(result));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        try { Assert.False(pending.IsCompleted); Assert.Equal(0, result.Closes); }
        finally { release.TrySetResult(); }
        Assert.Same(result, await pending); Assert.Equal(1, producer.Closes); Assert.Equal(0, result.Closes);
        await result.DisposeAsync(); Assert.Equal(1, result.Closes);
    }

    [Fact]
    public async Task IndexedComparisonEntitiesFactoryWaitsForUnpublishedResultCleanupAfterProducerCloseFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("producer close failed");
        var producer = new ComparisonFactoryOwner(() => ValueTask.FromException(failure));
        var result = new ComparisonFactoryOwner(async () => { entered.SetResult(); await release.Task; });
        var pending = AnalysisComparisonEntities.BuildAfterProducerCloseAsync(producer, () => Task.FromResult(result));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        try { Assert.False(pending.IsCompleted); Assert.Equal(1, result.Closes); }
        finally { release.TrySetResult(); }
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => pending));
        Assert.Equal(1, producer.Closes); Assert.Equal(1, result.Closes);
    }

    [Fact]
    public async Task IndexedComparisonEntitiesFactoryRetainsBuildAndProducerCloseFailuresWithoutPublishing()
    {
        var build = new IOException("build failed"); var close = new IOException("producer close failed");
        var producer = new ComparisonFactoryOwner(() => ValueTask.FromException(close));
        var error = await Assert.ThrowsAsync<AggregateException>(() => AnalysisComparisonEntities.BuildAfterProducerCloseAsync<ComparisonFactoryOwner>(
            producer, () => Task.FromException<ComparisonFactoryOwner>(build)));
        Assert.Equal(new Exception[] { build, close }, error.Flatten().InnerExceptions); Assert.Equal(1, producer.Closes);
    }

    [Fact]
    public async Task IndexedComparisonEntitiesFactoryRetainsBothCloseFailuresAndAttemptsEachOwnerOnce()
    {
        var producerFailure = new IOException("producer close failed"); var resultFailure = new IOException("result close failed");
        var producer = new ComparisonFactoryOwner(() => ValueTask.FromException(producerFailure));
        var result = new ComparisonFactoryOwner(() => ValueTask.FromException(resultFailure));
        var error = await Assert.ThrowsAsync<AggregateException>(() => AnalysisComparisonEntities.BuildAfterProducerCloseAsync(producer, () => Task.FromResult(result)));
        Assert.Equal(new Exception[] { producerFailure, resultFailure }, error.Flatten().InnerExceptions);
        Assert.Equal(1, producer.Closes); Assert.Equal(1, result.Closes);
    }

    private static async Task CheckComparisonEntities(AnalysisSnapshot snapshot, AnalysisMetric metric, bool arbitraryIds = false)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(snapshot, io, arbitraryPathIds: arbitraryIds);
        await using var source = await dataset.AcquireAsync(Cancellation); await using var entities = await source.IndexComparisonEntitiesAsync(metric, Cancellation);
        snapshot = snapshot with { Freshness = entities.Descriptor.Freshness };
        var projection = AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation); Equal(projection.Totals, entities.Totals);
        using var expected = AnalysisComparison.Build(snapshot, snapshot, metric, cancellation: Cancellation);
        Assert.Equal((ulong)expected.Rows.Length, entities.Count); var seen = new HashSet<ulong>(); ulong offset = 0;
        while (true)
        {
            var page = await entities.ReadAsync(offset, 2, Cancellation);
            foreach (var row in page.Rows)
            {
                var members = new List<ulong>(); ulong at = 0;
                while (true)
                {
                    var aliases = await entities.ReadMembersAsync(row.Id, at, 1, Cancellation);
                    members.AddRange(aliases.Rows.Select(p => p.Id));
                    foreach (var alias in aliases.Rows)
                    { Assert.True(seen.Add(alias.Id)); Assert.Equal(row.Id, (await entities.FindPathAsync(alias.Id, Cancellation))!.Id); }
                    if (!aliases.HasMore) break; Assert.True(aliases.NextOffset > at); at = aliases.NextOffset;
                }
                var observation = Assert.Single(expected.Rows.Where(e => e.Current!.NodeIds.Order().SequenceEqual(members.Order()))).Current!;
                Assert.Equal(observation.Value, row.Value); Assert.Equal(observation.ObservedIdentity, row.ObservedIdentity);
                Assert.Equal(observation.VersionEvidence, row.VersionEvidence); Assert.Equal(observation.ModifiedUtc, row.ModifiedUtc);
                Assert.Equal(observation.Fresh, row.Fresh); Assert.Equal((ulong)members.Count, row.ObservedPaths);
                var charges = projection.ComparisonCharges.Where(c => row.DirectorySelf ? c.DirectorySelf && members.Contains(c.ParentId) :
                    !c.DirectorySelf && c.Aliases.Any(members.Contains)).ToArray();
                Assert.Equal(charges.Length == 0 ? null : (ulong?)charges.Last().ParentId, row.AttributionParent);
            }
            if (!page.HasMore) break; Assert.True(page.NextOffset > offset); offset = page.NextOffset;
        }
        Assert.Equal(expected.Rows.Sum(row => row.Current!.NodeIds.Length), seen.Count);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize, false)]
    [InlineData(AnalysisMetric.UniqueFileSize, false)]
    [InlineData(AnalysisMetric.OnDisk, false)]
    [InlineData(AnalysisMetric.Files, false)]
    [InlineData(AnalysisMetric.FileSize, true)]
    [InlineData(AnalysisMetric.UniqueFileSize, true)]
    [InlineData(AnalysisMetric.OnDisk, true)]
    [InlineData(AnalysisMetric.Files, true)]
    public async Task IndexedComparisonEntitiesMatchMemoryAccountingIncludingUnverifiedAndDirectoryComponents(AnalysisMetric metric, bool unverified)
    {
        var s = AnalysisAccountingTests.Golden();
        s = s with { Objects = [s.Objects[0] with { VerifiedIdentity = unverified ? null : s.Objects[0].VerifiedIdentity }, s.Objects[1]],
            Facts = s.Facts.Add(new("directory", null, 2, "directory-self", "fixture-v1", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await CheckComparisonEntities(s, metric);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonEntitiesReuseCoherenceAndIncludedComponentRulesWithArbitraryPathIds(AnalysisMetric metric)
    {
        var s = AnalysisAccountingTests.Golden();
        s = s with { Nodes = s.Nodes.Select(n => n with { Id = n.Id * 19, ParentId = n.ParentId * 19 }).ToImmutableArray(),
            Facts = s.Facts.Add(s.Facts[0] with { Id = "envelope", ComponentId = "all", Measurement = AnalysisMeasurement.Known(96), Includes = ["primary"] }) };
        s = s with { Nodes = s.Nodes.SetItem(4, s.Nodes[4] with { ObjectId = "X2" }),
            Objects = s.Objects.Add(s.Objects[0] with { Id = "X2", LogicalLength = AnalysisMeasurement.Known(101) }),
            Facts = s.Facts.Add(s.Facts[0] with { Id = "duplicate", ObjectId = "X2" }) };
        await CheckComparisonEntities(s, metric, arbitraryIds: true);
    }

    [Theory]
    [InlineData(MeasurementState.Pending)]
    [InlineData(MeasurementState.Excluded)]
    [InlineData(MeasurementState.Failed)]
    [InlineData(MeasurementState.Changed)]
    public async Task IndexedComparisonEntitiesPreserveMissingPriorityWithoutTurningKnownZeroIntoMissing(MeasurementState state)
    {
        var s = AnalysisAccountingTests.Golden();
        s = s with { Facts = [s.Facts[0] with { Measurement = AnalysisMeasurement.Known(0) },
            s.Facts[0] with { Id = "other-component", ComponentId = "fork", Measurement = AnalysisMeasurement.Missing(state) }, s.Facts[1]] };
        await CheckComparisonEntities(s, AnalysisMetric.OnDisk);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonEntitiesKeepIncompatibleRootAmountsAndUnknownEntriesOutOfKnownDeltas(AnalysisMetric metric)
    {
        var s = AnalysisAccountingTests.Golden();
        s = s with { Roots = s.Roots.Add(s.Roots[0] with { Id = "s", CanonicalLocator = "Other", DisplayLocator = "Other" }),
            Nodes = s.Nodes.Add(new(7, "s", 0, "Other", AnalysisKind.Directory)).Add(new(8, "s", 7, "other", AnalysisKind.File, "Q"))
                .Add(new(9, "r", 1, "unreadable", AnalysisKind.Unknown) { MetadataState = MeasurementState.Failed }),
            Objects = s.Objects.Add(new("Q", "other:Q", AnalysisMeasurement.Known(0), AnalysisMeasurement.Known(1))),
            Facts = s.Facts.Add(new("other", "Q", null, "primary", "different", AnalysisMeasurement.Known(10), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await CheckComparisonEntities(s, metric);
    }

    [Fact]
    public async Task IndexedComparisonEntitiesKeepKnownPerFileObservationsWhenCombinedAllocationBasesAreIncompatible()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var old = AnalysisAccountingTests.Golden();
        old = old with { Roots = old.Roots.Add(old.Roots[0] with { Id = "s", CanonicalLocator = "Other", DisplayLocator = "Other" }),
            Nodes = old.Nodes.Add(new(7, "s", 0, "Other", AnalysisKind.Directory)).Add(new(8, "s", 7, "other", AnalysisKind.File, "Q")),
            Objects = old.Objects.Add(new("Q", "other:Q", AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1))),
            Facts = old.Facts.Add(new("other", "Q", null, "primary", "different", AnalysisMeasurement.Known(10), "fixture", DateTimeOffset.UnixEpoch, [])) };
        var current = old with { Facts = old.Facts.SetItem(0, old.Facts[0] with { Measurement = AnalysisMeasurement.Known(96) }) };
        using var comparison = AnalysisComparison.Build(old, current, AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.False(comparison.Compatibility.SameBasis); Assert.Null(comparison.NetChange);
        var pair = Assert.Single(comparison.Rows.Where(row => row.Current!.NodeIds.Contains(4)));
        Assert.Equal(AnalysisMeasurement.Known(80), pair.Previous!.Value); Assert.Equal(AnalysisMeasurement.Known(96), pair.Current!.Value);
        Assert.Null(pair.Delta); Assert.Equal(AnalysisChange.Unknown, pair.Change);
        var projection = AnalysisAggregate.Project(current, AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.Empty(projection.Charges); Assert.False(projection.Totals.CompatibleBasis); Assert.Equal((UInt128)0, projection.Totals.Known);
        await using var dataset = await Build(current, io); await using var source = await dataset.AcquireAsync(Cancellation);
        await using var entities = await source.IndexComparisonEntitiesAsync(AnalysisMetric.OnDisk, Cancellation);
        Assert.False(entities.Totals.CompatibleBasis); Assert.Equal((UInt128)0, entities.Totals.Known);
        Assert.Equal(AnalysisMeasurement.Known(96), (await entities.FindPathAsync(4, Cancellation))!.Value);
        Assert.Equal(AnalysisMeasurement.Known(10), (await entities.FindPathAsync(8, Cancellation))!.Value);
        Assert.Equal(AnalysisMeasurement.Missing(MeasurementState.Unsupported), (await entities.FindPathAsync(6, Cancellation))!.Value);
    }

    [Fact]
    public async Task IndexedComparisonEntitiesRetainTwoOwnersAndBoundAdmissionUntilActualClose()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); var source = await dataset.AcquireAsync(Cancellation);
        var a = await source.IndexComparisonEntitiesAsync(AnalysisMetric.UniqueFileSize, Cancellation);
        var b = await source.IndexComparisonEntitiesAsync(AnalysisMetric.OnDisk, Cancellation);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => source.IndexComparisonEntitiesAsync(AnalysisMetric.Files, Cancellation));
            await source.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
            Assert.Equal(2UL, (await a.ReadAsync(cancellation: Cancellation)).Total); Assert.Equal(2UL, (await b.ReadAsync(cancellation: Cancellation)).Total);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => a.ReadAsync(3, cancellation: Cancellation));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => a.ReadMembersAsync(1, maximum: 513, cancellation: Cancellation));
        }
        finally { await a.DisposeAsync(); await b.DisposeAsync(); await source.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => a.ReadAsync(cancellation: Cancellation));
    }

    [Fact]
    public async Task IndexedComparisonEntitiesReleaseFailedAndActualCanceledConstructionBeforeRetry()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); await using var source = await dataset.AcquireAsync(Cancellation);
        directory.BeforeNextRead = () => throw new IOException("entity read failure");
        await Assert.ThrowsAsync<IOException>(() => source.IndexComparisonEntitiesAsync(AnalysisMetric.OnDisk, Cancellation));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); directory.BeforeNextRead = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.IndexComparisonEntitiesAsync(AnalysisMetric.Files, cancel.Token));
        Assert.True(cancel.IsCancellationRequested);
        await using var entities = await source.IndexComparisonEntitiesAsync(AnalysisMetric.UniqueFileSize, Cancellation);
        Assert.Equal(2UL, entities.Count); Assert.Null(await entities.FindPathAsync(1, Cancellation));
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonEntitiesPageLargeAliasSetsWithoutMaterializingAliasArrays(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden(); const int count = 14000;
        s = s with { Nodes = ImmutableArray.Create(s.Nodes[0]).AddRange(Enumerable.Range(0, count).Select(i =>
                new AnalysisNode((ulong)i + 2, "r", 1, "alias-" + i.ToString("D6"), AnalysisKind.File, "X"))),
            Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(count) }], Facts = [s.Facts[0]] };
        await using var dataset = await Build(s, io); await using var source = await dataset.AcquireAsync(Cancellation);
        await using var entities = await source.IndexComparisonEntitiesAsync(metric, Cancellation);
        Equal(AnalysisAggregate.Project(s, metric, cancellation: Cancellation).Totals, entities.Totals);
        bool shared = metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk;
        Assert.Equal(shared ? 1UL : (ulong)count, entities.Count);
        ulong observed = 0, offset = 0;
        while (true)
        {
            var page = await entities.ReadAsync(offset, 113, Cancellation);
            foreach (var row in page.Rows)
            {
                ulong at = 0;
                while (true)
                {
                    var aliases = await entities.ReadMembersAsync(row.Id, at, 113, Cancellation); observed += (ulong)aliases.Rows.Length;
                    Assert.InRange(aliases.Rows.Length, 1, 113);
                    if (!aliases.HasMore) break; Assert.True(aliases.NextOffset > at); at = aliases.NextOffset;
                }
                Assert.Equal(shared ? (ulong)count : 1UL, row.ObservedPaths); Assert.True(row.UniformPathMetadata); Assert.True(row.UniformObjectMetadata);
            }
            if (!page.HasMore) break; Assert.True(page.NextOffset > offset); offset = page.NextOffset;
        }
        Assert.Equal((ulong)count, observed);
    }

    [Fact]
    public async Task IndexedComparisonEntitiesRetainNonuniformMetadataAndNullableVersionEvidenceWithoutGuessing()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        s = s with { Nodes = s.Nodes.SetItem(3, s.Nodes[3] with { Sparse = true }).SetItem(4, s.Nodes[4] with { ObjectId = "X2", Sparse = false }),
            Objects = s.Objects.Add(s.Objects[0] with { Id = "X2", LinkCount = AnalysisMeasurement.Known(3), VersionEvidence = "v1" }),
            Facts = s.Facts.Add(s.Facts[0] with { Id = "f3", ObjectId = "X2" }) };
        await using var dataset = await Build(s, io); await using var source = await dataset.AcquireAsync(Cancellation);
        await using var entities = await source.IndexComparisonEntitiesAsync(AnalysisMetric.UniqueFileSize, Cancellation);
        var row = (await entities.FindPathAsync(4, Cancellation))!;
        Assert.False(row.UniformObjectMetadata); Assert.False(row.UniformPathMetadata); Assert.Null(row.VersionEvidence);
        Assert.Equal(AnalysisMeasurement.Known(100), row.Value); Assert.Equal(1UL, row.AttributionParent);
    }

    [Fact]
    public async Task IndexedComparisonEntitiesUseUtcInstantsAndIgnoreObservationTimeForMetadataUniformity()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden(); var instant = DateTimeOffset.UnixEpoch;
        s = s with { Nodes = s.Nodes.SetItem(3, s.Nodes[3] with { Times = new(instant, instant, 1) })
                .SetItem(4, s.Nodes[4] with { ObjectId = "X2", Times = new(instant, instant.ToOffset(TimeSpan.FromHours(3)), 1) }),
            Objects = [s.Objects[0] with { ModifiedUtc = instant }, s.Objects[1], s.Objects[0] with { Id = "X2", ModifiedUtc = instant.ToOffset(TimeSpan.FromHours(3)) }],
            Facts = s.Facts.Add(s.Facts[0] with { Id = "f3", ObjectId = "X2" }) };
        await using var dataset = await Build(s, io); await using var source = await dataset.AcquireAsync(Cancellation);
        await using var entities = await source.IndexComparisonEntitiesAsync(AnalysisMetric.OnDisk, Cancellation);
        var row = (await entities.FindPathAsync(4, Cancellation))!;
        Assert.True(row.UniformObjectMetadata); Assert.True(row.UniformPathMetadata); Assert.Equal(instant, row.ModifiedUtc);
    }
}
