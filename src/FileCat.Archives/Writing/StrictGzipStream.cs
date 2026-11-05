using System.Buffers.Binary;
using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using SharpCompress.Compressors.Deflate;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace FileCat.Archives.Writing;

/// <summary>
/// One metadata-neutral gzip member. Unlike tolerant decompression, admission requires an explicit DEFLATE end,
/// exact compressed-byte consumption, CRC, ISIZE and physical EOF. The already-vendored bounded raw inflater exposes
/// its terminal status; the runtime's DeflateStream API alone does not expose that framing evidence.
/// </summary>
internal sealed class StrictGzipReadStream : Stream
{
    private readonly Stream _source;
    private readonly CancellationToken _ct;
    private readonly long _physicalLength;
    private readonly ZlibCodec _codec = new();
    private readonly byte[] _encoded = new byte[64 << 10];
    private readonly byte[] _decoded = new byte[64 << 10];
    private long _remainingEncoded;
    private long _produced;
    private uint _crc;
    private int _available;
    private int _offset;
    private bool _ended;
    private bool _disposed;
    private bool _failed;
    public byte OperatingSystem { get; }
    public byte ExtraFlags { get; }

    public StrictGzipReadStream(Stream source, CancellationToken ct)
    {
        if (!source.CanSeek || !source.CanRead || source.Position != 0 || source.Length < 20 || source.Length > ArchiveWritePolicy.MaxPhysicalBytes)
            throw Invalid("The gzip source is truncated, too large, or not a pinned seekable stream.");
        _source = source;
        _ct = ct;
        _physicalLength = source.Length;
        Span<byte> header = stackalloc byte[10];
        source.ReadExactly(header);
        if (!header[..3].SequenceEqual((ReadOnlySpan<byte>)[0x1f, 0x8b, 8]) || (header[3] & 0xe0) != 0)
            throw Invalid("The gzip header is invalid.");
        if (header[3] != 0 || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 0 || header[8] is not (0 or 2 or 4))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature,
                "This gzip stream has stored names, timestamps or other metadata that this writing profile cannot preserve.");
        OperatingSystem = header[9];
        ExtraFlags = header[8];
        _remainingEncoded = _physicalLength - 18;
        if (_codec.InitializeInflate(expectRfc1950Header: false) != 0) throw Invalid("The gzip decoder could not initialize.");
        _codec.InputBuffer = _encoded;
        _codec.OutputBuffer = _decoded;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw Invalid("The gzip decoder was stopped after a refusal.");
        if (buffer.Length == 0) return 0;
        try
        {
            while (_available == 0 && !_ended) Pump();
            int count = Math.Min(buffer.Length, _available);
            _decoded.AsSpan(_offset, count).CopyTo(buffer);
            _offset += count;
            _available -= count;
            return count;
        }
        catch { _failed = true; throw; }
    }

    private void Pump()
    {
        _ct.ThrowIfCancellationRequested();
        if (_codec.AvailableBytesIn == 0 && _remainingEncoded > 0)
        {
            int requested = (int)Math.Min(_remainingEncoded, _encoded.Length);
            int n = _source.Read(_encoded, 0, requested);
            if (n == 0) throw Invalid("The gzip stream is truncated.");
            _remainingEncoded -= n;
            _codec.NextIn = 0;
            _codec.AvailableBytesIn = n;
        }
        _codec.NextOut = 0;
        _codec.AvailableBytesOut = _decoded.Length;
        int before = _codec.AvailableBytesIn;
        int status;
        try { status = _codec.Inflate(FlushType.None); }
        catch (ZlibException) { throw Invalid("The gzip DEFLATE data is invalid."); }
        if (status is not (0 or 1 or -5)) throw Invalid("The gzip DEFLATE data is invalid.");
        int produced = _codec.NextOut;
        if (produced > ArchiveWritePolicy.MaxPhysicalBytes - _produced) throw Limit();
        _produced += produced;
        if (_produced > 64L << 20 && _produced > _physicalLength * 1000) throw Limit();
        _crc = Crc32.Append(_crc, _decoded.AsSpan(0, produced));
        _offset = 0;
        _available = produced;
        if (status == 1)
        {
            if (_remainingEncoded != 0 || _codec.AvailableBytesIn != 0 || _codec.TotalBytesIn != _physicalLength - 18)
                throw Invalid("Concatenated gzip streams and trailing bytes are not writable.");
            Span<byte> footer = stackalloc byte[8];
            _source.ReadExactly(footer);
            if (BinaryPrimitives.ReadUInt32LittleEndian(footer) != _crc ||
                BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]) != unchecked((uint)_produced))
                throw Invalid("The gzip checksum or uncompressed length is invalid.");
            if (_source.ReadByte() != -1) throw Invalid("The gzip contains trailing data.");
            _ended = true;
        }
        else if (produced == 0 && before == _codec.AvailableBytesIn)
            throw Invalid("The gzip DEFLATE stream is incomplete or made no bounded progress.");
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing) _codec.EndInflate();
        _disposed = true;
        base.Dispose(disposing);
    }
    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _produced - _available; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidArchive, message);
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The gzip expansion exceeds the archive writing limit.");
}

