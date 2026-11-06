using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

internal sealed record ArchiveNewFilePublicationResult(ArchiveFileObservation Published, ArchiveTransactionFacts Facts);

/// <summary>
/// Unregistered new-file transaction for explicitly creator-private outputs with qualified creation provenance.
/// No replacement, generic cleanup or profile enablement. Retained evidence is reported as a committed cleanup warning.
/// </summary>
internal static class ArchiveNewFilePublication
{
    internal static Task<ArchiveNewFilePublicationResult> PublishAsync<T>(JobJournal journal, ArchivePreparedTar<T> prepared,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> captured, Func<ArchiveArtifactReservation, T> create, CancellationToken ct)
        where T : class, IArchiveRetainedStage => Task.Run(() => Publish(journal, prepared, captured, create, ct), ct);

    private static ArchiveNewFilePublicationResult Publish<T>(JobJournal journal, ArchivePreparedTar<T> prepared,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> captured, Func<ArchiveArtifactReservation, T> create, CancellationToken ct)
        where T : class, IArchiveRetainedStage
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(create);
        ct.ThrowIfCancellationRequested(); var facts = prepared.Facts;
        if (facts.Original is not null || facts.RecordVersion != ArchiveTransactionFacts.SchemaVersion || facts.Phase != ArchiveTransactionPhase.Verifying ||
            facts.VerifiedStage is null || facts.VerifiedStage != prepared.Verified.Stage.Version || prepared.CreationMetadata is null ||
            prepared.Stage is not IArchiveCreatePublicationStage stage) throw Unsupported();
        // A stale/deserialized snapshot cannot grant a second publication attempt or a different native action.
        journal.RequireCurrentArchiveTransaction(facts);
        var inputs = captured.ToDictionary(); string leaf = Path.GetFileName(facts.ArchivePath);
        ArchiveSealedSidecar<T>? metadataSidecar = null; bool claimAttempted = false;
        try
        {
            ReviewInputs(ct); ReviewSidecars(ct); ReviewOrigin(ct);
            var review = stage.ReviewCreate(prepared.Verified.Stage, leaf, ct);
            var metadata = new ArchiveCreationMetadata(facts, review);
            metadataSidecar = ArchiveJournaledSidecar.WriteCreationMetadata(journal, facts, prepared.CreationMetadata, create, metadata, ct);
            facts = metadataSidecar.Facts;
            stage.Review(prepared.Verified.Stage, ct); ReviewInputs(ct); ReviewSidecars(ct);
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.ReadyToPublish, security: metadata.Sha256));
            stage.Review(prepared.Verified.Stage, ct); ReviewInputs(ct); ReviewSidecars(ct); ct.ThrowIfCancellationRequested();
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.Publishing));
            ct.ThrowIfCancellationRequested(); claimAttempted = true;
            stage.ClaimNewName(leaf, review, ct);
            // Once namespace mutation may have occurred, finish observations/journaling without cancellation
            // manufacturing a NotCommitted result or authorizing rollback over another occupant/mark.
            var published = stage.ReviewPublished(leaf, review, CancellationToken.None);
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.Reconciling, published: published.Version));
            ReviewInputs(CancellationToken.None); ReviewSidecars(CancellationToken.None);
            published = stage.ReviewPublished(leaf, review, CancellationToken.None);
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.Cleaning));
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.CommittedWithRetainedArtifacts,
                unresolved: facts.Sidecars.Select(s => s.Path).ToArray()));
            return new(published, facts);
        }
        catch
        {
            journal.RequireRecovery();
            try
            {
                facts = journal.RecordArchiveCreationReview(facts.Nonce, claimAttempted);
            }
            catch { /* Keep the original failure and existing durable evidence if appending reconciliation also fails. */ }
            throw;
        }
        finally { metadataSidecar?.Dispose(); }

        void Append(ArchiveTransactionFacts next) { journal.ArchiveTransaction(next); facts = next; }
        void ReviewInputs(CancellationToken cancellation)
        {
            if (inputs.Count != prepared.Verified.Inputs.Count) throw Unsupported();
            foreach (var (id, binding) in inputs)
            {
                cancellation.ThrowIfCancellationRequested();
                if (binding is null || !prepared.Verified.Inputs.TryGetValue(id, out var expected) || !binding.Reviewed.Matches(expected)) throw Unsupported();
                binding.File.Review(expected, cancellation);
            }
            foreach (var directory in prepared.Verified.Directories.Values) directory.Review(cancellation);
        }
        void ReviewSidecars(CancellationToken cancellation)
        {
            foreach (var evidence in prepared.Sidecars) evidence.File.Review(evidence.Observation, cancellation);
            if (metadataSidecar is not null) metadataSidecar.File.Review(metadataSidecar.Observation, cancellation);
        }
        void ReviewOrigin(CancellationToken cancellation)
        {
            var evidence = prepared.Sidecars.Single(s => s.Observation.Version.Identity == facts.OriginEvidenceIdentity);
            var version = evidence.Observation.Version;
            var required = ArchiveCreationOrigin.Review(evidence.File.OpenReadView(evidence.Observation, cancellation), new(version.Length, version.Sha256),
                new(facts.Nonce, facts.Profile, ArchiveOriginOperation.Create, checked(inputs.Count + prepared.Verified.Directories.Count)),
                inputs.OrderBy(p => p.Key).Select(p => p.Value.Reviewed), prepared.Verified.Stage.Origin.Format, cancellation,
                prepared.Verified.Directories.OrderBy(p => p.Key).Select(p => p.Value.Reviewed));
            if (required.Digest != prepared.Verified.Stage.Origin.Digest) throw Unsupported();
        }
    }
    private static ArchiveWriteException Unsupported() => new(ArchiveWriteReason.PublicationUnavailable,
        "This transaction does not qualify for private new-file publication with its required provenance; retain its evidence without replacement.");
}
