using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile
{
    private static readonly object CrossDirectoryExchangeGate = new();

    /// <summary>
    /// Unregistered cross-directory swap of one sealed private stage and one public original. The transaction
    /// caller must first durably record both parent/name identities and preserve the receipt or uncertainty for
    /// recovery. Neither child remains usable after a syscall attempt: their immutable parent locks no longer
    /// describe their namespace bindings. No pathname cleanup or reverse exchange is authorized here.
    /// </summary>
    internal UnixArchiveCrossDirectoryReceipt ExchangeRetainedAcrossParents(
        UnixArchiveFile original, ArchiveFileObservation expectedStage, ArchiveFileObservation expectedOriginal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(expectedStage);
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        if (ReferenceEquals(this, original) || ReferenceEquals(parent, original.parent))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest,
                "Cross-directory exchange requires distinct held objects and parents.");
        // No caller may enter this operation while holding either parent Gate. Normal child operations only
        // acquire one Gate. A single pair gate makes the public-then-private lock order deterministic.
        lock (CrossDirectoryExchangeGate)
        lock (original.parent.Gate)
        lock (parent.Gate)
        {
            Live(); original.Live(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != UnixArchiveArtifactKind.Stage || !rebuildClaimed ||
                !verificationSealed || evidenceClaimed || writable || namespaceChanged ||
                original.artifact || original.writable || original.namespaceChanged)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest,
                    "Cross-directory exchange requires one sealed private stage and one unchanged held original.");
            parent.RequirePrivateStageOf(original.parent);
            original.parent.CheckCreationNamespace();
            parent.CheckCreationNamespace();
            original.parent.RequireName(original.KnownName, original.Identity);
            parent.RequireName(KnownName, Identity);
            parent.CheckPrivate(held);
            ((IArchiveRetainedFile)this).Review(expectedStage, ct);
            ((IArchiveRetainedFile)original).Review(expectedOriginal, ct);
            var stageMetadata = CaptureOuterMetadata(ct);
            var originalMetadata = original.CaptureOuterMetadata(ct);
            stageMetadata.RequireSupportedExtendedAttributes();
            originalMetadata.RequireSupportedExtendedAttributes();
            if (stageMetadata.Native.LinkCount != 1 || originalMetadata.Native.LinkCount != 1)
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature,
                    "Cross-directory archive exchange requires single-link objects.");
            UnixArchiveNative.SyncFile(held);
            UnixArchiveNative.SyncDirectory(parent.Handle);
            ct.ThrowIfCancellationRequested();

            string stageName = KnownName, targetName = original.KnownName;
            bool attempted = false;
            try
            {
                int sourceDescriptor = UnixArchiveNative.Fd(parent.Handle);
                int targetDescriptor = UnixArchiveNative.Fd(original.parent.Handle);
                attempted = true;
                int result = UnixArchiveNative.Mac
                    ? RenameMac(sourceDescriptor, stageName, targetDescriptor, targetName, 2 /* RENAME_SWAP */)
                    : RenameLinux(sourceDescriptor, stageName, targetDescriptor, targetName, 2 /* RENAME_EXCHANGE */);
                if (result != 0)
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                        "The cross-directory exchange failed or its effect is uncertain (errno " + Marshal.GetLastPInvokeError() + "); retain both held objects and names.");
                // Cancellation cannot hide a possible namespace effect. Recheck both held inodes and the exact
                // opposite-parent names, then sync both parents before returning a terminal receipt.
                original.parent.VerifyNamespace(); parent.VerifyNamespace();
                original.parent.RequireName(targetName, Identity);
                parent.RequireName(stageName, original.Identity);
                var publishedMetadata = CaptureOuterMetadata(CancellationToken.None);
                var retiredMetadata = original.CaptureOuterMetadata(CancellationToken.None);
                if (!SameExceptChanged(publishedMetadata, stageMetadata) ||
                    !SameExceptChanged(retiredMetadata, originalMetadata) ||
                    CaptureVersion(CancellationToken.None) != expectedStage.Version ||
                    original.CaptureVersion(CancellationToken.None) != expectedOriginal.Version)
                    throw Changed();
                UnixArchiveNative.SyncDirectory(original.parent.Handle);
                UnixArchiveNative.SyncDirectory(parent.Handle);
                return new(original.parent.Identity.ToString(), targetName, parent.Identity.ToString(), stageName,
                    expectedStage.Version, expectedOriginal.Version);
            }
            catch (Exception ex) when (attempted && ex is IOException or UnauthorizedAccessException or ArchiveWriteException)
            {
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                    "The cross-directory exchange may have changed both names; retain the held objects and journal for reconciliation.");
            }
            finally
            {
                if (attempted)
                {
                    crossParentTerminal = true;
                    original.crossParentTerminal = true;
                }
            }
        }
    }

    /// <summary>
    /// Exchanges two held names in one pinned directory. The caller must durably record intent first. This is only
    /// a native primitive: it neither authorizes an update nor cleans a retired object by pathname. In particular,
    /// neither Unix syscall conditions the exchange on the reviewed target inode still occupying its name.
    /// </summary>
    internal (ArchiveFileObservation Published, ArchiveFileObservation Retired) ExchangeRetained(
        UnixArchiveFile original, ArchiveFileObservation expectedStage, ArchiveFileObservation expectedOriginal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(expectedStage);
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        if (ReferenceEquals(this, original) || !ReferenceEquals(parent, original.parent))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Archive exchange requires two distinct held objects in one native directory.");

        lock (parent.Gate)
        {
            Live(); original.Live(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != UnixArchiveArtifactKind.Stage || !rebuildClaimed || !verificationSealed ||
                evidenceClaimed || writable || namespaceChanged || original.artifact || original.writable || original.namespaceChanged ||
                KnownName == original.KnownName)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Archive exchange requires one sealed private stage and one unchanged held original.");

            parent.VerifyNamespace(); parent.CheckCreationNamespace();
            parent.RequireName(KnownName, Identity); parent.RequireName(original.KnownName, original.Identity);
            parent.CheckPrivate(held);
            ((IArchiveRetainedFile)this).Review(expectedStage, ct);
            ((IArchiveRetainedFile)original).Review(expectedOriginal, ct);
            var stageMetadata = CaptureOuterMetadata(ct);
            var originalMetadata = original.CaptureOuterMetadata(ct);
            stageMetadata.RequireSupportedExtendedAttributes();
            originalMetadata.RequireSupportedExtendedAttributes();
            if (stageMetadata.Native.LinkCount != 1 || originalMetadata.Native.LinkCount != 1)
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "Archive exchange requires single-link regular objects.");
            UnixArchiveNative.SyncFile(held);
            ct.ThrowIfCancellationRequested();

            string stageName = KnownName, targetName = original.KnownName;
            int descriptor = UnixArchiveNative.Fd(parent.Handle);
            int result = UnixArchiveNative.Mac
                ? RenameMac(descriptor, stageName, descriptor, targetName, 2 /* RENAME_SWAP */)
                : RenameLinux(descriptor, stageName, descriptor, targetName, 2 /* RENAME_EXCHANGE */);
            if (result != 0)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                    "The native archive exchange failed or its outcome is uncertain (errno " + Marshal.GetLastPInvokeError() + "); retain both held objects and names.");

            // From this point cancellation cannot hide the public effect. Keep each held inode and its new expected
            // name even if a concurrent same-user namespace change makes either postcheck fail.
            KnownName = targetName; original.KnownName = stageName;
            namespaceChanged = true; original.namespaceChanged = true;
            try
            {
                parent.VerifyNamespace();
                parent.RequireName(targetName, Identity); parent.RequireName(stageName, original.Identity);
                parent.CheckPrivate(held);
                var publishedMetadata = CaptureOuterMetadata(CancellationToken.None);
                var retiredMetadata = original.CaptureOuterMetadata(CancellationToken.None);
                if (!SameExceptChanged(publishedMetadata, stageMetadata) || !SameExceptChanged(retiredMetadata, originalMetadata))
                    throw Changed();
                var published = ObserveRetained(CancellationToken.None);
                var retired = original.ObserveRetained(CancellationToken.None);
                if (published.Version != expectedStage.Version || retired.Version != expectedOriginal.Version)
                    throw Changed();
                UnixArchiveNative.SyncDirectory(parent.Handle);
                return (published, retired);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArchiveWriteException)
            {
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                    "The archive exchange may have changed both names; retain the held stage, original and journal for reconciliation.");
            }
        }
    }

    private static bool SameExceptChanged(ArchiveUnixOuterMetadata actual, ArchiveUnixOuterMetadata expected) =>
        actual.Native with { Changed = expected.Native.Changed } == expected.Native &&
        actual.AclPresent == expected.AclPresent && actual.AclBytes.SequenceEqual(expected.AclBytes) &&
        actual.ExtendedAttributes?.Digest == expected.ExtendedAttributes?.Digest &&
        actual.Origin.Digest == expected.Origin.Digest;
}
