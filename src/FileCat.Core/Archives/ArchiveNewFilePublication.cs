using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

internal sealed record ArchiveNewFilePublicationResult(ArchiveFileObservation Published, ArchiveTransactionFacts Facts, ArchiveCreationCleanupFacts Cleanup);

/// <summary>
/// Unregistered new-file transaction for explicitly creator-private outputs with qualified creation provenance.
/// No replacement, generic cleanup or profile enablement. Retained evidence is reported as a committed cleanup warning.
/// </summary>
internal static class ArchiveNewFilePublication
{
    internal static Task<ArchiveNewFilePublicationResult> PublishAsync<T>(JobJournal journal, ArchivePreparedContainer<T> prepared,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> captured, Func<ArchiveArtifactReservation, T> create, CancellationToken ct, bool cleanup = false)
        where T : class, IArchiveRetainedStage => Task.Run(() => Publish(journal, prepared, captured, create, ct, cleanup), ct);

    private static ArchiveNewFilePublicationResult Publish<T>(JobJournal journal, ArchivePreparedContainer<T> prepared,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> captured, Func<ArchiveArtifactReservation, T> create, CancellationToken ct, bool cleanup)
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
            ReviewInputs(ct, reopen: false); ReviewSidecars(ct); ReviewOrigin(ct);
            var review = stage.ReviewCreate(prepared.Verified.Stage, leaf, ct);
            var metadata = new ArchiveCreationMetadata(facts, review);
            metadataSidecar = ArchiveJournaledSidecar.WriteCreationMetadata(journal, facts, prepared.CreationMetadata, create, metadata, ct);
            facts = metadataSidecar.Facts;
            stage.Review(prepared.Verified.Stage, ct); ReviewInputs(ct, reopen: false); ReviewSidecars(ct);
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.ReadyToPublish, security: metadata.Sha256));
            // The one input recheck: identity, length and outer metadata (provenance included) immediately before the claim.
            stage.Review(prepared.Verified.Stage, ct); ReviewInputs(ct, reopen: true); ReviewSidecars(ct); ct.ThrowIfCancellationRequested();
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.Publishing));
            ct.ThrowIfCancellationRequested(); claimAttempted = true;
            stage.ClaimNewName(leaf, review, ct);
            // Once namespace mutation may have occurred, finish observations/journaling without cancellation
            // manufacturing a NotCommitted result or authorizing rollback over another occupant/mark.
            var published = stage.ReviewPublished(leaf, review, CancellationToken.None);
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.Reconciling, published: published.Version));
            ReviewInputs(CancellationToken.None, reopen: false); ReviewSidecars(CancellationToken.None);
            published = stage.ReviewPublished(leaf, review, CancellationToken.None);
            Append(facts.AdvanceCreationPublication(ArchiveTransactionPhase.Cleaning));
            var sidecars = prepared.Sidecars.Concat([metadataSidecar]).ToArray();
            var byId = sidecars.ToDictionary(sidecar => sidecar.Observation.Version.Identity, StringComparer.Ordinal);
            var ordered = facts.Sidecars.Select(sidecar => byId[sidecar.Identity!]).ToArray();
            var catalog = ArchiveCreationCleanupFacts.Begin(facts, published, ordered.Select((sidecar, index) =>
                ArchiveContentCleanupArtifact.Capture(facts.Sidecars[index].Path,
                    facts.Sidecars[index].Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest,
                    sidecar.Observation)).ToArray(), review.DestinationParentIdentity);
            journal.ArchiveCreationCleanup(catalog);
            if (cleanup && ordered.All(sidecar => sidecar.File is IArchiveContentUpdateStage or IArchiveOwnedCleanupFile))
            {
                using var owned = new ArchiveCreationCleanup(new LiveLease<T>(journal, catalog, prepared.Stage, published, ordered));
                try { catalog = owned.Execute(ct); }
                catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception)
                { catalog = owned.Facts; } // The archive committed. Retain remaining evidence and expose recovery.
            }
            // A final target review can fail after every unlink was recorded. Keep Cleaning and its
            // unresolved catalog: inventing a retained filename or claiming completion would hide that hold.
            if (catalog.IsResolved || catalog.ReviewedCount < catalog.Artifacts.Count)
                Append(facts.AdvanceCreationPublication(catalog.IsResolved ? ArchiveTransactionPhase.Committed : ArchiveTransactionPhase.CommittedWithRetainedArtifacts,
                    unresolved: catalog.Artifacts.Skip(catalog.ReviewedCount).Select(artifact => artifact.Path).ToArray()));
            return new(published, facts, catalog);
        }
        catch
        {
            // Before the claim the archive name was never touched; the review record then permits an exact discard.
            if (claimAttempted) journal.RequireRecovery(); else journal.RequirePreparationReview();
            try
            {
                facts = journal.RecordArchiveCreationReview(facts.Nonce, claimAttempted);
            }
            catch { /* Keep the original failure and existing durable evidence if appending reconciliation also fails. */ }
            throw;
        }
        finally { metadataSidecar?.Dispose(); }

        void Append(ArchiveTransactionFacts next) { journal.ArchiveTransaction(next); facts = next; }
        void ReviewInputs(CancellationToken cancellation, bool reopen)
        {
            if (inputs.Count != prepared.Verified.Inputs.Count) throw Unsupported();
            foreach (var (id, binding) in inputs)
            {
                cancellation.ThrowIfCancellationRequested();
                if (binding is null || !prepared.Verified.Inputs.TryGetValue(id, out var expected) || !binding.Reviewed.Matches(expected)) throw Unsupported();
                if (reopen) binding.File.ReviewMetadata(expected, cancellation);
            }
            if (reopen) foreach (var directory in prepared.Verified.Directories.Values) directory.Review(cancellation);
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

    private sealed class LiveLease<T>(JobJournal journal, ArchiveCreationCleanupFacts catalog, T target, ArchiveFileObservation published,
        IReadOnlyList<ArchiveSealedSidecar<T>> files) : IArchiveCreationCleanupLease where T : class, IArchiveRetainedStage
    {
        private readonly bool[] removed = new bool[files.Count];
        public JobJournal Journal => journal;
        public ArchiveCreationCleanupFacts Catalog => catalog;
        public void ReviewTarget(CancellationToken ct) => target.Review(published, ct);
        public bool HasArtifact(int index) => !removed[index];
        public void ReviewArtifact(int index, CancellationToken ct)
        {
            if (files[index].File is IArchiveContentUpdateStage windows) windows.ReviewOwnedCleanup(catalog.Artifacts[index], published.ParentIdentity, ct);
            else ((IArchiveOwnedCleanupFile)files[index].File).ReviewOwnedCleanup(catalog.Artifacts[index], published.ParentIdentity, ct);
        }
        public void DeleteArtifact(int index, CancellationToken ct)
        {
            if (files[index].File is IArchiveContentUpdateStage windows) windows.DeleteOwnedCleanup(catalog.Artifacts[index], published.ParentIdentity, ct);
            else ((IArchiveOwnedCleanupFile)files[index].File).DestroyOwnedCleanup(catalog.Artifacts[index], published.ParentIdentity, ct);
            removed[index] = true;
        }
        public void RequireArtifactAbsent(int index)
        { if (!removed[index]) throw Unsupported(); } // Exact native unlink succeeded; no pathname absence inference.
        public void FinishArtifacts(CancellationToken ct) { if (target is IArchivePrivateCreationStage stage) stage.RemovePrivateStageFolder(ct); }
        public void Dispose() { } // Prepared/metadata owners retain responsibility for disposing all leases.
    }
}
