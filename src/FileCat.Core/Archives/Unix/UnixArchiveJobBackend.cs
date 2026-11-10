using FileCat.Core.Jobs;

namespace FileCat.Core.Archives.Unix;

/// <summary>
/// Unregistered Unix qualification candidate. Every transaction is prepared in a creator-private stage folder below the
/// reviewed archive folder. A new archive is moved into that folder by one no-replace rename (ArchiveNewFilePublication);
/// an existing archive is replaced by one journaled atomic two-folder exchange (UnixArchiveExchangeUpdate). The private
/// objects are then destroyed exactly and the empty folder removed. Worker roles are supplied by the caller: application
/// composition must pass only qualified confined launchers for this RID.
/// </summary>
internal static class UnixArchiveJobBackend
{
    internal static IArchiveJobBackend Create(ArchiveWriteFormat format,
        Func<Guid, TimeSpan, IArchiveWorkerProcess> assess, Func<Guid, TimeSpan, IArchiveWorkerProcess> transform,
        Func<Guid, TimeSpan, IArchiveWorkerProcess> verify, Func<ArchivePlan, ArchiveJobAvailability>? availability = null,
        bool retainArtifacts = false)
    {
        ArgumentNullException.ThrowIfNull(assess); ArgumentNullException.ThrowIfNull(transform); ArgumentNullException.ThrowIfNull(verify);
        return new ArchiveNativeJobBackend<UnixArchiveFile>(format,
            availability ?? (_ => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? ArchiveJobAvailability.Ready : ArchiveJobAvailability.Unqualified),
            (plan, ct) => new UnixArchiveJobScope(plan, ct, retainArtifacts),
            new(assess, protocolVersion: 2), new(transform, protocolVersion: 2), new(verify, protocolVersion: 2), supportsSourceRemoval: false,
            openReview: UnixArchiveTargetReadLease.Open);
    }
}

/// <summary>The native authority of one Unix archive job; see <see cref="UnixArchiveJobBackend"/>.</summary>
internal sealed class UnixArchiveJobScope : IArchiveJobNativeScope<UnixArchiveFile>
{
    private readonly UnixArchiveDirectory target;
    private readonly UnixArchiveFile? original;
    private readonly CancellationToken ct;
    private readonly bool retainArtifacts;
    private UnixArchiveDirectory? privateDirectory;
    private Guid privateNonce;
    private UnixArchiveExchangeUpdateResult<UnixArchiveFile>? update;
    public ArchiveRetainedBinding? Original { get; }

    internal UnixArchiveJobScope(ArchivePlan plan, CancellationToken ct, bool retainArtifacts)
    {
        this.ct = ct; this.retainArtifacts = retainArtifacts; ct.ThrowIfCancellationRequested();
        target = UnixArchiveDirectory.Open(Path.GetDirectoryName(plan.ArchivePath) ?? throw new ArgumentException("A target directory is required."));
        try
        {
            if (plan.Baseline is not null)
            {
                original = target.OpenSource(Path.GetFileName(plan.ArchivePath));
                // Refuse an archive whose promised outer metadata cannot be reproduced before any stage exists.
                original.RequireUpdateAdmissible(ct);
                Original = new(original, ((IArchiveRetainedFile)original).Observe(ct));
            }
        }
        catch { try { original?.Dispose(); } finally { target.Dispose(); } throw; }
    }

    public IArchiveSelectionTree OpenInputTree(string path) => UnixArchiveSelectionTree.Open(path);

    public ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum)
    {
        if (privateDirectory is null) { privateDirectory = target.CreatePrivateStageDirectory(nonce, ct); privateNonce = nonce; }
        else if (privateNonce != nonce) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "One archive scope prepares one transaction.");
        return privateDirectory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum);
    }

    public UnixArchiveFile Create(ArchiveArtifactReservation reservation) =>
        privateDirectory is not null && reservation.ParentIdentity == privateDirectory.Identity.ToString()
            ? privateDirectory.CreateArtifact(reservation) : target.CreateArtifact(reservation);

    public ArchiveJobPublication Publish(JobJournal journal, ArchivePreparedContainer<UnixArchiveFile> prepared,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> inputs, CancellationToken token)
    {
        if (privateDirectory is null) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The transaction has no private stage folder.");
        if (original is null)
        {
            // Exclusive move into the archive folder, then exact destruction of the sidecars and removal of the empty folder.
            var created = ArchiveNewFilePublication.PublishAsync(journal, prepared, inputs, Create, token, cleanup: !retainArtifacts).GetAwaiter().GetResult();
            return new(created.Published, created.Facts.UnresolvedArtifacts) { CleanupNeedsReview = !created.Cleanup.IsResolved };
        }
        try { update = UnixArchiveExchangeUpdate.Publish(journal, prepared, target, privateDirectory, original, inputs, token); }
        catch when (!retainArtifacts && NotPublished(journal, prepared))
        {
            // The original is untouched. Remove this transaction's own private files now; a failure keeps them for review.
            try { using var refused = UnixArchiveExchangeUpdate.NotPublished(journal, prepared, privateDirectory); refused.Cleanup(journal, CancellationToken.None); }
            catch (Exception cleanup) when (cleanup is ArchiveWriteException or IOException or UnauthorizedAccessException or InvalidOperationException) { }
            throw;
        }
        return new(update.Published!, update.RetainedArtifacts);
    }

    private static bool NotPublished(JobJournal journal, ArchivePreparedContainer<UnixArchiveFile> prepared)
    {
        try { return journal.CurrentArchiveExchangeUpdate(prepared.Facts.Nonce).Phase == ArchiveExchangeUpdatePhase.NotPublished; }
        catch (InvalidOperationException) { return false; }
    }

    public ArchiveJobPublication CleanupPublication(JobJournal journal, ArchiveJobPublication published, CancellationToken token)
    {
        if (retainArtifacts || update is null || token.IsCancellationRequested) return published;
        try { update.Cleanup(journal, token); }
        catch (Exception error) when (error is OperationCanceledException or ArchiveWriteException or IOException or UnauthorizedAccessException or InvalidOperationException) { }
        return new(published.Published, update.RetainedArtifacts) { CleanupNeedsReview = !update.Facts.IsResolved };
    }

    public void Dispose()
    {
        try { update?.Dispose(); }
        finally
        {
            try { original?.Dispose(); }
            finally { try { privateDirectory?.Dispose(); } finally { target.Dispose(); } }
        }
    }
}
