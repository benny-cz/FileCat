namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile
{
    private sealed class CreationReview(UnixArchiveFile issuer, ArchiveFileObservation stage, string targetLeaf, ArchiveUnixOuterMetadata metadata, byte[] witness)
        : ArchiveCreatePublicationReview(issuer, stage, targetLeaf, UnixArchiveNative.Mac ? ArchiveCreationBackend.MacApfs : ArchiveCreationBackend.LinuxExt4, witness)
    { internal ArchiveUnixOuterMetadata Metadata { get; } = metadata; }

    ArchiveFileObservation IArchiveCreatePublicationStage.ApplyCreationOrigin(ArchiveFileObservation sealedStage, ArchiveOriginSnapshot required, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            Live(); ct.ThrowIfCancellationRequested();
            ArchiveCreationOrigin.RequireSupported(required);
            if (required.Format != (UnixArchiveNative.Mac ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl))
                throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The required Unix origin format differs from this platform.");
            if (!artifact || !createdWritable || createdKind != UnixArchiveArtifactKind.Stage || !rebuildClaimed || evidenceClaimed || writable || namespaceChanged)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Creation origin requires the sealed rebuilt stage.");
            ((IArchiveRetainedFile)this).Review(sealedStage, ct);
            CopyOriginNoReplace(required, ct);
            var observed = ObserveRetained(ct);
            if (observed.Version != sealedStage.Version || observed.ParentIdentity != sealedStage.ParentIdentity ||
                observed.LeafName != sealedStage.LeafName || observed.MemberModified != sealedStage.MemberModified ||
                observed.MemberMode != sealedStage.MemberMode || observed.Origin.Digest != required.Digest) throw Changed();
            return observed;
        }
    }

    ArchiveCreatePublicationReview IArchiveCreatePublicationStage.ReviewCreate(ArchiveFileObservation verified, string targetLeaf, CancellationToken ct)
    {
        // A stage in a creator-private stage folder is published into that folder's reviewed parent.
        var destination = parent.PrivateStageParent;
        lock (destination?.Gate ?? parent.Gate)
        lock (parent.Gate)
        {
            Live(); UnixArchiveNative.Leaf(targetLeaf); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != UnixArchiveArtifactKind.Stage || !rebuildClaimed || evidenceClaimed || writable || namespaceChanged)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Only this transaction's sealed rebuilt private stage can create a new archive.");
            parent.VerifyNamespace(); parent.CheckCreationNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(held);
            if (destination is not null) { parent.RequirePrivateStageOf(destination); destination.CheckCreationNamespace(); }
            var observed = ObserveRetained(ct); var metadata = CaptureOuterMetadata(ct);
            ArchiveCreationOrigin.RequireSupported(verified.Origin);
            if (!observed.Matches(verified) || metadata.Digest != verified.MetadataDigest || metadata.Origin.Digest != verified.Origin.Digest) throw Changed();
            metadata.RequireSupportedExtendedAttributes();
            byte[] witness = ArchiveCreationMetadata.EncodeWitness(w =>
            {
                var f = metadata.Native; w.Write(1); w.Write((int)f.Format); w.Write(f.Device); w.Write(f.Inode); w.Write(f.Length);
                w.Write(f.Owner); w.Write(f.Group); w.Write(f.Mode); w.Write(f.LinkCount); Time(f.Modified); Time(f.Changed);
                w.Write(f.Created.HasValue); if (f.Created is { } birth) Time(birth);
                w.Write(f.Flags); w.Write(f.Attributes); w.Write(f.AttributeMask); w.Write(metadata.AclPresent);
                w.Write(metadata.AclBytes.Length); w.Write(metadata.AclBytes); w.Write((int)metadata.Origin.Format); w.Write((int)metadata.Origin.State);
                w.Write(metadata.ExtendedAttributes!.Attributes.Count);
                foreach (var attribute in metadata.ExtendedAttributes.Attributes)
                { byte[] name = ArchiveWritePolicy.Utf8.GetBytes(attribute.Name); w.Write(name.Length); w.Write(name); w.Write(attribute.Value.Length); w.Write(attribute.Value); }
                void Time(ArchiveUnixOuterMetadata.Timestamp t) { w.Write(t.Seconds); w.Write(t.Nanoseconds); }
            });
            return new CreationReview(this, observed, targetLeaf, metadata, witness) { DestinationParentIdentity = destination?.Identity.ToString() };
        }
    }
    void IArchiveCreatePublicationStage.ClaimNewName(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct)
    {
        if (parent.PrivateStageParent is { } destination) { ClaimIntoDestination(destination, leaf, review, ct); return; }
        lock (parent.Gate)
        {
            review.RequireIssuer(this); if (review is not CreationReview native || namespaceChanged || leaf != review.TargetLeaf) throw Changed();
            parent.CheckCreationNamespace();
            RenameNoReplace(leaf, review.Stage.Version, native.Metadata, ct);
        }
    }
    ArchiveFileObservation IArchiveCreatePublicationStage.ReviewPublished(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct)
    {
        if (parent.PrivateStageParent is not null) return ReviewPublishedInDestination(leaf, review, ct);
        lock (parent.Gate)
        {
            Live(); review.RequireIssuer(this); ct.ThrowIfCancellationRequested();
            if (review is not CreationReview native || !namespaceChanged || KnownName != leaf || leaf != review.TargetLeaf || writable) throw Changed();
            parent.VerifyNamespace(); parent.CheckCreationNamespace(); parent.RequireName(leaf, Identity); parent.CheckPrivate(held);
            var actual = CaptureOuterMetadata(ct); var before = native.Metadata;
            if (actual.Native with { Changed = before.Native.Changed } != before.Native || actual.AclPresent != before.AclPresent ||
                !actual.AclBytes.SequenceEqual(before.AclBytes) || actual.ExtendedAttributes?.Digest != before.ExtendedAttributes?.Digest || actual.Origin.Digest != before.Origin.Digest)
                throw Changed();
            var observed = ObserveRetained(ct); if (observed.Version != review.Stage.Version) throw Changed(); return observed;
        }
    }
}
