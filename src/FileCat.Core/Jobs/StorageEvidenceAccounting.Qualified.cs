namespace FileCat.Core.Jobs;

internal static partial class StorageEvidenceAccounting
{
    private static StorageHistoryAccounting QualifiedCensus(string directory, IReadOnlySet<Guid>? live,
        StorageEvidenceBackend backend, CancellationToken ct)
    {
        long bytes = 0, reserve = 0, journals = 0, pending = 0, artifacts = 0;
        bool complete = true; string? problem = null; string identity;
        try { using var initial = backend.OpenArtifacts(directory, ct); identity = initial.DirectoryIdentity; }
        catch (FileNotFoundException) { return new(0, 0, 0, 0, 0, true, null); }
        var seen = new HashSet<Guid>();
        try
        {
            foreach (var entry in StorageEvidenceCursor.Entries(backend, directory, artifacts: false, identity, ct))
            {
                string leaf = entry.Leaf;
                if (!StorageJournalPolicy.IsCandidate(leaf)) continue;
                ct.ThrowIfCancellationRequested(); journals++;
                bool named = StorageJournalPolicy.TryJobId(leaf, out Guid association);
                if (named && (!seen.Add(association) || seen.Count > 1_000_000)) throw new IOException("Storage journal identity accounting is duplicate or exceeds its bounded observation limit.");
                if (named && live?.Contains(association) == true) continue;
                var observed = backend.ObserveJournal(directory, leaf, ct);
                if (observed.DirectoryIdentity != identity || observed.Identity != entry.Identity || observed.ByteLength != entry.ByteLength)
                    throw new IOException("The storage cursor entry changed during accounting.");
                bytes = checked(bytes + observed.ByteLength);
                bool needsReview = true;
                try
                {
                    using var owner = backend.OpenJournal(directory, leaf, ct);
                    var status = JournalRecovery.ReadStorageReviewStatus(owner, ct);
                    if (status.Current.DirectoryIdentity != identity || status.Current.Evidence.Identity != observed.Identity || status.Current.Evidence.ByteLength != observed.ByteLength)
                        throw new IOException("The storage journal changed during accounting.");
                    association = status.Current.AssociationId;
                    needsReview = status.ReviewPending;
                }
                catch (IOException busy) when ((busy.HResult & 0xffff) is 32 or 33)
                { /* A foreign active owner stays pending. Its exact private metadata was charged above. */ }
                using var evidence = backend.OpenArtifacts(directory, ct);
                RequireAccountingIdentity(evidence, identity);
                var associatedFiles = evidence.Artifacts(association, StorageReviewNames.ArtifactCount, ct);
                if (associatedFiles.Count > StorageReviewNames.ArtifactCount) throw new IOException("The artifact inventory exceeds its bounded allowance.");
                long associated = associatedFiles.Aggregate(0L, (sum, item) => checked(sum + item.ByteLength));
                if (needsReview)
                {
                    pending++;
                    reserve = checked(reserve + Math.Max(0, StorageJournalPolicy.ReviewWriteBytes - associated));
                }
                if (!named) { complete = false; problem ??= "An unrecognized storage journal name requires review."; }
            }
            foreach (var entry in StorageEvidenceCursor.Entries(backend, directory, artifacts: true, identity, ct))
            {
                ct.ThrowIfCancellationRequested(); string leaf = entry.Leaf;
                if (!StorageJournalPolicy.IsReviewArtifact(leaf)) continue;
                artifacts++;
                if (!StorageJournalPolicy.TryArtifactJobId(leaf, out Guid association))
                {
                    bytes = checked(bytes + entry.ByteLength); complete = false;
                    problem ??= "An unassociated storage artifact requires review."; continue;
                }
                using var evidence = backend.OpenArtifacts(directory, ct);
                RequireAccountingIdentity(evidence, identity);
                var catalog = evidence.Artifacts(association, StorageReviewNames.ArtifactCount, ct);
                if (catalog.Count > StorageReviewNames.ArtifactCount) throw new IOException("The storage artifact inventory exceeds its bounded allowance.");
                var info = catalog.SingleOrDefault(i => i.Leaf == leaf) ?? throw new IOException("A storage artifact changed during accounting.");
                if (info.Identity != entry.Identity || info.ByteLength != entry.ByteLength) throw new IOException("The storage artifact cursor entry changed.");
                bytes = checked(bytes + info.ByteLength);
                if (evidence.JournalExists(StorageJournalPolicy.FileName(DateTime.UnixEpoch, association), association, ct)) continue;
                bool retained = false;
                foreach (var candidate in catalog)
                {
                    if (!StorageReviewNames.TryParse(candidate.Leaf, out _, out var kind, out _, out var temporary) || kind != "ack" || temporary) continue;
                    var pair = JournalRecovery.ReadRetainedStorageReview(evidence, candidate, ct);
                    if (pair is not null && (pair.AcknowledgmentLeaf == leaf && pair.AcknowledgmentEvidence.Identity == info.Identity && pair.AcknowledgmentEvidence.ByteLength == info.ByteLength ||
                        pair.Acknowledgment.ReportLeaf == leaf && pair.Acknowledgment.ReportEvidence.Identity == info.Identity && pair.Acknowledgment.ReportEvidence.ByteLength == info.ByteLength))
                    { retained = true; break; }
                }
                if (!retained) { complete = false; problem ??= "Unassociated storage review artifacts require reconciliation."; }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException)
        { complete = false; problem ??= "Storage history accounting could not finish: " + error.Message; }
        return new(bytes, reserve, journals, pending, artifacts, complete, problem);
    }

    private static void RequireAccountingIdentity(StorageReviewArtifacts evidence, string expected)
    { if (!evidence.IsOwned || evidence.DirectoryIdentity != expected) throw new IOException("The storage namespace changed during accounting."); }
}
