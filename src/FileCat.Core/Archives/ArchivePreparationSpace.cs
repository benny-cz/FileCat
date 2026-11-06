using FileCat.Core.FileSystem;

namespace FileCat.Core.Archives;

/// <summary>Conservative byte accounting, not an allocation reservation or a promise that free space stays available.</summary>
internal sealed record ArchiveSpaceEstimate
{
    internal const long SafetyMarginBytes = 64L << 20;
    internal long OriginalBytes { get; }
    internal long StageBytes { get; }
    internal long EvidenceBytes { get; }
    internal long KnownSnapshotBytes { get; }
    internal long RetainedOriginalCopyBytes { get; }
    internal long PublicationScratchBytes { get; }
    internal long AdditionalBytes { get; }
    internal long TotalFootprintBytes { get; }

    internal ArchiveSpaceEstimate(long originalBytes, long stageBytes, long evidenceBytes, long knownSnapshotBytes,
        long retainedOriginalCopyBytes = 0, long publicationScratchBytes = 0)
    {
        if (originalBytes is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes || stageBytes is < 1024 or > ArchiveWritePolicy.MaxPhysicalBytes ||
            evidenceBytes is < 0 or > ArchiveWritePolicy.MaxMetadataBytes || knownSnapshotBytes is < 0 or > ArchiveWritePolicy.MaxLogicalBytes ||
            retainedOriginalCopyBytes is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes || publicationScratchBytes is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive space estimate exceeds its reviewed byte bounds.");
        OriginalBytes = originalBytes; StageBytes = stageBytes; EvidenceBytes = evidenceBytes; KnownSnapshotBytes = knownSnapshotBytes;
        RetainedOriginalCopyBytes = retainedOriginalCopyBytes; PublicationScratchBytes = publicationScratchBytes;
        AdditionalBytes = checked(stageBytes + evidenceBytes + knownSnapshotBytes + retainedOriginalCopyBytes + publicationScratchBytes + SafetyMarginBytes);
        // The retained original is already allocated and excluded from available bytes. Display its footprint
        // separately; do not charge its existing allocation a second time against free space.
        TotalFootprintBytes = checked(originalBytes + AdditionalBytes);
    }
}

internal sealed record ArchiveSpaceReview
{
    internal string ParentIdentity { get; }
    internal ArchiveSpaceEstimate Estimate { get; }
    internal FileSystemCapacitySnapshot? Capacity { get; }
    internal bool CapacityKnown => Capacity is not null;
    internal DateTimeOffset ObservedAtUtc { get; } = DateTimeOffset.UtcNow;
    internal ArchiveSpaceReview(string parentIdentity, ArchiveSpaceEstimate estimate, FileSystemCapacitySnapshot? capacity)
    {
        ArchiveTransactionFacts.ValidateText(parentIdentity, 4096); ArgumentNullException.ThrowIfNull(estimate);
        if (capacity is not null)
        {
            ArchiveTransactionFacts.ValidateText(capacity.VolumeIdentity, 32768);
            if (capacity.AvailableBytes < 0) throw new ArgumentException("A known filesystem capacity cannot be negative.");
        }
        ParentIdentity = parentIdentity; Estimate = estimate; Capacity = capacity;
    }
    internal void RequireAvailable()
    {
        if (Capacity is { } capacity && capacity.AvailableBytes < Estimate.AdditionalBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The archive destination has insufficient available space for the full stage, evidence, known snapshots and 64 MiB safety margin.");
    }
}

/// <summary>Only the reservation's retained native directory supplies a destination-bound observation.</summary>
internal interface IArchiveDestinationSpace
{
    ArchiveSpaceReview ReviewSpace(string expectedParentIdentity, ArchiveSpaceEstimate estimate, CancellationToken ct);
}
