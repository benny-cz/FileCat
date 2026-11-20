using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public async Task IndexedGroupSelectionCapturesExactDeduplicatedMemoryMembersForEveryMetricAndDimension(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation), dimension, false, Cancellation);
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(metric, dimension, cancellation: Cancellation);
        var all = await groups.CaptureMembersAsync(cancellation: Cancellation);
        Assert.Equal(tree.CaptureMembers(AnalysisTree.RootKey).Order(), all.NodeIds); Assert.Equal(all.NodeIds.Length, all.NodeIds.Distinct().Count());
        Assert.Equal(snapshot.Identity.SessionId, all.SessionId); Assert.Equal(snapshot.Identity.Epoch, all.Epoch); Assert.Equal(snapshot.Identity.Revision, all.Revision);
        Assert.Equal(AnalysisSelectionKind.RealGroupMembers, all.Kind); Assert.Empty(all.ObjectIds);
        foreach (var group in await AllGroups(groups))
        {
            var captured = await groups.CaptureMembersAsync(group.Definition.Key, cancellation: Cancellation);
            Assert.Equal(tree.CaptureMembers(group.Definition.Key).Order(), captured.NodeIds);
            Assert.Equal(group.Definition.Label, captured.ScopeSummary);
        }
        Assert.Equal(all.NodeIds, (await groups.CaptureMembersAsync(AnalysisTree.RootKey, cancellation: Cancellation)).NodeIds);
    }

    [Theory]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public async Task IndexedGroupSelectionIncludesOnlyMatchingSharedAliasesAndRetainsTheCapturedQuery(AnalysisGroupingDimension dimension)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisGroupingTests.Fixture(), io);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.UniqueFileSize, dimension, new("name:x.cs", CaseSensitive: true, RootId: "r"), Cancellation);
        var shared = Assert.Single(await AllGroups(groups), group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
        var captured = await groups.CaptureMembersAsync(shared.Definition.Key, cancellation: Cancellation);
        Assert.Equal(new ulong[] { 4 }, captured.NodeIds); Assert.Contains("name:x.cs", captured.FilterSummary); Assert.Contains("captured root r", captured.FilterSummary);
        Assert.Contains("case sensitive", captured.FilterSummary); Assert.DoesNotContain(5UL, captured.NodeIds);
        Assert.Equal(captured.NodeIds, (await groups.CaptureMembersAsync(cancellation: Cancellation)).NodeIds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task IndexedGroupSelectionLimitsCountDistinctPathsAcrossMemberAndReferencePlacements(int maximum)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisGroupingTests.Fixture(), io);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        if (maximum < 4) await Assert.ThrowsAsync<InvalidOperationException>(() => groups.CaptureMembersAsync(maximumPaths: maximum, cancellation: Cancellation));
        else Assert.Equal(new ulong[] { 2, 4, 5, 6 }, (await groups.CaptureMembersAsync(maximumPaths: maximum, cancellation: Cancellation)).NodeIds);
        var shared = Assert.Single(await AllGroups(groups), group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
        Assert.Equal(new ulong[] { 4, 5 }, (await groups.CaptureMembersAsync(shared.Definition.Key, maximumPaths: 2, cancellation: Cancellation)).NodeIds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => groups.CaptureMembersAsync(shared.Definition.Key, maximumPaths: 1, cancellation: Cancellation));
        Assert.Equal(0, AnalysisExplorerAdmission.Active);
    }

    [Fact]
    public async Task IndexedGroupSelectionCancellationAndCloseWaitForActualQueuedWorkAndReleaseAdmission()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        var directory = new MemoryDirectory(); var dataset = await Build(AnalysisGroupingTests.Fixture(), io, directory);
        var lease = await dataset.AcquireAsync(Cancellation); var groups = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); Task? hold = null, close = null; Task<AnalysisSelection>? capture = null;
        try
        {
            hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            capture = groups.CaptureMembersAsync(cancellation: stop.Token); Assert.Equal(1, AnalysisExplorerAdmission.Active);
            stop.Cancel(); close = groups.DisposeAsync().AsTask();
            Assert.False(capture.IsCompleted); Assert.False(close.IsCompleted); Assert.False(directory.Closed);
            release.Set(); await hold; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture); await close;
            Assert.Equal(0, AnalysisExplorerAdmission.Active); Assert.True(directory.Closed);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => groups.CaptureMembersAsync(cancellation: Cancellation));
        }
        finally { release.Set(); if (hold is not null) await hold; if (capture is not null) try { await capture; } catch (OperationCanceledException) { } await groups.DisposeAsync(); }
    }

    [Fact]
    public async Task IndexedGroupSelectionRemainsInertAndUsableAfterOriginalOwnersCloseAndRejectsForeignKeys()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory(); var dataset = await Build(AnalysisGroupingTests.Fixture(), io, directory);
        var lease = await dataset.AcquireAsync(Cancellation); var groups = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        try
        {
            var captured = await groups.CaptureMembersAsync(cancellation: Cancellation); Assert.Equal(new ulong[] { 4, 5, 6 }, captured.NodeIds);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => groups.CaptureMembersAsync("group:1:" + new string('0', 64), cancellation: Cancellation));
            await Assert.ThrowsAsync<InvalidDataException>(() => groups.CaptureMembersAsync("n:4", cancellation: Cancellation));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => groups.CaptureMembersAsync(maximumPaths: AnalysisLimits.ReviewPaths + 1, cancellation: Cancellation));
            Assert.Equal(new ulong[] { 4, 5, 6 }, captured.NodeIds); Assert.False(directory.Closed);
        }
        finally { await groups.DisposeAsync(); }
        Assert.True(directory.Closed);
    }
}
