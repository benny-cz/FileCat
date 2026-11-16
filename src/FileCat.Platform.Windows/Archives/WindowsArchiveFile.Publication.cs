using FileCat.Core.Archives;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveFile
{
    private sealed class CreationReview(WindowsArchiveFile issuer, ArchiveFileObservation stage, string targetLeaf, WindowsArchiveOuterMetadata metadata, byte[] witness)
        : ArchiveCreatePublicationReview(issuer, stage, targetLeaf, ArchiveCreationBackend.WindowsNtfs, witness)
    { internal WindowsArchiveOuterMetadata Metadata { get; } = metadata; }

    ArchiveFileObservation IArchiveCreatePublicationStage.ApplyCreationOrigin(ArchiveFileObservation sealedStage, ArchiveOriginSnapshot required, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); ct.ThrowIfCancellationRequested(); ArchiveCreationOrigin.RequireSupported(required);
            if (!artifact || !createdWritable || createdKind != WindowsArchiveArtifactKind.Stage || !rebuildClaimed || evidenceClaimed || writable || namespaceChanged || !mayRename)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Creation origin requires the sealed rebuilt stage.");
            ((IArchiveRetainedFile)this).Review(sealedStage, ct);
            var before = CaptureOuterMetadata(ct);
            WindowsArchiveNative.RequireOnlyDataStream(handle, sealedStage.Version.Length);
            CopyOriginNoReplace(required, ct);
            var observed = ObserveRetained(ct); var after = CaptureOuterMetadata(ct);
            if (observed.Version != sealedStage.Version || observed.Origin.Digest != required.Digest || before.Identity != after.Identity ||
                before.CreationTime != after.CreationTime || before.LastWriteTime != after.LastWriteTime || before.Attributes != after.Attributes ||
                before.LinkCount != after.LinkCount || !before.SecurityDescriptor.SequenceEqual(after.SecurityDescriptor)) throw PublicationChanged();
            WindowsArchiveNative.RequireCreationStreams(handle, observed.Version.Length, required);
            return observed;
        }
    }

    ArchiveCreatePublicationReview IArchiveCreatePublicationStage.ReviewCreate(ArchiveFileObservation verified, string targetLeaf, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); WindowsArchiveDirectory.ValidateLeaf(targetLeaf); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != WindowsArchiveArtifactKind.Stage || !rebuildClaimed || evidenceClaimed || writable || namespaceChanged || !mayRename)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Only this transaction's sealed rebuilt private stage can create a new archive.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(handle);
            ArchiveCreationOrigin.RequireSupported(verified.Origin);
            WindowsArchiveNative.RequireCreationStreams(handle, verified.Version.Length, verified.Origin);
            var observed = ObserveRetained(ct); var metadata = CaptureOuterMetadata(ct);
            if (!observed.Matches(verified) || metadata.Digest != verified.MetadataDigest)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The private creation stage changed after verification.");
            byte[] witness = ArchiveCreationMetadata.EncodeWitness(w =>
            {
                w.Write(metadata.Origin.State == ArchiveOriginState.Present ? 2 : 1); w.Write(WindowsArchiveOuterMetadata.QueriedSecurityInformation);
                w.Write(metadata.CreationTime); w.Write(metadata.LastWriteTime); w.Write(metadata.Attributes); w.Write(metadata.LinkCount);
                w.Write(metadata.SecurityDescriptor.Length); w.Write(metadata.SecurityDescriptor);
                w.Write((int)metadata.Origin.Format); w.Write((int)metadata.Origin.State);
                w.Write(metadata.Origin.State == ArchiveOriginState.Present ? 2 : 1); // Complete data + optional exact origin stream inventory.
                // This is the observed permission/label subset on the same newly created object, not an update
                // descriptor or a claim that unqueried audit/resource-policy fields were absent or copied.
            });
            return new CreationReview(this, observed, targetLeaf, metadata, witness);
        }
    }
    void IArchiveCreatePublicationStage.ClaimNewName(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            review.RequireIssuer(this); if (review is not CreationReview native || namespaceChanged || leaf != review.TargetLeaf) throw PublicationChanged();
            WindowsArchiveNative.RequireCreationStreams(handle, review.Stage.Version.Length, review.Stage.Origin);
            // ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS: another object has the name; the no-replace rename changed nothing.
            try { RenameNoReplace(leaf, review.Stage.Version, native.Metadata, ct); }
            catch (IOException error) when (error is not ArchiveWriteException && error.HResult is unchecked((int)0x80070050) or unchecked((int)0x800700B7))
            { throw ArchiveWriteMessages.NameTaken(leaf, error); }
        }
    }
    ArchiveFileObservation IArchiveCreatePublicationStage.ReviewPublished(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); review.RequireIssuer(this); ct.ThrowIfCancellationRequested();
            if (review is not CreationReview native || !namespaceChanged || KnownName != leaf || leaf != review.TargetLeaf || writable) throw PublicationChanged();
            parent.VerifyNamespace(); parent.RequireName(leaf, Identity); parent.CheckPrivate(handle);
            WindowsArchiveNative.RequireCreationStreams(handle, review.Stage.Version.Length, review.Stage.Origin);
            var observed = ObserveRetained(ct);
            if (observed.Version != review.Stage.Version || observed.MetadataDigest != native.Metadata.Digest || observed.Origin.Digest != review.Stage.Origin.Digest) throw PublicationChanged();
            return observed;
        }
    }
    private static ArchiveWriteException PublicationChanged() => new(ArchiveWriteReason.NeedsReview, "The new archive's native name, bytes, permissions or mark changed; retain the transaction for review.");
}
