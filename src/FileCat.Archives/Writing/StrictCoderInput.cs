using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Buffered exact coder input; read-ahead never counts as consumption. The source remains borrowed.</summary>
internal sealed class StrictCoderInput(Stream source, long length, CancellationToken ct) : Stream
{
    private readonly byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
    private long fetched;
    private int offset, count;
    private bool disposed;
    internal long Consumed { get; private set; }
    private void Fill()
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (offset != count || fetched == length) return;
        int wanted = (int)Math.Min(buffer.Length, length - fetched); offset = 0; count = source.Read(buffer.AsSpan(0, wanted));
        if (count <= 0 || count > wanted) throw Invalid(); fetched += count;
    }
    public override int ReadByte()
    { Fill(); if (offset == count) return -1; Consumed++; return buffer[offset++]; }
    public override int Read(byte[] bytes, int start, int size) => Read(bytes.AsSpan(start, size));
    public override int Read(Span<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (bytes.IsEmpty) return 0; Fill(); int n = Math.Min(bytes.Length, count - offset);
        buffer.AsSpan(offset, n).CopyTo(bytes); offset += n; Consumed += n; return n;
    }
    internal void VerifyEnd()
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (Consumed != length || fetched != length || offset != count || source.ReadByte() != -1) throw Invalid();
    }
    protected override void Dispose(bool disposing)
    { if (disposing && !disposed) { disposed = true; CryptographicOperations.ZeroMemory(buffer); } base.Dispose(disposing); }
    public override bool CanRead => !disposed; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => length; public override long Position { get => Consumed; set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] bytes, int offset, int count) => throw new NotSupportedException();
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidArchive, "The compressed coder input is incomplete, has trailing bytes or violates its read grant.");
}
