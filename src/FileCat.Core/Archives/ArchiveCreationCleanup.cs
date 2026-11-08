using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>One native live or recovered scope. A catalog supplies observations, never pathname authority.</summary>
internal interface IArchiveCreationCleanupLease : IDisposable
{
    JobJournal Journal { get; }
    ArchiveCreationCleanupFacts Catalog { get; }
    void ReviewTarget(CancellationToken ct);
    bool HasArtifact(int index);
    void ReviewArtifact(int index, CancellationToken ct);
    void DeleteArtifact(int index, CancellationToken ct);
    void RequireArtifactAbsent(int index);
}

internal sealed class ArchiveCreationCleanup : IDisposable
{
    private readonly object gate = new();
    private readonly IArchiveCreationCleanupLease lease;
    private bool disposed;
    internal ArchiveCreationCleanupFacts Facts { get; private set; }
    internal ArchiveCreationCleanup(IArchiveCreationCleanupLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease); this.lease = lease;
        try
        {
            Facts = lease.Journal.CurrentArchiveCreationCleanup(lease.Catalog.Nonce);
            if (Facts.ScopeSha256 != lease.Catalog.ScopeSha256 || Facts.Target != lease.Catalog.Target ||
                Facts.TargetMetadataSha256 != lease.Catalog.TargetMetadataSha256 || Facts.TargetOriginSha256 != lease.Catalog.TargetOriginSha256)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The creation cleanup scope changed.");
        }
        catch { lease.Dispose(); throw; }
    }
    internal ArchiveCreationCleanupFacts Execute(CancellationToken ct)
    {
        lock (gate) return ExecuteOwned(ct);
    }
    private ArchiveCreationCleanupFacts ExecuteOwned(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var journal = lease.Journal;
        try
        {
            Current(); lease.ReviewTarget(ct);
            if (Facts.IsResolved) return Facts;
            if (Facts.Phase == ArchiveCreationCleanupPhase.NeedsReview) Append(ArchiveCreationCleanupPhase.Cataloged);
            while (Facts.ReviewedCount < Facts.Artifacts.Count)
            {
                ct.ThrowIfCancellationRequested(); Current(); lease.ReviewTarget(ct); int index = Facts.ReviewedCount;
                if (!lease.HasArtifact(index))
                { lease.RequireArtifactAbsent(index); Append(ArchiveCreationCleanupPhase.Absent); }
                else
                {
                    lease.ReviewArtifact(index, ct);
                    if (Facts.Phase != ArchiveCreationCleanupPhase.DeleteIntent) Append(ArchiveCreationCleanupPhase.DeleteIntent);
                    Current(); ct.ThrowIfCancellationRequested(); lease.ReviewTarget(ct); lease.DeleteArtifact(index, ct);
                    Append(ArchiveCreationCleanupPhase.Deleted);
                }
                lease.ReviewTarget(CancellationToken.None);
            }
            for (int i = 0; i < Facts.Artifacts.Count; i++) lease.RequireArtifactAbsent(i);
            lease.ReviewTarget(CancellationToken.None); Append(ArchiveCreationCleanupPhase.Completed); return Facts;
        }
        catch
        {
            journal.RequireRecovery();
            if (!Facts.IsResolved && Facts.Phase != ArchiveCreationCleanupPhase.NeedsReview)
                try { Append(ArchiveCreationCleanupPhase.NeedsReview); } catch { }
            throw;
        }
        void Current() => journal.RequireCurrentArchiveCreationCleanup(Facts);
        void Append(ArchiveCreationCleanupPhase phase)
        { var next = Facts.Advance(phase); journal.ArchiveCreationCleanup(next); Facts = next; }
    }
    internal void Finish(string summary)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Facts.IsResolved) throw new InvalidOperationException("Creation cleanup is not complete.");
            lease.Journal.RequireCurrentArchiveCreationCleanup(Facts); lease.ReviewTarget(CancellationToken.None);
            lease.Journal.Finish(JobState.Completed, summary);
        }
    }
    public void Dispose() { lock (gate) { if (!disposed) { disposed = true; lease.Dispose(); } } }
}
