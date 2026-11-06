namespace FileCat.Core.Archives;

// This is one explicit native recipe, not a generic overwrite fallback or filesystem availability claim.
internal enum ArchiveContentUpdateRecipe { WindowsNtfsTransactionalData = 1 }

internal interface IArchiveContentUpdateStage : IArchiveRetainedStage
{
    string ReviewContentSnapshotCreation(ArchiveArtifactReservation reservation, CancellationToken ct);
}

internal interface IArchiveContentUpdateSource : IArchiveRetainedFile
{
    ArchiveContentUpdateRecipe Recipe { get; }
    ArchiveArtifactReservation ReserveContentSnapshot(Guid nonce, ArchiveSpaceEstimate prepared, CancellationToken ct);
    IArchiveContentTransaction BeginContentTransaction(ArchiveFileObservation reviewed, Guid nonce, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Observation only. Native implementations must require their own exact live issued instance at commit.</summary>
internal abstract class ArchiveContentUpdateReview(ArchiveFileObservation privateOutput, ArchiveFileObservation retainedOriginal)
{
    internal ArchiveFileObservation PrivateOutput { get; } = privateOutput;
    internal ArchiveFileObservation RetainedOriginal { get; } = retainedOriginal;
}

internal interface IArchiveContentTransaction : IDisposable
{
    Guid TransactionId { get; }
    ArchiveFileObservation RetainOriginal(IArchiveContentUpdateStage snapshot, CancellationToken ct);
    ArchiveContentUpdateReview PrepareOutput(IArchiveContentUpdateStage output, ArchiveVerifiedNativeStage verified, ArchiveOriginSnapshot requiredOrigin, CancellationToken ct);
    ArchiveFileObservation Commit(ArchiveContentUpdateReview review, CancellationToken ct);
    ArchiveFileObservation ReviewPublished(ArchiveContentUpdateReview review, CancellationToken ct);
    void Rollback();
}
