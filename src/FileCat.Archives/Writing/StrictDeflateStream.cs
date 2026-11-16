using System.Security.Cryptography;
using FileCat.Core.Archives;
using SharpCompress.Compressors.Deflate;

namespace FileCat.Archives.Writing;

/// <summary>Exactly one raw-deflate stream, with real block completion, output size and consumed-input checks.</summary>
internal sealed class StrictDeflateReadStream : Stream
{
    private readonly Stream source;
    private readonly CancellationToken ct;
    private readonly long packedLength, length;
    private readonly byte[] input = new byte[ArchiveIoAuthority.MaximumReadBytes], output = new byte[ArchiveIoAuthority.MaximumReadBytes];
    private readonly ZlibCodec codec = new();
    private long fetched, produced;
    private bool ended, failed, disposed;
    internal bool Complete => ended;
    internal long CompressedBytesConsumed => codec.TotalBytesIn;
    internal StrictDeflateReadStream(Stream source, long packedLength, long length, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!source.CanRead || packedLength < 2 || length < 0) throw Invalid();
        if (packedLength > ArchiveWritePolicy.MaxPhysicalBytes || length > ArchiveWritePolicy.MaxPayloadBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The raw-deflate extent exceeds the strict verification budget.");
        this.source = source; this.ct = ct; this.packedLength = packedLength; this.length = length;
        try
        {
            if (codec.InitializeInflate(false) != ZlibConstants.Z_OK) throw Invalid();
            codec.InputBuffer = input; codec.OutputBuffer = output;
        }
        catch { if (codec.istate is not null) codec.EndInflate(); throw; }
    }
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (failed) throw Invalid(); if (buffer.IsEmpty || ended) return 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (codec.AvailableBytesIn == 0 && fetched < packedLength)
                {
                    int wanted = (int)Math.Min(input.Length, packedLength - fetched); int n = source.Read(input.AsSpan(0, wanted));
                    if (n <= 0 || n > wanted) throw Invalid(); fetched += n; codec.NextIn = 0; codec.AvailableBytesIn = n;
                }
                long beforeIn = codec.TotalBytesIn, beforeOut = codec.TotalBytesOut;
                codec.NextOut = 0; codec.AvailableBytesOut = (int)Math.Min(Math.Min(buffer.Length, output.Length), length - produced + 1);
                int result = codec.Inflate(FlushType.None), count = codec.NextOut;
                if (count < 0 || count > length - produced) throw Invalid(); produced += count;
                if (result == ZlibConstants.Z_STREAM_END)
                {
                    if (produced != length || codec.TotalBytesIn != packedLength || fetched != packedLength || codec.AvailableBytesIn != 0 || source.ReadByte() != -1)
                        throw Invalid();
                    ended = true;
                }
                else if (result is not (ZlibConstants.Z_OK or ZlibConstants.Z_BUF_ERROR)) throw Invalid();
                if (count != 0) { output.AsSpan(0, count).CopyTo(buffer); return count; }
                if (ended) return 0;
                if (beforeIn == codec.TotalBytesIn && beforeOut == codec.TotalBytesOut && !(codec.AvailableBytesIn == 0 && fetched < packedLength)) throw Invalid();
            }
        }
        catch (Exception ex) { failed = true; if (ex is ZlibException) throw Invalid(ex); throw; }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        { disposed = true; try { codec.EndInflate(); } finally { CryptographicOperations.ZeroMemory(input); CryptographicOperations.ZeroMemory(output); codec.InputBuffer = codec.OutputBuffer = null!; } }
        base.Dispose(disposing);
    }
    public override bool CanRead => !disposed && !failed; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => length; public override long Position { get => produced; set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    private static ArchiveWriteException Invalid(Exception? ex = null) => new(ArchiveWriteReason.InvalidArchive, "The raw-deflate coder is incomplete, damaged, has a wrong declared size or contains trailing input.", ex!);
}
