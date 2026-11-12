using System.ComponentModel;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Unregistered qualification candidate using the actual confined application worker and NTFS recipes.</summary>
internal static class WindowsArchiveJobBackend
{
    internal const string Elevated = "Archive writing needs FileCat running without administrator rights.";
    internal const string LinkedFolder = "Archive writing is not available in this folder: FileCat changes archives only in local " +
        "folders that are not links, mount points or synced by a cloud service (such as OneDrive).";
    internal const string LinkedArchive = "This archive is a link or a cloud file (such as OneDrive), which FileCat does not change. " +
        "Copy it into a local folder to change it.";

    /// <summary>
    /// Why this writer cannot write <paramref name="archivePath"/> at all, from its own checks, or null when it can: an
    /// unelevated process, a folder held without following links on a local, writable NTFS volume, and an archive that is
    /// a regular, non-reparse file with one link, within the size FileCat changes. Cheap (a few handle opens), shares
    /// everything and holds nothing; the job checks all of it again.
    /// </summary>
    internal static string? TargetProblem(string archivePath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (WindowsTokenAuthority.IsHighIntegrity) return Elevated;
        if (!Path.IsPathFullyQualified(archivePath) || Path.GetDirectoryName(archivePath) is not { Length: > 0 } folder ||
            Path.GetFileName(archivePath) is not { Length: > 0 } leaf) return LinkedFolder;
        if (!Directory.Exists(folder)) return null; // Nothing to judge yet: the job says what is missing.
        try
        {
            using var directory = WindowsArchiveDirectory.Open(folder);
            return directory.ProbeExisting(leaf) is > 1 ? ArchiveWriteMessages.HardLinkedText(archivePath) : null;
        }
        catch (ArchiveWriteException error) when (error.Reason == ArchiveWriteReason.BudgetExceeded) { return ArchiveListingRevision.TooLarge; }
        catch (IOException error) when (error.Message == WindowsArchiveNative.NotRegular) { return LinkedArchive; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.ComponentModel.Win32Exception) { return LinkedFolder; }
    }

