using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Fresh retained authority for an explicit restore from the exact original-byte snapshot.</summary>
internal sealed class WindowsArchiveRestorationLease : IArchiveContentRestorationLease
{
    private readonly object gate = new();
    private readonly WindowsArchiveRecoveryJournal journal;
    private readonly WindowsArchiveDirectory directory;
    private readonly WindowsArchiveFile target, snapshot;
    private bool claimed, disposed;
    public JobJournal Journal => journal.Journal;
    public ArchiveTransactionFacts Preparation { get; }
    public ArchiveContentUpdateFacts Content { get; }
    public ArchiveContentCleanupFacts Catalog { get; }
    public ArchiveRetainedBinding Target { get; }
    public ArchiveRetainedBinding Snapshot { get; }
    public IArchiveContentUpdateSource TargetSource => target;

    private WindowsArchiveRestorationLease(string path, string expectedSha256, Guid nonce, CancellationToken ct)
    {
        journal = WindowsArchiveRecoveryJournal.Open(path, expectedSha256, ct);
        try
        {
            var recovery = Journal.RetainedArchiveRecovery();
            Preparation = recovery.Transactions.Single(t => t.Nonce == nonce); Content = recovery.ContentUpdates.Single(t => t.Nonce == nonce);
            Catalog = recovery.ContentCleanups.Single(t => t.Nonce == nonce);
            if (recovery.ContentRecoveries.Any(t => t.Nonce == nonce) || Content.Snapshot is null || Content.PrivateOutput is null || Catalog.IsResolved) throw Changed();
            directory = WindowsArchiveDirectory.Open(Path.GetDirectoryName(Preparation.ArchivePath)!);
            try
            {
                if (directory.Identity.ToString() != Preparation.ParentIdentity) throw Changed();
                target = directory.OpenSource(Path.GetFileName(Preparation.ArchivePath));
                try
                {
                    target.BindUpdateSource();
                    Target = new(target, ((IArchiveRetainedFile)target).Observe(ct));
                    if (Target.Reviewed.Version != Content.PrivateOutput || Target.Reviewed.MetadataDigest != Content.PrivateMetadataSha256 ||
                        Target.Reviewed.Origin.Digest != Content.RequiredOriginSha256 || Target.Reviewed.Version.Sha256 == Content.Snapshot.Sha256) throw Changed();
                    var row = Catalog.Artifacts[^1];
                    if (row.Path != Content.SnapshotPath || row.Kind != ArchiveArtifactKind.Stage || row.Version != Content.Snapshot) throw Changed();
                    snapshot = directory.OpenRecoveryArtifactOrAbsent(Path.GetFileName(row.Path), Identity(row.Version.Identity), WindowsArchiveArtifactKind.Stage,
                        row.Version.Length) ?? throw Changed();
                    try
                    {
                        snapshot.BindUpdateSource();
                        snapshot.ReviewRecoveredCleanup(row, Catalog.ParentIdentity, ct);
                        Snapshot = new(snapshot, ((IArchiveRetainedFile)snapshot).Observe(ct));
                        Review(ct);
                    }
                    catch { snapshot.Dispose(); throw; }
                }
                catch { target.Dispose(); throw; }
            }
            catch { directory.Dispose(); throw; }
        }
        catch { journal.Dispose(); throw; }
    }
    internal static WindowsArchiveRestorationLease Open(string path, string expectedSha256, Guid nonce, CancellationToken ct)
    {
        try { return new(path, expectedSha256, nonce, ct); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "A complete matching retained snapshot and current archive are required for restoration.", error); }
    }
    public void Claim(CancellationToken ct)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); if (claimed) throw Changed(); claimed = true; Review(ct);
        }
    }
    public void Review(CancellationToken ct)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); _ = Journal.RetainedArchiveRecovery();
            target.RequireClearUpdateSource();
            ((IArchiveRetainedFile)target).Review(Target.Reviewed, ct); ReviewSnapshot(ct);
        }
    }
    public void ReviewSnapshot(CancellationToken ct)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); _ = Journal.RetainedArchiveRecovery();
            snapshot.RequireClearUpdateSource();
            snapshot.ReviewRecoveredCleanup(Catalog.Artifacts[^1], Catalog.ParentIdentity, ct);
            ((IArchiveRetainedFile)snapshot).Review(Snapshot.Reviewed, ct);
        }
    }
    public ArchiveArtifactReservation Reserve(Guid nonce, ArchiveArtifactKind kind, long maximum)
    { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); if (!claimed) throw Changed(); return directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum); } }
    public IArchiveContentUpdateStage Create(ArchiveArtifactReservation reservation)
    { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); if (!claimed) throw Changed(); return directory.CreateArtifact(reservation); } }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true;
            try { snapshot.Dispose(); target.Dispose(); directory.Dispose(); } finally { journal.Dispose(); }
        }
    }
    private static FileIdentity Identity(string value) => WindowsArchiveRecoveryIdentity.Parse(value);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.NeedsReview, "The current archive or retained snapshot differs from the reviewed restoration source; keep both versions.");
}
