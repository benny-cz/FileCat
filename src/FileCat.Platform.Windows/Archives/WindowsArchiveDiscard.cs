using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Archives;

/// <summary>
/// Exact discard of an unpublished Windows transaction whose private artifacts were created in the archive folder. Live:
/// the job's held archive folder. Recovery: a fresh exclusive journal and the archive folder reopened by identity. Every
/// object is reopened by its recorded NTFS identity and deleted through that handle; nothing is removed by name.
/// </summary>
internal static class WindowsArchiveDiscard
{
    internal static IArchiveDiscardLease Live(JobJournal journal, ArchiveTransactionFacts preparation, WindowsArchiveDirectory directory)
    {
        if (directory.Identity.ToString() != preparation.ParentIdentity || ArchiveDiscardFacts.PrivateFolder(preparation) is not null)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The transaction is not bound to this archive folder; keep its files.");
        return new Lease(journal, null, preparation, directory);
    }

    internal static IArchiveDiscardLease OpenRecovery(string journalPath, Guid nonce, CancellationToken ct)
    {
        var recoveryJournal = WindowsArchiveRecoveryJournal.OpenCurrent(journalPath, ct);
        try
        {
            ArchiveTransactionFacts preparation;
            try { preparation = recoveryJournal.Journal.RetainedArchiveRecovery().Transactions.Single(facts => facts.Nonce == nonce); }
            catch (InvalidOperationException error)
            { throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The journal lacks one complete unpublished transaction; keep its files for review.", error); }
            if (!ArchiveDiscardFacts.IsDiscardable(preparation) || ArchiveDiscardFacts.PrivateFolder(preparation) is not null)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "These files are not an unpublished transaction beside the archive; keep them for review.");
            var directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(preparation.ArchivePath)!);
            try
            {
                if (directory.Identity.ToString() != preparation.ParentIdentity)
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive folder was replaced; keep the recorded files for review.");
                return new Lease(recoveryJournal.Journal, recoveryJournal, preparation, directory);
            }
            catch { directory.Dispose(); throw; }
        }
        catch { recoveryJournal.Dispose(); throw; }
    }

    private sealed class Lease(JobJournal journal, WindowsArchiveRecoveryJournal? recoveryJournal, ArchiveTransactionFacts preparation,
        WindowsArchiveDirectory directory) : IArchiveDiscardLease
    {
        private readonly Dictionary<string, WindowsArchiveFile> opened = new(StringComparer.Ordinal);
        private bool disposed;
        public JobJournal Journal => journal;
        public ArchiveTransactionFacts Preparation => preparation;
        public string? FolderToRemove => null;

        private string Leaf(string path)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!string.Equals(Path.GetDirectoryName(path), directory.DisplayPath, StringComparison.OrdinalIgnoreCase))
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "A recorded transaction file is outside its archive folder; keep it for review.");
            return Path.GetFileName(path);
        }

        public bool HasArtifact(ArchiveDiscardArtifact artifact)
        {
            if (opened.ContainsKey(artifact.Path)) return true;
            bool stage = artifact.Path == preparation.StagePath;
            var sidecar = stage ? null : preparation.Sidecars.Single(item => item.Path == artifact.Path);
            var kind = stage ? WindowsArchiveArtifactKind.Stage : sidecar!.Role == ArchiveSidecarRole.OriginEvidence
                ? WindowsArchiveArtifactKind.Provenance : WindowsArchiveArtifactKind.Manifest;
            // Opening binds the recorded identity; another object under the name is refused for review.
            var file = directory.OpenRecoveryArtifactOrAbsent(Leaf(artifact.Path), WindowsArchiveRecoveryIdentity.Parse(artifact.Identity), kind,
                stage ? preparation.StageMaximumLength : sidecar!.MaximumLength);
            if (file is null) return false;
            opened[artifact.Path] = file; return true;
        }

        public void DestroyArtifact(ArchiveDiscardArtifact artifact, bool resumingIntent, CancellationToken ct)
        {
            if (!HasArtifact(artifact)) throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "A recorded transaction file disappeared during discard.");
            var file = opened[artifact.Path];
            file.DeleteDiscarded(artifact.Version, ct);
            opened.Remove(artifact.Path); file.Dispose();
        }

        public void RequireAbsent(string path) => directory.RequireRecoveryNameAbsent(Leaf(path));
        public void FinishArtifacts(CancellationToken ct) { ObjectDisposedException.ThrowIf(disposed, this); }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { foreach (var file in opened.Values) file.Dispose(); }
            finally
            {
                // The live job scope owns its folder; a recovery lease owns what it opened.
                if (recoveryJournal is not null) { try { directory.Dispose(); } finally { recoveryJournal.Dispose(); } }
            }
        }
    }
}
