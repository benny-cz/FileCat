using FileCat.Core.Jobs;

namespace FileCat.Core.Archives.Unix;

/// <summary>
/// Fresh recovery of the exact cleanup of one new Unix archive that was published out of a creator-private stage folder.
/// It reopens the journal exclusively, the reviewed archive folder, the published archive and the recorded private folder
/// by identity, then destroys only the recorded sidecars (content through the held descriptor, then the re-observed name)
/// and removes the empty folder. The published archive must still be exactly the verified creation; it is never touched.
/// </summary>
internal static class UnixArchiveCreationCleanupRecovery
{
    internal static IArchiveCreationCleanupLease OpenCurrentLease(string path, Guid nonce, CancellationToken ct)
    {
        try { return new Lease(path, nonce, ct); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The journal lacks a complete current creation cleanup scope; retain its files.", error); }
    }

    private sealed class Lease : IArchiveCreationCleanupLease
    {
        private readonly UnixArchiveRecoveryJournal journal;
        private readonly UnixArchiveDirectory folder;
        private readonly UnixArchiveDirectory? privateFolder;
        private readonly UnixArchiveFile target;
        private readonly UnixArchiveFile?[] files;
        private readonly string privatePath;
        private bool disposed;
        public JobJournal Journal => journal.Journal;
        public ArchiveCreationCleanupFacts Catalog { get; }
        public string? FolderToRemove { get; }

        internal Lease(string path, Guid nonce, CancellationToken ct)
        {
            journal = UnixArchiveRecoveryJournal.OpenCurrent(path, ct);
            try
            {
                var recovery = Journal.RetainedArchiveRecovery();
                var preparation = recovery.Transactions.Single(facts => facts.Nonce == nonce);
                Catalog = recovery.CreationCleanups.Single(facts => facts.Nonce == nonce); Catalog.ValidatePreparation(preparation);
                string folderPath = Path.GetDirectoryName(preparation.ArchivePath)!;
                privatePath = Path.GetDirectoryName(preparation.StagePath)!;
                // Only a stage prepared in a private stage folder directly below the archive folder has exact Unix cleanup.
                if (Path.GetDirectoryName(privatePath) != folderPath ||
                    !Path.GetFileName(privatePath).StartsWith(ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix, StringComparison.Ordinal) ||
                    Catalog.Artifacts.Any(artifact => Path.GetDirectoryName(artifact.Path) != privatePath))
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                        "These retained sidecars are not in FileCat's private stage folder; keep them for review.");
                folder = UnixArchiveDirectory.Open(folderPath);
                try
                {
                    target = folder.OpenSource(Path.GetFileName(preparation.ArchivePath), Catalog.Target.Length);
                    try
                    {
                        ReviewTarget(ct); files = new UnixArchiveFile?[Catalog.Artifacts.Count];
                        if (folder.ObserveName(Path.GetFileName(privatePath)) is null)
                        {
                            // Only a cleanup whose every sidecar is already recorded may lack its folder (removed before Completed).
                            if (Catalog.ReviewedCount != Catalog.Artifacts.Count) throw Changed();
                        }
                        else
                        {
                            privateFolder = folder.OpenPrivateStageDirectory(Path.GetFileName(privatePath), preparation.ParentIdentity);
                            FolderToRemove = privatePath;
                            try
                            {
                                for (int i = Catalog.ReviewedCount; i < files.Length; i++)
                                {
                                    var artifact = Catalog.Artifacts[i]; string leaf = Path.GetFileName(artifact.Path);
                                    var observed = privateFolder.ObserveName(leaf);
                                    if (observed is null) continue;
                                    if (observed.Value.ToString() != artifact.Version.Identity) throw Changed();
                                    files[i] = privateFolder.OpenOwnedForCleanup(leaf, artifact.Version.Identity, artifact.Version.Length);
                                    Review(i, ct);
                                }
                            }
                            catch { foreach (var file in files) file?.Dispose(); privateFolder.Dispose(); throw; }
                        }
                    }
                    catch { target.Dispose(); throw; }
                }
                catch { folder.Dispose(); throw; }
            }
            catch { journal.Dispose(); throw; }
        }

        public void ReviewTarget(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var observed = ((IArchiveRetainedFile)target).Observe(ct);
            if (observed.Version != Catalog.Target || observed.MetadataDigest != Catalog.TargetMetadataSha256 ||
                observed.Origin.Digest != Catalog.TargetOriginSha256) throw Changed();
        }
        private UnixArchiveFile? File(int index) { ObjectDisposedException.ThrowIf(disposed, this); return files[index]; }
        public bool HasArtifact(int index) => File(index) is not null;
        public void ReviewArtifact(int index, CancellationToken ct) { _ = File(index) ?? throw Changed(); Review(index, ct); }
        private void Review(int index, CancellationToken ct)
        {
            var file = files[index]!; var expected = Catalog.Artifacts[index];
            var metadata = file.CaptureOuterMetadata(ct);
            // Resuming an interrupted destruction: its durable intent precedes an object whose content is already gone.
            if (metadata.Native.Length == 0 && expected.Version.Length != 0)
            {
                if (Catalog.Phase != ArchiveCreationCleanupPhase.DeleteIntent || Catalog.ReviewedCount != index) throw Changed();
                return;
            }
            if (metadata.Digest != expected.MetadataSha256 || file.CaptureVersion(ct) != expected.Version) throw Changed();
        }
        public void DeleteArtifact(int index, CancellationToken ct)
        {
            var file = File(index) ?? throw Changed(); var expected = Catalog.Artifacts[index];
            file.DestroyOwned(expected.Version, expected.MetadataSha256, ct);
            file.Dispose(); files[index] = null;
        }
        public void RequireArtifactAbsent(int index)
        {
            if (File(index) is not null) throw Changed();
            // An absent name in FileCat's private folder after its recorded intent is the interrupted unlink of this object.
            if (privateFolder?.ObserveName(Path.GetFileName(Catalog.Artifacts[index].Path)) is not null) throw Changed();
        }
        public void FinishArtifacts(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (privateFolder is not null && folder.ObserveName(Path.GetFileName(privatePath)) is not null) privateFolder.RemoveEmptyPrivateStageDirectory(ct);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { foreach (var file in files) file?.Dispose(); target.Dispose(); privateFolder?.Dispose(); folder.Dispose(); }
            finally { journal.Dispose(); }
        }
        private static ArchiveWriteException Changed() => new(ArchiveWriteReason.NeedsReview, "The created archive or a recorded private sidecar changed; keep it for review.");
    }
}
