using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetTests
{
    [Fact]
    public async Task SeparateWritersShareQueuedBytesAndClosureWaitsForEveryAcceptedBatch()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1); var header = Header();
        await using var a = await AnalysisObservationWriter.CreateAsync(io, Device, () => new MemoryDirectory(), header.Identity, header.Roots, header.Policy, Cancellation);
        await using var b = await AnalysisObservationWriter.CreateAsync(io, Device, () => new MemoryDirectory(), header.Identity, header.Roots, header.Policy, Cancellation);
        foreach (var writer in new[] { a, b }) await writer.AppendBatchAsync([new(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis")], Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation);
        var writes = new List<Task<ImmutableArray<ulong>>>(); Task? closing = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            for (int i = 0; i < 8; i++)
            {
                var batch = FullBatch(i); Assert.Equal(AnalysisLimits.BatchBytes, AnalysisObservationWriter.PayloadBytes(batch));
                writes.Add((i % 2 == 0 ? a : b).AppendBatchAsync(batch, Cancellation));
            }
            Assert.Equal(AnalysisPendingPayload.Writes.Limit, AnalysisPendingPayload.Writes.ReservedBytes);
            await Assert.ThrowsAsync<IOException>(() => b.AppendBatchAsync(FullBatch(8), Cancellation));
            closing = a.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
            Assert.Equal(AnalysisPendingPayload.Writes.Limit, AnalysisPendingPayload.Writes.ReservedBytes);
        }
        finally { release.Set(); }
        await hold; Assert.All(await Task.WhenAll(writes), batch => Assert.Equal(256, batch.Length)); await closing!;
        Assert.Equal(0, AnalysisPendingPayload.Writes.ReservedBytes);
        Assert.Single(await b.AppendBatchAsync([new(Entry("after-drain", identity: null), 1, "r", "basis")], Cancellation));
    }
    [Fact]
    public async Task QueuedCaptureDoesNotRetainLiveReadWitnessesAndCancellationKeepsItsByteChargeUntilActualReturn()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1); var header = Header();
        await using var writer = await AnalysisObservationWriter.CreateAsync(io, Device, () => new MemoryDirectory(), header.Identity, header.Roots, header.Policy, Cancellation);
        await writer.AppendBatchAsync([new(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis")], Cancellation);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation);
        Task<ImmutableArray<ulong>>? queued = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            var submitted = QueueWitness(writer, stop.Token); queued = submitted.Task;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Assert.False(submitted.Witness.IsAlive);
            long reserved = AnalysisPendingPayload.Writes.ReservedBytes; Assert.True(reserved > 0);
            stop.Cancel(); Assert.False(queued.IsCompleted); Assert.Equal(reserved, AnalysisPendingPayload.Writes.ReservedBytes);
        }
        finally { release.Set(); }
        await hold; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued!); Assert.Equal(0, AnalysisPendingPayload.Writes.ReservedBytes);
    }
    [Fact]
    public async Task OversizedEncodingAndIssueStringsAreRejectedBeforeChangingCaptureOrQueueAdmission()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var header = Header();
        await using var writer = await AnalysisObservationWriter.CreateAsync(io, Device, () => new MemoryDirectory(), header.Identity, header.Roots, header.Policy, Cancellation);
        await writer.AppendBatchAsync([new(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis")], Cancellation);
        foreach (var entry in new[] { Entry("small") with { NameEncoding = new('x', AnalysisLimits.BatchBytes) }, Entry("small") with { IssueCode = new('x', AnalysisLimits.BatchBytes) } })
            await Assert.ThrowsAsync<ArgumentException>(() => writer.AppendBatchAsync([new(entry, 1, "r", "basis")], Cancellation));
        Assert.Equal(0, AnalysisPendingPayload.Writes.ReservedBytes);
        Assert.Equal(2UL, Assert.Single(await writer.AppendBatchAsync([new(Entry("accepted", identity: null), 1, "r", "basis")], Cancellation)));
    }
    private static ImmutableArray<AnalysisObservationItem> FullBatch(int batch)
    {
        var rows = ImmutableArray.CreateBuilder<AnalysisObservationItem>(256);
        for (int i = 0; i < 256; i++)
        {
            string prefix = $"{batch:D2}-{i:D3}-"; string name = prefix.PadRight(245, 'x');
            rows.Add(new(Entry(name, identity: null), 1, "r", "basis"));
        }
        return rows.ToImmutable();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<ImmutableArray<ulong>> Task, WeakReference Witness) QueueWitness(AnalysisObservationWriter writer, CancellationToken cancellation)
    {
        var witness = new AnalysisAncestry(Guid.NewGuid(), ImmutableArray.Create(new byte[128 * 1024]), "identity", "v", null);
        var entry = Entry("detached", identity: null) with { RelativeName = witness.RelativeName, Ancestry = witness };
        return (writer.AppendBatchAsync([new(entry, 1, "r", "basis")], cancellation), new WeakReference(witness));
    }
}
