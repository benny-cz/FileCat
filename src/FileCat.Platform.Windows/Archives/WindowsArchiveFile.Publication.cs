using FileCat.Core.Archives;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveFile
{
    private sealed class CreationReview(WindowsArchiveFile issuer, ArchiveFileObservation stage, string targetLeaf, WindowsArchiveOuterMetadata metadata, byte[] witness)
        : ArchiveCreatePublicationReview(issuer, stage, targetLeaf, ArchiveCreationBackend.WindowsNtfs, witness)
    { internal WindowsArchiveOuterMetadata Metadata { get; } = metadata; }

    ArchiveCreatePublicationReview IArchiveCreatePublicationStage.ReviewCreate(ArchiveFileObservation verified, string targetLeaf, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); WindowsArchiveDirectory.ValidateLeaf(targetLeaf); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != WindowsArchiveArtifactKind.Stage || !rebuildClaimed || evidenceClaimed || writable || namespaceChanged || !mayRename)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Only this transaction's sealed rebuilt private stage can create a new archive.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(handle);
            WindowsArchiveNative.RequireOnlyDataStream(handle, verified.Version.Length);
            var observed = ObserveRetained(ct); var metadata = CaptureOuterMetadata(ct);
            if (!observed.Matches(verified) || metadata.Digest != verified.MetadataDigest || metadata.Origin.State != ArchiveOriginState.Absent)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The private creation stage changed after verification.");
            byte[] witness = ArchiveCreationMetadata.EncodeWitness(w =>
            {
                w.Write(1); w.Write(WindowsArchiveOuterMetadata.QueriedSecurityInformation);
                w.Write(metadata.CreationTime); w.Write(metadata.LastWriteTime); w.Write(metadata.Attributes); w.Write(metadata.LinkCount);
                w.Write(metadata.SecurityDescriptor.Length); w.Write(metadata.SecurityDescriptor);
                w.Write((int)metadata.Origin.Format); w.Write((int)metadata.Origin.State);
                w.Write(1); // Complete unnamed-data-stream-only query succeeded on this retained object.
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
            WindowsArchiveNative.RequireOnlyDataStream(handle, review.Stage.Version.Length);
            RenameNoReplace(leaf, review.Stage.Version, native.Metadata, ct);
        }
    }
    ArchiveFileObservation IArchiveCreatePublicationStage.ReviewPublished(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); review.RequireIssuer(this); ct.ThrowIfCancellationRequested();
            if (review is not CreationReview native || !namespaceChanged || KnownName != leaf || leaf != review.TargetLeaf || writable) throw PublicationChanged();
            parent.VerifyNamespace(); parent.RequireName(leaf, Identity); parent.CheckPrivate(handle);
            WindowsArchiveNative.RequireOnlyDataStream(handle, review.Stage.Version.Length);
            var observed = ObserveRetained(ct);
            if (observed.Version != review.Stage.Version || observed.MetadataDigest != native.Metadata.Digest || observed.Origin.State != ArchiveOriginState.Absent) throw PublicationChanged();
            return observed;
        }
    }
    private static ArchiveWriteException PublicationChanged() => new(ArchiveWriteReason.NeedsReview, "The new archive's native name, bytes, permissions or mark changed; retain the transaction for review.");
}
