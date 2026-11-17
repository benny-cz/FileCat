namespace FileCat.Core.Jobs;

internal sealed record StorageEvidenceObservation(string DirectoryIdentity, string Identity, long ByteLength);

/// <summary>Storage-only private namespace entry points. Factories own the independent evidence envelope.</summary>
internal sealed record StorageEvidenceBackend(
    Func<string, string, CancellationToken, StorageReviewEvidence> OpenJournal,
    Func<string, CancellationToken, StorageRetainedReviewEvidence> OpenArtifacts,
    Func<string, string, CancellationToken, StorageEvidenceObservation> ObserveJournal,
    Func<string, bool, CancellationToken, StorageEvidenceCursor> OpenCursor,
    Func<string, string, CancellationToken, StorageHistoryEvidence>? OpenHistory = null);

/// <summary>Bound read-only evidence, including a live writer's previously published durable prefix.
/// It grants no acknowledgment, publication or deletion authority.</summary>
internal abstract class StorageHistoryEvidence : IDisposable
{
    internal abstract string DirectoryIdentity { get; }
    internal abstract string Identity { get; }
    internal abstract Stream Stream { get; }
    /// <summary>Charge an export output before its factory opens it, within this same evidence envelope.</summary>
    internal virtual IDisposable ClaimExportOutput() => throw new NotSupportedException("Storage call export is unavailable on this evidence reader.");
    internal virtual StorageEvidenceExport CreateExport(string destination, CancellationToken ct) =>
        throw new NotSupportedException("Bound storage evidence publication is unavailable on this reader.");
    public abstract void Dispose();
}

public sealed record StorageExportResult(string Destination, string? Warning);

/// <summary>New output only. Owns staging and directory handles until same-object publication or cleanup.</summary>
internal abstract class StorageEvidenceExport : IDisposable
{
    internal abstract Stream Stream { get; }
    internal abstract StorageExportResult Publish(CancellationToken ct);
    public abstract void Dispose();
}

internal abstract class StorageEvidenceCursor : IDisposable
{
    internal abstract string DirectoryIdentity { get; }
    internal abstract IReadOnlyList<StorageReviewArtifactInfo> ReadBatch(CancellationToken ct);
    public abstract void Dispose();
    internal static IEnumerable<StorageReviewArtifactInfo> Entries(StorageEvidenceBackend backend, string directory, bool artifacts,
        string? expectedIdentity, CancellationToken ct)
    {
        StorageEvidenceCursor cursor;
        try { cursor = backend.OpenCursor(directory, artifacts, ct); }
        catch (FileNotFoundException) when (expectedIdentity is null) { yield break; }
        using (cursor)
        {
            if (expectedIdentity is not null && cursor.DirectoryIdentity != expectedIdentity) throw new IOException("The evidence cursor opened a changed namespace.");
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var batch = cursor.ReadBatch(ct);
                if (batch.Count == 0) yield break;
                foreach (var item in batch) yield return item;
            }
        }
    }
}
