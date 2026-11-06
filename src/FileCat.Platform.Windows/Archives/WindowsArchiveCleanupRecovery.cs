using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Fresh native recovery scope. It preserves archive bytes and only reconciles the recorded private catalog.</summary>
internal sealed class WindowsArchiveCleanupRecovery : IDisposable
{
    private readonly ArchiveContentCleanupRecovery recovery;
    private WindowsArchiveCleanupRecovery(IArchiveContentCleanupRecoveryLease lease) => recovery = new(lease);
    internal ArchiveContentRecoveryFacts? Facts => recovery.Facts;
    internal ArchiveContentRecoveryFacts ResumeCleanup(CancellationToken ct) => recovery.ResumeCleanup(ct);
    internal void Finish(string summary) => recovery.Finish(summary);
    public void Dispose() => recovery.Dispose();
    internal static WindowsArchiveCleanupRecovery Open(string journalPath, string expectedJournalSha256, Guid nonce, CancellationToken ct)
        => new(OpenLease(journalPath, expectedJournalSha256, nonce, ct));
    internal static IArchiveContentCleanupRecoveryLease OpenLease(string journalPath, string expectedJournalSha256, Guid nonce, CancellationToken ct)
        => OpenLeaseCore(journalPath, expectedJournalSha256, nonce, ct);
    internal static IArchiveContentCleanupRecoveryLease OpenCurrentLease(string journalPath, Guid nonce, CancellationToken ct)
        => OpenLeaseCore(journalPath, null, nonce, ct);
    private static IArchiveContentCleanupRecoveryLease OpenLeaseCore(string journalPath, string? expectedJournalSha256, Guid nonce, CancellationToken ct)
    {
        try { return new NativeLease(journalPath, expectedJournalSha256, nonce, ct); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The journal lacks a complete matching recovery catalog or target observation; retain every artifact.", error); }
    }

    private sealed class NativeLease : IArchiveContentCleanupRecoveryLease
    {
        private readonly object gate = new();
        private readonly WindowsArchiveRecoveryJournal journalLease;
        private readonly WindowsArchiveDirectory directory;
        private readonly WindowsArchiveFile target;
        private readonly WindowsArchiveFile?[] files;
        private readonly ArchiveContentCleanupFacts catalog;
        private readonly ArchiveContentRecoveryFacts candidate;
        private bool disposed;
        public JobJournal Journal => journalLease.Journal;
        public ArchiveContentCleanupFacts Catalog => catalog;
        public ArchiveContentRecoveryFacts Candidate => candidate;

        internal NativeLease(string journalPath, string? expectedJournalSha256, Guid nonce, CancellationToken ct)
        {
            journalLease = expectedJournalSha256 is null ? WindowsArchiveRecoveryJournal.OpenCurrent(journalPath, ct)
                : WindowsArchiveRecoveryJournal.Open(journalPath, expectedJournalSha256, ct);
            try
            {
                var recovery = journalLease.Journal.RetainedArchiveRecovery();
                var preparation = recovery.Transactions.Single(f => f.Nonce == nonce);
                var content = recovery.ContentUpdates.Single(f => f.Nonce == nonce);
                catalog = recovery.ContentCleanups.Single(f => f.Nonce == nonce);
                var facts = recovery.ContentRecoveries.SingleOrDefault(f => f.Nonce == nonce);
                directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(preparation.ArchivePath)!);
                try
                {
                    if (directory.Identity.ToString() != preparation.ParentIdentity) throw Changed();
                    target = directory.OpenSource(Path.GetFileName(preparation.ArchivePath));
                    try
                    {
                        candidate = ArchiveContentRecoveryFacts.Begin(preparation, content, catalog, ((IArchiveRetainedFile)target).Observe(ct),
                            recovery.ContentRestorations.SingleOrDefault(f => f.Nonce == nonce));
                        if (facts is not null && (facts.Target != candidate.Target || facts.TargetMetadataSha256 != candidate.TargetMetadataSha256 ||
                            facts.TargetOriginSha256 != candidate.TargetOriginSha256 || facts.TargetState != candidate.TargetState)) throw Changed();
                        files = new WindowsArchiveFile?[catalog.Artifacts.Count];
                        try
                        {
                            int reviewed = facts?.ReviewedCount ?? catalog.DeletedCount;
                            for (int i = 0; i < files.Length; i++)
                            {
                                ct.ThrowIfCancellationRequested(); var artifact = catalog.Artifacts[i];
                                if (i < reviewed) { directory.RequireRecoveryNameAbsent(Path.GetFileName(artifact.Path)); continue; }
                                files[i] = directory.OpenRecoveryArtifactOrAbsent(Path.GetFileName(artifact.Path), Identity(artifact.Version.Identity),
                                    (WindowsArchiveArtifactKind)artifact.Kind, artifact.Version.Length);
                                files[i]?.ReviewRecoveredCleanup(artifact, catalog.ParentIdentity, ct);
                            }
                            ReviewTarget(ct);
                        }
                        catch { foreach (var file in files) file?.Dispose(); throw; }
                    }
                    catch { target.Dispose(); throw; }
                }
                catch { directory.Dispose(); throw; }
            }
            catch { journalLease.Dispose(); throw; }
        }
        public bool HasArtifact(int index) => Artifact(index) is not null;
        public void ReviewArtifact(int index, CancellationToken ct)
            => (Artifact(index) ?? throw Changed()).ReviewRecoveredCleanup(catalog.Artifacts[index], catalog.ParentIdentity, ct);
        public void DeleteArtifact(int index, CancellationToken ct)
        {
            var file = Artifact(index) ?? throw Changed();
            file.DeleteRecoveredCleanup(catalog.Artifacts[index], catalog.ParentIdentity, ct);
            file.Dispose(); files[index] = null;
        }
        public void RequireArtifactAbsent(int index)
        { _ = Artifact(index); directory.RequireRecoveryNameAbsent(Path.GetFileName(catalog.Artifacts[index].Path)); }
        private WindowsArchiveFile? Artifact(int index)
        { ObjectDisposedException.ThrowIf(disposed, this); return files[index]; }
        public void ReviewTarget(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var actual = ((IArchiveRetainedFile)target).Observe(ct);
            if (actual.Version != candidate.Target || actual.MetadataDigest != candidate.TargetMetadataSha256 || actual.Origin.Digest != candidate.TargetOriginSha256 ||
                actual.ParentIdentity != catalog.ParentIdentity || actual.LeafName != Path.GetFileName(catalog.ArchivePath)) throw Changed();
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true;
                try { foreach (var file in files) file?.Dispose(); target.Dispose(); directory.Dispose(); }
                finally { journalLease.Dispose(); }
            }
        }
        private static FileIdentity Identity(string value) => new(Convert.ToUInt64(value[..16], 16), value[17..]);
        private static ArchiveWriteException Changed() => new(ArchiveWriteReason.NeedsReview, "The recovery target, journal or catalog objects changed; retain them for review.");
    }
}
