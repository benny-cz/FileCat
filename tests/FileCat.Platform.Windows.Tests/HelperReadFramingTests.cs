using System.Buffers.Binary;
using System.Text.Json;
using FileCat.Platform.Windows.Recovery;

namespace FileCat.Platform.Windows.Tests;

public sealed class HelperReadFramingTests
{
    [Fact]
    public void An_error_reply_with_content_ends_the_session()
    {
        using var pipe = new ScriptedPipe([.. Info(), .. Header(4, status: 5), 1, 2, 3, 4,
            .. Header(4), 0xA1, 0xB2, 0xC3, 0xD4]);
        using var source = new PipeDeviceSource(pipe, "controlled helper");
        byte[] bytes = new byte[4];
        Assert.Throws<IOException>(() => source.Read(0, bytes));
        Assert.Throws<IOException>(() => source.Read(0, bytes));
        Assert.Equal(1, pipe.ReadRequests);
    }

    [Fact]
    public void An_ordinary_error_reply_can_be_retried()
    {
        using var pipe = new ScriptedPipe([.. Info(), .. Header(0, status: 5),
            .. Header(4), 0xA1, 0xB2, 0xC3, 0xD4]);
        using var source = new PipeDeviceSource(pipe, "controlled helper");
        byte[] bytes = new byte[4];
        Assert.Throws<IOException>(() => source.Read(0, bytes));
        Assert.Equal(4, source.Read(0, bytes));
        Assert.Equal(new byte[] { 0xA1, 0xB2, 0xC3, 0xD4 }, bytes);
        Assert.Equal(2, pipe.ReadRequests);
    }

    [Fact]
    public void A_successful_zero_byte_reply_before_declared_end_ends_the_session()
    {
        using var pipe = new ScriptedPipe([.. Info(), .. Header(0)]);
        using var source = new PipeDeviceSource(pipe, "controlled helper");
        byte[] bytes = new byte[4];
        Exception? first = null, second = null;
        try { source.Read(100, bytes); } catch (Exception ex) { first = ex; }
        try { source.Read(100, bytes); } catch (Exception ex) { second = ex; }
        TestContext.Current.TestOutputHelper?.WriteLine("HELPER_ZERO_REPLY " + JsonSerializer.Serialize(new
        {
            FirstError = first?.GetType().Name,
            SecondError = second?.GetType().Name,
            pipe.ReadRequests,
        }));
        Assert.IsType<IOException>(first);
        Assert.IsType<IOException>(second);
        Assert.Equal(1, pipe.ReadRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Malformed_read_reply_ends_the_session_before_another_request(bool shortBody)
    {
        using var pipe = new ScriptedPipe(Responses(shortBody));
        using var source = new PipeDeviceSource(pipe, "controlled helper");
        byte[] firstBytes = new byte[4], secondBytes = new byte[4];
        Exception? first = null, second = null;
        try { source.Read(0, firstBytes); } catch (Exception ex) { first = ex; }
        try { source.Read(0, secondBytes); } catch (Exception ex) { second = ex; }

        TestContext.Current.TestOutputHelper?.WriteLine("HELPER_READ_FRAMING " + JsonSerializer.Serialize(new
        {
            shortBody,
            FirstError = first?.GetType().Name,
            SecondError = second?.GetType().Name,
            pipe.ReadRequests,
            SecondBytes = Convert.ToHexString(secondBytes),
        }));
        Assert.IsType<IOException>(first);
        Assert.IsType<IOException>(second);
        Assert.Equal(1, pipe.ReadRequests);
    }

    private static byte[] Responses(bool shortBody)
    {
        byte[] info = Info();
        byte[] firstHeader = Header(shortBody ? 4 : 5);
        if (shortBody) return [.. info, .. firstHeader, 0xB1, 0xB2];

        // The client must never interpret the malformed first reply's leftover bytes as a
        // second reply. Without terminal failure, these bytes masquerade as status=0/count=4.
        return [.. info, .. firstHeader, 0, 0, 0, 0, 4, .. Header(4), 0xA1, 0xB2, 0xC3, 0xD4];
    }

    private static byte[] Info()
    {
        byte[] info = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(4), 4096);
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(12), 512);
        return info;
    }

    private static byte[] Header(int count, int status = 0)
    {
        byte[] header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, status);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), count);
        return header;
    }

    private sealed class ScriptedPipe(byte[] incoming) : Stream
    {
        private readonly MemoryStream _input = new(incoming);
        public int ReadRequests { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(Span<byte> buffer) => _input.Read(buffer);
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length == RawReadProtocol.RequestSize && buffer[0] == RawReadProtocol.Read) ReadRequests++;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _input.Dispose(); base.Dispose(disposing); }
    }
}
