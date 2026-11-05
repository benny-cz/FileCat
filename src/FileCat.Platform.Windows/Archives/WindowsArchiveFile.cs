using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Archives;

/// <summary>
/// A bounded stream over the exact parent-owned file. No SafeFileHandle is exposed to a codec/worker. Dispose only
/// releases handles: it never deletes evidence. Rename/delete primitives must be called by a coordinator only after
/// the corresponding durable journal intent. They are not a complete publication or recovery protocol.
/// </summary>
internal sealed partial class WindowsArchiveFile : Stream
{
    private readonly WindowsArchiveDirectory parent;
    private readonly SafeFileHandle handle;
    private readonly long maximumLength;
    private readonly bool artifact, mayRename;
    private readonly bool createdWritable;
    private bool writable, disposed, removed;
    private long position;
    public FileIdentity Identity { get; }
    public string KnownName { get; private set; }
    public string DisplayPath => Path.Combine(parent.DisplayPath, KnownName); // Evidence, never mutation authority.
    public uint LinkCount { get { lock (parent.Gate) { EnsureLive(); return Validate(); } } }

    internal WindowsArchiveFile(WindowsArchiveDirectory parent, SafeFileHandle handle, string leaf, FileIdentity identity,
        long maximumLength, bool artifact, bool writable, bool mayRename)
    {
        this.parent = parent; this.handle = handle; KnownName = leaf; Identity = identity;
        this.maximumLength = maximumLength; this.artifact = artifact; this.writable = writable; createdWritable = writable; this.mayRename = mayRename;
    }

    public override bool CanRead { get { lock (parent.Gate) return !disposed && !removed; } }
    public override bool CanSeek => CanRead;
    public override bool CanWrite { get { lock (parent.Gate) return !disposed && !removed && writable; } }
    public override long Length { get { lock (parent.Gate) { EnsureLive(); return RandomAccess.GetLength(handle); } } }
    public override long Position
    {
        get { lock (parent.Gate) { EnsureLive(); return position; } }
        set { lock (parent.Gate) { EnsureLive(); CheckRange(value, 0); position = value; } }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        lock (parent.Gate)
        {
            EnsureLive();
            // Physical input length is admitted at open; never grant an out-of-budget read if a mapped writer grew it.
            if (RandomAccess.GetLength(handle) > maximumLength) throw Limit();
            int count = (int)Math.Min(buffer.Length, maximumLength - position);
            int read = RandomAccess.Read(handle, buffer[..count], position); position += read; return read;
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        lock (parent.Gate)
        {
            EnsureWritable(); CheckRange(position, buffer.Length);
            RandomAccess.Write(handle, buffer, position); position += buffer.Length;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (parent.Gate)
        {
            EnsureLive();
            long start = origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => position,
                SeekOrigin.End => RandomAccess.GetLength(handle), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            long next;
            try { next = checked(start + offset); } catch (OverflowException) { throw Limit(); }
            CheckRange(next, 0); return position = next;
        }
    }

    public override void SetLength(long value)
    {
        lock (parent.Gate) { EnsureWritable(); CheckRange(value, 0); RandomAccess.SetLength(handle, value); }
    }

    public override void Flush()
    {
        lock (parent.Gate) { EnsureLive(); if (writable) WindowsArchiveNative.Flush(handle); }
    }

    public ArchiveObjectVersion Seal(CancellationToken cancellationToken = default)
    {
        lock (parent.Gate)
        {
            EnsureWritable(); writable = false; // A failed/canceled seal cannot resume writes into reviewed evidence.
            Validate(); parent.CheckPrivate(handle); WindowsArchiveNative.Flush(handle);
            return CaptureVersion(cancellationToken);
        }
    }

    public ArchiveObjectVersion CaptureVersion(CancellationToken cancellationToken = default)
    {
        lock (parent.Gate)
        {
            EnsureLive(); Validate();
            if (writable) throw new InvalidOperationException("Seal the private archive file before reviewing its version.");
            long length = RandomAccess.GetLength(handle);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[1 << 20]; long offset = 0;
            while (offset < length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset);
                if (read == 0) throw new IOException("The archive file changed while being reviewed.");
                hash.AppendData(buffer.AsSpan(0, read)); offset += read;
            }
            cancellationToken.ThrowIfCancellationRequested(); Validate();
            if (RandomAccess.GetLength(handle) != length) throw new IOException("The archive file changed while being reviewed.");
            return new(Identity.ToString(), length, Convert.ToHexString(hash.GetHashAndReset()));
        }
    }

    public void RenameNoReplace(string leaf, ArchiveObjectVersion expected, WindowsArchiveOuterMetadata expectedMetadata,
        CancellationToken cancellationToken = default)
    {
        WindowsArchiveDirectory.ValidateLeaf(leaf);
        lock (parent.Gate)
        {
            ReviewForMutation(expected, expectedMetadata, cancellationToken);
            NtFile.Rename(handle, parent.Handle, leaf, replace: false);
            KnownName = leaf;
        }
    }

    public void DeleteReviewed(ArchiveObjectVersion expected, WindowsArchiveOuterMetadata expectedMetadata,
        CancellationToken cancellationToken = default)
    {
        lock (parent.Gate)
        {
            ReviewForMutation(expected, expectedMetadata, cancellationToken);
            WindowsArchiveNative.DeleteExact(handle);
            // POSIX disposition unlinks when this deleting handle closes, even while other readers retain the file.
            // Do not report cleanup complete while our own still-open handle keeps the name present.
            removed = true; handle.Dispose();
        }
    }

    private void ReviewForMutation(ArchiveObjectVersion expected, WindowsArchiveOuterMetadata expectedMetadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(expectedMetadata); EnsureLive();
        if (!mayRename || writable) throw new InvalidOperationException("This archive lease has no reviewed namespace authority.");
        if (expected.Identity != Identity.ToString() || expectedMetadata.Identity != Identity)
            throw new IOException("A different archive object was reviewed; retain this object.");
        Validate(); // Preserve the specific read-only/type/physical-limit refusal before generic metadata capture.
        if (artifact) parent.CheckPrivate(handle);
        RequireMetadata();
        if (CaptureVersion(cancellationToken) != expected) throw new IOException("The archive object changed; retain it for review.");
        RequireMetadata();
        cancellationToken.ThrowIfCancellationRequested();
        void RequireMetadata()
        {
            if (CaptureOuterMetadata(cancellationToken).Digest != expectedMetadata.Digest)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                    "The archive object's outer metadata or origin mark changed; retain it without renaming or deletion.");
        }
    }

    private uint Validate() => WindowsArchiveNative.Regular(handle, maximumLength, artifact, mayRename);
    private void EnsureLive()
    { ObjectDisposedException.ThrowIf(disposed, this); if (removed) throw new IOException("The archive object's held name was removed."); }
    private void EnsureWritable()
    { EnsureLive(); if (!writable) throw new NotSupportedException("This archive file lease is read-only."); }
    private void CheckRange(long offset, int count)
    { if (offset < 0 || count < 0 || offset > maximumLength || count > maximumLength - offset) throw Limit(); }
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The archive file request exceeds its physical byte limit.");

    protected override void Dispose(bool disposing)
    {
        lock (parent.Gate)
        {
            if (!disposed) { disposed = true; handle.Dispose(); parent.ReleaseChild(); }
        }
        base.Dispose(disposing);
    }
}
