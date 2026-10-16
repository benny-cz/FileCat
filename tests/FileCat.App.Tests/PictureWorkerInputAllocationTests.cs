using System.Reflection;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class PictureWorkerInputAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void Worker_input_does_not_allocate_a_second_full_picture_copy()
    {
        const int size = 8 * 1024 * 1024;
        using var input = new ZeroStream(size);
        var read = typeof(PictureWorker).GetMethod("ReadAll", BindingFlags.NonPublic | BindingFlags.Static)!;
        long before = GC.GetAllocatedBytesForCurrentThread();
        object? result = read.Invoke(null, [input]);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Picture worker input: {size} bytes read, {allocated} thread-allocated bytes.");

        Assert.NotNull(result);
        Assert.Equal(size, input.BytesRead);
        Assert.InRange(allocated, 0, 20L * 1024 * 1024);
    }

    private sealed class ZeroStream(long remaining) : Stream
    {
        private long _remaining = remaining;
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(_remaining, buffer.Length);
            buffer[..count].Clear();
            _remaining -= count;
            BytesRead += count;
            return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
