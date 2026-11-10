using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile
{
    private bool cleanupOwned, removed;
    internal bool IsRemoved { get { lock (parent.Gate) return removed; } }

    internal void MarkCleanupOwned() { lock (parent.Gate) { Live(); cleanupOwned = true; } }

    /// <summary>
    /// Exact cleanup of one recorded transaction object held by this process, in a creator-owned directory that grants no
    /// group/other namespace writes (see UNIX_PRIVATE_CLEANUP_DECISION.md). The content is destroyed first through the held
    /// descriptor, so no copy of archive data survives a later name race. The name is then removed only after it is
    /// re-observed to bind the held object; afterwards the held object's link count must be zero. If an equivalent-
    /// privilege process substitutes the name between those calls, the held object keeps its link: that is reported
    /// for review and nothing else is touched. Resumable: an already emptied held object proceeds to name removal.
    /// </summary>
    internal void DestroyOwned(ArchiveObjectVersion expected, string? expectedMetadataDigest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (parent.Gate)
        {
            Live(); ct.ThrowIfCancellationRequested();
            if (!(cleanupOwned || artifact && createdWritable) || writable)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Only this transaction's recorded private objects can be cleaned.");
            parent.VerifyNamespace(); parent.CheckCreationNamespace(); parent.RequireName(KnownName, Identity);
            var metadata = CaptureOuterMetadata(ct);
            if (Identity.ToString() != expected.Identity || metadata.Native.LinkCount != 1 || metadata.Native.Owner != UnixArchiveNative.GetEuid())
                throw Review("The recorded transaction object has another identity, owner or link; retain it.");
            int fd = UnixArchiveNative.Fd(held);
            if (metadata.Native.Length != 0 || expected.Length == 0)
            {
                if (metadata.Native.Length != expected.Length) throw Review("The recorded transaction object changed size; retain it.");
                if (expectedMetadataDigest is not null ? metadata.Digest != expectedMetadataDigest : CaptureVersion(ct) != expected)
                    throw Review("The recorded transaction object changed after it was reconciled; retain it.");
                ct.ThrowIfCancellationRequested();
                if (Ftruncate(fd, 0) != 0) throw Review("The transaction object's content could not be removed (errno " + Marshal.GetLastPInvokeError() + "); retain it.");
                UnixArchiveNative.SyncFile(held);
            }
            // Content is gone from the exact object. Finish name removal without cancellation.
            if (Validate().Size != 0) throw Review("The transaction object was rewritten during cleanup; retain it.");
            parent.RequireName(KnownName, Identity);
            if (UnlinkAt(UnixArchiveNative.Fd(parent.Handle), KnownName, 0) != 0)
                throw Review("The emptied transaction object's name could not be removed (errno " + Marshal.GetLastPInvokeError() + "); retain it.");
            removed = true;
            ulong links = ArchiveUnixOuterMetadata.Capture(held).Native.LinkCount;
            UnixArchiveNative.SyncDirectory(parent.Handle);
            if (links != 0)
                throw Review("Another object replaced FileCat's private transaction name during cleanup and was removed; the emptied object remains linked elsewhere.");
        }
    }

    private static ArchiveWriteException Review(string message) => new(ArchiveWriteReason.NeedsReview, message);
    [LibraryImport("libc", EntryPoint = "ftruncate", SetLastError = true)]
    private static partial int Ftruncate(int fd, long length);
    [LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int UnlinkAt(int directory, string leaf, int flags);
}
