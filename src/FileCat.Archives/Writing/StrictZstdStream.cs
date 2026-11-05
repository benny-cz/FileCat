using System.Buffers.Binary;
using FileCat.Core.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.ZStandard;
using SharpCompress.Compressors.ZStandard.Unsafe;

namespace FileCat.Archives.Writing;

/// <summary>Bounded header facts, parsed before constructing a decoder or admitting its window.</summary>
internal sealed class ZstdFrameHeader
{
    private readonly byte[] bytes;
    internal ReadOnlySpan<byte> Bytes => bytes;
    internal bool Checksum { get; }
    internal ulong WindowSize { get; }
    internal ulong? ContentSize { get; }
    private ZstdFrameHeader(byte[] bytes, bool checksum, ulong window, ulong? content)
    { this.bytes = bytes; Checksum = checksum; WindowSize = window; ContentSize = content; }
    internal static ZstdFrameHeader Read(Stream source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            Span<byte> prefix = stackalloc byte[5]; source.ReadExactly(prefix);
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            if (magic is >= 0x184D2A50 and <= 0x184D2A5F) throw Unsupported("Skippable Zstandard frames are not writable.");
            if (magic != 0xFD2FB528) throw Invalid();
            byte flags = prefix[4];
            if ((flags & 8) != 0) throw Invalid();
            if ((flags & 0x13) != 0) throw Unsupported("Zstandard dictionary fields or unknown header extensions are not writable.");
            bool single = (flags & 0x20) != 0; int sizeFlag = flags >> 6;
            int contentBytes = sizeFlag == 0 ? single ? 1 : 0 : 1 << sizeFlag;
            byte[] bytes = new byte[5 + (single ? 0 : 1) + contentBytes]; prefix.CopyTo(bytes);
            source.ReadExactly(bytes.AsSpan(5)); int offset = single ? 5 : 6;
            ulong? content = contentBytes switch
            {
                0 => null, 1 => bytes[offset], 2 => (ulong)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)) + 256,
                4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)), 8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset)),
                _ => throw Invalid(),
            };
            if (content > (ulong)ArchiveWritePolicy.MaxPhysicalBytes) throw Limit();
            ulong window;
            if (single) window = content!.Value;
            else { byte descriptor = bytes[5]; ulong basis = 1UL << (10 + (descriptor >> 3)); window = basis + basis / 8 * (uint)(descriptor & 7); }
            if (window > ArchiveWritePolicy.MaxZstdWindowBytes) throw Limit();
            return new(bytes, (flags & 4) != 0, window, content);
        }
        catch (EndOfStreamException ex) { throw Invalid(ex); }
    }
    internal static ArchiveWriteException Invalid(Exception? cause = null) => cause is null
        ? new(ArchiveWriteReason.InvalidArchive, "The Zstandard frame is incomplete, damaged, or has trailing data.")
        : new(ArchiveWriteReason.InvalidArchive, "The Zstandard frame is incomplete, damaged, or has trailing data.", cause);
    internal static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The Zstandard window, size or expansion exceeds the archive writing limit.");
    private static ArchiveWriteException Unsupported(string message) => new(ArchiveWriteReason.UnsupportedFeature, message);
}

