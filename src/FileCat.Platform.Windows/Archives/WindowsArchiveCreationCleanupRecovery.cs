using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Fresh exclusive journal and exact NTFS objects for creation-sidecar reconciliation.</summary>
internal static class WindowsArchiveCreationCleanupRecovery
{
    internal static IArchiveCreationCleanupLease OpenCurrentLease(string path, Guid nonce, CancellationToken ct)
    {
        try { return new Lease(path, nonce, ct); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The journal lacks a complete current creation cleanup scope; retain its files.", error); }
    }

    private sealed class Lease : IArchiveCreationCleanupLease
    {
        private readonly WindowsArchiveRecoveryJournal journal;
        private readonly WindowsArchiveDirectory directory;
        private readonly WindowsArchiveFile target;
        private readonly WindowsArchiveFile?[] files;
        private bool disposed;
        public JobJournal Journal => journal.Journal;
        public ArchiveCreationCleanupFacts Catalog { get; }
        internal Lease(string path, Guid nonce, CancellationToken ct)
        {
            journal = WindowsArchiveRecoveryJournal.OpenCurrent(path, ct);
            try
            {
                var recovery = Journal.RetainedArchiveRecovery();
                var preparation = recovery.Transactions.Single(facts => facts.Nonce == nonce);
                Catalog = recovery.CreationCleanups.Single(facts => facts.Nonce == nonce); Catalog.ValidatePreparation(preparation);
                directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(preparation.ArchivePath)!);
                try
                {
                    if (directory.Identity.ToString() != preparation.ParentIdentity) throw Changed();
                    target = directory.OpenSource(Path.GetFileName(preparation.ArchivePath));
                    try
                    {
                        ReviewTarget(ct); files = new WindowsArchiveFile?[Catalog.Artifacts.Count];
                        try
                        {
                            for (int i = Catalog.ReviewedCount; i < files.Length; i++)
                            {
                                var artifact = Catalog.Artifacts[i];
                                files[i] = directory.OpenRecoveryArtifactOrAbsent(Path.GetFileName(artifact.Path), Identity(artifact.Version.Identity),
                                    (WindowsArchiveArtifactKind)artifact.Kind, artifact.Version.Length);
                                files[i]?.ReviewRecoveredCleanup(artifact, preparation.ParentIdentity, ct);
                            }
                        }
                        catch { foreach (var file in files) file?.Dispose(); throw; }
                    }
                    catch { target.Dispose(); throw; }
                }
                catch { directory.Dispose(); throw; }
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
        private WindowsArchiveFile? File(int index) { ObjectDisposedException.ThrowIf(disposed, this); return files[index]; }
        public bool HasArtifact(int index) => File(index) is not null;
        public void ReviewArtifact(int index, CancellationToken ct)
            => (File(index) ?? throw Changed()).ReviewRecoveredCleanup(Catalog.Artifacts[index], directory.Identity.ToString(), ct);
        public void DeleteArtifact(int index, CancellationToken ct)
        {
            var file = File(index) ?? throw Changed(); file.DeleteRecoveredCleanup(Catalog.Artifacts[index], directory.Identity.ToString(), ct);
            file.Dispose(); files[index] = null;
        }
        public void RequireArtifactAbsent(int index)
        { _ = File(index); directory.RequireRecoveryNameAbsent(Path.GetFileName(Catalog.Artifacts[index].Path)); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { foreach (var file in files) file?.Dispose(); target.Dispose(); directory.Dispose(); }
            finally { journal.Dispose(); }
        }
        private static FileIdentity Identity(string value) => WindowsArchiveRecoveryIdentity.Parse(value);
        private static ArchiveWriteException Changed() => new(ArchiveWriteReason.NeedsReview, "The creation target or retained sidecar changed; keep it for review.");
    }
}
