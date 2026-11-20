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
    internal const string Unavailable = "Archive writing is not available in this folder: FileCat could not hold it the way it holds a " +
        "folder it writes in.";
    internal const string LinkedArchive = "This archive is a symbolic link or not an ordinary file, which FileCat does not change.";
    internal const string NotOrdinaryUser = "Archive writing needs FileCat running as an ordinary user, not as root.";

    /// <summary>
    /// Why this writer cannot write <paramref name="archivePath"/> at all, from its own checks, or null when it can: an
    /// ordinary user; the folder (resolved through a symbolic link, as the job resolves it) held on its qualified volume, and
    /// no other user able to change it (on macOS, an access list may only deny deletion, as on the home folder, Desktop,
    /// Documents and Downloads); and an existing archive the job's update admission accepts. A few system calls; holds
    /// nothing; the job checks all of it again.
    /// </summary>
    internal static string? TargetProblem(string archivePath)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return null;
        if (Environment.IsPrivilegedProcess) return NotOrdinaryUser;
        if (!Path.IsPathFullyQualified(archivePath)) return Unavailable;
        archivePath = ArchiveLocalPath.ResolveParent(archivePath);
        if (Path.GetDirectoryName(archivePath) is not { Length: > 0 } folder || Path.GetFileName(archivePath) is not { Length: > 0 } leaf)
            return Unavailable;
        if (!Directory.Exists(folder)) return null; // Nothing to judge yet: the job says what is missing.
        try
        {
            using var directory = UnixArchiveDirectory.Open(folder);
            directory.CheckCreationNamespace();
            return directory.ProbeExisting(leaf);
        }
        catch (UnauthorizedAccessException) { return NotOrdinaryUser; }
        catch (ArchiveWriteException error) when (error.Message.StartsWith("Archive writing needs", StringComparison.Ordinal)) { return error.Message; }
        // A file being written as it is looked at: nothing to say for good; the job reads it again and says what it finds.
        catch (ArchiveWriteException error) when (error.Reason == ArchiveWriteReason.SourceChanged) { return null; }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException) { return Unavailable; }
    }

    internal static IArchiveJobBackend Create(ArchiveWriteFormat format,
        Func<Guid, TimeSpan, IArchiveWorkerProcess> assess, Func<Guid, TimeSpan, IArchiveWorkerProcess> transform,
        Func<Guid, TimeSpan, IArchiveWorkerProcess> verify, Func<ArchivePlan, ArchiveJobAvailability>? availability = null,
        bool retainArtifacts = false)
    {
        ArgumentNullException.ThrowIfNull(assess); ArgumentNullException.ThrowIfNull(transform); ArgumentNullException.ThrowIfNull(verify);
        return new ArchiveNativeJobBackend<UnixArchiveFile>(format,
            availability ?? (_ => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? ArchiveJobAvailability.Ready : ArchiveJobAvailability.Unqualified),
            (plan, ct) => new UnixArchiveJobScope(plan, ct, retainArtifacts),
            new(assess, protocolVersion: 2), new(transform, protocolVersion: 2), new(verify, protocolVersion: 2), supportsSourceRemoval: true,
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
    private readonly string leaf;
    // A move removes sources only while this reader still binds the verified publication (plan §11.6.1).
    private UnixArchiveFile? publicationTarget;
    private ArchiveFileObservation? publishedObservation;
    private string? publicationPinFailure;
    public ArchiveRetainedBinding? Original { get; }

    internal UnixArchiveJobScope(ArchivePlan plan, CancellationToken ct, bool retainArtifacts)
    {
        this.ct = ct; this.retainArtifacts = retainArtifacts; ct.ThrowIfCancellationRequested(); leaf = Path.GetFileName(plan.ArchivePath);
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

    private bool createdInPrivateDirectory;
    public UnixArchiveFile Create(ArchiveArtifactReservation reservation)
    {
        if (privateDirectory is null || reservation.ParentIdentity != privateDirectory.Identity.ToString()) return target.CreateArtifact(reservation);
        createdInPrivateDirectory = true; // From here the folder holds recorded transaction objects; only exact cleanup removes them.
        return privateDirectory.CreateArtifact(reservation);
    }

    public ArchiveJobPublication Publish(JobJournal journal, ArchivePreparedContainer<UnixArchiveFile> prepared,
        IReadOnlyDictionary<int, ArchiveRetainedBinding> inputs, CancellationToken token)
    {
        if (privateDirectory is null) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The transaction has no private stage folder.");
        if (original is null)
        {
            // Exclusive move into the archive folder, then exact destruction of the sidecars and removal of the empty folder.
            var created = ArchiveNewFilePublication.PublishAsync(journal, prepared, inputs, Create, token, cleanup: !retainArtifacts).GetAwaiter().GetResult();
            PinPublication(created.Published);
            return new(created.Published, created.Facts.UnresolvedArtifacts) { CleanupNeedsReview = !created.Cleanup.IsResolved };
        }
        try { update = UnixArchiveExchangeUpdate.Publish(journal, prepared, target, privateDirectory, original, inputs, token); }
        catch (Exception error) when (NotPublished(journal, prepared))
        {
            // The journal records that the exchange never happened: the original is untouched, and the refusal is worded as
            // one before saving. Remove this transaction's own private files now; a failure keeps them for review.
            error.Data[ArchiveWriteMessages.NothingPublishedKey] = true;
            if (retainArtifacts) throw;
            try { using var refused = UnixArchiveExchangeUpdate.NotPublished(journal, prepared, privateDirectory); refused.Cleanup(journal, CancellationToken.None); }
            catch (Exception cleanup) when (cleanup is ArchiveWriteException or IOException or UnauthorizedAccessException or InvalidOperationException) { }
            throw;
        }
        PinPublication(update.Published!);
        return new(update.Published!, update.RetainedArtifacts);
    }

    private void PinPublication(ArchiveFileObservation published)
    {
        publishedObservation = published;
        try
        {
            publicationTarget = target.OpenSource(leaf, published.Version.Length);
            if (((IArchiveRetainedFile)publicationTarget).ObjectIdentity != published.Version.Identity)
            { publicationTarget.Dispose(); publicationTarget = null; publicationPinFailure = "Another object now has the archive name."; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { publicationPinFailure = error.Message; } // Publication committed; a move then keeps its originals.
    }

    public void ReviewPublication(ArchiveJobPublication published, bool verifyBytes, CancellationToken token)
    {
        if (publicationTarget is null || !ReferenceEquals(published.Published, publishedObservation))
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The published archive no longer has its live native move reader. " + publicationPinFailure);
        var file = (IArchiveRetainedFile)publicationTarget;
        if (verifyBytes) file.Review(published.Published, token);
        else { using var view = file.OpenReadView(published.Published, token); }
    }

    public (bool Kept, string? Location, string? Reason) DiscardUnpublished(JobJournal journal, CancellationToken token)
    {
        var preparation = journal.DiscardableArchiveTransaction();
        if (preparation is null) return (journal.NeedsRecovery, privateDirectory?.DisplayPath, null);
        if (retainArtifacts || privateDirectory is null) return (true, privateDirectory?.DisplayPath ?? preparation.StagePath, null);
        try
        {
            using var discard = new ArchiveTransactionDiscard(UnixArchiveDiscard.Live(journal, preparation, privateDirectory));
            discard.Execute(token);
            return (journal.NeedsRecovery, null, null);
        }
        catch (Exception error) when (error is ArchiveWriteException or IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or ObjectDisposedException)
        { return (true, privateDirectory.DisplayPath, error.Message); }
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
        try { publicationTarget?.Dispose(); update?.Dispose(); }
        finally
        {
            try { original?.Dispose(); }
            finally
            {
                // A refusal before the first object (for example insufficient space) leaves the new folder empty and
                // unrecorded: remove it rather than leave a hidden folder beside the archive. rmdir refuses anything else.
                if (privateDirectory is not null && !createdInPrivateDirectory && update is null)
                    try { privateDirectory.RemoveEmptyPrivateStageDirectory(CancellationToken.None); }
                    catch (Exception error) when (error is ArchiveWriteException or IOException or UnauthorizedAccessException or ObjectDisposedException) { }
                try { privateDirectory?.Dispose(); } finally { target.Dispose(); }
            }
        }
    }
}
