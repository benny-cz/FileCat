using FileCat.Core.Jobs;

namespace FileCat.Core.Archives.Unix;

/// <summary>
/// Fresh recovery of one interrupted Unix exchange update. It reopens the retained journal exclusively, the reviewed archive
/// folder and the recorded private stage folder by identity, re-observes both names and only then records the actual
/// outcome: NotPublished (the original still at the public name), Exchanged (both names swapped, both versions exact) or
/// NeedsReview (anything else). It then finishes exact cleanup of reconciled private objects. It never swaps back,
/// republishes or touches the public archive name.
/// </summary>
internal sealed class UnixArchiveExchangeRecovery : IDisposable
{
    private readonly UnixArchiveRecoveryJournal journalLease;
    private readonly Guid nonce;
    private JobJournal Journal => journalLease.Journal;
    internal ArchiveExchangeUpdateFacts Facts { get; private set; }

    private UnixArchiveExchangeRecovery(UnixArchiveRecoveryJournal journal, Guid nonce)
    {
        journalLease = journal; this.nonce = nonce;
        Facts = journal.Journal.RetainedArchiveRecovery().ExchangeUpdates.Single(f => f.Nonce == nonce);
    }

    internal static UnixArchiveExchangeRecovery Open(string journalPath, Guid nonce, CancellationToken ct, string? expectedJournalSha256 = null)
    {
        var journal = expectedJournalSha256 is null ? UnixArchiveRecoveryJournal.OpenCurrent(journalPath, ct)
            : UnixArchiveRecoveryJournal.Open(journalPath, expectedJournalSha256, ct);
        try { return new(journal, nonce); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            journal.Dispose();
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The journal lacks a complete matching Unix exchange record; retain every artifact.", error);
        }
        catch { journal.Dispose(); throw; }
    }

    internal sealed record Inspection(bool Published, IReadOnlyList<string> Present, IReadOnlyList<string> Absent, string PrivateDirectory);

