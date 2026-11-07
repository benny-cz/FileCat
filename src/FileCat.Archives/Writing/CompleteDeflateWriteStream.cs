using System.IO.Compression;

namespace FileCat.Archives.Writing;

/// <summary>Borrowed output, including a complete raw-Deflate stream when a BCJ2 branch receives no bytes.</summary>
internal sealed class CompleteDeflateWriteStream(Stream output, CompressionLevel level, CancellationToken ct) : Stream
{
    private DeflateStream? codec;
    private bool disposed, failed;
    public override void Write(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (failed) throw new IOException("The Deflate encoder has failed."); if (bytes.IsEmpty) return;
        try { (codec ??= new(output, level, leaveOpen: true)).Write(bytes); }
        catch { failed = true; throw; }
    }
    public override void Write(byte[] bytes, int offset, int count) => Write(bytes.AsSpan(offset, count));
    public override void Flush()
    { ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested(); if (codec is not null) codec.Flush(); else output.Flush(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            if (codec is not null) codec.Dispose();
            else if (!failed)
            {
                ct.ThrowIfCancellationRequested();
                // BFINAL=1, fixed Huffman block, end-of-block symbol. The BCL intentionally suppresses output
                // when it has received no input, whereas each declared 7z coder needs a complete bitstream.
                output.Write((ReadOnlySpan<byte>)[3, 0]);
            }
        }
        base.Dispose(disposing);
    }
    public override bool CanWrite => !disposed && !failed; public override bool CanRead => false; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long n) => throw new NotSupportedException();
}
