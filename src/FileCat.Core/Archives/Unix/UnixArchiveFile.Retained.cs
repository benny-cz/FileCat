namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile
{
    string IArchiveRetainedFile.ObjectIdentity => Identity.ToString();
    ArchiveFileObservation IArchiveRetainedFile.Observe(CancellationToken ct) => ObserveRetained(ct);
    void IArchiveRetainedFile.Review(ArchiveFileObservation expected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected); var actual = ObserveRetained(ct);
        if (!actual.Matches(expected)) throw Changed();
    }
    private ArchiveFileObservation ObserveRetained(CancellationToken ct)
    {
        lock (parent.Gate)
        {
            Live(); parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
            var before = CaptureOuterMetadata(ct); if (before.Native.LinkCount != 1) throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "A retained archive binding requires a single-link regular object.");
            var version = CaptureVersion(ct); var after = CaptureOuterMetadata(ct);
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
            if (before.Digest != after.Digest) throw Changed();
            return RetainedObservation(version, after);
        }
    }
    Stream IArchiveRetainedFile.OpenReadView(ArchiveFileObservation expected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (parent.Gate)
        {
            Live(); ct.ThrowIfCancellationRequested(); parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
            if (writable || expected.Version.Identity != Identity.ToString() || expected.ParentIdentity != parent.Identity.ToString() ||
                expected.LeafName != KnownName || Length != expected.Version.Length || !RetainedObservation(expected.Version, CaptureOuterMetadata(ct)).Matches(expected)) throw Changed();
            return new ArchiveRetainedReadStream(expected.Version.Length, ReadAt, () => CanRead);
            int ReadAt(long offset, Span<byte> target)
            {
                lock (parent.Gate)
                {
                    Live(); ct.ThrowIfCancellationRequested();
                    if (writable || Validate().Size != expected.Version.Length) throw Changed();
                    return RandomAccess.Read(held, target, offset);
                }
            }
        }
    }
    private ArchiveFileObservation RetainedObservation(ArchiveObjectVersion version, ArchiveUnixOuterMetadata metadata)
    {
        DateTimeOffset? modified = null;
        if (metadata.Native.Modified.Nanoseconds % 100 == 0)
        {
            try { modified = DateTimeOffset.FromUnixTimeSeconds(metadata.Native.Modified.Seconds).AddTicks(metadata.Native.Modified.Nanoseconds / 100); }
            catch (ArgumentOutOfRangeException) { } // Explicitly unrepresentable input metadata, never silently rounded.
        }
        return new(version, parent.Identity.ToString(), KnownName, metadata.Digest, metadata.Origin, modified,
            metadata.Native.Mode <= 0x1ff ? (int)metadata.Native.Mode : null);
    }
    string IArchiveRetainedArtifact.ReviewCreation(ArchiveArtifactReservation reservation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (parent.Gate)
        {
            Writeable(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != (UnixArchiveArtifactKind)reservation.Kind || namespaceChanged || rebuildClaimed || evidenceClaimed ||
                maximum != reservation.MaximumLength || KnownName != reservation.LeafName ||
                parent.Identity.ToString() != reservation.ParentIdentity || Length != 0 || position != 0)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The fresh stage differs from its reserved native creation scope.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(held);
            return Identity.ToString();
        }
    }
    Stream IArchiveRetainedArtifact.BeginEvidence(ArchiveArtifactReservation reservation, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            ((IArchiveRetainedArtifact)this).ReviewCreation(reservation, ct);
            if (createdKind == UnixArchiveArtifactKind.Stage) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A manifest or provenance artifact is required.");
            evidenceClaimed = true; return this;
        }
    }
    ArchiveFileObservation IArchiveRetainedArtifact.SealEvidence(CancellationToken ct)
    {
        lock (parent.Gate)
        {
            if (!evidenceClaimed || createdKind == UnixArchiveArtifactKind.Stage || namespaceChanged) throw new InvalidOperationException("The native artifact was not claimed for evidence writing.");
            var version = Seal(ct); var observed = ObserveRetained(ct); if (version != observed.Version) throw Changed(); return observed;
        }
    }
    Stream IArchiveRetainedStage.BeginRebuild(long exactMaximumLength, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            Writeable(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != UnixArchiveArtifactKind.Stage || namespaceChanged || rebuildClaimed || maximum != exactMaximumLength || Length != 0 || position != 0)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A fresh empty stage with the exact reviewed output bound is required.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(held); rebuildClaimed = true; return this;
        }
    }
    ArchiveFileObservation IArchiveRetainedStage.SealForVerification(CancellationToken ct)
    {
        lock (parent.Gate)
        {
            if (!rebuildClaimed) throw new InvalidOperationException("The native stage was not claimed for rebuilding.");
            var version = Seal(ct); var observed = ObserveRetained(ct); if (observed.Version != version) throw Changed(); return observed;
        }
    }
    void IArchiveRetainedArtifact.StopWrites() { lock (parent.Gate) writable = false; }
}