/// <summary>Runtime DEFLATE encoding plus an explicit neutral gzip header and checked trailer. Leaves the stage open.</summary>
internal sealed class StrictGzipWriteStream : Stream
{
    private readonly Stream _stage;
    private readonly CompressionLevel _level;
    private readonly OutputGate _gate;
    private System.IO.Compression.DeflateStream? _deflate;
    private long _length;
    private uint _crc;
    private bool _disposed;
    private bool _failed;

    public StrictGzipWriteStream(Stream stage, CompressionLevel level, byte operatingSystem = 255)
    {
        if (!Enum.IsDefined(level) || !stage.CanWrite) throw new ArgumentException("A writable stage and supported compression level are required.");
        _stage = stage;
        _gate = new(this);
        _level = level;
        Span<byte> header = stackalloc byte[10];
        header.Clear();
        header[0] = 0x1f; header[1] = 0x8b; header[2] = 8;
        header[8] = level switch { CompressionLevel.Fastest => 4, CompressionLevel.SmallestSize => 2, _ => 0 };
        header[9] = operatingSystem;
        stage.Write(header);
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            Check();
            if (buffer.Length > ArchiveWritePolicy.MaxPhysicalBytes - _length)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The gzip input exceeds the archive writing limit.");
            if (buffer.Length == 0) return;
            _deflate ??= new System.IO.Compression.DeflateStream(_gate, _level, leaveOpen: true);
            _deflate.Write(buffer); _crc = Crc32.Append(_crc, buffer); _length += buffer.Length;
        }
        catch { _failed = true; throw; }
    }
    internal void Abort() => _failed = true;
    private void Check() { if (_failed) throw new IOException("The gzip encoder was stopped after a refusal."); }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            if (_failed)
            {
                // Release native encoder state through a gate that forwards no more bytes. Preserve the earlier
                // transform/write failure instead of finalizing a misleading container or masking that failure.
                try { _deflate?.Dispose(); } catch { }
            }
            else
            {
                try
                {
                    if (_deflate is not null) _deflate.Dispose();
                    else _stage.Write((ReadOnlySpan<byte>)[3, 0]); // canonical final empty DEFLATE block
                    Span<byte> footer = stackalloc byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(footer, _crc);
                    BinaryPrimitives.WriteUInt32LittleEndian(footer[4..], unchecked((uint)_length));
                    _stage.Write(footer);
                }
                catch { _failed = true; throw; }
            }
        }
        base.Dispose(disposing);
    }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed && !_failed;
    public override long Length => _length;
    public override long Position { get => _length; set => throw new NotSupportedException(); }
    public override void Flush() { ObjectDisposedException.ThrowIf(_disposed, this); try { Check(); _deflate?.Flush(); } catch { _failed = true; throw; } }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    private sealed class OutputGate(StrictGzipWriteStream owner) : Stream
    {
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => !owner._failed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] b, int o, int c) => Write(b.AsSpan(o, c));
        public override void Write(ReadOnlySpan<byte> b) { owner.Check(); owner._stage.Write(b); }
        public override void Flush() { owner.Check(); owner._stage.Flush(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException();
    }
}
