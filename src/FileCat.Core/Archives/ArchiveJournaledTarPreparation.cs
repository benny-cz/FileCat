using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>Unpublished native preparation. The caller continues to own every source lease.</summary>
internal sealed class ArchivePreparedTar<T>(T stage, ArchiveVerifiedNativeStage verified, ArchiveTransactionFacts facts,
    IReadOnlyList<ArchiveSealedSidecar<T>> sidecars, ArchiveSpaceReview space) : IDisposable where T : class, IArchiveRetainedStage
{
    internal T Stage { get; } = stage;
    internal ArchiveVerifiedNativeStage Verified { get; } = verified;
    internal ArchiveTransactionFacts Facts { get; } = facts;
    internal ArchiveSpaceReview Space { get; } = space;
    internal IReadOnlyList<ArchiveSealedSidecar<T>> Sidecars { get; } = Array.AsReadOnly(sidecars.ToArray());
    public void Dispose() { try { Stage.Dispose(); } finally { foreach (var sidecar in Sidecars) sidecar.Dispose(); } }
}

/// <summary>
/// Unregistered TAR preparation with private native sidecars and journal phase ordering. No publication, cleanup,
/// metadata application or ReadyToPublish claim. Capture/global budgets and final platform qualification remain gates.
/// </summary>
internal static class ArchiveJournaledTarPreparation
{
    internal static async Task<ArchivePreparedTar<T>> PrepareAsync<T>(JobJournal journal, string archivePath,
        ArchiveTarTransformPlan plan, ArchiveRetainedBinding? original, IReadOnlyDictionary<int, ArchiveRetainedBinding> captured,
        Func<Guid, ArchiveArtifactKind, long, ArchiveArtifactReservation> reserve, Func<ArchiveArtifactReservation, T> create,
        ArchiveTransformClient transform, ArchiveVerificationClient verifier, CancellationToken ct, long knownSnapshotBytes = 0) where T : class, IArchiveRetainedStage
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(reserve); ArgumentNullException.ThrowIfNull(create); ArgumentNullException.ThrowIfNull(transform); ArgumentNullException.ThrowIfNull(verifier);
        ct.ThrowIfCancellationRequested(); ArchiveTransactionFacts.ValidatePath(archivePath);
        string targetLeaf = Path.GetFileName(archivePath);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.IsNullOrEmpty(targetLeaf) || original is not null && !string.Equals(original.Reviewed.LeafName, targetLeaf, pathComparison)) throw Invalid();
        if (captured.Count != plan.Inputs.Count || captured.Count > ArchiveWritePolicy.MaxMembers || (original is null) != (plan.Source is null)) throw Invalid();
        // New folder capture must supply its own retained metadata/origin snapshot before that operation can use this bridge.
        if (plan.Additions.Any(a => a.Type == ArchiveMemberType.Directory))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "New directories require retained folder metadata and provenance capture.");
        var inputs = captured.ToDictionary(); var sidecars = new List<ArchiveSealedSidecar<T>>(); T? stage = null;
        ArchiveTransactionFacts? facts = null; ArchiveSpaceReview? space = null;
        try
        {
            var setup = await Task.Run(() =>
            {
                ReviewInputs();
                Guid nonce = Guid.NewGuid(); var profile = plan.Expected.Profile;
                var records = new List<ArchiveOriginEvidenceRecord>(inputs.Count + (original is null ? 0 : 1));
                if (original is not null) records.Add(new(ArchiveOriginSourceKind.OriginalArchive, original.Reviewed.Version, original.Reviewed.Origin));
                foreach (var pair in inputs.OrderBy(p => p.Key)) { ct.ThrowIfCancellationRequested(); records.Add(new(ArchiveOriginSourceKind.File, pair.Value.Reviewed.Version, pair.Value.Reviewed.Origin)); }
                var originScope = new ArchiveOriginEvidenceScope(nonce, profile, original is null ? ArchiveOriginOperation.Create : ArchiveOriginOperation.Update, records.Count);
                long expectedLength = ArchiveManifestSidecar.EncodedLength(plan.Expected, ct);
                long assessedLength = plan.Source is null ? 0 : ArchiveManifestSidecar.EncodedLength(plan.Source.Manifest, ct);
                long originLength = ArchiveOriginEvidenceWriter.EncodedLength(originScope, records, ct);
                long evidenceLength = checked(2 * expectedLength + assessedLength + originLength);
                if (evidenceLength > ArchiveWritePolicy.MaxMetadataBytes) throw ArchiveWriteBudget.Limit();
                long stageBound = ArchiveTransformWire.OutputBound(plan.Expected, ct);
                var stageName = reserve(nonce, ArchiveArtifactKind.Stage, stageBound);
                var expectedName = reserve(nonce, ArchiveArtifactKind.Manifest, expectedLength);
                var originName = reserve(nonce, ArchiveArtifactKind.Provenance, originLength);
                var verifiedName = reserve(nonce, ArchiveArtifactKind.Manifest, expectedLength);
                var assessedName = plan.Source is null ? null : reserve(nonce, ArchiveArtifactKind.Manifest, assessedLength);
                if (original is not null && original.Reviewed.ParentIdentity != stageName.ParentIdentity ||
                    inputs.Values.Any(b => b.Reviewed.ParentIdentity == stageName.ParentIdentity && string.Equals(b.Reviewed.LeafName, targetLeaf, pathComparison)))
                    throw Invalid();
                CheckReservation(stageName, ArchiveArtifactKind.Stage, stageBound);
                CheckReservation(expectedName, ArchiveArtifactKind.Manifest, expectedLength);
                CheckReservation(originName, ArchiveArtifactKind.Provenance, originLength);
                CheckReservation(verifiedName, ArchiveArtifactKind.Manifest, expectedLength);
                if (assessedName is not null) CheckReservation(assessedName, ArchiveArtifactKind.Manifest, assessedLength);
                space = stageName.ReviewSpace(new(original?.Reviewed.Version.Length ?? 0, stageBound, evidenceLength, knownSnapshotBytes), ct);
                var reservedSidecars = new List<ArchiveSidecarFacts>
                {
                    new(ArchiveSidecarRole.ExpectedOutput, expectedName.Path, expectedLength),
                    new(ArchiveSidecarRole.OriginEvidence, originName.Path, originLength, originSources: records.Count),
                    new(ArchiveSidecarRole.VerifiedOutput, verifiedName.Path, expectedLength),
                };
                if (assessedName is not null) reservedSidecars.Add(new(ArchiveSidecarRole.AssessedSource, assessedName.Path, assessedLength));
                facts = new(nonce, 1, profile, ArchiveTransactionPhase.Reserved, archivePath, stageName.ParentIdentity, original?.Reviewed.Version,
                    stageName.Path, null, stageMaximumLength: stageName.MaximumLength, sidecars: reservedSidecars);
                stage = ArchiveJournaledStage.Create(journal, facts, stageName, create, ct); facts = journal.CurrentArchiveTransaction(nonce);
                if (assessedName is not null)
                    Keep(ArchiveJournaledSidecar.WriteManifest(journal, facts, assessedName, create, ArchiveManifestPurpose.AssessedSource, plan.Source!.Manifest, ct));
                Keep(ArchiveJournaledSidecar.WriteManifest(journal, facts, expectedName, create, ArchiveManifestPurpose.ExpectedOutput, plan.Expected, ct));
                Keep(ArchiveJournaledSidecar.WriteOrigin(journal, facts, originName, create, originScope, records, ct));
                return verifiedName;

                void CheckReservation(ArchiveArtifactReservation name, ArchiveArtifactKind kind, long bound)
                {
                    if (name.Nonce != nonce || name.Kind != kind || name.MaximumLength != bound || name.ParentIdentity != stageName.ParentIdentity) throw Invalid();
                }
            }, ct).ConfigureAwait(false);
            var verified = await ArchiveRetainedTarPipeline.RebuildAndVerifyAsync(plan, original, inputs, stage!, transform, verifier, ct,
                beforeRebuild: () => { ReviewSidecars(); Append(facts!.AdvancePreparation(ArchiveTransactionPhase.Rebuilding)); },
                beforeVerification: sealedStage => { ReviewSidecars(); Append(facts!.AdvancePreparation(ArchiveTransactionPhase.Verifying, finalizedStage: sealedStage.Version)); },
                beforeFinalizing: () => Append(facts!.AdvancePreparation(ArchiveTransactionPhase.Finalizing))).ConfigureAwait(false);
            await Task.Run(() =>
            {
                Keep(ArchiveJournaledSidecar.WriteVerifiedManifest(journal, facts!, setup, create, verified.Verification, ct));
                stage!.Review(verified.Stage, ct); ReviewInputs(); ReviewSidecars();
                Append(facts!.AdvancePreparation(ArchiveTransactionPhase.Verifying, verified.Stage.Version));
            }, ct).ConfigureAwait(false);
            return new(stage!, verified, facts!, sidecars, space!);
        }
        catch
        {
            if (facts is not null) journal.RequireRecovery();
            if (stage is not null) { try { stage.StopWrites(); } finally { stage.Dispose(); } }
            foreach (var sidecar in sidecars) sidecar.Dispose();
            throw;
        }

        void Append(ArchiveTransactionFacts next) { journal.ArchiveTransaction(next); facts = next; }
        void Keep(ArchiveSealedSidecar<T> evidence) { sidecars.Add(evidence); facts = evidence.Facts; }
        void ReviewSidecars() { foreach (var evidence in sidecars) { ct.ThrowIfCancellationRequested(); evidence.File.Review(evidence.Observation, ct); } }
        void ReviewInputs()
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            if (original is not null)
            {
                if (original.Reviewed.Version.Length != plan.Source!.SourceLength || original.Reviewed.Version.Sha256 != plan.Source.SourceSha256 ||
                    !identities.Add(original.Reviewed.Version.Identity)) throw Invalid();
                original.File.Review(original.Reviewed, ct);
            }
            foreach (var (id, binding) in inputs)
            {
                ct.ThrowIfCancellationRequested();
                if (binding is null || !plan.Inputs.TryGetValue(id, out var expected) || binding.Reviewed.Input(id) != expected || !identities.Add(binding.Reviewed.Version.Identity)) throw Invalid();
                binding.File.Review(binding.Reviewed, ct);
            }
        }
    }

    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The reviewed TAR capture differs from its frozen preparation plan.");
}
