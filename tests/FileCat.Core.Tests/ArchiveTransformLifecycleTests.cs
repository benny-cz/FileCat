using FileCat.Core.Archives;
using Scripted = FileCat.Core.Tests.ArchiveAssessmentClientTests.Scripted;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveTransformLifecycleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ArchiveTransformInvocation Empty() => ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(null, [], [], [], Ct), Ct);
    private static byte[] Completed(Guid nonce, ArchiveTransformInvocation invocation, bool wrong = false, bool trailing = false)
    {
        using var bytes = new MemoryStream(); var wire = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes);
        wire.Write(bytes, ArchiveWorkerMessage.Hello, ArchiveTransformWire.Hello); wire.Write(bytes, ArchiveWorkerMessage.Write, new byte[1032]);
        var expected = invocation.Expected;
        string digest = wrong ? new('B', 64) : expected.Digest;
        new ArchiveWorkerRecords(wire, bytes, writing: true).Write(ArchiveControlRecord.Receipt, ArchiveTransformWire.Receipt(new(1024, digest, expected.ContentDigest, expected.MetadataDigest)));
        wire.Write(bytes, ArchiveWorkerMessage.Completed, Convert.FromHexString(digest)); if (trailing) bytes.WriteByte(0); return bytes.ToArray();
    }

    [Theory]
    [InlineData("stderr", ArchiveWriteReason.WorkerUnavailable)]
    [InlineData("cpu", ArchiveWriteReason.BudgetExceeded)]
    [InlineData("manifest", ArchiveWriteReason.VerificationFailed)]
    [InlineData("trailing", ArchiveWriteReason.ProtocolViolation)]
    public async Task Terminal_output_is_not_success_until_complete_reaping_accounting_and_parent_comparison(string fault, ArchiveWriteReason reason)
    {
        var invocation = Empty(); Scripted? child = null; using var stage = new MemoryStream();
        var client = new ArchiveTransformClient((nonce, _) => child = new(Completed(nonce, invocation, fault == "manifest", fault == "trailing"))
        { ErrorAfterReaping = fault == "stderr", CpuAfterReaping = fault == "cpu" ? TimeSpan.FromMinutes(1) : TimeSpan.Zero });
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.RebuildAsync(invocation, stage,
            () => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), Ct));
        Assert.Equal(reason, failure.Reason); Assert.True(child!.Disposed);
    }

    [Fact]
    public async Task Queue_reservation_survives_terminal_receipt_until_actual_reaping_finishes()
    {
        var invocation = Empty(); var queue = new ArchiveWorkerQueue(); var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reaping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var stage = new MemoryStream();
        var client = new ArchiveTransformClient((nonce, _) => new Scripted(Completed(nonce, invocation)) { Reached = reached, Reaping = reaping.Task }, queue);
        var result = client.RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), Ct);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct); var pending = queue.EnterAsync(Ct).AsTask();
            Assert.False(pending.IsCompleted); reaping.TrySetResult(); await result; using var reservation = await pending;
        }
        finally { reaping.TrySetResult(); }
    }

    [Fact]
    public async Task Cancellation_stops_and_reaps_a_stalled_transform_before_releasing_its_queue()
    {
        var invocation = Empty(); Scripted? child = null; var queue = new ArchiveWorkerQueue(); using var stage = new MemoryStream();
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ArchiveTransformClient((nonce, _) =>
        {
            using var bytes = new MemoryStream(); new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes).Write(bytes, ArchiveWorkerMessage.Hello, ArchiveTransformWire.Hello);
            child = new(bytes.ToArray(), stalled: true); launched.TrySetResult(); return child;
        }, queue);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var rebuild = client.RebuildAsync(invocation, stage,
            () => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), cancellation.Token);
        await launched.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rebuild);
        Assert.True(child!.Killed); Assert.True(child.Disposed); using var reservation = await queue.EnterAsync(Ct);
    }
}
