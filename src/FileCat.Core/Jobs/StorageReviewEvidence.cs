namespace FileCat.Core.Jobs;

/// <summary>Detached inventory is not deletion or read authority; an open compares the exact native identity.</summary>
internal sealed record StorageReviewArtifactInfo(string Leaf, string Identity, long ByteLength);

/// <summary>Platform authority for one inactive journal in the private evidence namespace. Its factory acquires the
/// independent evidence envelope and writer exclusion. It retains both until Dispose, including on failure.
/// Artifact operations are handle-relative/no-follow, private, single-link and exact-object operations. A factory must
/// fit journal ownership plus one directory/cursor and one artifact stream into the three-slot envelope.</summary>
internal abstract class StorageReviewArtifacts : IDisposable
{
    internal abstract string DirectoryIdentity { get; }
    internal abstract bool IsOwned { get; }
    // The inventory closes its cursor before returning. At most maximum+1 entries may be materialized.
    internal abstract IReadOnlyList<StorageReviewArtifactInfo> Artifacts(Guid association, int maximum, CancellationToken ct);
    internal abstract StorageReviewArtifact OpenArtifact(StorageReviewArtifactInfo expected, CancellationToken ct);
    internal abstract void DeleteArtifact(StorageReviewArtifact artifact, StorageEvidenceVersion expected, CancellationToken ct);
    public abstract void Dispose();
}

internal abstract class StorageReviewEvidence : StorageReviewArtifacts
{
    internal abstract string JournalLeaf { get; }
    internal abstract string JournalIdentity { get; }
    internal abstract Stream Journal { get; }
    internal virtual StorageEvidenceExport CreateExport(string destination, CancellationToken ct) =>
        throw new NotSupportedException("Bound storage evidence publication is unavailable on this owner.");
    internal abstract StorageReviewArtifact CreateArtifact(string temporaryLeaf, CancellationToken ct);
    internal abstract void PublishNew(StorageReviewArtifact artifact, string publishedLeaf, CancellationToken ct);
    internal abstract void DeleteJournal(StorageEvidenceVersion expected, CancellationToken ct);
}

/// <summary>Read/retention authority after a journal has gone. It can never publish an acknowledgment or replay work.
/// The platform rechecks journal absence while retaining each exact artifact before removal.</summary>
internal abstract class StorageRetainedReviewEvidence : StorageReviewArtifacts
{
    internal abstract bool JournalExists(string historicalLeaf, Guid association, CancellationToken ct);
    internal abstract void DeleteUnreferenced(StorageReviewArtifact artifact, StorageEvidenceVersion expected,
        string historicalLeaf, Guid association, CancellationToken ct);
}

internal abstract class StorageReviewArtifact : IDisposable
{
    internal abstract string Leaf { get; }
    internal abstract string Identity { get; }
    internal abstract Stream Stream { get; }
    internal abstract void FlushDurable(CancellationToken ct);
    public abstract void Dispose();
}
