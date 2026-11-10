namespace FileCat.Core.Archives;

/// <summary>A lazy source file and its ephemeral native parent have one lifetime; no handle per catalog member.</summary>
internal sealed class ArchiveOwnedSourceFile<T>(T file, IDisposable? parent) : Stream, IArchiveRetainedFile where T : Stream, IArchiveRetainedFile
{
    private int disposed;
    public string ObjectIdentity => file.ObjectIdentity;
    public ArchiveFileObservation Observe(CancellationToken ct) => file.Observe(ct);
    public void Review(ArchiveFileObservation expected, CancellationToken ct) => file.Review(expected, ct);
    public void ReviewMetadata(ArchiveFileObservation expected, CancellationToken ct) => file.ReviewMetadata(expected, ct);
    public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => file.OpenReadView(expected, ct);
    public override bool CanRead => Volatile.Read(ref disposed) == 0 && file.CanRead;
    public override bool CanSeek => CanRead;
    public override bool CanWrite => false;
    public override long Length => file.Length;
    public override long Position { get => file.Position; set => file.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => file.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => file.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) { try { file.Dispose(); } finally { parent?.Dispose(); } }
        base.Dispose(disposing);
    }
}
