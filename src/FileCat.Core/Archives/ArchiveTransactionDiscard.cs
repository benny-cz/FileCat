using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>
/// One native scope for discarding an unpublished transaction: the live job's held folders, or a fresh recovery that
/// reopened them by recorded identity. The catalog supplies observations only; every destruction is bound natively.
/// </summary>
internal interface IArchiveDiscardLease : IDisposable
{
    JobJournal Journal { get; }
    ArchiveTransactionFacts Preparation { get; }
    /// <summary>The recorded object's name exists in its held folder (whatever it binds).</summary>
    bool HasArtifact(ArchiveDiscardArtifact artifact);
    /// <summary>Opens the name, requires the recorded identity, this user, a single link (and the sealed version when
    /// recorded), then destroys content first and the re-observed name second.</summary>
    void DestroyArtifact(ArchiveDiscardArtifact artifact, bool resumingIntent, CancellationToken ct);
    /// <summary>The recorded name is absent from its held folder.</summary>
    void RequireAbsent(string path);
    /// <summary>After every object is recorded deleted or absent: remove the empty private stage folder, if any.</summary>
    void FinishArtifacts(CancellationToken ct);
    /// <summary>The private stage folder removed by <see cref="FinishArtifacts"/>, or null when there is none.</summary>
    string? FolderToRemove { get; }
}

/// <summary>
/// Runs the recorded discard protocol: catalog, then per object DeleteIntent → Deleted (or Absent), every unrecorded
/// reserved name absent, the empty private folder removed, Completed. Any mismatch records NeedsReview and keeps the rest.
/// </summary>
internal sealed class ArchiveTransactionDiscard : IDisposable
{
    private readonly object gate = new();
    private readonly IArchiveDiscardLease lease;
    private bool disposed, durable;
    internal ArchiveDiscardFacts Facts { get; private set; }

    /// <summary>Opening reviews only: a new catalog is derived but recorded by <see cref="Execute"/>, after approval.</summary>
    internal ArchiveTransactionDiscard(IArchiveDiscardLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease); this.lease = lease;
        try
        {
            var preparation = lease.Preparation; var recorded = lease.Journal.CurrentArchiveDiscard(preparation.Nonce);
            durable = recorded is not null; Facts = recorded ?? ArchiveDiscardFacts.Begin(preparation);
            Facts.ValidatePreparation(preparation);
        }
        catch { lease.Dispose(); throw; }
    }

    /// <summary>For a review dialog: the recorded objects still present (the folder last) and those already absent.</summary>
    internal (IReadOnlyList<string> Present, IReadOnlyList<string> Absent) Observe()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var present = new List<string>(); var absent = new List<string>();
            foreach (var artifact in Facts.Artifacts.Skip(Facts.ReviewedCount)) (lease.HasArtifact(artifact) ? present : absent).Add(artifact.Path);
            if (lease.FolderToRemove is { } folder) present.Add(folder + Path.DirectorySeparatorChar);
            return (present.AsReadOnly(), absent.AsReadOnly());
        }
    }

    internal ArchiveDiscardFacts Execute(CancellationToken ct)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var journal = lease.Journal;
            try
            {
                if (!durable) { journal.ArchiveDiscard(Facts); durable = true; }
                Current();
                if (Facts.IsResolved) return Facts;
                if (Facts.Phase == ArchiveDiscardPhase.NeedsReview) Append(ArchiveDiscardPhase.Cataloged);
                // An occupied reserved name means an object whose creation was never recorded: keep everything for review.
                foreach (string path in Facts.Unrecorded) lease.RequireAbsent(path);
                while (Facts.ReviewedCount < Facts.Artifacts.Count)
                {
                    ct.ThrowIfCancellationRequested(); Current(); var artifact = Facts.Artifacts[Facts.ReviewedCount];
                    if (!lease.HasArtifact(artifact)) { lease.RequireAbsent(artifact.Path); Append(ArchiveDiscardPhase.Absent); continue; }
                    bool resuming = Facts.Phase == ArchiveDiscardPhase.DeleteIntent;
                    if (!resuming) Append(ArchiveDiscardPhase.DeleteIntent);
                    Current(); lease.DestroyArtifact(artifact, resuming, ct);
                    Append(ArchiveDiscardPhase.Deleted);
                }
                foreach (var artifact in Facts.Artifacts) lease.RequireAbsent(artifact.Path);
                foreach (string path in Facts.Unrecorded) lease.RequireAbsent(path);
                lease.FinishArtifacts(CancellationToken.None);
                Append(ArchiveDiscardPhase.Completed); return Facts;
            }
            catch
            {
                if (durable && !Facts.IsResolved && Facts.Phase != ArchiveDiscardPhase.NeedsReview)
                    try { Append(ArchiveDiscardPhase.NeedsReview); } catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException) { }
                journal.RequireRecovery();
                throw;
            }
        }
        void Current() => lease.Journal.RequireCurrentArchiveDiscard(Facts);
        void Append(ArchiveDiscardPhase phase) { var next = Facts.Advance(phase); lease.Journal.ArchiveDiscard(next); Facts = next; }
    }

    /// <summary>Closes the journal once the discard is complete; the live job closes its own journal instead.</summary>
    internal void Finish(string summary)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Facts.IsResolved) throw new InvalidOperationException("The discard is not complete.");
            lease.Journal.RequireCurrentArchiveDiscard(Facts);
            lease.Journal.Finish(JobState.Canceled, summary);
        }
    }

    public void Dispose() { lock (gate) { if (!disposed) { disposed = true; lease.Dispose(); } } }
}
