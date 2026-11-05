using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

/// <summary>Parent-owned bounded held-inode stream. Disposal retains evidence. No handle is granted to a codec/worker.</summary>
internal sealed partial class UnixArchiveFile : Stream, IArchiveRetainedStage
{
    private readonly UnixArchiveDirectory parent;
    private readonly SafeFileHandle held;
    private readonly long maximum;
    private readonly bool artifact, createdWritable;
    private readonly UnixArchiveArtifactKind? createdKind;
    private bool rebuildClaimed, evidenceClaimed;
    private bool writable, disposed, namespaceChanged;
    private long position;
    internal FileIdentity Identity { get; }
    internal string KnownName { get; private set; }
    internal string DisplayPath => Path.Combine(parent.DisplayPath, KnownName);
    internal UnixArchiveFile(UnixArchiveDirectory parent, SafeFileHandle held, string leaf, FileIdentity identity, long maximum, bool artifact, bool writable, UnixArchiveArtifactKind? createdKind = null)
    { this.parent = parent; this.held = held; KnownName = leaf; Identity = identity; this.maximum = maximum; this.artifact = artifact; this.writable = writable; createdWritable = writable; this.createdKind = createdKind; }
    public override bool CanRead { get { lock (parent.Gate) return !disposed; } }
    public override bool CanSeek => CanRead;
    public override bool CanWrite { get { lock (parent.Gate) return !disposed && writable; } }
    public override long Length { get { lock (parent.Gate) { Live(); return Validate().Size; } } }
    public override long Position
    {
        get { lock (parent.Gate) { Live(); return position; } }
        set { lock (parent.Gate) { Live(); Range(value, 0); position = value; } }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        lock (parent.Gate)
        {
            Live(); Validate(); int count = (int)Math.Min(buffer.Length, maximum - position);
            int read = RandomAccess.Read(held, buffer[..count], position); position += read; return read;
        }
    }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        lock (parent.Gate)
        { Writeable(); Range(position, buffer.Length); Validate(); parent.CheckPrivate(held); RandomAccess.Write(held, buffer, position); position += buffer.Length; }
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (parent.Gate)
        {
            Live(); long start = origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => position, SeekOrigin.End => Length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            long next; try { next = checked(start + offset); } catch (OverflowException) { throw Budget(); }
            Range(next, 0); return position = next;
        }
    }
    public override void SetLength(long value)
    { lock (parent.Gate) { Writeable(); Range(value, 0); Validate(); parent.CheckPrivate(held); RandomAccess.SetLength(held, value); } }
    public override void Flush() { lock (parent.Gate) { Live(); if (writable) UnixArchiveNative.SyncFile(held); } }
    internal ArchiveObjectVersion Seal(CancellationToken ct = default)
    {
        lock (parent.Gate)
        { Writeable(); writable = false; ct.ThrowIfCancellationRequested(); Validate(); parent.CheckPrivate(held); UnixArchiveNative.SyncFile(held); return CaptureVersion(ct); }
    }
    internal ArchiveUnixOuterMetadata CaptureOuterMetadata(CancellationToken ct = default)
    { lock (parent.Gate) { Live(); if (writable) throw new InvalidOperationException("Seal the private artifact before reviewing metadata."); Validate(); return ArchiveUnixOuterMetadata.Capture(held, ct); } }
    internal ArchiveObjectVersion CaptureVersion(CancellationToken ct = default)
    {
        lock (parent.Gate)
        {
            Live(); if (writable) throw new InvalidOperationException("Seal the private artifact before reviewing its version.");
            var before = CaptureOuterMetadata(ct); long length = before.Native.Length; long offset = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] bytes = new byte[1 << 20];
            while (offset < length)
            {
                ct.ThrowIfCancellationRequested(); int read = RandomAccess.Read(held, bytes.AsSpan(0, (int)Math.Min(bytes.Length, length - offset)), offset);
                if (read == 0) throw Changed(); hash.AppendData(bytes.AsSpan(0, read)); offset += read;
            }
            ct.ThrowIfCancellationRequested(); Validate(); if (CaptureOuterMetadata(ct).Digest != before.Digest) throw Changed();
            return new(Identity.ToString(), length, Convert.ToHexString(hash.GetHashAndReset()));
        }
    }
    internal void RenameNoReplace(string leaf, ArchiveObjectVersion version, ArchiveUnixOuterMetadata metadata, CancellationToken ct = default)
    {
        UnixArchiveNative.Leaf(leaf); ArgumentNullException.ThrowIfNull(version); ArgumentNullException.ThrowIfNull(metadata);
        lock (parent.Gate)
        {
            Live(); if (!artifact || writable) throw new InvalidOperationException("Only a sealed private artifact has namespace authority.");
            if (leaf == KnownName) throw new ArgumentException("A distinct publication name is required.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(held); Validate();
            if (version.Identity != Identity.ToString() || metadata.Identity != Identity || CaptureOuterMetadata(ct).Digest != metadata.Digest || CaptureVersion(ct) != version || CaptureOuterMetadata(ct).Digest != metadata.Digest)
                throw Changed();
            UnixArchiveNative.SyncFile(held);
            ct.ThrowIfCancellationRequested();
            int result = UnixArchiveNative.Mac ? RenameMac(UnixArchiveNative.Fd(parent.Handle), KnownName, UnixArchiveNative.Fd(parent.Handle), leaf, 4 /* EXCL */)
                : RenameLinux(UnixArchiveNative.Fd(parent.Handle), KnownName, UnixArchiveNative.Fd(parent.Handle), leaf, 1 /* NOREPLACE */);
            if (result != 0) throw new IOException("The exclusive archive name could not be claimed (errno " + Marshal.GetLastPInvokeError() + ").");
            KnownName = leaf; namespaceChanged = true; // The syscall happened. Never hide this fact if post-review/durability fails.
            try
            {
                // Unix pathname rename is not a conditional held-inode operation. Reconcile the actual destination,
                // content and promised fields after it; any anomaly is NeedsReview, never pathname cleanup/rollback.
                parent.VerifyNamespace(); parent.RequireName(leaf, Identity); parent.CheckPrivate(held);
                var after = CaptureOuterMetadata(CancellationToken.None);
                if (after.Native with { Changed = metadata.Native.Changed } != metadata.Native ||
                    after.Origin.Digest != metadata.Origin.Digest || after.AclPresent != metadata.AclPresent || !after.AclBytes.SequenceEqual(metadata.AclBytes) ||
                    CaptureVersion(CancellationToken.None) != version) throw Changed();
                UnixArchiveNative.SyncDirectory(parent.Handle);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive rename occurred but requires reconciliation; retain the held object and both names: " + DisplayPath); }
        }
    }
    internal void DeleteReviewed(ArchiveObjectVersion version, ArchiveUnixOuterMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(version); ArgumentNullException.ThrowIfNull(metadata);
        lock (parent.Gate) { Live(); throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "No qualified exact-object Unix unlink primitive is available; retain this artifact for review."); }
    }
    private UnixStat Validate()
    {
        var info = UnixFiles.Stat(held) ?? throw UnixArchiveNative.Unavailable();
        if (!info.IsRegular || info.Identity != Identity) throw Changed();
        if (info.Size < 0 || info.Size > maximum) throw Budget();
        // Link count is obtained in the complete native snapshot. Private admission/namespace review additionally
        // checks it there; no assumption is made that Unix sharing modes exclude another writer or rename.
        return info;
    }
    private void Live() => ObjectDisposedException.ThrowIf(disposed, this);
    private void Writeable() { Live(); if (!writable) throw new NotSupportedException("The held Unix archive is read-only."); }
    private void Range(long offset, int count) { if (offset < 0 || offset > maximum || count < 0 || count > maximum - offset) throw Budget(); }
    private static ArchiveWriteException Budget() => new(ArchiveWriteReason.BudgetExceeded, "The Unix archive request exceeds its physical byte bound.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The held Unix archive content or metadata changed; retain it for review.");
    protected override void Dispose(bool disposing)
    { lock (parent.Gate) { if (!disposed) { disposed = true; held.Dispose(); parent.ReleaseChild(); } } base.Dispose(disposing); }
    [LibraryImport("libc", EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameLinux(int sourceParent, string source, int destinationParent, string destination, uint flags);
    [LibraryImport("libc", EntryPoint = "renameatx_np", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameMac(int sourceParent, string source, int destinationParent, string destination, uint flags);
}
