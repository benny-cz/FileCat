using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>Fresh restoration preparation: separately bind the destination baseline and the retained byte source.</summary>
internal static class ArchiveJournaledSnapshotPreparation
{
    internal static async Task<ArchivePreparedContainer<T>> PrepareAsync<T>(JobJournal journal, string archivePath, ArchiveWriteProfile profile,
        ArchiveRetainedBinding target, ArchiveRetainedBinding snapshot,
        Func<Guid, ArchiveArtifactKind, long, ArchiveArtifactReservation> reserve, Func<ArchiveArtifactReservation, T> create,
        ArchiveAssessmentClient assessor, ArchiveVerificationClient verifier, CancellationToken ct, ArchiveJobPassword? password = null) where T : class, IArchiveRetainedStage
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reserve); ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(assessor); ArgumentNullException.ThrowIfNull(verifier);
        ArchiveTransactionFacts.ValidatePath(archivePath);
        ArchiveJobPassword.Require(profile, password);
        if (password is not null && (!assessor.UsesSecretProtocol || !verifier.UsesSecretProtocol))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "Password-bearing restoration requires fixed protocol-2 worker factories.");
        if (profile.Format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd or ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip or ArchiveWriteFormat.Gzip or ArchiveWriteFormat.Bzip2 or ArchiveWriteFormat.Zstd or ArchiveWriteFormat.Zip) ||
            target.Reviewed.Version.Identity == snapshot.Reviewed.Version.Identity ||
            !string.Equals(Path.GetFileName(archivePath), target.Reviewed.LeafName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw Invalid();
        ReviewInputs();
        var assessed = await assessor.AssessAsync(new(profile, snapshot.Reviewed.Version.Length, snapshot.Reviewed.Version.Sha256),
            () => snapshot.File.OpenReadView(snapshot.Reviewed, ct), ct, password?.CreateGrant()).ConfigureAwait(false);
        if (assessed.SourceLength != snapshot.Reviewed.Version.Length || assessed.SourceSha256 != snapshot.Reviewed.Version.Sha256) throw Invalid();
        T? stage = null; ArchiveTransactionFacts? facts = null; ArchiveSpaceReview? space = null;
        var sidecars = new List<ArchiveSealedSidecar<T>>();
        try
        {
            var setup = await Task.Run(() =>
            {
                ReviewInputs(); Guid nonce = Guid.NewGuid();
                ArchiveOriginEvidenceRecord[] records = [new(ArchiveOriginSourceKind.OriginalArchive, target.Reviewed.Version, target.Reviewed.Origin),
                    new(ArchiveOriginSourceKind.File, snapshot.Reviewed.Version, snapshot.Reviewed.Origin)];
                var originScope = new ArchiveOriginEvidenceScope(nonce, profile, ArchiveOriginOperation.Update, records.Length);
                long manifestLength = ArchiveManifestSidecar.EncodedLength(assessed.Manifest, ct);
                long originLength = ArchiveOriginEvidenceWriter.EncodedLength(originScope, records, ct);
                long evidenceLength = checked(3 * manifestLength + originLength);
                if (evidenceLength > ArchiveWritePolicy.MaxMetadataBytes) throw ArchiveWriteBudget.Limit();
                var stageName = Name(ArchiveArtifactKind.Stage, snapshot.Reviewed.Version.Length);
                var assessedName = Name(ArchiveArtifactKind.Manifest, manifestLength);
                var expectedName = Name(ArchiveArtifactKind.Manifest, manifestLength);
                var originName = Name(ArchiveArtifactKind.Provenance, originLength);
                var verifiedName = Name(ArchiveArtifactKind.Manifest, manifestLength);
                space = stageName.ReviewSpace(new(target.Reviewed.Version.Length, Math.Max(1024, stageName.MaximumLength), evidenceLength,
                    snapshot.Reviewed.Version.Length), ct);
                ArchiveSidecarFacts[] reserved = [new(ArchiveSidecarRole.AssessedSource, assessedName.Path, manifestLength),
                    new(ArchiveSidecarRole.ExpectedOutput, expectedName.Path, manifestLength), new(ArchiveSidecarRole.OriginEvidence, originName.Path, originLength, originSources: records.Length),
                    new(ArchiveSidecarRole.VerifiedOutput, verifiedName.Path, manifestLength)];
                facts = new(nonce, 1, profile, ArchiveTransactionPhase.Reserved, archivePath, stageName.ParentIdentity,
                    target.Reviewed.Version, stageName.Path, null, stageMaximumLength: stageName.MaximumLength, sidecars: reserved);
                stage = ArchiveJournaledStage.Create(journal, facts, stageName, create, ct); facts = journal.CurrentArchiveTransaction(nonce);
                Keep(ArchiveJournaledSidecar.WriteManifest(journal, facts, assessedName, create, ArchiveManifestPurpose.AssessedSource, assessed.Manifest, ct));
                Keep(ArchiveJournaledSidecar.WriteManifest(journal, facts, expectedName, create, ArchiveManifestPurpose.ExpectedOutput, assessed.Manifest, ct));
                Keep(ArchiveJournaledSidecar.WriteOrigin(journal, facts, originName, create, originScope, records, ct));
                return (Verified: verifiedName, Origin: originScope);
                ArchiveArtifactReservation Name(ArchiveArtifactKind kind, long maximum)
                {
                    var name = reserve(nonce, kind, maximum);
                    if (name.Nonce != nonce || name.Kind != kind || name.MaximumLength != maximum || name.ParentIdentity != target.Reviewed.ParentIdentity) throw Invalid();
                    return name;
                }
            }, ct).ConfigureAwait(false);
            var sealedStage = await Task.Run(() =>
            {
                ReviewInputs(); ReviewSidecars(); Append(facts!.AdvancePreparation(ArchiveTransactionPhase.Rebuilding));
                // BeginRebuild lends the creator-owned stage itself; its owner, not this copy loop, disposes it.
                var output = stage!.BeginRebuild(snapshot.Reviewed.Version.Length, ct);
                using (var input = snapshot.File.OpenReadView(snapshot.Reviewed, ct))
                {
                    byte[] buffer = new byte[64 * 1024]; long copied = 0;
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested(); int count = input.Read(buffer); if (count == 0) break;
                        copied = checked(copied + count); if (copied > snapshot.Reviewed.Version.Length) throw Invalid();
                        output.Write(buffer, 0, count);
                    }
                    if (copied != snapshot.Reviewed.Version.Length) throw Invalid();
                }
                Append(facts!.AdvancePreparation(ArchiveTransactionPhase.Finalizing));
                var state = stage!.SealForVerification(ct);
                if (state.Version.Length != snapshot.Reviewed.Version.Length || state.Version.Sha256 != snapshot.Reviewed.Version.Sha256 ||
                    stage is not IArchiveCreatePublicationStage markedStage) throw Invalid();
                var originSidecar = sidecars.Single(s => s.Observation.Version.Identity == facts!.OriginEvidenceIdentity);
                var evidence = new ArchiveOriginEvidenceInfo(originSidecar.Observation.Version.Length, originSidecar.Observation.Version.Sha256);
                var originStream = originSidecar.File.OpenReadView(originSidecar.Observation, ct);
                var origin = target.Reviewed.Origin.Format == ArchiveOriginFormat.WindowsZoneIdentifier
                    ? ArchiveUpdateOrigin.ReviewWindows(originStream, evidence, setup.Origin, target.Reviewed, [snapshot.Reviewed], ct)
                    : ArchiveUpdateOrigin.ReviewUnixExact(originStream, evidence, setup.Origin, target.Reviewed, [snapshot.Reviewed], ct);
                // A private new stage uses the canonical zone-only creation mark. The eventual existing target
                // separately retains its complete original fields and receives the full update-origin union.
                var stageOrigin = origin.Format == ArchiveOriginFormat.WindowsZoneIdentifier && origin.State == ArchiveOriginState.Present
                    ? ArchiveWindowsOriginZone.ZoneOnly(ArchiveWindowsOriginZone.Parse(origin).DeclaredZone ?? throw Invalid()) : origin;
                state = markedStage.ApplyCreationOrigin(state, stageOrigin, ct);
                ReviewInputs(); ReviewSidecars(); Append(facts!.AdvancePreparation(ArchiveTransactionPhase.Verifying, finalizedStage: state.Version));
                return state;
            }, ct).ConfigureAwait(false);
            var verification = await verifier.VerifyAsync(sealedStage.Version, assessed.Manifest, () => stage!.OpenReadView(sealedStage, ct), ct, password?.CreateGrant()).ConfigureAwait(false);
            await Task.Run(() =>
            {
                Keep(ArchiveJournaledSidecar.WriteVerifiedManifest(journal, facts!, setup.Verified, create, verification, ct));
                stage!.Review(sealedStage, ct); ReviewInputs(); ReviewSidecars();
                Append(facts!.AdvancePreparation(ArchiveTransactionPhase.Verifying, sealedStage.Version));
            }, ct).ConfigureAwait(false);
            return new(stage!, new(sealedStage, verification, target.Reviewed, new() { [0] = snapshot.Reviewed }), facts!, sidecars, space!, null);
        }
        catch
        {
            if (facts is not null) journal.RequireRecovery();
            if (stage is not null) { try { stage.StopWrites(); } finally { stage.Dispose(); } }
            foreach (var sidecar in sidecars) sidecar.Dispose(); throw;
        }
        void ReviewInputs() { ct.ThrowIfCancellationRequested(); target.File.Review(target.Reviewed, ct); snapshot.File.Review(snapshot.Reviewed, ct); }
        void ReviewSidecars() { foreach (var sidecar in sidecars) { ct.ThrowIfCancellationRequested(); sidecar.File.Review(sidecar.Observation, ct); } }
        void Append(ArchiveTransactionFacts next) { journal.ArchiveTransaction(next); facts = next; }
        void Keep(ArchiveSealedSidecar<T> sidecar) { sidecars.Add(sidecar); facts = sidecar.Facts; }
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview, "The current archive, retained snapshot or exact restoration preparation differs from its reviewed objects.");
}
