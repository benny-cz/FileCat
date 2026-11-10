using FileCat.Core.Jobs;

namespace FileCat.Core.Archives.Unix;

/// <summary>
/// Owns the published read binding and the remaining private transaction objects of one committed or refused Unix
/// exchange update. Disposal closes handles only; cleanup is an explicit, journaled, exact operation.
/// </summary>
internal sealed class UnixArchiveExchangeUpdateResult<T> : IDisposable where T : class, IUnixArchiveExchangeStage
{
    private readonly object gate = new();
    private readonly UnixArchiveDirectory privateDirectory;
    private readonly IReadOnlyList<T> sidecars;
    private readonly T stage;
    private bool disposed;
    internal ArchiveExchangeUpdateFacts Facts { get; private set; }
    internal ArchiveFileObservation? Published { get; }
    internal UnixArchiveFile? PublishedFile { get; }

    internal UnixArchiveExchangeUpdateResult(ArchiveExchangeUpdateFacts facts, UnixArchiveDirectory privateDirectory, T stage,
        IReadOnlyList<T> sidecars, ArchiveFileObservation? published, UnixArchiveFile? publishedFile)
    { Facts = facts; this.privateDirectory = privateDirectory; this.stage = stage; this.sidecars = sidecars; Published = published; PublishedFile = publishedFile; }

