using System.Diagnostics;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveWorkerCancellationTests
{
    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("03")]
    [InlineData("+3")]
    [InlineData(" 3")]
    [InlineData("3 ")]
    [InlineData("9223372036854775808")]
    [InlineData("3\0")]
    public void Noncanonical_or_stdio_cancellation_handles_are_refused(string handle)
        => Assert.Throws<ArgumentException>(() => ArchiveWorkerCancellation.Parse(ArchiveWorkerCancellation.Argument, handle));

    [Fact]
    public void Canonical_launch_handle_has_no_archive_or_path_authority()
    {
        Assert.Equal(3, ArchiveWorkerCancellation.Parse(ArchiveWorkerCancellation.Argument, "3"));
        Assert.Throws<ArgumentException>(() => ArchiveWorkerCancellation.Parse("other", "3"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parser_wait_is_cancellable_while_the_OS_input_or_output_is_blocked(bool writing)
    {
        using var blocked = new Blocked(writing); using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var transport = new ArchiveWorkerTransport(writing ? Stream.Null : blocked, writing ? blocked : Stream.Null, cancellation.Token);
        Task parser = Task.Run(() => { if (writing) transport.Output.Write(new byte[ArchiveWorkerTransport.MaximumChunkBytes * 2]); else transport.Input.ReadByte(); }, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(blocked.Entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parser.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.InRange(blocked.MaximumWrite, 0, ArchiveWorkerTransport.MaximumChunkBytes);
        }
        finally { blocked.Release.Set(); }
    }

    [Fact]
    public void Complete_transport_preserves_large_bytes_and_waits_for_actual_flush()
    {
        byte[] bytes = new byte[ArchiveWorkerTransport.MaximumChunkBytes * 5 + 3]; new Random(348).NextBytes(bytes);
        using var source = new MemoryStream(bytes); using var target = new ObservedOutput();
        using var transport = new ArchiveWorkerTransport(source, target, CancellationToken.None);
        transport.Input.CopyTo(transport.Output); transport.Output.Flush();
        Assert.Equal(bytes, target.ToArray()); Assert.Equal(1, target.Flushes);
        Assert.InRange(target.MaximumWrite, 1, ArchiveWorkerTransport.MaximumChunkBytes);
    }

    [Theory]
    [InlineData("archive-assess")]
    [InlineData("archive-transform")]
    [InlineData("archive-verify")]
    public async Task Real_worker_cancels_while_waiting_for_request_and_exits_zero_without_a_receipt(string role)
    {
        ArchiveAssessmentClientTests.Child? child = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var elapsed = new Stopwatch(); var queue = new ArchiveWorkerQueue();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveWorkerSession.RunAsync(
            (nonce, _) => child = ArchiveAssessmentClientTests.Child.Start(nonce, role), queue, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
            (nonce, process, _) =>
            {
                var wire = new ArchiveWorkerProtocol(nonce, 1 << 20);
                Assert.Equal(ArchiveWorkerMessage.Hello, wire.Read(process.Output).Kind);
                elapsed.Start(); cancellation.Cancel();
                Assert.Equal(-1, process.Output.ReadByte()); return 7;
            }, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
        Assert.True(child!.Reaped); Assert.True(child.Disposed); Assert.False(child.HardKilled); Assert.Equal(0u, child.ExitCode);
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        using var next = await queue.EnterAsync(TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"{role} cancellation through reap: {elapsed.Elapsed.TotalMilliseconds:F1} ms; exit {child.ExitCode}.");
    }

    [Fact]
    public async Task Uncooperative_real_worker_is_killed_after_two_seconds_and_reaped_before_queue_release()
    {
        ArchiveAssessmentClientTests.Child? child = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var elapsed = new Stopwatch(); var queue = new ArchiveWorkerQueue();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveWorkerSession.RunAsync(
            (nonce, _) => child = ArchiveAssessmentClientTests.Child.Start(nonce, "archive-cancel-ignore"), queue,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), (_, process, _) =>
            {
                using var reader = new StreamReader(process.Output, leaveOpen: true);
                Assert.Equal("CANCELLATION-IGNORE-READY", reader.ReadLine());
                elapsed.Start(); cancellation.Cancel(); Assert.Null(reader.ReadLine()); return 7;
            }, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
        Assert.True(child!.Reaped); Assert.True(child.Disposed); Assert.True(child.HardKilled); Assert.NotEqual(0u, child.ExitCode);
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(1.9), TimeSpan.FromSeconds(4));
        using var next = await queue.EnterAsync(TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"Forced cancellation through reap: {elapsed.Elapsed.TotalMilliseconds:F1} ms; exit {child.ExitCode}.");
    }

    [Fact]
    public Task Real_worker_can_cancel_with_a_saturated_output_pipe() => ArchiveWorkerCancellationAssertions.Run(
        (nonce, _) => ArchiveAssessmentClientTests.Child.Start(nonce, "archive-cancel-output"), "archive-cancel-output");

    [Fact]
    public async Task A_byte_on_the_EOF_only_channel_is_malformed_and_never_a_successful_cancellation_receipt()
    {
        Guid nonce = Guid.NewGuid(); await using var child = ArchiveAssessmentClientTests.Child.Start(nonce);
        Assert.Equal(ArchiveWorkerMessage.Hello, new ArchiveWorkerProtocol(nonce, 1 << 20).Read(child.Output).Kind);
        child.SendMalformedCancellation(); Assert.Equal(-1, child.Output.ReadByte());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!child.HasExited) await Task.Delay(10, timeout.Token);
        Assert.Equal(64u, child.ExitCode); Assert.False(child.HardKilled);
    }

    private sealed class ObservedOutput : MemoryStream
    {
        internal int MaximumWrite, Flushes;
        public override void Write(ReadOnlySpan<byte> buffer) { MaximumWrite = Math.Max(MaximumWrite, buffer.Length); base.Write(buffer); }
        public override void Flush() { Flushes++; base.Flush(); }
    }
    private sealed class Blocked(bool writing) : Stream
    {
        internal readonly ManualResetEventSlim Entered = new(), Release = new();
        internal int MaximumWrite;
        public override bool CanRead => !writing; public override bool CanWrite => writing; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) { Entered.Set(); Release.Wait(); return 0; }
        public override void Write(byte[] b, int o, int c) { MaximumWrite = Math.Max(MaximumWrite, c); Entered.Set(); Release.Wait(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long n) => throw new NotSupportedException();
        // Tests explicitly release blocked IO before their streams leave scope; the worker transport never joins it.
    }
}
