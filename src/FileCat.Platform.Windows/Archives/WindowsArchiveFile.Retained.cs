using FileCat.Core.Archives;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveFile
{
    string IArchiveRetainedFile.ObjectIdentity => Identity.ToString();
    ArchiveFileObservation IArchiveRetainedFile.Observe(CancellationToken ct) => ObserveRetained(ct);
    void IArchiveRetainedFile.Review(ArchiveFileObservation expected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected); var actual = ObserveRetained(ct);
        if (!actual.Matches(expected)) throw ChangedRetained();
    }
    void IArchiveRetainedFile.ReviewMetadata(ArchiveFileObservation expected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (parent.Gate)
        {
            EnsureLive(); ct.ThrowIfCancellationRequested(); parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
            var metadata = CaptureOuterMetadata(ct); if (metadata.LinkCount != 1) throw ArchiveWriteMessages.HardLinked(DisplayPath);
            if (expected.Version.Identity != Identity.ToString() || Length != expected.Version.Length ||
                !RetainedObservation(expected.Version, metadata).Matches(expected)) throw ChangedRetained();
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
        }
    }
    private ArchiveFileObservation ObserveRetained(CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
            var before = CaptureOuterMetadata(ct); if (before.LinkCount != 1) throw ArchiveWriteMessages.HardLinked(DisplayPath);
            var version = CaptureVersion(ct); var after = CaptureOuterMetadata(ct);
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); if (before.Digest != after.Digest) throw ChangedRetained();
            return RetainedObservation(version, after);
        }
    }
    Stream IArchiveRetainedFile.OpenReadView(ArchiveFileObservation expected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (parent.Gate)
        {
            EnsureLive(); ct.ThrowIfCancellationRequested(); parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
            if (writable || expected.Version.Identity != Identity.ToString() || expected.ParentIdentity != parent.Identity.ToString() ||
                expected.LeafName != KnownName || Length != expected.Version.Length || !RetainedObservation(expected.Version, CaptureOuterMetadata(ct)).Matches(expected)) throw ChangedRetained();
            return new ArchiveRetainedReadStream(expected.Version.Length, ReadAt, () => CanRead);
            int ReadAt(long offset, Span<byte> target)
            {
                lock (parent.Gate)
                {
                    EnsureLive(); ct.ThrowIfCancellationRequested(); Validate();
                    if (writable || RandomAccess.GetLength(handle) != expected.Version.Length) throw ChangedRetained();
                    return RandomAccess.Read(handle, target, offset);
                }
            }
        }
    }
    private ArchiveFileObservation RetainedObservation(ArchiveObjectVersion version, WindowsArchiveOuterMetadata metadata)
    {
        DateTimeOffset? modified = null;
        try { modified = new DateTimeOffset(DateTime.FromFileTimeUtc(metadata.LastWriteTime), TimeSpan.Zero); }
        catch (ArgumentOutOfRangeException) { }
        return new(version, parent.Identity.ToString(), KnownName, metadata.Digest, metadata.Origin, modified,
            0x1a4); // The portable new-file default is 0644; native read-only state remains in the review digest.
    }
    string IArchiveRetainedArtifact.ReviewCreation(ArchiveArtifactReservation reservation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (parent.Gate)
        {
            EnsureWritable(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != (WindowsArchiveArtifactKind)reservation.Kind || namespaceChanged || rebuildClaimed || evidenceClaimed ||
                maximumLength != reservation.MaximumLength || KnownName != reservation.LeafName ||
                parent.Identity.ToString() != reservation.ParentIdentity || Length != 0 || position != 0 || Validate() != 1)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The fresh stage differs from its reserved native creation scope.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(handle);
            return Identity.ToString();
        }
    }
    Stream IArchiveRetainedArtifact.BeginEvidence(ArchiveArtifactReservation reservation, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            ((IArchiveRetainedArtifact)this).ReviewCreation(reservation, ct);
            if (createdKind == WindowsArchiveArtifactKind.Stage) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A manifest or provenance artifact is required.");
            evidenceClaimed = true; return this;
        }
    }
    ArchiveFileObservation IArchiveRetainedArtifact.SealEvidence(CancellationToken ct)
    {
        lock (parent.Gate)
        {
            if (!evidenceClaimed || createdKind == WindowsArchiveArtifactKind.Stage || namespaceChanged) throw new InvalidOperationException("The native artifact was not claimed for evidence writing.");
            var version = Seal(ct); var observed = ObserveRetained(ct); if (version != observed.Version) throw ChangedRetained(); return observed;
        }
    }
    Stream IArchiveRetainedStage.BeginRebuild(long exactMaximumLength, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureWritable(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != WindowsArchiveArtifactKind.Stage || namespaceChanged || rebuildClaimed || maximumLength != exactMaximumLength || Length != 0 || position != 0)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A fresh empty stage with the exact reviewed output bound is required.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(handle); rebuildClaimed = true; return this;
        }
    }
    ArchiveFileObservation IArchiveRetainedStage.SealForVerification(CancellationToken ct)
    {
        lock (parent.Gate)
        {
            if (!rebuildClaimed) throw new InvalidOperationException("The native stage was not claimed for rebuilding.");
            var version = Seal(ct); var observed = ObserveRetained(ct); if (observed.Version != version) throw ChangedRetained(); return observed;
        }
    }
    void IArchiveRetainedArtifact.StopWrites() { lock (parent.Gate) writable = false; }
    private static ArchiveWriteException ChangedRetained() => new(ArchiveWriteReason.SourceChanged, "The retained Windows archive binding changed; preserve its objects for review.");
}
