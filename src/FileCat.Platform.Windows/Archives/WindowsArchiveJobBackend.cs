using System.ComponentModel;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Unregistered qualification candidate using the actual confined application worker and NTFS recipes.</summary>
internal static class WindowsArchiveJobBackend
{
    internal static IArchiveJobBackend Create(ArchiveWriteFormat format, string executable, bool retainArtifacts = false)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("An absolute application worker is required.", nameof(executable));
        return new ArchiveNativeJobBackend<WindowsArchiveFile>(format,
            _ => OperatingSystem.IsWindows() ? ArchiveJobAvailability.Ready : ArchiveJobAvailability.Unqualified,
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
