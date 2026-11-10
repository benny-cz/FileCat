using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.Unix;

/// <summary>
/// New archives prepared in a creator-private stage folder: the sealed, verified stage is moved into the reviewed archive
/// folder with one no-replace rename between the two held folder descriptors, then its sidecars are destroyed exactly and
/// the empty private folder is removed. Nothing is ever replaced, and no FileCat file is left beside the new archive.
/// </summary>
internal sealed partial class UnixArchiveFile : IArchiveOwnedCleanupFile, IArchivePrivateCreationStage
{
    // After the move: a binding of the same open file description in the archive folder. Observation, review and reading
    // are forwarded to it; this binding no longer occupies a slot of the private folder.
    private UnixArchiveFile? createdPublic;
    private bool privateSlotReleased;

    private void ClaimIntoDestination(UnixArchiveDirectory destination, string leaf, ArchiveCreatePublicationReview review, CancellationToken ct)
    {
        UnixArchiveNative.Leaf(leaf);
        lock (CrossDirectoryExchangeGate)
        lock (destination.Gate)
        lock (parent.Gate)
        {
            Live(); review.RequireIssuer(this); ct.ThrowIfCancellationRequested();
            if (review is not CreationReview native || namespaceChanged || leaf != review.TargetLeaf || writable || !artifact ||
                createdKind != UnixArchiveArtifactKind.Stage || review.DestinationParentIdentity != destination.Identity.ToString()) throw Changed();
            parent.RequirePrivateStageOf(destination); destination.CheckCreationNamespace();
            parent.RequireName(KnownName, Identity); parent.CheckPrivate(held); Validate();
            var version = review.Stage.Version; var metadata = native.Metadata;
            if (metadata.Identity != Identity || CaptureOuterMetadata(ct).Digest != metadata.Digest || CaptureVersion(ct) != version ||
                CaptureOuterMetadata(ct).Digest != metadata.Digest) throw Changed();
            UnixArchiveNative.SyncFile(held);
            ct.ThrowIfCancellationRequested();
            string privateLeaf = KnownName;
            int result = UnixArchiveNative.Mac
                ? RenameMac(UnixArchiveNative.Fd(parent.Handle), privateLeaf, UnixArchiveNative.Fd(destination.Handle), leaf, 4 /* RENAME_EXCL */)
                : RenameLinux(UnixArchiveNative.Fd(parent.Handle), privateLeaf, UnixArchiveNative.Fd(destination.Handle), leaf, 1 /* RENAME_NOREPLACE */);
            if (result != 0) throw new IOException("The exclusive archive name could not be claimed (errno " + Marshal.GetLastPInvokeError() + ").");
            // The held object left the private folder. Never hide that, even if the reconciliation below fails.
            KnownName = leaf; namespaceChanged = true;
            parent.ReleaseChild(); privateSlotReleased = true;
            try
            {
                createdPublic = destination.AdoptMovedStage(held, leaf, Identity, maximum);
                if (parent.ObserveName(privateLeaf) is not null) throw Changed();
                destination.VerifyNamespace(); destination.RequireName(leaf, Identity);
                var after = CaptureOuterMetadata(CancellationToken.None);
                if (after.Native with { Changed = metadata.Native.Changed } != metadata.Native ||
                    after.Origin.Digest != metadata.Origin.Digest || after.AclPresent != metadata.AclPresent || !after.AclBytes.SequenceEqual(metadata.AclBytes) ||
                    after.ExtendedAttributes?.Digest != metadata.ExtendedAttributes?.Digest || CaptureVersion(CancellationToken.None) != version) throw Changed();
                UnixArchiveNative.SyncDirectory(destination.Handle); UnixArchiveNative.SyncDirectory(parent.Handle);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArchiveWriteException)
            {
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                    "The new archive left FileCat's private folder but requires reconciliation; retain both folders: " + Path.Combine(destination.DisplayPath, leaf), ex);
            }
        }
    }

    private ArchiveFileObservation ReviewPublishedInDestination(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct)
    {
        UnixArchiveFile published; ArchiveUnixOuterMetadata before;
        lock (parent.Gate)
        {
            Live(); review.RequireIssuer(this); ct.ThrowIfCancellationRequested();
            if (review is not CreationReview native || !namespaceChanged || KnownName != leaf || leaf != review.TargetLeaf || writable || createdPublic is null) throw Changed();
            published = createdPublic; before = native.Metadata;
        }
        return published.ReviewMovedPublication(review.Stage.Version, before, ct);
    }

    private ArchiveFileObservation ReviewMovedPublication(ArchiveObjectVersion version, ArchiveUnixOuterMetadata before, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            Live(); ct.ThrowIfCancellationRequested();
            parent.VerifyNamespace(); parent.CheckCreationNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(held);
            var actual = CaptureOuterMetadata(ct);
            if (actual.Native with { Changed = before.Native.Changed } != before.Native || actual.AclPresent != before.AclPresent ||
                !actual.AclBytes.SequenceEqual(before.AclBytes) || actual.ExtendedAttributes?.Digest != before.ExtendedAttributes?.Digest ||
                actual.Origin.Digest != before.Origin.Digest) throw Changed();
            var observed = ObserveRetained(ct); if (observed.Version != version) throw Changed(); return observed;
        }
    }

    // A sealed sidecar in this transaction's private stage folder, whose reviewed parent received the new archive.
    void IArchiveOwnedCleanupFile.ReviewOwnedCleanup(ArchiveContentCleanupArtifact expected, string destinationParentIdentity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var destination = parent.PrivateStageParent ?? throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest,
            "Exact Unix cleanup is limited to FileCat's private stage folder.");
        lock (destination.Gate)
        lock (parent.Gate)
        {
            Live(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind == UnixArchiveArtifactKind.Stage || writable || namespaceChanged ||
                destination.Identity.ToString() != destinationParentIdentity || Identity.ToString() != expected.Version.Identity ||
                Path.GetFileName(expected.Path) != KnownName || Path.GetDirectoryName(expected.Path) != parent.DisplayPath)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The recorded sidecar is not this transaction's private object; retain it.");
            parent.RequirePrivateStageOf(destination);
            var observed = ObserveRetained(ct);
            if (observed.Version != expected.Version || observed.MetadataDigest != expected.MetadataSha256 || observed.Origin.Digest != expected.OriginSha256)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The recorded sidecar changed after it was sealed; retain it.");
        }
    }

    void IArchiveOwnedCleanupFile.DestroyOwnedCleanup(ArchiveContentCleanupArtifact expected, string destinationParentIdentity, CancellationToken ct)
    {
        ((IArchiveOwnedCleanupFile)this).ReviewOwnedCleanup(expected, destinationParentIdentity, ct);
        DestroyOwned(expected.Version, expected.MetadataSha256, ct);
    }

    void IArchivePrivateCreationStage.RemovePrivateStageFolder(CancellationToken ct)
    {
        lock (parent.Gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (createdPublic is null || !privateSlotReleased || parent.PrivateStageParent is null)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Only a stage moved out of its private folder can remove that folder.");
        }
        parent.RemoveEmptyPrivateStageDirectory(ct);
    }
}
