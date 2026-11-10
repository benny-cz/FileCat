using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile
{
    /// <summary>
    /// Plan §11.6.1: remove one captured source of a move, after verified publication. Unix has no conditional unlink of a
    /// held inode, so the source is first re-reviewed through its held descriptor (exact archived bytes, metadata and
    /// origin), then renamed aside to an exclusive name in the same folder. Only when that name binds this held object is it
    /// unlinked; the held object must then have no link left. Another object that took the original name is put back, a
    /// hard-linked original is kept (removing one name would not move its content), and nothing is removed by a bare path.
    /// </summary>
    internal void RemoveCapturedSource(ArchiveFileObservation expected, CancellationToken ct, string? asideLeaf = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (asideLeaf is not null) UnixArchiveNative.Leaf(asideLeaf);
        lock (parent.Gate)
        {
            Live(); ct.ThrowIfCancellationRequested();
            if (artifact || writable || cleanupOwned || namespaceChanged)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Source removal requires a reviewed captured source.");
            // A link added since capture: say so plainly, before the generic review refuses the binding.
            if (CaptureOuterMetadata(ct).Native.LinkCount != 1)
                throw Kept("The original has other hard links, so removing this name would not move its content; it was kept.");
            ((IArchiveRetainedFile)this).Review(expected, ct);
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
            int folder = UnixArchiveNative.Fd(parent.Handle); string original = KnownName;
            string aside = asideLeaf ?? ".filecat-move-" + Guid.NewGuid().ToString("N");
            ct.ThrowIfCancellationRequested();
            if (RenameAt(folder, original, aside) != 0)
                throw new IOException("The original could not be set aside for removal (errno " + Marshal.GetLastPInvokeError() + "); it was kept.");
            // The name changed. Finish without cancellation and never leave the original under the temporary name silently.
            KnownName = aside; namespaceChanged = true;
            if (parent.ObserveName(aside) != Identity)
            {
                // Another object took the original name between the review and the rename: put it back exactly.
                if (RenameAt(folder, aside, original) != 0)
                    throw Review("Another object replaced the original while it was being removed; that object is kept as " +
                        Path.Combine(parent.DisplayPath, aside) + ".");
                KnownName = original;
                throw Kept("The original was replaced while it was being removed; it was kept.");
            }
            if (UnlinkAt(folder, aside, 0) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (RenameAt(folder, aside, original) == 0) KnownName = original;
                throw KnownName == original
                    ? new IOException("The original could not be removed (errno " + error + "); it was kept.")
                    : Review("The original could not be removed (errno " + error + ") and is kept as " + Path.Combine(parent.DisplayPath, aside) + ".");
            }
            removed = true;
            ulong links = ArchiveUnixOuterMetadata.Capture(held).Native.LinkCount;
            UnixArchiveNative.SyncDirectory(parent.Handle);
            if (links != 0) throw Review("Another object took FileCat's temporary removal name; the original remains linked elsewhere.");
        }
    }

    private static ArchiveWriteException Kept(string message) => new(ArchiveWriteReason.SourceChanged, message);

    private static int RenameAt(int folder, string from, string to) => UnixArchiveNative.Mac
        ? RenameMac(folder, from, folder, to, 4 /* RENAME_EXCL */)
        : RenameLinux(folder, from, folder, to, 1 /* RENAME_NOREPLACE */);
}
