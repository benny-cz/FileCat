namespace FileCat.Core.Archives.Unix;

/// <summary>
/// The update-stage operations the Unix exchange coordinator needs. Implemented by the native held stage; test drivers
/// may wrap it to observe durable facts between steps without any hook in production code.
/// </summary>
internal interface IUnixArchiveExchangeStage : IArchiveCreatePublicationStage
{
    ArchiveFileObservation ApplyUpdateMetadata(ArchiveFileObservation verifiedStage, UnixArchiveFile original,
        ArchiveFileObservation reviewedOriginal, ArchiveOriginSnapshot requiredOrigin, CancellationToken ct);
    UnixArchiveCrossDirectoryReceipt ExchangeRetainedAcrossParents(UnixArchiveFile original, ArchiveFileObservation expectedStage,
        ArchiveFileObservation expectedOriginal, CancellationToken ct);
    void DestroyOwned(ArchiveObjectVersion expected, string? expectedMetadataDigest, CancellationToken ct);
}

internal sealed partial class UnixArchiveFile
{
    ArchiveFileObservation IUnixArchiveExchangeStage.ApplyUpdateMetadata(ArchiveFileObservation verifiedStage, UnixArchiveFile original,
        ArchiveFileObservation reviewedOriginal, ArchiveOriginSnapshot requiredOrigin, CancellationToken ct) =>
        ApplyUpdateMetadata(verifiedStage, original, reviewedOriginal, requiredOrigin, ct);
    UnixArchiveCrossDirectoryReceipt IUnixArchiveExchangeStage.ExchangeRetainedAcrossParents(UnixArchiveFile original,
        ArchiveFileObservation expectedStage, ArchiveFileObservation expectedOriginal, CancellationToken ct) =>
        ExchangeRetainedAcrossParents(original, expectedStage, expectedOriginal, ct);
    void IUnixArchiveExchangeStage.DestroyOwned(ArchiveObjectVersion expected, string? expectedMetadataDigest, CancellationToken ct) =>
        DestroyOwned(expected, expectedMetadataDigest, ct);
}
