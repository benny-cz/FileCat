using FileCat.Core.Jobs;

namespace FileCat.Core.Archives.Unix;

/// <summary>
/// Exact discard of an unpublished Unix transaction inside its creator-private stage folder (UNIX_PRIVATE_CLEANUP_DECISION.md).
/// Live: the job's still-held private folder. Recovery: a fresh exclusive journal, the archive folder and the private folder
/// reopened by recorded identity. Each object is reopened by its recorded identity; a sealed sidecar must also still have
/// its recorded version, and anything else (a partial stage) is destroyed as this user's single-link scratch object.
/// </summary>
internal static class UnixArchiveDiscard
{
    internal static IArchiveDiscardLease Live(JobJournal journal, ArchiveTransactionFacts preparation, UnixArchiveDirectory privateFolder)
    {
        if (ArchiveDiscardFacts.PrivateFolder(preparation) is not { } path || path != privateFolder.DisplayPath ||
            preparation.ParentIdentity != privateFolder.Identity.ToString() || !privateFolder.IsPrivateStageDirectory)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The transaction is not bound to this private stage folder; keep its files.");
        return new Lease(journal, null, preparation, null, privateFolder, path);
    }

    internal static IArchiveDiscardLease OpenRecovery(string journalPath, Guid nonce, CancellationToken ct)
    {
        var recoveryJournal = UnixArchiveRecoveryJournal.OpenCurrent(journalPath, ct);
        try
        {
            ArchiveTransactionFacts preparation;
            try { preparation = recoveryJournal.Journal.RetainedArchiveRecovery().Transactions.Single(facts => facts.Nonce == nonce); }
            catch (InvalidOperationException error) { throw Changed(error); }
            if (!ArchiveDiscardFacts.IsDiscardable(preparation) || ArchiveDiscardFacts.PrivateFolder(preparation) is not { } privatePath)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "These files are not an unpublished transaction in FileCat's private stage folder; keep them for review.");
            var folder = UnixArchiveDirectory.Open(Path.GetDirectoryName(preparation.ArchivePath)!);
            try
            {
                string leaf = Path.GetFileName(privatePath);
                // An absent folder means every recorded object is already gone; the protocol then records them absent.
                var privateFolder = folder.ObserveName(leaf) is null ? null : folder.OpenPrivateStageDirectory(leaf, preparation.ParentIdentity);
                return new Lease(recoveryJournal.Journal, recoveryJournal, preparation, folder, privateFolder, privatePath);
            }
            catch { folder.Dispose(); throw; }
        }
        catch { recoveryJournal.Dispose(); throw; }
    }

    private static ArchiveWriteException Changed(Exception? cause = null) => cause is null
        ? new(ArchiveWriteReason.NeedsReview, "A recorded transaction object or its private folder changed; keep it for review.")
        : new(ArchiveWriteReason.NeedsReview, "The journal lacks one complete unpublished transaction; keep its files for review.", cause);

    private sealed class Lease(JobJournal journal, UnixArchiveRecoveryJournal? recoveryJournal, ArchiveTransactionFacts preparation,
        UnixArchiveDirectory? archiveFolder, UnixArchiveDirectory? privateFolder, string privatePath) : IArchiveDiscardLease
    {
        private bool disposed, folderRemoved;
        public JobJournal Journal => journal;
        public ArchiveTransactionFacts Preparation => preparation;
        public string? FolderToRemove => privateFolder is null || folderRemoved ? null : privatePath;

        private UnixArchiveDirectory? Folder() { ObjectDisposedException.ThrowIf(disposed, this); return folderRemoved ? null : privateFolder; }
        private string Leaf(string path)
        {
            if (Path.GetDirectoryName(path) != privatePath) throw Changed();
            return Path.GetFileName(path);
        }
        public bool HasArtifact(ArchiveDiscardArtifact artifact) => Folder()?.ObserveName(Leaf(artifact.Path)) is not null;

        public void DestroyArtifact(ArchiveDiscardArtifact artifact, bool resumingIntent, CancellationToken ct)
        {
            var folder = Folder() ?? throw Changed(); string leaf = Leaf(artifact.Path);
            long maximum = artifact.Path == preparation.StagePath ? preparation.StageMaximumLength
                : preparation.Sidecars.Single(sidecar => sidecar.Path == artifact.Path).MaximumLength;
            using var file = folder.OpenOwnedForCleanup(leaf, artifact.Identity, maximum);
            // A sealed sidecar must still be its exact recorded version (an interrupted destruction may have emptied it).
            if (artifact.Version is { } version) file.DestroyOwned(version, null, ct);
            else file.DestroyScratch(ct);
            if (!file.IsRemoved) throw Changed();
        }

        public void RequireAbsent(string path)
        {
            if (Folder()?.ObserveName(Leaf(path)) is not null)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "An unrecorded object occupies a FileCat transaction name; keep it for review: " + path);
        }

        public void FinishArtifacts(CancellationToken ct)
        {
            var folder = Folder(); if (folder is null) return;
            // rmdir refuses anything left over, including objects FileCat never recorded.
            folder.RemoveEmptyPrivateStageDirectory(ct); folderRemoved = true;
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            // The live job scope owns its folders; a recovery lease owns everything it opened.
            if (recoveryJournal is null) return;
            try { privateFolder?.Dispose(); archiveFolder?.Dispose(); }
            finally { recoveryJournal.Dispose(); }
        }
    }
}
