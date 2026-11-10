using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile
{
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
