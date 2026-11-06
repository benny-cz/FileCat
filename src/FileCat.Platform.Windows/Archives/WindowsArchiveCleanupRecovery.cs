using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Fresh native recovery scope. It preserves archive bytes and only reconciles the recorded private catalog.</summary>
internal sealed class WindowsArchiveCleanupRecovery : IDisposable
{
    private readonly object gate = new();
    private readonly WindowsArchiveRecoveryJournal journalLease;
    private readonly WindowsArchiveDirectory directory;
    private readonly WindowsArchiveFile target;
    private readonly WindowsArchiveFile?[] files;
    private readonly ArchiveContentCleanupFacts catalog;
    private readonly ArchiveContentRecoveryFacts candidate;
    private bool disposed;
    internal ArchiveContentRecoveryFacts? Facts { get; private set; }

    private WindowsArchiveCleanupRecovery(string journalPath, string expectedJournalSha256, Guid nonce, CancellationToken ct)
    {
        journalLease = WindowsArchiveRecoveryJournal.Open(journalPath, expectedJournalSha256, ct);
        try
        {
            var recovery = journalLease.Journal.RetainedArchiveRecovery();
            var preparation = recovery.Transactions.Single(f => f.Nonce == nonce);
            var content = recovery.ContentUpdates.Single(f => f.Nonce == nonce);
            catalog = recovery.ContentCleanups.Single(f => f.Nonce == nonce);
            Facts = recovery.ContentRecoveries.SingleOrDefault(f => f.Nonce == nonce);
            directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(preparation.ArchivePath)!);
            try
            {
                if (directory.Identity.ToString() != preparation.ParentIdentity) throw Changed();
                target = directory.OpenSource(Path.GetFileName(preparation.ArchivePath));
                try
                {
                    candidate = ArchiveContentRecoveryFacts.Begin(preparation, content, catalog, ((IArchiveRetainedFile)target).Observe(ct));
                    if (Facts is not null && (Facts.Target != candidate.Target || Facts.TargetMetadataSha256 != candidate.TargetMetadataSha256 ||
                        Facts.TargetOriginSha256 != candidate.TargetOriginSha256 || Facts.TargetState != candidate.TargetState)) throw Changed();
                    files = new WindowsArchiveFile?[catalog.Artifacts.Count];
                    try
                    {
                        int reviewed = Facts?.ReviewedCount ?? catalog.DeletedCount;
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
    internal static WindowsArchiveCleanupRecovery Open(string journalPath, string expectedJournalSha256, Guid nonce, CancellationToken ct)
    {
        try { return new(journalPath, expectedJournalSha256, nonce, ct); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The journal lacks a complete matching recovery catalog or target observation; retain every artifact.", error); }
    }

    internal ArchiveContentRecoveryFacts ResumeCleanup(CancellationToken ct)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var journal = journalLease.Journal;
            if (Facts?.IsResolved == true) { journal.RequireCurrentArchiveContentRecovery(Facts); return Facts; }
            try
            {
                ReviewTarget(ct);
                if (Facts is null) Append(candidate);
                else { Current(); if (Facts.Phase == ArchiveContentRecoveryPhase.NeedsReview) Append(Facts.Advance(ArchiveContentRecoveryPhase.Reviewed)); }
                while (Facts!.ReviewedCount < files.Length)
                {
                    ct.ThrowIfCancellationRequested(); Current(); ReviewTarget(ct);
                    int index = Facts.ReviewedCount; var row = catalog.Artifacts[index]; var file = files[index];
                    if (file is null)
                    {
                        directory.RequireRecoveryNameAbsent(Path.GetFileName(row.Path));
                        Append(Facts.Advance(ArchiveContentRecoveryPhase.Absent));
                    }
                    else
                    {
                        file.ReviewRecoveredCleanup(row, catalog.ParentIdentity, ct);
                        if (Facts.Phase != ArchiveContentRecoveryPhase.DeleteIntent) Append(Facts.Advance(ArchiveContentRecoveryPhase.DeleteIntent));
                        Current(); ct.ThrowIfCancellationRequested(); ReviewTarget(ct);
                        file.DeleteRecoveredCleanup(row, catalog.ParentIdentity, ct);
                        Append(Facts.Advance(ArchiveContentRecoveryPhase.Deleted));
                        file.Dispose(); files[index] = null;
                    }
                    ReviewTarget(CancellationToken.None);
                }
                for (int i = 0; i < files.Length; i++) directory.RequireRecoveryNameAbsent(Path.GetFileName(catalog.Artifacts[i].Path));
                ReviewTarget(CancellationToken.None); Append(Facts.Advance(ArchiveContentRecoveryPhase.Completed)); return Facts;
            }
            catch
            {
                journal.RequireRecovery();
                if (Facts is not null) try { Append(Facts.Advance(ArchiveContentRecoveryPhase.NeedsReview)); } catch { }
                throw;
            }
            void Current() => journal.RequireCurrentArchiveContentRecovery(Facts!);
            void Append(ArchiveContentRecoveryFacts next) { journal.ArchiveContentRecovery(next); Facts = next; }
        }
    }
    internal void Finish(string summary)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Facts?.IsResolved != true) throw Changed();
            journalLease.Journal.RequireCurrentArchiveContentRecovery(Facts); ReviewTarget(CancellationToken.None);
            journalLease.Journal.Finish(JobState.Completed, summary);
        }
    }
    private void ReviewTarget(CancellationToken ct)
    {
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