    /// <summary>
    /// Observe the current names and the remaining recorded objects without writing anything, for user review. The same
    /// checks run again inside <see cref="Resume"/>; this result grants no authority.
    /// </summary>
    internal Inspection Inspect(CancellationToken ct)
    {
        if (Facts.IsResolved) return new(Facts.WasPublished, [], [], Facts.PrivateDirectoryPath);
        if (Facts.Phase == ArchiveExchangeUpdatePhase.NeedsReview)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "This archive update was recorded as needing review; its files are retained.");
        using var target = UnixArchiveDirectory.Open(Path.GetDirectoryName(Facts.ArchivePath)!);
        if (target.Identity.ToString() != Facts.PublicParentIdentity) throw Review("The archive folder is no longer the recorded folder.");
        string privateLeaf = Path.GetFileName(Facts.PrivateDirectoryPath);
        if (target.ObserveName(privateLeaf) is null)
        {
            if (Facts.CleanedCount != Facts.CleanupCount) throw Review("The private stage folder is missing before its recorded cleanup finished.");
            return new(Facts.WasPublished, [], [], Facts.PrivateDirectoryPath);
        }
        using var privateDirectory = target.OpenPrivateStageDirectory(privateLeaf, Facts.PrivateDirectoryIdentity);
        bool published = Facts.WasPublished;
        if (Facts.Phase is ArchiveExchangeUpdatePhase.Reserved or ArchiveExchangeUpdatePhase.MetadataApplied or ArchiveExchangeUpdatePhase.ExchangeIntent)
        {
            var atPublic = target.ObserveName(Path.GetFileName(Facts.ArchivePath))?.ToString();
            var atPrivate = privateDirectory.ObserveName(Path.GetFileName(Facts.StagePath))?.ToString();
            if (atPublic == Facts.Original.Identity && atPrivate == Facts.Stage.Identity) published = false;
            else if (Facts.Phase == ArchiveExchangeUpdatePhase.ExchangeIntent && atPublic == Facts.Stage.Identity && atPrivate == Facts.Original.Identity) published = true;
            else throw Review("The archive name and the private stage name do not match either recorded side of the exchange.");
        }
        var present = new List<string>(); var absent = new List<string>();
        for (int i = Facts.CleanedCount; i < Facts.CleanupCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            string path = i < Facts.Sidecars.Count ? Facts.Sidecars[i].Path : Facts.StagePath;
            (privateDirectory.ObserveName(Path.GetFileName(path)) is null ? absent : present).Add(path);
        }
        return new(published, present.AsReadOnly(), absent.AsReadOnly(), Facts.PrivateDirectoryPath);
    }

    /// <summary>Classify, reconcile and clean. Returns the resolved facts or throws after recording NeedsReview.</summary>
    internal ArchiveExchangeUpdateFacts Resume(CancellationToken ct)
    {
        if (Facts.IsResolved) return Facts;
        if (Facts.Phase == ArchiveExchangeUpdatePhase.NeedsReview)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "This archive update was recorded as needing review; its files are retained.");
        try
        {
            using var target = UnixArchiveDirectory.Open(Path.GetDirectoryName(Facts.ArchivePath)!);
            if (target.Identity.ToString() != Facts.PublicParentIdentity) throw Review("The archive folder is no longer the recorded folder.");
            string privateLeaf = Path.GetFileName(Facts.PrivateDirectoryPath), stageLeaf = Path.GetFileName(Facts.StagePath);
            string archiveLeaf = Path.GetFileName(Facts.ArchivePath);
            if (target.ObserveName(privateLeaf) is null)
            {
                // Only a completed cleanup may lack its folder: the final record was not yet appended.
                if (Facts.CleanedCount != Facts.CleanupCount) throw Review("The private stage folder is missing before its recorded cleanup finished.");
                Append(Facts.Advance(ArchiveExchangeUpdatePhase.Completed)); return Facts;
            }
            using var privateDirectory = target.OpenPrivateStageDirectory(privateLeaf, Facts.PrivateDirectoryIdentity);
            if (Facts.Phase is ArchiveExchangeUpdatePhase.Reserved or ArchiveExchangeUpdatePhase.MetadataApplied or ArchiveExchangeUpdatePhase.ExchangeIntent)
                Classify(target, privateDirectory, archiveLeaf, stageLeaf, ct);
            while (Facts.CleanedCount < Facts.CleanupCount)
            {
                ct.ThrowIfCancellationRequested();
                var expected = Facts.CleanupTarget(Facts.CleanedCount); string leaf = Path.GetFileName(expected.Path);
                if (Facts.Phase != ArchiveExchangeUpdatePhase.CleanupIntent) Append(Facts.Advance(ArchiveExchangeUpdatePhase.CleanupIntent));
                var observed = privateDirectory.ObserveName(leaf);
                if (observed is not null)
                {
                    if (observed.Value.ToString() != expected.Version.Identity) throw Review("A recorded private object was replaced; retain it.");
                    using var file = privateDirectory.OpenOwnedForCleanup(leaf, expected.Version.Identity, expected.Version.Length);
                    file.DestroyOwned(expected.Version, expected.MetadataSha256, CancellationToken.None);
                }
                // An absent name after the durable intent is an unlink whose outcome record was interrupted.
                Append(Facts.Advance(ArchiveExchangeUpdatePhase.Cleaned));
            }
            privateDirectory.RemoveEmptyPrivateStageDirectory(CancellationToken.None);
            Append(Facts.Advance(ArchiveExchangeUpdatePhase.Completed));
            return Facts;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            try { if (!Facts.IsResolved && Facts.Phase != ArchiveExchangeUpdatePhase.NeedsReview) Append(Facts.Advance(ArchiveExchangeUpdatePhase.NeedsReview)); }
            catch { /* The retained journal still holds the last durable facts. */ }
            throw;
        }
    }

    private void Classify(UnixArchiveDirectory target, UnixArchiveDirectory privateDirectory, string archiveLeaf, string stageLeaf, CancellationToken ct)
    {
        var atPublic = target.ObserveName(archiveLeaf)?.ToString(); var atPrivate = privateDirectory.ObserveName(stageLeaf)?.ToString();
        bool before = atPublic == Facts.Original.Identity && atPrivate == Facts.Stage.Identity;
        bool after = atPublic == Facts.Stage.Identity && atPrivate == Facts.Original.Identity;
        if (before)
        {
            // Without a recorded intent, or with an intent but both names unchanged, nothing was published.
            Append(Facts.Advance(ArchiveExchangeUpdatePhase.NotPublished)); return;
        }
        if (!after || Facts.Phase != ArchiveExchangeUpdatePhase.ExchangeIntent)
            throw Review("The archive name and the private stage name do not match either recorded side of the exchange.");
        // The swap happened. Confirm exact content and promised metadata on both sides before recording it.
        using var published = target.OpenSource(archiveLeaf, Facts.Stage.Length);
        if (published.CaptureVersion(ct) != Facts.Stage) throw Review("The published archive differs from the verified stage.");
        string promised = ArchiveUnixUpdateMetadata.PromisedDigest(published.CaptureOuterMetadata(ct), published.ReadOrigin(ct));
        if (promised != Facts.PromisedMetadataSha256) throw Review("The published archive's owner, permissions, ACL or marks differ from the reviewed original.");
        using var retired = privateDirectory.OpenOwnedForCleanup(stageLeaf, Facts.Original.Identity, Facts.Original.Length);
        if (retired.CaptureVersion(ct) != Facts.Original) throw Review("The retained original differs from its reviewed version.");
        Append(Facts.Advance(ArchiveExchangeUpdatePhase.Exchanged, published: Facts.Stage, retired: Facts.Original,
            retiredMetadata: retired.CaptureOuterMetadata(ct).Digest));
    }

    /// <summary>Close the journal with an ordinary end record once the exchange is resolved.</summary>
    internal void Finish(string summary)
    {
        if (!Facts.IsResolved) throw new InvalidOperationException("Only a resolved exchange recovery can close its journal.");
        Journal.Finish(JobState.Completed, summary);
    }

    private void Append(ArchiveExchangeUpdateFacts next) { Journal.ArchiveExchangeUpdate(next); Facts = next; }
    private static ArchiveWriteException Review(string message) => new(ArchiveWriteReason.NeedsReview, message + " Every version is retained.");
    public void Dispose() => journalLease.Dispose();
}
