using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>Fresh platform-owned leases. Every action still validates the actual native objects.</summary>
internal interface IArchiveContentCleanupRecoveryLease : IDisposable
{
    JobJournal Journal { get; }
    ArchiveContentCleanupFacts Catalog { get; }
    ArchiveContentRecoveryFacts Candidate { get; }
    void ReviewTarget(CancellationToken ct);
    bool HasArtifact(int index);
    void ReviewArtifact(int index, CancellationToken ct);
    void DeleteArtifact(int index, CancellationToken ct);
    void RequireArtifactAbsent(int index);
}

/// <summary>Durable cleanup reconciliation, separate from native authority and the original live operation.</summary>
internal sealed class ArchiveContentCleanupRecovery : IDisposable
{
    private readonly object gate = new();
    private readonly IArchiveContentCleanupRecoveryLease lease;
    private bool disposed;
    internal ArchiveContentRecoveryFacts? Facts { get; private set; }

    internal ArchiveContentCleanupRecovery(IArchiveContentCleanupRecoveryLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        this.lease = lease;
        try
        {
            Facts = lease.Journal.RetainedArchiveRecovery().ContentRecoveries.SingleOrDefault(f => f.Nonce == lease.Candidate.Nonce);
            if (Facts is not null && (Facts.Target != lease.Candidate.Target || Facts.TargetMetadataSha256 != lease.Candidate.TargetMetadataSha256 ||
                Facts.TargetOriginSha256 != lease.Candidate.TargetOriginSha256 || Facts.TargetState != lease.Candidate.TargetState ||
                Facts.RestorationSha256 != lease.Candidate.RestorationSha256)) throw Changed();
        }
        catch { lease.Dispose(); throw; }
    }

    internal ArchiveContentRecoveryFacts ResumeCleanup(CancellationToken ct)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var journal = lease.Journal;
            if (Facts?.IsResolved == true) { journal.RequireCurrentArchiveContentRecovery(Facts); return Facts; }
            try
            {
                lease.ReviewTarget(ct);
                if (Facts is null) Append(lease.Candidate);
                else { Current(); if (Facts.Phase == ArchiveContentRecoveryPhase.NeedsReview) Append(Facts.Advance(ArchiveContentRecoveryPhase.Reviewed)); }
                while (Facts!.ReviewedCount < lease.Catalog.Artifacts.Count)
                {
                    ct.ThrowIfCancellationRequested(); Current(); lease.ReviewTarget(ct);
                    int index = Facts.ReviewedCount;
                    if (!lease.HasArtifact(index))
                    {
                        lease.RequireArtifactAbsent(index);
                        Append(Facts.Advance(ArchiveContentRecoveryPhase.Absent));
                    }
                    else
                    {
                        lease.ReviewArtifact(index, ct);
                        if (Facts.Phase != ArchiveContentRecoveryPhase.DeleteIntent) Append(Facts.Advance(ArchiveContentRecoveryPhase.DeleteIntent));
                        Current(); ct.ThrowIfCancellationRequested(); lease.ReviewTarget(ct);
                        lease.DeleteArtifact(index, ct);
                        Append(Facts.Advance(ArchiveContentRecoveryPhase.Deleted));
                    }
                    lease.ReviewTarget(CancellationToken.None);
                }
                for (int i = 0; i < lease.Catalog.Artifacts.Count; i++) lease.RequireArtifactAbsent(i);
                lease.ReviewTarget(CancellationToken.None); Append(Facts.Advance(ArchiveContentRecoveryPhase.Completed)); return Facts;
            }
            catch
            {
                journal.RequireRecovery();
                if (Facts is not null) try { Append(Facts.Advance(ArchiveContentRecoveryPhase.NeedsReview)); } catch { }
                throw;
            }
            void Current() => journal.RequireCurrentArchiveContentRecovery(Facts!);
            void Append(ArchiveContentRecoveryFacts next) { journal.ArchiveContentRecovery(next); Facts = next; }
        }
    }

    internal void Finish(string summary)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Facts?.IsResolved != true) throw Changed();
            lease.Journal.RequireCurrentArchiveContentRecovery(Facts); lease.ReviewTarget(CancellationToken.None);
            lease.Journal.Finish(JobState.Completed, summary);
        }
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; lease.Dispose(); }
    }
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.NeedsReview, "The recovery target, journal or catalog objects changed; retain them for review.");
}
