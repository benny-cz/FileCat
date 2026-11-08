namespace FileCat.Core.Archives;

/// <summary>One reviewed native directory, with no payload or read-stream grant. Not a recursive tree snapshot.</summary>
internal sealed record ArchiveDirectoryObservation
{
    internal ArchiveObjectVersion Version { get; }
    internal string ParentIdentity { get; }
    internal string LeafName { get; }
    internal string MetadataDigest { get; }
    internal ArchiveOriginSnapshot Origin { get; }
    internal ArchiveTimestamp Modified { get; }
    internal int Mode { get; }
    // A native adapter may exclude only directory times changed by deleting its captured children.
    // This extra observation is neither a generic metadata waiver nor deletion authority.
    internal string? RemovalMetadataDigest { get; }

    internal ArchiveDirectoryObservation(string identity, string parentIdentity, string leafName, string metadataDigest,
        ArchiveOriginSnapshot origin, ArchiveTimestamp modified, int mode, string? removalMetadataDigest = null)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArchiveTransactionFacts.ValidateText(parentIdentity, 4096); ArchiveTransactionFacts.ValidateText(leafName, 1024);
        ArchiveWritePolicy.ValidateDigest(metadataDigest);
        if (removalMetadataDigest is not null) ArchiveWritePolicy.ValidateDigest(removalMetadataDigest);
        if (origin.State == ArchiveOriginState.Unavailable || (mode & ~0x1ff) != 0)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The folder's metadata or origin could not be captured.");
        Version = new(identity, 0, ArchiveOriginEvidenceRecord.EmptySha256);
        ParentIdentity = parentIdentity; LeafName = leafName; MetadataDigest = metadataDigest;
        Origin = origin; Modified = modified; Mode = mode;
        RemovalMetadataDigest = removalMetadataDigest;
    }

    internal bool Matches(ArchiveDirectoryObservation other) => Version == other.Version && ParentIdentity == other.ParentIdentity &&
        LeafName == other.LeafName && MetadataDigest == other.MetadataDigest && Origin.Digest == other.Origin.Digest &&
        Modified == other.Modified && Mode == other.Mode && RemovalMetadataDigest == other.RemovalMetadataDigest;
}

/// <summary>Reobserves the fixed native parent/leaf; callers cannot substitute a path at review time.</summary>
internal sealed class ArchiveCapturedDirectory(ArchiveCapturedInputs owner, int id,
    Func<CancellationToken, ArchiveDirectoryObservation> observe, ArchiveDirectoryObservation captured)
{
    internal ArchiveCapturedInputs Owner { get; } = owner;
    internal int Id { get; } = id;
    internal ArchiveDirectoryObservation Reviewed { get; } = captured;
    internal ArchiveCapturedDirectory BorrowFor(ArchiveCapturedInputs target)
    { target.RequireBorrowedFrom(Owner); return new(target, Id, observe, Reviewed); }
    internal void Review(CancellationToken ct) => Owner.ReviewDirectory(observe, Reviewed, ct);
}

internal readonly record struct ArchiveCapturedOriginSource(ArchiveOriginSourceKind Kind, ArchiveObjectVersion Version, ArchiveOriginSnapshot Origin);
internal static class ArchiveCapturedOriginSources
{
    internal static IEnumerable<ArchiveCapturedOriginSource> Enumerate(IEnumerable<ArchiveFileObservation> files,
        IEnumerable<ArchiveDirectoryObservation>? directories)
    {
        foreach (var file in files) yield return new(ArchiveOriginSourceKind.File, file.Version, file.Origin);
        foreach (var directory in directories ?? []) yield return new(ArchiveOriginSourceKind.Directory, directory.Version, directory.Origin);
    }
}
