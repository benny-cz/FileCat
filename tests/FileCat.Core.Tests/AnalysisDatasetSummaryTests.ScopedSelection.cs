using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    private static readonly ImmutableHashSet<ulong> ScopeMarks = [1, 2, 3, 4, 5, 6, 999];
    [Theory]
    [InlineData(AnalysisGroupingDimension.FileType, false)]
    [InlineData(AnalysisGroupingDimension.FileType, true)]
    [InlineData(AnalysisGroupingDimension.Extension, false)]
    [InlineData(AnalysisGroupingDimension.Extension, true)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge, false)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge, true)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation, false)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation, true)]
    public async Task CapturedScopeGroupCountsSeparateFiltersOtherGroupsMissingAndDirectoryContext(AnalysisGroupingDimension dimension, bool filtered)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.UniqueFileSize, dimension, filtered ? new("name:x.cs", RootId: "r") : null, Cancellation);
        var root = await groups.ClassifyMarksAsync(ScopeMarks, cancellation: Cancellation);
        Assert.Equal(new(snapshot.Identity, 7, filtered ? 1 : 3, filtered ? 2 : 3, filtered ? 3 : 0, 1), root);
        var shared = Assert.Single(await AllGroups(groups), group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
        var detail = await groups.ClassifyMarksAsync(ScopeMarks, shared.Definition.Key, Cancellation);
        Assert.Equal(filtered ? 1 : 2, detail.InScope); Assert.Equal(filtered ? 2 : 4, detail.Elsewhere); Assert.Equal(filtered ? 3 : 0, detail.HiddenByFilter); Assert.Equal(1, detail.Missing);
        Assert.Equal(detail.Total, detail.InScope + detail.Elsewhere + detail.HiddenByFilter + detail.Missing);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => groups.ClassifyMarksAsync(ScopeMarks, "group:1:" + new string('0', 64), Cancellation));
        await Assert.ThrowsAsync<InvalidDataException>(() => groups.ClassifyMarksAsync(ScopeMarks, "n:4", Cancellation));
    }

    [Fact]
    public async Task CapturedScopeDirectoryReferencesAndEmptyFoldersAreNotMistakenForFilteredOutFiles()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        snapshot = snapshot with { Nodes = snapshot.Nodes.Add(new(7, "r", 1, "empty", AnalysisKind.Directory)) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var all = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        var marks = ScopeMarks.Remove(999).Add(7); var directory = Assert.Single(await AllGroups(all), group => group.Kind == AnalysisGroupKind.DirectoryAllocation);
        Assert.Equal(new(snapshot.Identity, 7, 1, 6, 0, 0), await all.ClassifyMarksAsync(marks, directory.Definition.Key, Cancellation));
        Assert.Equal(new(snapshot.Identity, 7, 4, 3, 0, 0), await all.ClassifyMarksAsync(marks, cancellation: Cancellation));
        await using var filtered = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, new("name:no-such-file"), Cancellation);
        Assert.Equal(new(snapshot.Identity, 7, 0, 0, 7, 0), await filtered.ClassifyMarksAsync(marks, cancellation: Cancellation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedScopeHierarchyCountsUseExactRecordedAncestryAndMissingIds(bool filtered)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, new(filtered ? "name:x.cs" : ""), Cancellation);
        var counts = await hierarchy.ClassifyMarksAsync(ScopeMarks, 2, Cancellation);
        Assert.Equal(new(snapshot.Identity, 7, filtered ? 2 : 3, filtered ? 1 : 3, filtered ? 3 : 0, 1), counts);
        var top = await hierarchy.ClassifyMarksAsync(ScopeMarks, cancellation: Cancellation);
        Assert.Equal(filtered ? 3 : 6, top.InScope); Assert.Equal(0, top.Elsewhere); Assert.Equal(1, top.Missing);
        await Assert.ThrowsAsync<ArgumentException>(() => hierarchy.ClassifyMarksAsync([0], cancellation: Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchy.ClassifyMarksAsync(ScopeMarks, 4, Cancellation));
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task CapturedScopeLocateAndRangesMatchEverySortedHierarchyRow(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisGroupingTests.Fixture(), io);
        await using var lease = await dataset.AcquireAsync(Cancellation); await using var hierarchy = await lease.BuildHierarchyAsync(metric, cancellation: Cancellation);
        foreach (var sort in Enum.GetValues<AnalysisHierarchySort>())
        foreach (bool descending in new[] { false, true })
        foreach (ulong parent in new ulong[] { 0, 1, 2, 3 })
        {
            var rows = await hierarchy.ReadChildrenAsync(parent, sort, maximum: 16, cancellation: Cancellation, descending: descending);
            Assert.False(rows.HasMore);
            for (int index = 0; index < rows.Rows.Length; index++)
            {
                var position = await hierarchy.LocateChildAsync(rows.Rows[index].Path.Id, sort, descending, Cancellation);
                Assert.Equal(parent, position.ParentId); Assert.Equal((ulong)index, position.Offset); Assert.Equal(rows.Total, position.Total);
            }
            if (rows.Rows.IsEmpty) continue;
            ulong last = (ulong)rows.Rows.Length - 1;
            var captured = await hierarchy.CaptureTableRangeAsync(parent, 0, last, sort, descending, Cancellation);
            Assert.Equal(rows.Rows.Select(row => row.Path.Id), captured.Selection.NodeIds); Assert.Equal(0UL, captured.SyntheticRows);
            Assert.Equal(AnalysisSelectionKind.ExplicitPaths, captured.Selection.Kind); Assert.Equal(hierarchy.Descriptor.Identity.Revision, captured.Selection.Revision);
            Assert.Equal(rows.Rows[^1].Path.Id, Assert.Single((await hierarchy.CaptureTableRangeAsync(parent, last, last, sort, descending, Cancellation)).Selection.NodeIds));
        }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => hierarchy.LocateChildAsync(999, cancellation: Cancellation));
    }

    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public async Task CapturedScopeGroupRangesMatchSortedMemberPagesWithoutExpandingSyntheticGroups(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisGroupingTests.Fixture(), io);
        await using var lease = await dataset.AcquireAsync(Cancellation); await using var groups = await lease.BuildGroupingAsync(metric, dimension, cancellation: Cancellation);
        var all = await AllGroups(groups);
        foreach (var sort in Enum.GetValues<AnalysisTableSort>())
        foreach (bool descending in new[] { false, true })
        {
            var overview = await groups.CaptureTableRangeAsync(null, 0, groups.Count - 1, sort, descending, Cancellation);
            Assert.Empty(overview.Selection.NodeIds); Assert.Equal(groups.Count, overview.SyntheticRows);
            foreach (var group in all)
            {
                if (group.Members == 0) continue;
                var rows = await groups.ReadMemberRowsAsync(group.Definition.Key, maximum: 16, sort: sort, descending: descending, cancellation: Cancellation);
                Assert.False(rows.HasMore); ulong first = rows.Rows.Length > 1 ? 1UL : 0UL, last = rows.Total - 1;
                var captured = await groups.CaptureTableRangeAsync(group.Definition.Key, first, last, sort, descending, Cancellation);
                Assert.Equal(rows.Rows.Skip((int)first).Select(row => row.Member.Path.Id), captured.Selection.NodeIds);
                Assert.Equal(0UL, captured.SyntheticRows); Assert.Equal(groups.Descriptor.Identity.SessionId, captured.Selection.SessionId);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedScopeCanceledCountsAndRangesRetainActualQueuedWorkAndCloseAdmission(bool grouped)
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1); var directory = new MemoryDirectory();
        var dataset = await Build(AnalysisGroupingTests.Fixture(), io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var groups = grouped ? await lease.BuildGroupingAsync(AnalysisMetric.FileSize, AnalysisGroupingDimension.Extension, cancellation: Cancellation) : null;
        var hierarchy = grouped ? null : await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, cancellation: Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task? hold = null, close = null; Task<AnalysisSelectionVisibility>? counts = null; Task<AnalysisCapturedTableRange>? range = null;
        try
        {
            hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation); Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            counts = groups is not null ? groups.ClassifyMarksAsync(ScopeMarks, cancellation: stop.Token) : hierarchy!.ClassifyMarksAsync(ScopeMarks, cancellation: stop.Token);
            range = groups is not null ? groups.CaptureTableRangeAsync(null, 0, 0, cancellation: stop.Token) : hierarchy!.CaptureTableRangeAsync(0, 0, 0, cancellation: stop.Token);
            Assert.Equal(2, AnalysisExplorerAdmission.Active); stop.Cancel(); close = groups is not null ? groups.DisposeAsync().AsTask() : hierarchy!.DisposeAsync().AsTask();
            Assert.False(counts.IsCompleted); Assert.False(range.IsCompleted); Assert.False(close.IsCompleted); Assert.False(directory.Closed);
            release.Set(); await hold; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => counts); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => range); await close;
            Assert.True(directory.Closed); Assert.Equal(0, AnalysisExplorerAdmission.Active);
        }
        finally
        {
            release.Set(); if (hold is not null) await hold;
            foreach (var task in new Task?[] { counts, range }) if (task is not null) try { await task; } catch (OperationCanceledException) { }
            if (groups is not null) await groups.DisposeAsync(); if (hierarchy is not null) await hierarchy.DisposeAsync();
        }
    }

    [Fact]
    public async Task CapturedScopeEmptyFilterKeepsExcludedNamespaceRowsVisible()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        snapshot = snapshot with { Nodes = snapshot.Nodes.Add(new(7, "r", 2, "link", AnalysisKind.Link)) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var all = await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, new(""), Cancellation);
        Assert.Contains((await all.ReadChildrenAsync(2, cancellation: Cancellation)).Rows, row => row.Path.Id == 7);
        Assert.Equal(1, (await all.ClassifyMarksAsync([7], 2, Cancellation)).InScope);
        await using var filtered = await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, new("name:x.cs"), Cancellation);
        Assert.Equal(1, (await filtered.ClassifyMarksAsync([7], 2, Cancellation)).HiddenByFilter);
    }

    [Fact]
    public async Task CapturedScopeVisibilityRemainsAvailableAfterOriginalOwnersCloseWithoutAnySourceConnection()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory(); var dataset = await Build(AnalysisGroupingTests.Fixture(), io, directory);
        var lease = await dataset.AcquireAsync(Cancellation); var groups = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        try { Assert.Equal(3, (await groups.ClassifyMarksAsync(ScopeMarks, cancellation: Cancellation)).InScope); Assert.False(directory.Closed); }
        finally { await groups.DisposeAsync(); }
        Assert.True(directory.Closed); await Assert.ThrowsAsync<ObjectDisposedException>(() => groups.ClassifyMarksAsync(ScopeMarks, cancellation: Cancellation));
    }
}
