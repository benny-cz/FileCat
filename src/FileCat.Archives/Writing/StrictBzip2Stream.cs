using FileCat.Core.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.BZip2;

namespace FileCat.Archives.Writing;

/// <summary>One bounded complete BZip2 stream, including block/combined CRC, zero end padding and physical EOF.</summary>
internal sealed class StrictBzip2ReadStream : Stream
{
    private readonly Stream source;
    private readonly CancellationToken ct;
    private readonly long physicalLength;
    private readonly ByteSource encoded;
    private readonly CBZip2InputStream decoder;
    private long produced;
    private bool ended, failed, disposed;
    internal byte BlockSize { get; }
    internal StrictBzip2ReadStream(Stream source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek || source.Position != 0 || source.Length is < 14 or > ArchiveWritePolicy.MaxPhysicalBytes)
            throw Invalid();
        this.source = source; this.ct = ct; physicalLength = source.Length; encoded = new(source, physicalLength, ct);
        var header = encoded.Header;
        if (!header[..3].SequenceEqual("BZh"u8) || header[3] is < (byte)'1' or > (byte)'9') throw Invalid();
        BlockSize = (byte)(header[3] - '0'); // Validate before the codec allocates its block-dependent arrays.
        try { decoder = CBZip2InputStream.Create(encoded, decompressConcatenated: false, leaveOpen: true, tolerateTruncatedStream: false); }
        catch (SharpCompressException ex) { throw Invalid(ex); }
    }
    public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (failed) throw Invalid(); if (buffer.IsEmpty || ended) return 0;
        try
        {
            int n;
            try { n = decoder.Read(buffer); } catch (SharpCompressException ex) { throw Invalid(ex); }
            if (n > ArchiveWritePolicy.MaxPhysicalBytes - produced) throw Limit();
            produced += n;
            if (produced > 64L << 20 && produced > physicalLength * 1000) throw Limit();
            if (n == 0)
            {
                if (!decoder.HasValidatedSingleStreamEnd || encoded.Consumed != physicalLength || source.ReadByte() != -1) throw Invalid();
                ended = true;
            }
            return n;
        }
        catch { failed = true; throw; }
    }
    // Buffer physical reads without concealing how many bytes the bit decoder actually consumed. A codec read-ahead
    // buffer must never make trailing bytes or a second complete stream appear to have been validated.
    private sealed class ByteSource(Stream source, long length, CancellationToken ct) : Stream
    {
        private readonly byte[] bytes = new byte[64 * 1024];
        private int start, end;
        private long fetched;
        internal long Consumed { get; private set; }
        internal ReadOnlySpan<byte> Header
        {
            get
            {
                while (end < 4)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = source.Read(bytes.AsSpan(end, (int)Math.Min(bytes.Length - end, length - fetched)));
                    if (n == 0) throw Invalid(); end += n; fetched += n;
                }
                return bytes.AsSpan(0, 4);
            }
        }
        private void Fill()
        {
            ct.ThrowIfCancellationRequested();
            if (start != end || fetched == length) return;
            start = 0; end = source.Read(bytes.AsSpan(0, (int)Math.Min(bytes.Length, length - fetched)));
            if (end == 0) throw Invalid(); fetched += end;
        }
        public override int ReadByte() { Fill(); if (start == end) return -1; Consumed++; return bytes[start++]; }
        public override int Read(byte[] b, int o, int c) { int n = 0; while (n < c) { int v = ReadByte(); if (v < 0) break; b[o + n++] = (byte)v; } return n; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get => Consumed; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; decoder.Dispose(); } base.Dispose(disposing); }
    public override bool CanRead => !disposed && !failed; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => produced; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long n) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    private static ArchiveWriteException Invalid(Exception? cause = null) => new(ArchiveWriteReason.InvalidArchive,
        "The BZip2 stream is incomplete, damaged, concatenated, or has unsupported trailing bits or bytes.", cause!);
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The decoded BZip2 exceeds its size or expansion allowance.");
}

/// <summary>Bounded Apache-derived encoder over a revocable output gate. Disposal never owns the parent stage.</summary>
internal sealed class StrictBzip2WriteStream : Stream
{
    private readonly Stream stage;
    private readonly CancellationToken ct;
    private readonly CBZip2OutputStream encoder;
    private long length;
    private bool failed, disposed;
    internal StrictBzip2WriteStream(Stream stage, byte blockSize, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!stage.CanWrite || blockSize is < 1 or > 9) throw new ArgumentException("A writable stage and BZip2 block size 1–9 are required.");
        this.stage = stage; this.ct = ct; encoder = new(new OutputGate(this), blockSize, leaveOpen: true);
    }
    public override void Write(byte[] b, int o, int c) => Write(b.AsSpan(o, c));
    public override void Write(ReadOnlySpan<byte> b)
    {
        Live();
        try { if (b.Length > ArchiveWritePolicy.MaxPhysicalBytes - length) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The BZip2 input exceeds the archive writing limit."); encoder.Write(b); length += b.Length; }
        catch { failed = true; throw; }
    }
    internal void Abort() => failed = true;
    private void Live() { ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested(); if (failed) throw new IOException("The BZip2 encoder was stopped after a refusal."); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            if (failed) { try { encoder.Dispose(); } catch { } }
            else { try { encoder.Dispose(); } catch { failed = true; throw; } finally { disposed = true; } }
            disposed = true;
        }
        base.Dispose(disposing);
    }
    public override void Flush() { Live(); try { encoder.Flush(); } catch { failed = true; throw; } }
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => !disposed && !failed;
    public override long Length => length; public override long Position { get => length; set => throw new NotSupportedException(); }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long n) => throw new NotSupportedException();
    private sealed class OutputGate(StrictBzip2WriteStream owner) : Stream
    {
        private readonly byte[] bytes = new byte[ArchiveIoAuthority.MaximumReadBytes];
        private int buffered;
        public override void Write(byte[] b, int o, int c) => Write(b.AsSpan(o, c));
        public override void Write(ReadOnlySpan<byte> b)
        {
            owner.Live();
            while (!b.IsEmpty)
            {
                int n = Math.Min(b.Length, bytes.Length - buffered); b[..n].CopyTo(bytes.AsSpan(buffered)); buffered += n; b = b[n..];
                if (buffered == bytes.Length) Drain();
            }
        }
        // The codec emits individual bytes. Coalesce them before crossing the parent proxy or large valid streams
        // would exhaust the finite frame budget even though their byte allowance had not been consumed.
        public override void WriteByte(byte b) { owner.Live(); bytes[buffered++] = b; if (buffered == bytes.Length) Drain(); }
        private void Drain() { owner.Live(); if (buffered != 0) { owner.stage.Write(bytes.AsSpan(0, buffered)); buffered = 0; } }
        public override void Flush() { owner.Live(); Drain(); owner.stage.Flush(); }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => !owner.failed;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException();
    }
}