/// <summary>Exactly one complete standard frame. The framing guard runs before bytes reach unsafe codec paths.</summary>
internal sealed class StrictZstdReadStream : Stream
{
    private readonly CancellationToken ct;
    private readonly long physicalLength;
    private readonly BufferedInput buffered;
    private readonly FramedInput framed;
    private readonly DecompressionStream decoder;
    private long produced;
    private bool failed, disposed, ended;
    internal ZstdFrameHeader Header { get; }
    internal StrictZstdReadStream(Stream source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek || source.Position != 0 || source.Length is < 9 or > ArchiveWritePolicy.MaxPhysicalBytes) throw ZstdFrameHeader.Invalid();
        this.ct = ct; physicalLength = source.Length; buffered = new(source, physicalLength, ct); Header = ZstdFrameHeader.Read(buffered, ct);
        framed = new(buffered, physicalLength, Header, ct);
        decoder = new(framed, ArchiveIoAuthority.MaximumReadBytes, checkEndOfStream: true, leaveOpen: true);
        try { decoder.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 26); }
        catch { decoder.Dispose(); throw; }
    }
    public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (failed) throw ZstdFrameHeader.Invalid(); if (buffer.IsEmpty || ended) return 0;
        try
        {
            int n;
            try { n = decoder.Read(buffer); } catch (SharpCompressException ex) { throw ZstdFrameHeader.Invalid(ex); }
            if (n > ArchiveWritePolicy.MaxPhysicalBytes - produced) throw ZstdFrameHeader.Limit(); produced += n;
            if (produced > 64L << 20 && produced > physicalLength * 1000) throw ZstdFrameHeader.Limit();
            if (n == 0)
            {
                if (!framed.Complete || Header.ContentSize is { } expected && expected != (ulong)produced) throw ZstdFrameHeader.Invalid();
                ended = true;
            }
            return n;
        }
        catch { failed = true; throw; }
    }
    // Header/block inspection must not turn three-byte fields into individual parent requests on large streams.
    // This fixed buffer leaves the retained owner open and checks physical EOF after its reviewed length is consumed.
    private sealed class BufferedInput(Stream source, long length, CancellationToken ct) : Stream
    {
        private readonly byte[] bytes = new byte[ArchiveIoAuthority.MaximumReadBytes];
        private int offset, available;
        private long fetched;
        private bool ended, disposed;
        public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
        public override int Read(Span<byte> b)
        {
            ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested(); if (b.IsEmpty) return 0;
            if (available == 0)
            {
                if (fetched == length)
                {
                    if (!ended) { if (source.ReadByte() != -1) throw ZstdFrameHeader.Invalid(); ended = true; } return 0;
                }
                available = source.Read(bytes.AsSpan(0, (int)Math.Min(bytes.Length, length - fetched))); offset = 0;
                if (available == 0) throw ZstdFrameHeader.Invalid(); fetched += available;
            }
            int n = Math.Min(b.Length, available); bytes.AsSpan(offset, n).CopyTo(b); offset += n; available -= n; return n;
        }
        protected override void Dispose(bool disposing) { disposed = true; base.Dispose(disposing); }
        public override bool CanRead => !disposed; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
    private sealed class FramedInput : Stream
    {
        private readonly Stream source;
        private readonly long length;
        private readonly ZstdFrameHeader header;
        private readonly CancellationToken ct;
        private readonly byte[] pending = new byte[18];
        private int offset, available;
        private long consumed, remaining;
        private bool last, footer, failed;
        internal bool Complete { get; private set; }
        internal FramedInput(Stream source, long length, ZstdFrameHeader header, CancellationToken ct)
        {
            this.source = source; this.length = length; this.header = header; this.ct = ct;
            header.Bytes.CopyTo(pending); available = header.Bytes.Length; consumed = available;
        }
        public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
        public override int Read(Span<byte> buffer)
        {
            if (failed) throw ZstdFrameHeader.Invalid(); if (buffer.IsEmpty || Complete) return 0; int written = 0;
            try
            {
                while (!buffer.IsEmpty)
                {
                    ct.ThrowIfCancellationRequested();
                    if (available != 0)
                    {
                        int n = Math.Min(buffer.Length, available); pending.AsSpan(offset, n).CopyTo(buffer);
                        offset += n; available -= n; written += n; buffer = buffer[n..]; continue;
                    }
                    if (remaining != 0)
                    {
                        int n = source.Read(buffer[..(int)Math.Min(remaining, buffer.Length)]);
                        if (n == 0) throw ZstdFrameHeader.Invalid(); remaining -= n; consumed += n; written += n; buffer = buffer[n..]; continue;
                    }
                    if (last)
                    {
                        if (header.Checksum && !footer) { Queue(4); footer = true; continue; }
                        if (consumed != length || source.ReadByte() != -1) throw ZstdFrameHeader.Invalid(); Complete = true; break;
                    }
                    Queue(3); uint block = (uint)(pending[0] | pending[1] << 8 | pending[2] << 16);
                    uint kind = block >> 1 & 3, size = block >> 3;
                    if (kind == 3 || size > Math.Min(header.WindowSize, 128UL << 10)) throw ZstdFrameHeader.Invalid();
                    last = (block & 1) != 0; remaining = kind == 1 ? 1 : size;
                    if (remaining > length - consumed) throw ZstdFrameHeader.Invalid();
                }
                return written;
            }
            catch (EndOfStreamException ex) { failed = true; throw ZstdFrameHeader.Invalid(ex); }
            catch { failed = true; throw; }
        }
        private void Queue(int count)
        {
            if (count > length - consumed) throw ZstdFrameHeader.Invalid();
            source.ReadExactly(pending.AsSpan(0, count)); consumed += count; offset = 0; available = count;
        }
        public override bool CanRead => !failed; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; try { decoder.Dispose(); } finally { buffered.Dispose(); } } base.Dispose(disposing); }
    public override bool CanRead => !disposed && !failed; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => produced; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}

