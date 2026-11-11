using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

internal static class ArchiveCreationPublicationCases
{
    internal static async Task Failure<T>(string failure, string root, IArchiveRetainedFile input, ArchiveFileObservation state,
        Func<Guid, ArchiveArtifactKind, long, ArchiveArtifactReservation> reserve, Func<ArchiveArtifactReservation, T> create,
        ArchiveTransformClient transform, ArchiveVerificationClient verifier, CancellationToken ct) where T : class, IArchiveRetainedStage
    {
        var captured = new Dictionary<int, ArchiveRetainedBinding> { [1] = new(input, state) };
        var plan = ArchiveTarTransformPlan.Create(null, [state.Input(1)], [], [new("file", ArchiveMemberType.File, 1)], ct);
        string target = Path.Combine(root, "result.tar"); using var journal = JobJournal.Create(Path.Combine(root, "journals"),
            new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = new(target, null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned publication refusal", "owned", [], []));
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, target, plan, null, captured, reserve, create, transform, verifier, ct);
        if (failure == "existing-target") File.WriteAllBytes(target, "existing occupant"u8.ToArray());
        if (failure == "changed-input") File.SetLastWriteTimeUtc(Path.Combine(root, "input"), DateTime.UnixEpoch.AddSeconds(1));
        if (failure == "unsafe-directory") File.SetUnixFileMode(root, (UnixFileMode)0x1ff);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        int claims = 0;
        var wrapped = new Wrapped(prepared.Stage, false, beforeClaim: () =>
        {
            claims++; var current = journal.CurrentArchiveTransaction(prepared.Facts.Nonce);
            Assert.Equal(ArchiveTransactionPhase.Publishing, current.Phase); Assert.NotNull(current.SecuritySnapshotSha256);
            Assert.All(current.Sidecars, s => Assert.NotNull(s.Version));
            if (failure == "cancelled") stop.Cancel();
        }, afterClaim: () => { if (failure == "after-claim") throw new IOException("Injected after native namespace claim."); });
        var mirrored = new ArchivePreparedContainer<Wrapped>(wrapped, prepared.Verified, prepared.Facts,
            prepared.Sidecars.Select(s => new ArchiveSealedSidecar<Wrapped>(new(s.File, false), s.Facts, s.Observation)).ToArray(), prepared.Space, prepared.CreationMetadata);
        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => ArchiveNewFilePublication.PublishAsync(journal, mirrored, captured,
            reservation => new Wrapped(create(reservation), true, afterSeal: () =>
            { if (failure == "metadata-change") File.SetLastWriteTimeUtc(reservation.Path, DateTime.UnixEpoch); }), stop.Token));
        Assert.True(error is IOException or OperationCanceledException);
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions); Assert.Equal(ArchiveTransactionPhase.NeedsReview, facts.Phase);
        Assert.True(journal.NeedsRecovery); Assert.NotEmpty(facts.UnresolvedArtifacts); Assert.Null(facts.Published);
        if (failure == "after-claim") { Assert.True(File.Exists(target)); Assert.False(File.Exists(prepared.Facts.StagePath)); Assert.Equal(prepared.Verified.Stage.Version, ((IArchiveRetainedFile)prepared.Stage).Observe(ct).Version); }
        else { Assert.True(File.Exists(prepared.Facts.StagePath)); Assert.Equal(failure == "existing-target", File.Exists(target)); }
        if (failure == "existing-target")
        {
            // The no-replace claim was refused: the other file stays and the user is told exactly that, not a system error.
            Assert.Equal("existing occupant"u8.ToArray(), File.ReadAllBytes(target));
            var refusal = Assert.IsType<ArchiveWriteException>(error); Assert.Equal(ArchiveWriteReason.PublicationUnavailable, refusal.Reason);
            Assert.Equal("Another file named “result.tar” appeared while the archive was being prepared. That file was kept and the new " +
                "archive was not saved. Choose another name, then try again.", refusal.Data[ArchiveWriteMessages.UserMessageKey]);
        }
        Assert.Equal(failure is "changed-input" or "metadata-change" or "unsafe-directory" ? 0 : 1, claims);
        Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "uncertain creation cannot disappear"));
    }

    private sealed class Wrapped(IArchiveRetainedStage inner, bool owns, Action? beforeClaim = null, Action? afterClaim = null, Action? afterSeal = null)
        : IArchiveCreatePublicationStage
    {
        public string ObjectIdentity => inner.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct) => inner.Observe(ct);
        public void Review(ArchiveFileObservation expected, CancellationToken ct) => inner.Review(expected, ct);
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => inner.OpenReadView(expected, ct);
        public string ReviewCreation(ArchiveArtifactReservation reservation, CancellationToken ct) => inner.ReviewCreation(reservation, ct);
        public Stream BeginEvidence(ArchiveArtifactReservation reservation, CancellationToken ct) => inner.BeginEvidence(reservation, ct);
        public ArchiveFileObservation SealEvidence(CancellationToken ct) { var result = inner.SealEvidence(ct); afterSeal?.Invoke(); return result; }
        public Stream BeginRebuild(long maximum, CancellationToken ct) => inner.BeginRebuild(maximum, ct);
        public ArchiveFileObservation SealForVerification(CancellationToken ct) => inner.SealForVerification(ct);
        public void StopWrites() => inner.StopWrites();
        public void Dispose() { if (owns) inner.Dispose(); }
        public ArchiveFileObservation ApplyCreationOrigin(ArchiveFileObservation sealedStage, ArchiveOriginSnapshot required, CancellationToken ct)
            => ((IArchiveCreatePublicationStage)inner).ApplyCreationOrigin(sealedStage, required, ct);
        public ArchiveCreatePublicationReview ReviewCreate(ArchiveFileObservation verified, string target, CancellationToken ct) => ((IArchiveCreatePublicationStage)inner).ReviewCreate(verified, target, ct);
        public void ClaimNewName(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct)
        { beforeClaim?.Invoke(); ((IArchiveCreatePublicationStage)inner).ClaimNewName(leaf, review, ct); afterClaim?.Invoke(); }
        public ArchiveFileObservation ReviewPublished(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct) => ((IArchiveCreatePublicationStage)inner).ReviewPublished(leaf, review, ct);
    }
}
