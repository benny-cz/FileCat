using System.Collections.Concurrent;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveWorkerTransportSecretTests
{
    private static void Cleared(IEnumerable<byte[]> buffers)
    { Assert.NotEmpty(buffers); foreach (byte[] buffer in buffers) Assert.All(buffer, value => Assert.Equal((byte)0, value)); }

    [Fact]
    public void Short_reads_clear_consumed_ranges_and_never_abandon_a_resized_plaintext_array()
    {
        using var input = new ObservedInput(); using var transport = new ArchiveWorkerTransport(input, Stream.Null, CancellationToken.None);
        Assert.Equal(1, transport.Input.ReadByte());
        byte[] first = input.Buffers.First(); Assert.Equal(0, first[0]); Assert.Equal(2, first[1]);
        using var rest = new MemoryStream(); transport.Input.CopyTo(rest); Assert.Equal(new byte[] { 2, 3, 4, 5, 6, 7 }, rest.ToArray());
        Assert.Equal(3, input.Buffers.Count); Cleared(input.Buffers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Output_copies_clear_before_success_or_failure_reaches_the_caller(bool fail)
    {
        using var output = new ObservedOutput(fail); using var transport = new ArchiveWorkerTransport(Stream.Null, output, CancellationToken.None);
        byte[] input = Enumerable.Repeat((byte)0x5a, ArchiveWorkerTransport.MaximumChunkBytes * 2 + 5).ToArray();
        if (fail) Assert.Throws<IOException>(() => transport.Output.Write(input));
        else { transport.Output.Write(input); transport.Output.Flush(); Assert.Equal(input, output.Captured.ToArray()); }
        Assert.All(input, value => Assert.Equal((byte)0x5a, value)); Cleared(output.Buffers);
    }

    [Fact]
    public void Disposal_clears_queued_input_and_the_producer_clears_an_unqueued_cancelled_read()
    {
        using var source = new SaturatedInput(); var transport = new ArchiveWorkerTransport(source, Stream.Null, CancellationToken.None);
        try
        {
            Assert.True(source.Third.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            transport.Dispose();
            Assert.True(SpinWait.SpinUntil(() => source.Buffers.All(b => b.All(value => value == 0)), TimeSpan.FromSeconds(5)));
            Cleared(source.Buffers);
        }
        finally { transport.Dispose(); }
    }

    [Fact]
    public async Task Cancellation_does_not_wipe_an_OS_write_buffer_until_that_write_returns()
    {
        using var target = new BlockedOutput(); using var transport = new ArchiveWorkerTransport(Stream.Null, target, CancellationToken.None);
        Task writer = Task.Run(() => transport.Output.Write(new byte[] { 1, 2, 3, 4 }), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(target.Entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            transport.Dispose();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, target.Buffer);
            target.Release.Set();
            Assert.True(SpinWait.SpinUntil(() => target.Buffer!.All(value => value == 0), TimeSpan.FromSeconds(5)));
        }
        finally { target.Release.Set(); }
    }

    private sealed class ObservedInput : Stream
    {
        internal readonly ConcurrentQueue<byte[]> Buffers = new(); private int position;
        public override int Read(byte[] buffer, int offset, int count)
        {
            Buffers.Enqueue(buffer); int size = position == 0 ? 3 : position == 3 ? 4 : 0;
            for (int i = 0; i < size; i++) buffer[offset + i] = (byte)(++position);
            return size;
        }
        public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class SaturatedInput : Stream
    {
        internal readonly ConcurrentQueue<byte[]> Buffers = new(); internal readonly ManualResetEventSlim Third = new();
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)0x5a); Buffers.Enqueue(buffer); if (Buffers.Count == 3) Third.Set(); return count;
        }
        public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class ObservedOutput(bool fail) : Stream
    {
        internal readonly ConcurrentQueue<byte[]> Buffers = new(); internal readonly MemoryStream Captured = new();
        public override void Write(byte[] buffer, int offset, int count)
        { Buffers.Enqueue(buffer); if (fail) throw new IOException("Synthetic public-vector output failure."); Captured.Write(buffer, offset, count); }
        public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class BlockedOutput : Stream
    {
        internal readonly ManualResetEventSlim Entered = new(), Release = new(); internal byte[]? Buffer;
        public override void Write(byte[] buffer, int offset, int count)
        { Assert.Equal(0, offset); Assert.Equal(buffer.Length, count); Buffer = buffer; Entered.Set(); Release.Wait(); }
        public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
