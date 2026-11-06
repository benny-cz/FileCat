using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>Owns the committed read pin and retained snapshot. Disposal closes handles; it never deletes artifacts.</summary>
internal sealed class ArchiveContentUpdateResult<T>(IArchiveContentTransaction transaction, T snapshot,
    ArchiveFileObservation published, ArchiveContentUpdateFacts facts) : IDisposable where T : class, IArchiveContentUpdateStage
{
    internal ArchiveFileObservation Published { get; } = published;
    internal ArchiveContentUpdateFacts Facts { get; } = facts;
    internal T Snapshot { get; } = snapshot;
    public void Dispose() { try { transaction.Dispose(); } finally { Snapshot.Dispose(); } }
}

/// <summary>
/// Unregistered NTFS same-object publication with a separate durable recipe. Recovery is observation-only;
/// cleanup, hard-link policy, crash qualification and release admission remain separate gates.
/// </summary>
internal static class ArchiveContentUpdate
{
    internal static ArchiveContentUpdateResult<T> Publish<T>(JobJournal journal, ArchivePreparedTar<T> prepared,
        IArchiveContentUpdateSource original, IReadOnlyDictionary<int, ArchiveRetainedBinding> captured, Func<ArchiveArtifactReservation, T> create, TimeSpan nativeTimeout, CancellationToken ct) where T : class, IArchiveContentUpdateStage
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(create);
        if (!OperatingSystem.IsWindows() || original.Recipe != ArchiveContentUpdateRecipe.WindowsNtfsTransactionalData) throw Invalid();
        var preparation = prepared.Facts; var baseline = prepared.Verified.Original;
        if (baseline is null || preparation.Original != baseline.Version || preparation.VerifiedStage != prepared.Verified.Stage.Version ||
            preparation.Phase != ArchiveTransactionPhase.Verifying || preparation.OriginEvidence is null || preparation.RecordVersion != ArchiveTransactionFacts.SchemaVersion)
            throw Invalid();
        journal.RequireCurrentArchiveTransaction(preparation);
        var inputs = captured.ToDictionary();
        ArchiveContentUpdateFacts? facts = null;
        T? snapshot = null; IArchiveContentTransaction? transaction = null;
        bool commitAttempted = false, transferred = false;
        try
        {
            ct.ThrowIfCancellationRequested(); ReviewInputs(ct); ReviewSidecars(ct);
            ((IArchiveRetainedFile)original).Review(baseline, ct);
            var requiredOrigin = ReviewOrigin(ct);
            var reservation = original.ReserveContentSnapshot(preparation.Nonce, prepared.Space.Estimate, ct);
            Append(ArchiveContentUpdateFacts.Reserve(preparation, reservation, baseline, requiredOrigin));
            Current(); ct.ThrowIfCancellationRequested();
            // The issuer-bound reservation's creator is the same held native directory as the prepared stage.
            snapshot = create(reservation);
            string policy = snapshot.ReviewContentSnapshotCreation(reservation, ct);
            Append(facts!.Advance(ArchiveContentUpdatePhase.SnapshotCreated, snapshotIdentity: snapshot.ObjectIdentity, creationPolicy: policy));
            Current(); ReviewInputs(ct); ReviewSidecars(ct);
            transaction = original.BeginContentTransaction(baseline, preparation.Nonce, nativeTimeout, ct);
            Append(facts!.Advance(ArchiveContentUpdatePhase.TransactionOpened, transactionId: transaction.TransactionId));
            Current();
            var retained = transaction.RetainOriginal(snapshot, ct);
            Append(facts!.Advance(ArchiveContentUpdatePhase.SnapshotSealed, snapshot: retained.Version));
            Current(); ReviewInputs(ct); ReviewSidecars(ct);
            if (ReviewOrigin(ct).Digest != requiredOrigin.Digest) throw Invalid();
            var ready = transaction.PrepareOutput(prepared.Stage, prepared.Verified, requiredOrigin, ct);
            Append(facts!.Advance(ArchiveContentUpdatePhase.Ready, privateOutput: ready.PrivateOutput));
            Current(); ReviewInputs(ct); ReviewSidecars(ct); ct.ThrowIfCancellationRequested();
            Append(facts!.Advance(ArchiveContentUpdatePhase.CommitIntent));
            Current(); ct.ThrowIfCancellationRequested(); commitAttempted = true;
            var published = transaction.Commit(ready, ct);
            // Cancellation cannot hide a possible public effect or authorize a second transaction over it.
            ReviewInputs(CancellationToken.None); ReviewSidecars(CancellationToken.None);
            published = transaction.ReviewPublished(ready, CancellationToken.None);
            Append(facts!.Advance(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, published: published.Version));
            transferred = true;
            return new(transaction, snapshot, published, facts!);
        }
        catch
        {
            journal.RequireRecovery();
            if (facts is not null)
            {
                bool rolledBack = false;
                if (transaction is not null && !commitAttempted)
                {
                    try
                    {
                        transaction.Rollback();
                        Append(facts.Advance(ArchiveContentUpdatePhase.RolledBackWithRetainedArtifacts)); rolledBack = true;
                    }
                    catch { /* Retain the original exception and all artifacts if rollback or recording cannot be confirmed. */ }
                }
                if (!rolledBack)
                {
                    try { Append(facts.Advance(ArchiveContentUpdatePhase.NeedsReview)); }
                    catch { /* Existing durable intent and reserved names remain recovery evidence. */ }
                }
            }
            throw;
        }
        finally
        {
            if (!transferred) { try { transaction?.Dispose(); } finally { snapshot?.Dispose(); } }
        }

        void Append(ArchiveContentUpdateFacts next) { journal.ArchiveContentUpdate(next); facts = next; }
        void Current() => journal.RequireCurrentArchiveContentUpdate(facts!);
        void ReviewInputs(CancellationToken cancellation)
        {
            if (inputs.Count != prepared.Verified.Inputs.Count) throw Invalid();
            foreach (var (id, binding) in inputs)
            {
                cancellation.ThrowIfCancellationRequested();
                if (binding is null || !prepared.Verified.Inputs.TryGetValue(id, out var expected) || !binding.Reviewed.Matches(expected)) throw Invalid();
                binding.File.Review(expected, cancellation);
            }
        }
        void ReviewSidecars(CancellationToken cancellation)
        {
            ((IArchiveRetainedFile)prepared.Stage).Review(prepared.Verified.Stage, cancellation);
            foreach (var evidence in prepared.Sidecars) ((IArchiveRetainedFile)evidence.File).Review(evidence.Observation, cancellation);
        }
        ArchiveOriginSnapshot ReviewOrigin(CancellationToken cancellation)
        {
            var evidence = prepared.Sidecars.Single(s => s.Observation.Version.Identity == preparation.OriginEvidenceIdentity);
            var version = evidence.Observation.Version;
            if (version != preparation.OriginEvidence) throw Invalid();
            return ArchiveUpdateOrigin.ReviewWindows(((IArchiveRetainedFile)evidence.File).OpenReadView(evidence.Observation, cancellation),
                new(version.Length, version.Sha256), new(preparation.Nonce, preparation.Profile, ArchiveOriginOperation.Update, checked(inputs.Count + 1)),
                baseline, inputs.OrderBy(pair => pair.Key).Select(pair => pair.Value.Reviewed), cancellation);
        }
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview,
        "The native content update differs from its current preparation or complete captured evidence; retain all artifacts.");
}
