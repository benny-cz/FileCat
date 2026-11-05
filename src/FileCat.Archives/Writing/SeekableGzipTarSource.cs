using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>At most three forward decompression passes over the same retained physical grant. No payload spool.</summary>
internal sealed class SeekableGzipTarSource : Stream
{
    private readonly Stream source;
    private readonly long length;
    private readonly byte operatingSystem, extraFlags;
    private readonly CancellationToken ct;
    private readonly byte[] skip = new byte[64 * 1024];
    private StrictGzipReadStream? decoder;
    private long position;
    private int passes;
    private bool failed, disposed, ended;
    internal SeekableGzipTarSource(Stream source, long length, byte operatingSystem, byte extraFlags, CancellationToken ct)
    {
        if (length is < 1024 or > ArchiveWritePolicy.MaxPhysicalBytes) throw Changed();
        this.source = source; this.length = length; this.operatingSystem = operatingSystem; this.extraFlags = extraFlags; this.ct = ct;
        Reset();
    }
    private void Live() { ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested(); if (failed) throw Changed(); }
    private void Reset()
    {
        if (passes == 3) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The TAR.GZ replay exceeded its fixed pass budget.");
        decoder?.Dispose(); source.Position = 0; decoder = new(source, ct); passes++;
        if (decoder.OperatingSystem != operatingSystem || decoder.ExtraFlags != extraFlags) { decoder.Dispose(); throw Changed(); }
        position = 0; ended = false;
    }
    internal void Complete()
    { while (Read(skip) != 0) { } }
    public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
    public override int Read(Span<byte> buffer)
    {
        Live(); if (buffer.IsEmpty) return 0;
        try
        {
            if (position == length)
            {
                if (!ended) { if (decoder!.ReadByte() != -1) throw Changed(); ended = true; } return 0;
            }
            int count = decoder!.Read(buffer[..(int)Math.Min(buffer.Length, length - position)]);
            if (count == 0) throw Changed(); position += count; return count;
        }
        catch { failed = true; throw; }
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        Live();
        try
        {
            long target = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(position + offset), SeekOrigin.End => checked(length + offset), _ => throw Changed() };
            if (target < 0 || target > length) throw Changed();
            if (target < position) { Complete(); Reset(); }
            while (position < target)
                if (Read(skip.AsSpan(0, (int)Math.Min(skip.Length, target - position))) == 0) throw Changed();
            return position;
        }
        catch (OverflowException) { failed = true; throw Changed(); }
        catch { failed = true; throw; }
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; decoder?.Dispose(); } base.Dispose(disposing); }
    public override bool CanRead => !disposed && !failed;
    public override bool CanSeek => !disposed && !failed;
    public override bool CanWrite => false;
    public override long Length { get { Live(); return length; } }
    public override long Position { get { Live(); return position; } set => Seek(value, SeekOrigin.Begin); }
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long n) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The decompressed TAR length, wrapper or replay state differs from its reviewed catalog.");
}
