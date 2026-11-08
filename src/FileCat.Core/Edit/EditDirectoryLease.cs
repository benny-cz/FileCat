using FileCat.Core.Jobs;
using FileCat.Core.Archives;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

/// <summary>Trusted parent-only namespace authority. Paths/identities are evidence; they never grant mutation authority.</summary>
internal abstract class EditDirectoryLease : IDisposable
{
    protected readonly object Gate = new();
    private int references = 1;
    private bool disposed;
    internal abstract string Path { get; }
    internal abstract string Identity { get; }
    internal abstract SafeFileHandle Handle { get; }
    internal abstract EditDirectoryLease CreateDirectory(string leaf, CancellationToken ct);
    internal abstract EditDirectoryLease OpenDirectory(string leaf, string? expectedIdentity, CancellationToken ct);
    internal abstract FileStream CreateFile(string leaf, CancellationToken ct);
    internal abstract FileStream OpenFile(string leaf, string? expectedIdentity, CancellationToken ct);
    protected abstract void ReviewCore(CancellationToken ct);
    protected abstract void CloseCore();

    protected void RequireOpen() => ObjectDisposedException.ThrowIf(disposed, this);
    internal void Review(CancellationToken ct) { lock (Gate) { RequireOpen(); ReviewCore(ct); } }
    // A child retains its parent's native namespace after the public parent owner has left.
    internal void ReviewRetained(CancellationToken ct)
    { lock (Gate) { if (references == 0) throw new ObjectDisposedException(GetType().Name); ReviewCore(ct); } }
    internal IDisposable RetainForChild()
    {
        lock (Gate) { RequireOpen(); references++; return new ChildOwner(this); }
    }
    private void Release()
    { lock (Gate) { if (--references == 0) CloseCore(); } }
    public void Dispose()
    { lock (Gate) { if (disposed) return; disposed = true; Release(); } }
    private sealed class ChildOwner(EditDirectoryLease parent) : IDisposable
    {
        private EditDirectoryLease? owner = parent;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
    protected static void Leaf(string value)
    {
        if (SafeNames.Validate(value) is { } problem) throw new ArgumentException(problem, nameof(value));
        if (ArchiveWritePolicy.Utf8.GetByteCount(value) > 255) throw new ArgumentException("The edit filename is too long.", nameof(value));
    }
}
