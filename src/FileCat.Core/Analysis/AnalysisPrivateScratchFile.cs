using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Analysis;

/// <summary>Offset I/O on an owned temporary file, for admitted workers. Never resolves or publishes a pathname.</summary>
internal abstract class AnalysisPrivateScratchFile : IDisposable
{
    public abstract long Length { get; }
    public abstract void Verify();
    public abstract int ReadAt(Span<byte> buffer, long offset);
    public abstract void WriteAt(ReadOnlySpan<byte> buffer, long offset);
    public abstract void Dispose();
}

/// <summary>No FileStream buffering: an unsuccessful append cannot flush again during a later checkpoint read.</summary>
internal sealed class AnalysisHandleScratchFile(SafeFileHandle handle, Action verify, Action close) : AnalysisPrivateScratchFile
{
    private int _disposed;
    public override void Verify()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        verify();
    }
    public override long Length { get { Verify(); return RandomAccess.GetLength(handle); } }
    private void Extent(long offset, int count)
    {
        if (offset < 0 || offset > AnalysisLimits.ScratchBytes || count > AnalysisLimits.ScratchBytes - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        Verify();
    }
    public override int ReadAt(Span<byte> buffer, long offset)
    { Extent(offset, buffer.Length); return RandomAccess.Read(handle, buffer, offset); }
    public override void WriteAt(ReadOnlySpan<byte> buffer, long offset)
    { Extent(offset, buffer.Length); RandomAccess.Write(handle, buffer, offset); }
    public override void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) close(); }
}