    internal static IArchiveJobBackend Create(ArchiveWriteFormat format, string executable, bool retainArtifacts = false)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("An absolute application worker is required.", nameof(executable));
        return new ArchiveNativeJobBackend<WindowsArchiveFile>(format,
            _ => !OperatingSystem.IsWindows() ? ArchiveJobAvailability.Unqualified
                : ArchiveAppContainer.RuntimeAccessProblem(executable) is { } problem ? new(false, problem) : ArchiveJobAvailability.Ready,
            (plan, ct) => new Scope(plan, ct, retainArtifacts),
            new((nonce, cpu) => ArchiveAppContainer.StartAssessment(executable, nonce, cpu, 2), protocolVersion: 2),
            new((nonce, cpu) => ArchiveAppContainer.StartTransform(executable, nonce, cpu, 2), protocolVersion: 2),
            new((nonce, cpu) => ArchiveAppContainer.StartVerification(executable, nonce, cpu, 2), protocolVersion: 2), supportsSourceRemoval: true,
            openReview: (path, ct) => new ReadLease(path, ct));
    }

    private sealed class ReadLease : IArchiveTargetReadLease
    {
        private readonly WindowsArchiveDirectory directory;
        private readonly WindowsArchiveFile file;
        public IArchiveRetainedFile File => file;
        internal ReadLease(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(path) ?? throw new ArgumentException("A target directory is required."));
            try { file = directory.OpenSource(Path.GetFileName(path)); }
            catch { directory.Dispose(); throw; }
        }
        public void Dispose() { try { file.Dispose(); } finally { directory.Dispose(); } }
    }

    private sealed class Scope : IArchiveJobNativeScope<WindowsArchiveFile>
    {
        private readonly WindowsArchiveDirectory directory;
        private readonly WindowsArchiveFile? original;
        private ArchiveContentUpdateResult<WindowsArchiveFile>? update;
        private WindowsArchiveFile? publicationTarget;
        private ArchiveFileObservation? publishedObservation;
        private string? publicationPinFailure;
        private bool ownsPublicationTarget;
        private readonly bool retainArtifacts;
        public ArchiveRetainedBinding? Original { get; }
        internal Scope(ArchivePlan plan, CancellationToken ct, bool retainArtifacts)
        {
            this.retainArtifacts = retainArtifacts;
            ct.ThrowIfCancellationRequested();
            directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(plan.ArchivePath) ?? throw new ArgumentException("A target directory is required."));
            try
            {
                if (plan.Baseline is not null)
                {
                    original = directory.OpenSource(Path.GetFileName(plan.ArchivePath));
                    Original = new(original, ((IArchiveRetainedFile)original).Observe(ct));
                }
            }
            catch { try { original?.Dispose(); } finally { directory.Dispose(); } throw; }
        }
        public IArchiveSelectionTree OpenInputTree(string path) => WindowsArchiveSelectionTree.Open(path);
        public ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum)
            => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum);
        public WindowsArchiveFile Create(ArchiveArtifactReservation reservation) => directory.CreateArtifact(reservation);
        public ArchiveJobPublication Publish(JobJournal journal, ArchivePreparedContainer<WindowsArchiveFile> prepared,
            IReadOnlyDictionary<int, ArchiveRetainedBinding> inputs, CancellationToken ct)
        {
            if (original is null)
            {
                var created = ArchiveNewFilePublication.PublishAsync(journal, prepared, inputs, directory.CreateArtifact, ct, cleanup: !retainArtifacts).GetAwaiter().GetResult();
                publicationTarget = prepared.Stage; publishedObservation = created.Published;
                return new(created.Published, created.Facts.UnresolvedArtifacts) { CleanupNeedsReview = !created.Cleanup.IsResolved };
            }
            update = WindowsArchiveContentUpdate.Publish(journal, prepared, original, inputs, TimeSpan.FromMinutes(2), ct);
            publishedObservation = update.Published;
            // BeginContentTransaction consumes the original lease. The transaction keeps its committed
            // deny-write/delete pin; acquire a fresh reader under that pin for the complete move lifetime.
            try
            {
                publicationTarget = directory.OpenSource(update.Published.LeafName);
                ownsPublicationTarget = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception)
            { publicationPinFailure = error.Message; } // Publication committed; keep originals if an extra reader is unavailable.
            return new(update.Published, Array.AsReadOnly(update.CleanupFacts.Artifacts.Select(artifact => artifact.Path).ToArray()));
        }
        public void ReviewPublication(ArchiveJobPublication published, bool verifyBytes, CancellationToken ct)
        {
            if (publicationTarget is null || !ReferenceEquals(published.Published, publishedObservation))
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The published archive no longer has its live native move reader. " + publicationPinFailure);
            var file = (IArchiveRetainedFile)publicationTarget;
            if (verifyBytes) file.Review(published.Published, ct);
            else { using var view = file.OpenReadView(published.Published, ct); }
        }
        public ArchiveJobPublication CleanupPublication(JobJournal journal, ArchiveJobPublication published, CancellationToken ct)
        {
            if (retainArtifacts || update is null || ct.IsCancellationRequested) return published;
            try { update.Cleanup(journal, ct); }
            catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception) { }
            return new(published.Published, Array.AsReadOnly(update.CleanupFacts.Artifacts.Skip(update.CleanupFacts.DeletedCount).Select(artifact => artifact.Path).ToArray()))
                { CleanupNeedsReview = !update.CleanupFacts.IsResolved };
        }
        public (bool Kept, string? Location, string? Reason) DiscardUnpublished(JobJournal journal, CancellationToken ct)
        {
            var preparation = journal.DiscardableArchiveTransaction();
            if (preparation is null) return (journal.NeedsRecovery, null, null);
            if (retainArtifacts || update is not null) return (true, preparation.StagePath, null);
            try
            {
                using var discard = new ArchiveTransactionDiscard(WindowsArchiveDiscard.Live(journal, preparation, directory));
                discard.Execute(ct);
                return (journal.NeedsRecovery, null, null);
            }
            catch (Exception error) when (error is ArchiveWriteException or IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or ObjectDisposedException or Win32Exception)
            { return (true, preparation.StagePath, error.Message); }
        }
        public void Dispose()
        {
            try { if (ownsPublicationTarget) publicationTarget?.Dispose(); }
            finally { DisposeUpdate(); }
        }
        private void DisposeUpdate()
        {
            try { update?.Dispose(); }
            finally { try { original?.Dispose(); } finally { directory.Dispose(); } }
        }
    }
}