/// <summary>Fixed bounded encoder options, a checksum on every output, and revocable stage-only output.</summary>
internal sealed class StrictZstdWriteStream : Stream
{
    private readonly Stream stage;
    private readonly CancellationToken ct;
    private readonly CompressionStream encoder;
    private long length;
    private bool failed, disposed;
    internal int EffectiveCompressionLevel { get; }
    internal StrictZstdWriteStream(Stream stage, int level, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); if (!stage.CanWrite) throw new ArgumentException("A writable owned stage is required.");
        this.stage = stage; this.ct = ct;
        EffectiveCompressionLevel = level switch { 0 or 2 => 3, 1 => 1, 3 => 9, _ => throw new ArgumentOutOfRangeException(nameof(level)) };
        var compressor = new Compressor(EffectiveCompressionLevel);
        try
        {
            compressor.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, 23);
            compressor.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
            compressor.SetParameter(ZSTD_cParameter.ZSTD_c_contentSizeFlag, 0);
            compressor.SetParameter(ZSTD_cParameter.ZSTD_c_nbWorkers, 0);
            compressor.SetParameter(ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching, 0);
            if (compressor.GetParameter(ZSTD_cParameter.ZSTD_c_compressionLevel) != EffectiveCompressionLevel ||
                compressor.GetParameter(ZSTD_cParameter.ZSTD_c_windowLog) != 23 || compressor.GetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag) != 1 ||
                compressor.GetParameter(ZSTD_cParameter.ZSTD_c_contentSizeFlag) != 0 || compressor.GetParameter(ZSTD_cParameter.ZSTD_c_nbWorkers) != 0 ||
                compressor.GetParameter(ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching) != 0) throw new IOException("The required Zstandard encoder options were not established.");
            encoder = new(new OutputGate(this), compressor, ArchiveIoAuthority.MaximumReadBytes, preserveCompressor: false, leaveOpen: true);
        }
        catch { compressor.Dispose(); throw; }
    }
    public override void Write(byte[] b, int o, int c) => Write(b.AsSpan(o, c));
    public override void Write(ReadOnlySpan<byte> b)
    {
        Live();
        try { if (b.Length > ArchiveWritePolicy.MaxPhysicalBytes - length) throw ZstdFrameHeader.Limit(); encoder.Write(b); length += b.Length; }
        catch { failed = true; throw; }
    }
    internal void Abort() => failed = true;
    private void Live() { ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested(); if (failed) throw new IOException("The Zstandard encoder was stopped after a refusal."); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            try { if (failed) { try { encoder.Dispose(); } catch { } } else encoder.Dispose(); }
            catch { failed = true; throw; }
            finally { disposed = true; }
        }
        base.Dispose(disposing);
    }
    // Flush already-emitted stage bytes, without forcing a tiny compression block for every TAR flush.
    // The complete frame and checksum are emitted by healthy Dispose, before any receipt is accepted.
    public override void Flush() { Live(); try { stage.Flush(); } catch { failed = true; throw; } }
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => !disposed && !failed;
    public override long Length => length; public override long Position { get => length; set => throw new NotSupportedException(); }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long n) => throw new NotSupportedException();
    private sealed class OutputGate(StrictZstdWriteStream owner) : Stream
    {
        public override void Write(byte[] b, int o, int c) => Write(b.AsSpan(o, c));
        public override void Write(ReadOnlySpan<byte> b) { owner.Live(); owner.stage.Write(b); }
        public override void Flush() { owner.Live(); owner.stage.Flush(); }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => !owner.failed;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException();
    }
}
