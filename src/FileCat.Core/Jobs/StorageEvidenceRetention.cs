namespace FileCat.Core.Jobs;

/// <summary>Byte-pressure pruning over exact inactive evidence. It selects one candidate with bounded memory,
/// then reacquires ownership and rechecks the complete snapshot. No unreviewed/foreign-active history is eligible.</summary>
internal static class StorageEvidenceRetention
{
    internal static bool PruneOldest(string directory, StorageEvidenceBackend backend, IReadOnlySet<Guid> live, CancellationToken ct,
        int? keep = null, DateTime? cutoffUtc = null)
    {
        if (keep < 0 || cutoffUtc is { Kind: not DateTimeKind.Utc }) throw new ArgumentOutOfRangeException(nameof(keep));
        long eligible = 0;
        StorageReviewedJournal? oldestJournal = null;
        DateTime oldest = DateTime.MaxValue;
        {
            foreach (var entry in StorageEvidenceCursor.Entries(backend, directory, artifacts: false, null, ct))
            {
                ct.ThrowIfCancellationRequested(); string leaf = entry.Leaf;
                if (!StorageJournalPolicy.IsCandidate(leaf) || StorageJournalPolicy.TryJobId(leaf, out var id) && live.Contains(id)) continue;
                try
                {
                    using var owner = backend.OpenJournal(directory, leaf, ct);
                    var status = JournalRecovery.ReadStorageReviewStatus(owner, ct);
                    if (status.Current.Evidence.Identity != entry.Identity || status.Current.Evidence.ByteLength != entry.ByteLength)
                        throw new IOException("The storage cursor entry changed during retention selection.");
                    if (status.ReviewPending) continue;
                    eligible++;
                    DateTime rank = status.Current.CreatedUtc ?? status.Acknowledgment?.AcknowledgedUtc ?? DateTime.MaxValue;
                    if (oldestJournal is null || rank < oldest) { oldest = rank; oldestJournal = status.Current; }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        if (oldestJournal is not null && (keep is null || eligible > keep || oldest < cutoffUtc))
        {
            using var owner = backend.OpenJournal(directory, oldestJournal.JournalLeaf, ct);
            return JournalRecovery.PruneStorageJournal(owner, oldestJournal, ct);
        }

        // Only after journal pruning yields no candidate do retained pairs become byte-pressure candidates.
        StorageRetainedReviewSnapshot? oldestPair = null;
        eligible = 0;
        {
            foreach (var entry in StorageEvidenceCursor.Entries(backend, directory, artifacts: true, null, ct))
            {
                ct.ThrowIfCancellationRequested(); string leaf = entry.Leaf;
                if (!StorageReviewNames.TryParse(leaf, out var association, out var kind, out _, out var temporary) || kind != "ack" || temporary || live.Contains(association)) continue;
                using var owner = backend.OpenArtifacts(directory, ct);
                var info = owner.Artifacts(association, StorageReviewNames.ArtifactCount, ct).SingleOrDefault(i => i.Leaf == leaf);
                if (info is null || info.Identity != entry.Identity || info.ByteLength != entry.ByteLength) continue;
                var pair = JournalRecovery.ReadRetainedStorageReview(owner, info, ct);
                if (pair is null || pair.JournalPresent) continue;
                eligible++;
                if (oldestPair is null || pair.Report.ReviewedUtc < oldestPair.Report.ReviewedUtc) oldestPair = pair;
            }
        }
        if (oldestPair is null || keep is not null && eligible <= keep && (cutoffUtc is null || oldestPair.Report.ReviewedUtc >= cutoffUtc)) return false;
        using var retained = backend.OpenArtifacts(directory, ct);
        return JournalRecovery.PruneRetainedStorageReview(retained, oldestPair, ct);
    }

    internal static void ApplyPolicy(string directory, StorageEvidenceBackend backend, IReadOnlySet<Guid> live, CancellationToken ct,
        int keep = 200, TimeSpan? age = null)
    {
        DateTime cutoff = DateTime.UtcNow - (age ?? TimeSpan.FromDays(30));
        while (PruneOldest(directory, backend, live, ct, keep, cutoff)) ct.ThrowIfCancellationRequested();
    }

}
