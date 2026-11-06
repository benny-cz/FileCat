using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

internal interface IArchiveContentRestorationLease : IDisposable
{
    JobJournal Journal { get; }
    ArchiveTransactionFacts Preparation { get; }
    ArchiveContentUpdateFacts Content { get; }
    ArchiveContentCleanupFacts Catalog { get; }
    ArchiveRetainedBinding Target { get; }
    ArchiveRetainedBinding Snapshot { get; }
    IArchiveContentUpdateSource TargetSource { get; }
    ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum);
    IArchiveContentUpdateStage Create(ArchiveArtifactReservation reservation);
    void Claim(CancellationToken ct);
    void Review(CancellationToken ct);
    void ReviewSnapshot(CancellationToken ct);
}

internal sealed class ArchiveContentRestorationResult(ArchivePreparedTar<IArchiveContentUpdateStage> prepared,
    ArchiveContentUpdateResult<IArchiveContentUpdateStage> update, ArchiveContentRestorationFacts facts) : IDisposable
{
    internal ArchiveContentRestorationFacts Facts { get; } = facts;
    internal ArchiveContentUpdateResult<IArchiveContentUpdateStage> Update { get; } = update;
    public void Dispose() { try { Update.Dispose(); } finally { prepared.Dispose(); } }
}

/// <summary>Always creates a fresh plan/job/rollback snapshot. It never resumes an interrupted archive mutation.</summary>
internal static class ArchiveContentRestoration
{
    internal static async Task<ArchiveContentRestorationResult> RestoreAsync(IArchiveContentRestorationLease lease, JobJournal freshJournal,
        ArchiveAssessmentClient assessor, ArchiveVerificationClient verifier, TimeSpan nativeTimeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lease); ArgumentNullException.ThrowIfNull(freshJournal);
        lease.Claim(ct);
        ArchivePreparedTar<IArchiveContentUpdateStage>? prepared = null; ArchiveContentUpdateResult<IArchiveContentUpdateStage>? result = null;
        ArchiveContentRestorationFacts? facts = null; bool transferred = false;
        try
        {
            prepared = await ArchiveJournaledSnapshotPreparation.PrepareAsync(freshJournal, lease.Preparation.ArchivePath, lease.Preparation.Profile,
                lease.Target, lease.Snapshot, lease.Reserve, lease.Create, assessor, verifier, ct).ConfigureAwait(false);
            result = await Task.Run(() => ArchiveContentUpdate.Publish(freshJournal, prepared,
                new RecordingSource(lease.TargetSource, BeforeCommit), new Dictionary<int, ArchiveRetainedBinding> { [0] = lease.Snapshot },
                lease.Create, nativeTimeout, ct), ct).ConfigureAwait(false);
            // Publish has completed all post-commit source/stage/native rereviews. Cancellation cannot hide that effect.
            if (facts is null) throw new InvalidOperationException("The restore commit has no durable source-journal intent.");
            lease.Journal.ArchiveContentRestoration(facts = facts.Advance(ArchiveContentRestorationPhase.Committed, result.Published));
            transferred = true; return new(prepared, result, facts);
        }
        catch
        {
            if (facts is not null && facts.Phase == ArchiveContentRestorationPhase.CommitIntent)
            {
                try { lease.Journal.ArchiveContentRestoration(facts.Advance(ArchiveContentRestorationPhase.NeedsReview)); } catch { }
            }
            throw;
        }
        finally { if (!transferred) { try { result?.Dispose(); } finally { prepared?.Dispose(); } } }

        void BeforeCommit(ArchiveContentUpdateReview review, CancellationToken cancellation)
        {
            // BeginContentTransaction has consumed the ordinary target lease. Its issued private review and
            // retained rollback snapshot now bind that target; the old snapshot/journal remain independently held.
            lease.ReviewSnapshot(cancellation);
            var old = lease.Journal.RetainedArchiveRecovery();
            var fresh = freshJournal.CurrentArchiveContentUpdate(prepared!.Facts.Nonce);
            var zone = ArchiveWindowsOriginZone.Parse(lease.Target.Reviewed.Origin).DeclaredZone;
            var rollbackOrigin = zone is { } value ? ArchiveWindowsOriginZone.ZoneOnly(value) : ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
            if (fresh.Snapshot != review.RetainedOriginal.Version || review.RetainedOriginal.Version.Length != lease.Target.Reviewed.Version.Length ||
                review.RetainedOriginal.Version.Sha256 != lease.Target.Reviewed.Version.Sha256 || review.RetainedOriginal.Origin.Digest != rollbackOrigin.Digest)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The fresh restore transaction did not retain the reviewed current archive.");
            facts = ArchiveContentRestorationFacts.Begin(lease.Preparation, lease.Content, lease.Catalog,
                old.ContentRestorations.SingleOrDefault(f => f.Nonce == lease.Content.Nonce), fresh,
                freshJournal.Path, lease.Target.Reviewed, lease.Snapshot.Reviewed, review.PrivateOutput);
            lease.Journal.ArchiveContentRestoration(facts); cancellation.ThrowIfCancellationRequested();
        }
    }

    private sealed class RecordingSource(IArchiveContentUpdateSource native, Action<ArchiveContentUpdateReview, CancellationToken> beforeCommit) : IArchiveContentUpdateSource
    {
        public ArchiveContentUpdateRecipe Recipe => native.Recipe;
        public string ObjectIdentity => native.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct) => native.Observe(ct);
        public void Review(ArchiveFileObservation expected, CancellationToken ct) => native.Review(expected, ct);
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => native.OpenReadView(expected, ct);
        public ArchiveArtifactReservation ReserveContentSnapshot(Guid nonce, ArchiveSpaceEstimate prepared, CancellationToken ct) => native.ReserveContentSnapshot(nonce, prepared, ct);
        public IArchiveContentTransaction BeginContentTransaction(ArchiveFileObservation reviewed, Guid nonce, TimeSpan timeout, CancellationToken ct)
            => new RecordingTransaction(native.BeginContentTransaction(reviewed, nonce, timeout, ct), beforeCommit);
    }
    private sealed class RecordingTransaction(IArchiveContentTransaction native, Action<ArchiveContentUpdateReview, CancellationToken> beforeCommit) : IArchiveContentTransaction
    {
        public Guid TransactionId => native.TransactionId;
        public ArchiveFileObservation RetainOriginal(IArchiveContentUpdateStage snapshot, CancellationToken ct) => native.RetainOriginal(snapshot, ct);
        public ArchiveContentUpdateReview PrepareOutput(IArchiveContentUpdateStage output, ArchiveVerifiedNativeStage verified, ArchiveOriginSnapshot requiredOrigin, CancellationToken ct)
            => native.PrepareOutput(output, verified, requiredOrigin, ct);
        public ArchiveFileObservation Commit(ArchiveContentUpdateReview review, CancellationToken ct) { beforeCommit(review, ct); return native.Commit(review, ct); }
        public ArchiveFileObservation ReviewPublished(ArchiveContentUpdateReview review, CancellationToken ct) => native.ReviewPublished(review, ct);
        public void Rollback() => native.Rollback();
        public void Dispose() => native.Dispose();
    }
}