    /// <summary>Paths still present for review: every cataloged object not yet cleaned, plus the private folder itself.</summary>
    internal IReadOnlyList<string> RetainedArtifacts
    {
        get
        {
            lock (gate)
            {
                if (Facts.IsResolved) return [];
                var rows = new List<string>();
                for (int i = Facts.CleanedCount; i < Facts.CleanupCount; i++) rows.Add(Facts.CleanupTarget(i).Path);
                rows.Add(Facts.PrivateDirectoryPath); return rows.AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Destroy the reconciled private objects exactly (sidecars, then the retired original or unpublished stage) and remove
    /// the empty private folder. Any mismatch stops, records NeedsReview and retains every remaining object.
    /// </summary>
    internal ArchiveExchangeUpdateFacts Cleanup(JobJournal journal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(journal);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            journal.RequireCurrentArchiveExchangeUpdate(Facts);
            if (Facts.IsResolved) return Facts;
            if (Facts.Phase is not (ArchiveExchangeUpdatePhase.Exchanged or ArchiveExchangeUpdatePhase.NotPublished or ArchiveExchangeUpdatePhase.Cleaned))
                throw new InvalidOperationException("Only a reconciled exchange update can be cleaned.");
            try
            {
                while (Facts.CleanedCount < Facts.CleanupCount)
                {
                    ct.ThrowIfCancellationRequested();
                    int index = Facts.CleanedCount; var expected = Facts.CleanupTarget(index);
                    if (index < sidecars.Count)
                    {
                        Append(Facts.Advance(ArchiveExchangeUpdatePhase.CleanupIntent));
                        sidecars[index].DestroyOwned(expected.Version, expected.MetadataSha256, CancellationToken.None);
                        sidecars[index].Dispose();
                    }
                    else
                    {
                        stage.Dispose(); // A terminal exchange binding; a fresh exact binding is reopened below.
                        using var slot = privateDirectory.OpenOwnedForCleanup(Path.GetFileName(Facts.StagePath),
                            expected.Version.Identity, expected.Version.Length);
                        Append(Facts.Advance(ArchiveExchangeUpdatePhase.CleanupIntent));
                        slot.DestroyOwned(expected.Version, expected.MetadataSha256, CancellationToken.None);
                    }
                    Append(Facts.Advance(ArchiveExchangeUpdatePhase.Cleaned));
                }
                privateDirectory.RemoveEmptyPrivateStageDirectory(CancellationToken.None);
                Append(Facts.Advance(ArchiveExchangeUpdatePhase.Completed));
                return Facts;
            }
            catch
            {
                journal.RequireRecovery();
                try { Append(Facts.Advance(ArchiveExchangeUpdatePhase.NeedsReview)); } catch { }
                throw;
            }
        }
        void Append(ArchiveExchangeUpdateFacts next) { journal.ArchiveExchangeUpdate(next); Facts = next; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true;
            try { PublishedFile?.Dispose(); } finally { stage.Dispose(); }
        }
    }
}

/// <summary>
/// Unix existing-archive publication: a verified stage in the creator-private stage directory receives the original's
/// promised outer metadata, then one atomic two-directory exchange makes it the archive while the original is retained
/// at the stage's private name. Every visible step is preceded by a durable record (UNIX_EXCHANGE_UPDATE.md).
/// </summary>
internal static class UnixArchiveExchangeUpdate
{
    internal static UnixArchiveExchangeUpdateResult<T> Publish<T>(JobJournal journal, ArchivePreparedContainer<T> prepared,
        UnixArchiveDirectory target, UnixArchiveDirectory privateDirectory, UnixArchiveFile original,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> captured, CancellationToken ct) where T : class, IUnixArchiveExchangeStage
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(privateDirectory); ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(captured);
        var preparation = prepared.Facts; var baseline = prepared.Verified.Original;
        if (baseline is null || preparation.Original != baseline.Version || preparation.VerifiedStage != prepared.Verified.Stage.Version ||
            preparation.Phase != ArchiveTransactionPhase.Verifying || preparation.OriginEvidence is null ||
            preparation.RecordVersion != ArchiveTransactionFacts.SchemaVersion || preparation.ParentIdentity != privateDirectory.Identity.ToString() ||
            baseline.ParentIdentity != target.Identity.ToString() || captured.Count != prepared.Verified.Inputs.Count) throw Invalid();
        journal.RequireCurrentArchiveTransaction(preparation);
        ArchiveExchangeUpdateFacts? facts = null; bool attempted = false, published = false;
        var sidecarFiles = preparation.Sidecars.Select(evidence =>
            prepared.Sidecars.Single(sidecar => sidecar.Observation.Version == evidence.Version).File).ToArray();
        try
        {
            ct.ThrowIfCancellationRequested(); ReviewInputs(ct); ReviewSidecars(ct);
            ((IArchiveRetainedFile)original).Review(baseline, ct);
            var originalMetadata = original.CaptureOuterMetadata(ct);
            if (originalMetadata.Digest != baseline.MetadataDigest) throw Changed();
            ArchiveUnixUpdateMetadata.RequireAdmissible(originalMetadata, UnixArchiveNative.GetEuid());
            var requiredOrigin = prepared.Verified.Stage.Origin;
            string promised = ArchiveUnixUpdateMetadata.PromisedDigest(originalMetadata, requiredOrigin);
            var catalog = preparation.Sidecars.Select(evidence =>
            {
                var held = prepared.Sidecars.Single(sidecar => sidecar.Observation.Version == evidence.Version);
                return ArchiveContentCleanupArtifact.Capture(evidence.Path,
                    evidence.Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest, held.Observation);
            }).ToArray();
            Append(ArchiveExchangeUpdateFacts.Reserve(preparation, target.Identity.ToString(), privateDirectory.DisplayPath, baseline, promised,
                prepared.Verified.Stage, catalog));
            var applied = prepared.Stage.ApplyUpdateMetadata(prepared.Verified.Stage, original, baseline, requiredOrigin, ct);
            Append(facts!.Advance(ArchiveExchangeUpdatePhase.MetadataApplied, applied: applied.MetadataDigest));
            ReviewInputs(ct); ReviewSidecars(ct); ct.ThrowIfCancellationRequested();
            Append(facts!.Advance(ArchiveExchangeUpdatePhase.ExchangeIntent));
            attempted = true;
            UnixArchiveCrossDirectoryReceipt receipt;
            try { receipt = prepared.Stage.ExchangeRetainedAcrossParents(original, applied, baseline, ct); }
            catch (UnixArchiveExchangeNotPerformedException refused)
            {
                attempted = false;
                throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                    "The file system did not allow replacing this archive (errno " + refused.Errno + "), for example because of its permissions or ACL. It was not changed.");
            }
            catch (OperationCanceledException) { attempted = false; throw; } // Raised before the native call.
            published = true;
            // From here the new archive is visible. Reconcile through fresh bindings without cancellation.
            var publishedFile = target.OpenSource(Path.GetFileName(preparation.ArchivePath), ArchiveWritePolicy.MaxPhysicalBytes);
            try
            {
                var publishedObservation = ((IArchiveRetainedFile)publishedFile).Observe(CancellationToken.None);
                if (publishedObservation.Version != receipt.Published || receipt.Published != applied.Version ||
                    ArchiveUnixUpdateMetadata.PromisedDigest(publishedFile.CaptureOuterMetadata(CancellationToken.None), requiredOrigin) != promised)
                    throw Changed();
                string retiredMetadata;
                using (var retired = privateDirectory.OpenOwnedForCleanup(Path.GetFileName(preparation.StagePath),
                    receipt.Retired.Identity, receipt.Retired.Length))
                {
                    var retiredFacts = retired.CaptureOuterMetadata(CancellationToken.None);
                    if (receipt.Retired != baseline.Version || ArchiveUnixUpdateMetadata.PromisedDigest(retiredFacts, baseline.Origin) !=
                        ArchiveUnixUpdateMetadata.PromisedDigest(originalMetadata, baseline.Origin)) throw Changed();
                    retiredMetadata = retiredFacts.Digest;
                }
                Append(facts!.Advance(ArchiveExchangeUpdatePhase.Exchanged, published: receipt.Published, retired: receipt.Retired, retiredMetadata: retiredMetadata));
                return new UnixArchiveExchangeUpdateResult<T>(facts!, privateDirectory, prepared.Stage, sidecarFiles, publishedObservation, publishedFile);
            }
            catch { publishedFile.Dispose(); throw; }
        }
        catch (Exception ex)
        {
            bool definite = facts is not null && !published && !attempted && ex is not ArchiveWriteException { Reason: ArchiveWriteReason.NeedsReview };
            if (facts is not null)
            {
                try { Append(facts.Advance(definite ? ArchiveExchangeUpdatePhase.NotPublished : ArchiveExchangeUpdatePhase.NeedsReview)); }
                catch { definite = false; /* The durable intent and reserved names remain recovery evidence. */ }
            }
            // A definite refusal keeps the journal unresolved only through its uncleaned exchange facts, so exact cleanup of
            // this transaction's private files can still complete it. Every other outcome needs explicit review.
            if (!definite) journal.RequireRecovery();
            throw;
        }

        void Append(ArchiveExchangeUpdateFacts next) { journal.ArchiveExchangeUpdate(next); facts = next; }
        void ReviewInputs(CancellationToken cancellation)
        {
            foreach (var (id, binding) in captured)
            {
                cancellation.ThrowIfCancellationRequested();
                if (binding is null || !prepared.Verified.Inputs.TryGetValue(id, out var expected) || !binding.Reviewed.Matches(expected)) throw Invalid();
                binding.File.Review(expected, cancellation);
            }
            foreach (var directory in prepared.Verified.Directories.Values) directory.Review(cancellation);
        }
        void ReviewSidecars(CancellationToken cancellation)
        {
            foreach (var evidence in prepared.Sidecars) ((IArchiveRetainedFile)evidence.File).Review(evidence.Observation, cancellation);
        }
    }

    /// <summary>Result for a preparation that was refused before any exchange: everything private is retained for cleanup.</summary>
    internal static UnixArchiveExchangeUpdateResult<T> NotPublished<T>(JobJournal journal, ArchivePreparedContainer<T> prepared,
        UnixArchiveDirectory privateDirectory) where T : class, IUnixArchiveExchangeStage
    {
        var current = journal.CurrentArchiveExchangeUpdate(prepared.Facts.Nonce);
        if (current.Phase != ArchiveExchangeUpdatePhase.NotPublished) throw Invalid();
        var sidecarFiles = prepared.Facts.Sidecars.Select(evidence =>
            prepared.Sidecars.Single(sidecar => sidecar.Observation.Version == evidence.Version).File).ToArray();
        return new UnixArchiveExchangeUpdateResult<T>(current, privateDirectory, prepared.Stage, sidecarFiles, null, null);
    }

    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview,
        "The Unix archive update differs from its current preparation or complete captured evidence; retain all artifacts.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged,
        "The archive or its update stage changed before publication could be confirmed; retain both versions for review.");
}
