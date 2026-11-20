using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Fact]
    public async Task EightSeparateExplorersShareAdmissionAndCancellationKeepsItThroughActualQueuedWork()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        await using var fixture = await ExplorerAdmissionFixture.CreateAsync(io, 9);
        var explorers = new List<AnalysisDatasetExplorer>();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task? hold = null; Task? closing = null; var requests = new List<Task<bool>>();
        try
        {
            for (int i = 0; i < 8; i++) explorers.Add(await AnalysisDatasetExplorer.CreateAsync(fixture.Leases[i], AnalysisMetric.Files, cancellation: Cancellation));
            hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            for (int i = 0; i < 8; i++) requests.Add(explorers[i].LoadPageAsync(cancellation: i == 0 ? stop.Token : Cancellation));
            Assert.Equal(8, AnalysisExplorerAdmission.Active); Assert.All(explorers, e => Assert.Equal(1, e.PendingRequests));
            Assert.Throws<InvalidOperationException>(() => { _ = explorers[1].InspectAsync(1, cancellation: Cancellation); });
            await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisDatasetExplorer.CreateAsync(fixture.Leases[8], AnalysisMetric.Files, cancellation: Cancellation));
            stop.Cancel(); closing = explorers[0].DisposeAsync().AsTask();
            Assert.False(requests[0].IsCompleted); Assert.False(closing.IsCompleted); Assert.Equal(8, AnalysisExplorerAdmission.Active);
            Assert.Throws<InvalidOperationException>(() => { _ = explorers[1].LoadChartAsync(cancellation: Cancellation); });
            release.Set(); await hold;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requests[0]); await closing;
            Assert.All(await Task.WhenAll(requests.Skip(1)), Assert.True); Assert.Equal(0, AnalysisExplorerAdmission.Active);
            await using var recovered = await AnalysisDatasetExplorer.CreateAsync(fixture.Leases[8], AnalysisMetric.Files, cancellation: Cancellation);
            Assert.True(await recovered.LoadPageAsync(cancellation: Cancellation)); Assert.Equal(0, AnalysisExplorerAdmission.Active);
        }
        finally
        {
            release.Set(); if (hold is not null) await hold;
            foreach (var explorer in explorers) await explorer.DisposeAsync();
            foreach (var request in requests) try { await request; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task ExplorerCreationUsesTheSameAdmissionAsExistingPageRequestsUntilRetainReturns()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        await using var fixture = await ExplorerAdmissionFixture.CreateAsync(io, 9);
        await using var existing = await AnalysisDatasetExplorer.CreateAsync(fixture.Leases[8], AnalysisMetric.Files, cancellation: Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var creates = new List<Task<AnalysisDatasetExplorer>>(); var created = new List<AnalysisDatasetExplorer>(); Task? hold = null;
        try
        {
            hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            for (int i = 0; i < 8; i++) creates.Add(AnalysisDatasetExplorer.CreateAsync(fixture.Leases[i], AnalysisMetric.Files, cancellation: i == 0 ? stop.Token : Cancellation));
            Assert.Equal(8, AnalysisExplorerAdmission.Active);
            Assert.Throws<InvalidOperationException>(() => { _ = existing.LoadPageAsync(cancellation: Cancellation); }); Assert.Equal(0, existing.PendingRequests);
            await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisDatasetExplorer.CreateAsync(fixture.Leases[8], AnalysisMetric.Files, cancellation: Cancellation));
            stop.Cancel(); Assert.False(creates[0].IsCompleted); Assert.Equal(8, AnalysisExplorerAdmission.Active);
            release.Set(); await hold;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creates[0]);
            created.AddRange(await Task.WhenAll(creates.Skip(1))); Assert.Equal(0, AnalysisExplorerAdmission.Active);
            Assert.True(await existing.LoadPageAsync(cancellation: Cancellation));
        }
        finally
        {
            release.Set(); if (hold is not null) await hold;
            foreach (var create in creates)
                try { var explorer = await create; if (!created.Contains(explorer)) created.Add(explorer); } catch (OperationCanceledException) { }
            foreach (var explorer in created) await explorer.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailedAndPreCanceledExplorerWorkReturnsAdmissionWithoutChangingPublishedResults()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var fixture = await ExplorerAdmissionFixture.CreateAsync(io, 1);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnalysisDatasetExplorer.CreateAsync(fixture.Leases[0], AnalysisMetric.Files, cancellation: stop.Token));
        for (int i = 0; i < 16; i++)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AnalysisDatasetExplorer.CreateAsync(fixture.Leases[0], (AnalysisMetric)int.MaxValue, cancellation: Cancellation));
            Assert.Equal(0, AnalysisExplorerAdmission.Active);
        }
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(fixture.Leases[0], AnalysisMetric.Files, cancellation: Cancellation);
        Assert.True(await explorer.LoadPageAsync(1, cancellation: Cancellation)); var original = explorer.Page;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => explorer.LoadPageAsync(1, offset: ulong.MaxValue, cancellation: Cancellation));
        Assert.ThrowsAny<OperationCanceledException>(() => { _ = explorer.InspectAsync(1, cancellation: stop.Token); });
        Assert.Same(original, explorer.Page); Assert.Equal(0, explorer.PendingRequests); Assert.Equal(0, AnalysisExplorerAdmission.Active);
        Assert.True(await explorer.LoadChartAsync(cancellation: Cancellation)); Assert.True(await explorer.InspectAsync(1, cancellation: Cancellation));
        Assert.Equal(0, AnalysisExplorerAdmission.Active);
    }

    private sealed class ExplorerAdmissionFixture : IAsyncDisposable
    {
        private readonly List<AnalysisDataset> _datasets = [];
        private readonly List<MemoryDirectory> _directories = [];
        internal readonly List<AnalysisDatasetLease> Leases = [];
        internal static async Task<ExplorerAdmissionFixture> CreateAsync(DeviceIoScheduler io, int count)
        {
            var fixture = new ExplorerAdmissionFixture();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var directory = new MemoryDirectory(); fixture._directories.Add(directory);
                    var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); fixture._datasets.Add(dataset);
                    fixture.Leases.Add(await dataset.AcquireAsync(Cancellation));
                }
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var lease in Leases) await lease.DisposeAsync();
            foreach (var dataset in _datasets) await dataset.DisposeAsync();
            Assert.All(_directories, directory => Assert.True(directory.Closed));
        }
    }
}
