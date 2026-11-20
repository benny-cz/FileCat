using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData(AnalysisMetric.FileSize, 100UL)]
    [InlineData(AnalysisMetric.UniqueFileSize, 100UL)]
    [InlineData(AnalysisMetric.OnDisk, 80UL)]
    [InlineData(AnalysisMetric.Files, 1UL)]
    public async Task ExplorerRetainsItsCapturedQueryAndPagesEvidenceAfterOriginalOwnersClose(AnalysisMetric metric, ulong known)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory);
        var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, metric, new("name:x.bin"), Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        Assert.False(directory.Closed); Assert.Equal(metric, explorer.Metric);
        Assert.Equal("name:x.bin", explorer.Filter!.Request.Text); Assert.Equal((UInt128)known, explorer.Totals.Known);
        Assert.True(await explorer.LoadPageAsync(1, maximum: 1, cancellation: Cancellation));
        var page = explorer.Page!; Assert.Equal(1UL, page.ParentId); Assert.Equal(1UL, page.Scope!.Path.Id);
        Assert.Single(page.Children.Rows); Assert.Empty(page.Ancestors); Assert.True(page.AncestorsComplete);
        if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
        {
            Assert.True(page.Children.HasMore); Assert.Equal(2UL, page.Children.Total);
            Assert.Equal((UInt128)known, Assert.Single(page.Buckets).Totals.Known);
            Assert.True(await explorer.LoadPageAsync(1, offset: page.Children.NextOffset, maximum: 1, cancellation: Cancellation));
            Assert.NotEqual(page.Children.Rows[0].Path.Id, explorer.Page!.Children.Rows[0].Path.Id);
            Assert.False(explorer.Page.Children.HasMore);
        }
        Assert.True(await explorer.InspectAsync(4, maximum: 1, cancellation: Cancellation));
        var inspector = explorer.Inspection!; Assert.True(inspector.Row.IsMatch); Assert.Equal("X", inspector.Object!.Id);
        Assert.Equal(new ulong[] { 1, 2 }, inspector.Ancestors.Select(n => n.Id)); Assert.True(inspector.AncestorsComplete);
        Assert.Equal(2UL, inspector.Aliases.Total); Assert.True(inspector.Aliases.HasMore);
        Assert.Single(inspector.Facts.Rows); Assert.Equal(0UL, inspector.Issues.Total);
        Assert.True(await explorer.InspectAsync(4, aliasOffset: inspector.Aliases.NextOffset, maximum: 1, cancellation: Cancellation));
        Assert.NotEqual(inspector.Aliases.Rows[0].Id, explorer.Inspection!.Aliases.Rows[0].Id);
        Assert.False(explorer.Inspection.Aliases.HasMore);
        await explorer.DisposeAsync(); Assert.True(directory.Closed);
        Assert.Null(explorer.Page); Assert.Null(explorer.Inspection);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => explorer.LoadPageAsync(cancellation: Cancellation));
    }

    [Fact]
    public async Task ExplorerDirectoryInspectorDistinguishesDirectoryChargesFromObjectAliases()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Facts = snapshot.Facts.Add(new("self", null, 2, "directory-self", "fixture-v1", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, [])),
            Issues = [new("fixture-issue", 2, MeasurementState.Failed)] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.True(await explorer.InspectAsync(2, cancellation: Cancellation));
        var inspector = explorer.Inspection!; Assert.Null(inspector.Object); Assert.Empty(inspector.Aliases.Rows);
        Assert.Equal(0UL, inspector.Aliases.Total); Assert.Equal("self", Assert.Single(inspector.Facts.Rows).Id);
        Assert.Equal("fixture-issue", Assert.Single(inspector.Issues.Rows).Code);
        Assert.True(await explorer.LoadPageAsync(2, cancellation: Cancellation));
        Assert.Equal(AnalysisHierarchyBucketKind.DirectoryAllocation, Assert.Single(explorer.Page!.Buckets).Kind);
        Assert.Equal(1UL, Assert.Single(explorer.Page.Ancestors).Id);
    }

    [Fact]
    public async Task ExplorerRejectsSupersededPagesAndInspectionsIndependently()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.True(await explorer.LoadPageAsync(cancellation: Cancellation));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); return true; }, Cancellation);
        Task<bool>? oldPage = null, newPage = null, oldInspection = null, newInspection = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            oldPage = explorer.LoadPageAsync(1, cancellation: Cancellation);
            oldInspection = explorer.InspectAsync(4, cancellation: Cancellation);
            newPage = explorer.LoadPageAsync(2, cancellation: Cancellation);
            newInspection = explorer.InspectAsync(6, cancellation: Cancellation);
            Assert.Equal(0UL, explorer.Page!.ParentId); Assert.Null(explorer.Inspection);
            Assert.False(newPage.IsCompleted); Assert.False(newInspection.IsCompleted);
        }
        finally { release.Set(); }
        await hold; Assert.False(await oldPage!); Assert.False(await oldInspection!);
        Assert.True(await newPage!); Assert.True(await newInspection!);
        Assert.Equal(2UL, explorer.Page!.ParentId); Assert.Equal(6UL, explorer.Inspection!.Row.Path.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplorerCloseWaitsForActualIoAndPreventsLatePublication(bool inspect)
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        var directory = new MemoryDirectory(); var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory);
        var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.OnDisk, cancellation: Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); return true; }, Cancellation);
        Task<bool>? request = null; Task? closing = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            request = inspect ? explorer.InspectAsync(4, cancellation: Cancellation) : explorer.LoadPageAsync(1, cancellation: Cancellation);
            closing = explorer.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
            Assert.False(directory.Closed); Assert.Null(explorer.Page); Assert.Null(explorer.Inspection);
        }
        finally { release.Set(); }
        await hold; Assert.False(await request!); await closing!; Assert.True(directory.Closed);
    }

    [Fact]
    public async Task ExplorerCanceledAndFailedRequestsKeepTheLastCompletePage()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.True(await explorer.LoadPageAsync(1, cancellation: Cancellation)); var original = explorer.Page;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => explorer.LoadPageAsync(1, offset: ulong.MaxValue, cancellation: Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => explorer.LoadPageAsync(4, cancellation: Cancellation));
        Assert.Same(original, explorer.Page);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); return true; }, Cancellation);
        Task<bool>? request = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            request = explorer.LoadPageAsync(2, cancellation: stop.Token); stop.Cancel();
            Assert.False(request.IsCompleted); Assert.Same(original, explorer.Page);
        }
        finally { release.Set(); }
        await hold; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request!);
        Assert.Same(original, explorer.Page);
    }

    [Fact]
    public async Task ExplorerBurstKeepsOnlyTheLatestAcceptedRequestAndBoundsPendingWork()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.Files, cancellation: Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); return true; }, Cancellation);
        var requests = new List<Task<bool>>(); ulong lastAccepted = 0;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            for (int i = 0; i < 128; i++)
            {
                ulong target = (ulong)(1 + i % 2);
                try { requests.Add(explorer.LoadPageAsync(target, cancellation: Cancellation)); lastAccepted = target; }
                catch (InvalidOperationException error) { Assert.StartsWith("Eight explorer requests", error.Message); }
                Assert.InRange(explorer.PendingRequests, 1, AnalysisLimits.IoRequests);
            }
            Assert.Null(explorer.Page);
        }
        finally { release.Set(); }
        await hold; var published = await Task.WhenAll(requests);
        Assert.Equal(1, published.Count(value => value)); Assert.Equal(lastAccepted, explorer.Page!.ParentId);
        Assert.Equal(0, explorer.PendingRequests);
    }

    [Fact]
    public async Task ExplorerBreadcrumbsHaveAnExplicitPayloadBoundWithoutChangingRawNames()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        string large = new('q', 60_000);
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>();
        for (ulong id = 1; id <= 4; id++) nodes.Add(new(id, "r", id - 1, large + id, AnalysisKind.Directory));
        nodes.Add(new(5, "r", 4, "payload.bin", AnalysisKind.File, "X"));
        snapshot = snapshot with { Nodes = nodes.ToImmutable(), Objects = [snapshot.Objects[0]], Facts = [snapshot.Facts[0]] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.True(await explorer.InspectAsync(5, cancellation: Cancellation));
        Assert.False(explorer.Inspection!.AncestorsComplete); Assert.Equal(2, explorer.Inspection.Ancestors.Length);
        Assert.Equal(new ulong[] { 3, 4 }, explorer.Inspection.Ancestors.Select(n => n.Id));
        Assert.All(explorer.Inspection.Ancestors, path => Assert.Equal(large + path.Id, path.Name));
    }
}
