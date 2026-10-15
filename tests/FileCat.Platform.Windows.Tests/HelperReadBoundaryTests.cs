using System.Buffers.Binary;
using System.Text.Json;
using FileCat.Platform.Windows.Recovery;

namespace FileCat.Platform.Windows.Tests;

public sealed class HelperReadBoundaryTests
{
    [Fact]
    public void A_read_near_the_end_requests_only_the_reported_remaining_bytes()
    {
        using var pipe = new ScriptedPipe([.. Info(), .. Header(1024), .. Enumerable.Repeat((byte)0xA5, 1024)]);
        using var source = new PipeDeviceSource(pipe, "bounded helper");
        var buffer = new byte[1536];

        int count = source.Read(1024, buffer);
        TestContext.Current.TestOutputHelper?.WriteLine("HELPER_BOUNDARY " + JsonSerializer.Serialize(new
        {
            source.Length, Requested = pipe.RequestLengths, Returned = count,
        }));
        Assert.Equal(1024, count);
        Assert.All(buffer[..1024], value => Assert.Equal((byte)0xA5, value));
        Assert.Equal([1024], pipe.RequestLengths);
    }

    [Fact]
    public void A_reply_past_the_reported_end_is_rejected_before_its_bytes_are_exposed()
    {
        using var pipe = new ScriptedPipe([.. Info(), .. Header(1536), .. Enumerable.Repeat((byte)0xB6, 1536)]);
        using var source = new PipeDeviceSource(pipe, "bounded helper");
        var buffer = new byte[1536];
        Exception? failure = Record.Exception(() => source.Read(1024, buffer));
        TestContext.Current.TestOutputHelper?.WriteLine("HELPER_OVERRUN " + JsonSerializer.Serialize(new
        {
            source.Length, Requested = pipe.RequestLengths, Error = failure?.GetType().Name,
        }));
        Assert.IsType<IOException>(failure);
        Assert.Throws<IOException>(() => source.Read(0, buffer));
        Assert.Single(pipe.RequestLengths);
    }

    [Fact]
    public void At_the_reported_end_the_client_returns_zero_without_contacting_the_helper()
    {
        using var pipe = new ScriptedPipe(Info());
        using var source = new PipeDeviceSource(pipe, "bounded helper");
        Assert.Equal(0, source.Read(2048, new byte[512]));
        Assert.Empty(pipe.RequestLengths);
    }

    [Fact]
    public void An_interior_read_preserves_exact_bytes()
    {
        using var pipe = new ScriptedPipe([.. Info(), .. Header(512), .. Enumerable.Repeat((byte)0xC7, 512)]);
        using var source = new PipeDeviceSource(pipe, "bounded helper");
        var buffer = new byte[512];
        Assert.Equal(512, source.Read(0, buffer));
        Assert.All(buffer, value => Assert.Equal((byte)0xC7, value));
        Assert.Equal([512], pipe.RequestLengths);
    }

    private static byte[] Info()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), 2048);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 512);
        return bytes;
    }

    private static byte[] Header(int count)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), count);
        return bytes;
    }

    private sealed class ScriptedPipe(byte[] incoming) : Stream
    {
        private readonly MemoryStream _input = new(incoming);
        public List<int> RequestLengths { get; } = [];
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(Span<byte> buffer) => _input.Read(buffer);
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length == RawReadProtocol.RequestSize && buffer[0] == RawReadProtocol.Read)
                RequestLengths.Add(BinaryPrimitives.ReadInt32LittleEndian(buffer[9..]));
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _input.Dispose(); base.Dispose(disposing); }
    }
}
