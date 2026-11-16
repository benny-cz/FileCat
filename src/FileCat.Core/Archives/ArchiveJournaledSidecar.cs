using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

internal sealed record ArchiveSealedSidecar<T>(T File, ArchiveTransactionFacts Facts, ArchiveFileObservation Observation) : IDisposable
    where T : class, IArchiveRetainedArtifact
{
    public void Dispose() => File.Dispose();
}

/// <summary>Native private evidence with durable name/identity/version records, full codec readback and no cleanup.</summary>
internal static class ArchiveJournaledSidecar
{
    internal static ArchiveSealedSidecar<T> WriteManifest<T>(JobJournal journal, ArchiveTransactionFacts facts,
        ArchiveArtifactReservation reservation, Func<ArchiveArtifactReservation, T> create, ArchiveManifestPurpose purpose,
        ArchiveWriteManifest manifest, CancellationToken ct) where T : class, IArchiveRetainedArtifact
    {
        ArgumentNullException.ThrowIfNull(facts); ArgumentNullException.ThrowIfNull(manifest);
        if (purpose is not (ArchiveManifestPurpose.AssessedSource or ArchiveManifestPurpose.ExpectedOutput) || manifest.Profile != facts.Profile)
            throw new ArgumentException("This preparation requires an assessed-source or expected-output manifest with its exact profile.");
        return WriteManifestCore(journal, facts, reservation, create, purpose, manifest, ct);
    }

    internal static ArchiveSealedSidecar<T> WriteVerifiedManifest<T>(JobJournal journal, ArchiveTransactionFacts facts,
        ArchiveArtifactReservation reservation, Func<ArchiveArtifactReservation, T> create, ArchiveVerificationResult verification,
        CancellationToken ct) where T : class, IArchiveRetainedArtifact
    {
        ArgumentNullException.ThrowIfNull(facts); ArgumentNullException.ThrowIfNull(verification);
        if (verification.Stage != facts.FinalizedStage || verification.Stage.Identity != facts.StageIdentity || verification.Stage.Length > facts.StageMaximumLength || facts.Phase != ArchiveTransactionPhase.Verifying ||
            verification.ObservedManifest.Profile != facts.Profile) throw new ArgumentException("The observed manifest differs from the verified native stage.");
        return WriteManifestCore(journal, facts, reservation, create, ArchiveManifestPurpose.VerifiedOutput, verification.ObservedManifest, ct);
    }

    private static ArchiveSealedSidecar<T> WriteManifestCore<T>(JobJournal journal, ArchiveTransactionFacts facts,
        ArchiveArtifactReservation reservation, Func<ArchiveArtifactReservation, T> create, ArchiveManifestPurpose purpose,
        ArchiveWriteManifest manifest, CancellationToken ct) where T : class, IArchiveRetainedArtifact
    {
        var role = (ArchiveSidecarRole)purpose;
        return Write(journal, facts, reservation, create, role, stream =>
        {
            var encoded = ArchiveManifestSidecar.Write(stream, facts.Nonce, purpose, manifest, ct);
            return (encoded.Length, encoded.Sha256);
        }, (stream, version) =>
        {
            using var reader = ArchiveManifestSidecar.OpenRead(stream, new(version.Length, version.Sha256), facts.Nonce, purpose, manifest.Profile, ct);
            while (reader.ReadNext() is not null) { }
            if (!reader.IsComplete || reader.VerifiedManifestDigest != manifest.Digest || reader.VerifiedContentDigest != manifest.ContentDigest ||
                reader.VerifiedMetadataDigest != manifest.MetadataDigest) throw Changed();
        }, ct);
    }

    internal static ArchiveSealedSidecar<T> WriteOrigin<T>(JobJournal journal, ArchiveTransactionFacts facts,
        ArchiveArtifactReservation reservation, Func<ArchiveArtifactReservation, T> create, ArchiveOriginEvidenceScope scope,
        IReadOnlyList<ArchiveOriginEvidenceRecord> records, CancellationToken ct) where T : class, IArchiveRetainedArtifact
    {
        ArgumentNullException.ThrowIfNull(facts); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(records);
        ct.ThrowIfCancellationRequested();
        var reserved = facts.Sidecars.SingleOrDefault(s => s.Role == ArchiveSidecarRole.OriginEvidence);
        if (scope.Nonce != facts.Nonce || scope.Profile != facts.Profile || scope.Operation != (facts.Original is null ? ArchiveOriginOperation.Create : ArchiveOriginOperation.Update) ||
            scope.Sources != records.Count || reserved?.OriginSources != scope.Sources)
            throw new ArgumentException("Origin evidence differs from its durable transaction scope.");
        var frozen = records.ToArray();
        _ = ArchiveOriginEvidenceWriter.EncodedLength(scope, frozen, ct);
        if (facts.Original is not null && (frozen.Length == 0 || frozen[0]?.Source != facts.Original || frozen[0].Kind != ArchiveOriginSourceKind.OriginalArchive))
            throw new ArgumentException("Origin evidence is not bound to the reviewed original archive.");
        return Write(journal, facts, reservation, create, ArchiveSidecarRole.OriginEvidence, stream =>
        {
            using var writer = new ArchiveOriginEvidenceWriter(stream, scope, ct);
            foreach (var record in frozen) writer.Append(record);
            var encoded = writer.Complete(); return (encoded.Length, encoded.Sha256);
        }, (stream, version) =>
        {
            using var reader = new ArchiveOriginEvidenceReader(stream, new(version.Length, version.Sha256), scope, ct);
            foreach (var expected in frozen)
            {
                var actual = reader.ReadNext();
                if (actual is null || actual.Kind != expected.Kind || actual.Source != expected.Source || actual.Origin.Digest != expected.Origin.Digest) throw Changed();
            }
            if (reader.ReadNext() is not null || !reader.IsComplete) throw Changed();
        }, ct);
    }

    internal static ArchiveSealedSidecar<T> WriteCreationMetadata<T>(JobJournal journal, ArchiveTransactionFacts facts,
        ArchiveArtifactReservation reservation, Func<ArchiveArtifactReservation, T> create, ArchiveCreationMetadata metadata,
        CancellationToken ct) where T : class, IArchiveRetainedArtifact
    {
        ArgumentNullException.ThrowIfNull(metadata); metadata.RequireScope(facts);
        return Write(journal, facts, reservation, create, ArchiveSidecarRole.CreationMetadata, stream =>
        { metadata.Write(stream, ct); return (metadata.Length, metadata.Sha256); }, (stream, _) => metadata.Readback(stream, ct), ct);
    }

    private static ArchiveSealedSidecar<T> Write<T>(JobJournal journal, ArchiveTransactionFacts facts, ArchiveArtifactReservation reservation,
        Func<ArchiveArtifactReservation, T> create, ArchiveSidecarRole role, Func<Stream, (long Length, string Sha256)> write,
        Action<Stream, ArchiveObjectVersion> readback, CancellationToken ct) where T : class, IArchiveRetainedArtifact
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(facts); ArgumentNullException.ThrowIfNull(reservation); ArgumentNullException.ThrowIfNull(create);
        ct.ThrowIfCancellationRequested();
        var sidecar = facts.Sidecars.SingleOrDefault(s => s.Role == role);
        if (facts.RecordVersion != ArchiveTransactionFacts.SchemaVersion ||
            facts.Phase != (role is ArchiveSidecarRole.VerifiedOutput or ArchiveSidecarRole.CreationMetadata ? ArchiveTransactionPhase.Verifying : ArchiveTransactionPhase.Reserved) || facts.StageIdentity is null ||
            role == ArchiveSidecarRole.CreationMetadata && (facts.Original is not null || facts.VerifiedStage is null) ||
            sidecar is null || sidecar.Identity is not null || sidecar.Path != reservation.Path || sidecar.MaximumLength != reservation.MaximumLength ||
            reservation.Nonce != facts.Nonce || reservation.ParentIdentity != facts.ParentIdentity ||
            reservation.Kind != (role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest))
            throw new ArgumentException("The native sidecar creation differs from its durable reservation.");
        // Require the already-flushed initial reservation before the native factory can create an object.
        journal.RequireCurrentArchiveTransaction(facts);
        T? file = null;
        try
        {
            file = create(reservation); string identity = file.ReviewCreation(reservation, ct);
            if (identity != file.ObjectIdentity) throw Changed();
            sidecar = sidecar.BindIdentity(identity); facts = facts.BindSidecar(sidecar); journal.ArchiveTransaction(facts);
            var encoded = write(file.BeginEvidence(reservation, ct));
            var observed = file.SealEvidence(ct);
            if (observed.Version.Identity != identity || observed.Version.Length != encoded.Length || observed.Version.Sha256 != encoded.Sha256) throw Changed();
            using (var view = file.OpenReadView(observed, ct)) readback(view, observed.Version);
            file.Review(observed, ct); ct.ThrowIfCancellationRequested();
            facts = facts.BindSidecar(sidecar.BindVersion(observed.Version)); journal.ArchiveTransaction(facts);
            return new(file, facts, observed);
        }
        catch
        {
            journal.RequirePreparationReview();
            if (file is not null) { try { file.StopWrites(); } finally { file.Dispose(); } }
            throw;
        }
    }

    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.NeedsReview, "Private archive evidence is incomplete or changed; retain its transaction artifacts for review.");
}
