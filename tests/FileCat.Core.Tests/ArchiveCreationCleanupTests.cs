using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveCreationCleanupTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void Creation_cleanup_prefixes_keep_recovery_until_all_sidecars_are_reconciled(int prefix)
    {
        using var f = new Fixture(); var facts = f.Catalog;
        for (int i = 1; i < prefix; i++)
        {
            var phase = i == 9 ? ArchiveCreationCleanupPhase.Completed : i % 2 == 1 ? ArchiveCreationCleanupPhase.DeleteIntent : ArchiveCreationCleanupPhase.Deleted;
            f.Journal.ArchiveCreationCleanup(facts = facts.Advance(phase));
        }
        Assert.Equal(prefix != 10, f.Journal.NeedsRecovery);
        var parsed = JournalRecovery.ReadArchiveTransactions(f.Journal.Path);
        Assert.Null(parsed.Problem); Assert.Equal(prefix != 10, parsed.RequiresReview);
        Assert.Equal(prefix, Assert.Single(parsed.CreationCleanups).Sequence);
        if (prefix == 10)
        {
            f.Journal.ArchiveTransaction(f.Preparation.AdvanceCreationPublication(ArchiveTransactionPhase.Committed));
            f.Journal.Finish(JobState.Completed, "Creation and exact cleanup completed.");
        }
        else Assert.Throws<IOException>(() => f.Journal.Finish(JobState.Completed, "Cannot hide pending cleanup."));
    }

    [Theory]
    [InlineData("scope")] [InlineData("target")] [InlineData("metadata")] [InlineData("origin")]
    [InlineData("path")] [InlineData("identity")] [InlineData("bytes")] [InlineData("kind")]
    [InlineData("count")] [InlineData("version")] [InlineData("extra")]
    public void Tampered_creation_catalogs_cannot_advance_the_current_history(string mutation)
    {
        using var f = new Fixture(); var node = f.Catalog.Advance(ArchiveCreationCleanupPhase.DeleteIntent).ToJson(); var row = node["artifacts"]![0]!;
        switch (mutation)
        {
            case "scope": node["scope"] = new string('D', 64); break;
            case "target": node["target"]!["identity"] = "other"; break;
            case "metadata": node["metadata"] = new string('D', 64); break;
            case "origin": node["origin"] = new string('D', 64); break;
            case "path": row["path"] = f.Preparation.ArchivePath; break;
            case "identity": row["identity"] = "other"; break;
            case "bytes": row["sha256"] = new string('D', 64); break;
            case "kind": row["kind"] = 99; break;
            case "count": node["reviewed"] = 1; break;
            case "version": node["v"] = 99; break;
            case "extra": node["overwrite"] = true; break;
        }
        Assert.Throws<ArgumentException>(() => f.Journal.ArchiveCreationCleanup(ArchiveCreationCleanupFacts.Parse(node)));
        Assert.Equal(1, Assert.Single(JournalRecovery.ReadArchiveTransactions(f.Journal.Path).CreationCleanups).Sequence);
    }

    [Fact]
    public void Creation_cleanup_refuses_stale_or_deserialized_authority_and_deletion_without_intent()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidOperationException>(() => f.Journal.RequireCurrentArchiveCreationCleanup(ArchiveCreationCleanupFacts.Parse(f.Catalog.ToJson())));
        Assert.Throws<ArgumentException>(() => f.Journal.ArchiveCreationCleanup(f.Catalog.Advance(ArchiveCreationCleanupPhase.Deleted)));
        f.Journal.ArchiveCreationCleanup(f.Catalog.Advance(ArchiveCreationCleanupPhase.DeleteIntent));
        Assert.Throws<InvalidOperationException>(() => f.Journal.RequireCurrentArchiveCreationCleanup(f.Catalog));
    }

    [Theory]
    [InlineData("complete")] [InlineData("changed-target")] [InlineData("changed-sidecar")]
    [InlineData("cancel-before")] [InlineData("cancel-after")] [InlineData("unlink-outcome-failure")]
    public void Creation_cleanup_rechecks_native_scope_and_keeps_truthful_interrupted_outcomes(string mode)
    {
        using var f = new Fixture(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (mode == "cancel-before") stop.Cancel();
        if (mode == "changed-target") File.WriteAllBytes(f.Preparation.ArchivePath, [99]);
        if (mode == "changed-sidecar") File.WriteAllBytes(f.Catalog.Artifacts[0].Path, [99]);
        using var cleanup = new ArchiveCreationCleanup(new Lease(f, f.Journal, mode, stop));
        if (mode == "complete")
        {
            Assert.True(cleanup.Execute(stop.Token).IsResolved); Assert.All(f.Catalog.Artifacts, item => Assert.False(File.Exists(item.Path)));
            Assert.True(File.Exists(f.Preparation.ArchivePath)); return;
        }
        var error = Assert.ThrowsAny<Exception>(() => cleanup.Execute(stop.Token));
        Assert.True(error is IOException or OperationCanceledException); Assert.True(File.Exists(f.Preparation.ArchivePath));
        Assert.True(f.Journal.NeedsRecovery);
        Assert.Equal(ArchiveCreationCleanupPhase.NeedsReview, cleanup.Facts.Phase);
        Assert.Equal(mode == "cancel-after" ? 1u : 0u, cleanup.Facts.DeletedMask);
        Assert.Equal(mode is "cancel-after" or "unlink-outcome-failure", !File.Exists(f.Catalog.Artifacts[0].Path));
    }

    [Fact]
    public void Fresh_creation_recovery_records_an_unrecorded_absence_and_closes_only_after_exact_reconciliation()
    {
        using var f = new Fixture(); using var stop = new CancellationTokenSource();
        using (var first = new ArchiveCreationCleanup(new Lease(f, f.Journal, "unlink-outcome-failure", stop)))
            Assert.Throws<IOException>(() => first.Execute(TestContext.Current.CancellationToken));
        string path = f.Journal.Path; f.Journal.Dispose(); string before = Fixture.Hash(path);
        using var journal = JobJournal.ReopenArchiveRecovery(path, new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read), before, () => { }, TestContext.Current.CancellationToken);
        using var recovery = new ArchiveCreationCleanup(new Lease(f, journal, "complete", stop));
        var result = recovery.Execute(TestContext.Current.CancellationToken);
        Assert.True(result.IsResolved); Assert.Equal(1u, result.AbsentMask); Assert.Equal(14u, result.DeletedMask);
        recovery.Finish("Fresh exact creation cleanup finished.");
        Assert.False(JournalRecovery.ReadArchiveTransactions(path).RequiresReview);
    }

    [Fact]
    public void Final_creation_review_failure_keeps_recovery_open_after_every_unlink_was_recorded()
    {
        using var f = new Fixture(); using var stop = new CancellationTokenSource();
        using (var first = new ArchiveCreationCleanup(new Lease(f, f.Journal, "final-review-failure", stop)))
        {
            Assert.Throws<IOException>(() => first.Execute(TestContext.Current.CancellationToken));
            Assert.Equal(4, first.Facts.ReviewedCount); Assert.Equal(15u, first.Facts.DeletedMask);
            Assert.Equal(ArchiveCreationCleanupPhase.NeedsReview, first.Facts.Phase);
        }
        Assert.True(f.Journal.NeedsRecovery); Assert.All(f.Catalog.Artifacts, artifact => Assert.False(File.Exists(artifact.Path)));
        string path = f.Journal.Path; f.Journal.Dispose(); string before = Fixture.Hash(path);
        using var journal = JobJournal.ReopenArchiveRecovery(path, new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read), before, () => { }, TestContext.Current.CancellationToken);
        using var recovery = new ArchiveCreationCleanup(new Lease(f, journal, "complete", stop));
        Assert.True(recovery.Execute(TestContext.Current.CancellationToken).IsResolved);
        recovery.Finish("Fresh target review reconciled the completed unlink outcomes.");
        Assert.False(JournalRecovery.ReadArchiveTransactions(path).RequiresReview);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir root = new();
        internal JobJournal Journal { get; }
        internal ArchiveTransactionFacts Preparation { get; }
        internal ArchiveCreationCleanupFacts Catalog { get; }
        internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        internal Fixture()
        {
            var ct = TestContext.Current.CancellationToken; var nonce = Guid.NewGuid(); string archive = Path.Combine(root.Path, "archive.tar");
            File.WriteAllBytes(archive, [1, 2, 3]); var target = new ArchiveObjectVersion("target", 3, Hash(archive));
            var origin = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
            var sidecars = new[] { ArchiveSidecarRole.ExpectedOutput, ArchiveSidecarRole.OriginEvidence, ArchiveSidecarRole.VerifiedOutput, ArchiveSidecarRole.CreationMetadata }
                .Select(role =>
                {
                    string path = Path.Combine(root.Path, "." + role); File.WriteAllBytes(path, new byte[512]);
                    return new ArchiveSidecarFacts(role, path, 512, role.ToString(), new(role.ToString(), 512, Hash(path)), role == ArchiveSidecarRole.OriginEvidence ? 0 : null);
                }).ToArray();
            Journal = JobJournal.Create(root.Dir("journal"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = new(archive, null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned creation cleanup fixture", "owned", [], []));
            ArchiveTransactionFacts PreparationAt(int sequence, ArchiveTransactionPhase phase) => new(nonce, sequence, new(ArchiveWriteFormat.Tar), phase,
                archive, "parent", null, Path.Combine(root.Path, ".stage"), target.Identity, sequence >= 5 ? target : null,
                manifestPath: sidecars[0].Path, manifest: sidecars[0].Version, originEvidencePath: sidecars[1].Path, originEvidenceIdentity: sidecars[1].Identity,
                originEvidenceSources: 0, originEvidence: sidecars[1].Version, stageMaximumLength: 3,
                sidecars: [.. sidecars[..^1], new(ArchiveSidecarRole.CreationMetadata, sidecars[^1].Path, 512)], finalizedStage: sequence >= 4 ? target : null);
            ArchiveTransactionFacts current = null!;
            foreach (var (sequence, phase) in new[] { (1,ArchiveTransactionPhase.Reserved),(2,ArchiveTransactionPhase.Rebuilding),
                (3,ArchiveTransactionPhase.Finalizing),(4,ArchiveTransactionPhase.Verifying),(5,ArchiveTransactionPhase.Verifying) })
                Journal.ArchiveTransaction(current = PreparationAt(sequence, phase));
            Journal.ArchiveTransaction(current = current.BindSidecar(current.Sidecars[^1].BindIdentity(sidecars[^1].Identity!)));
            Journal.ArchiveTransaction(current = current.BindSidecar(current.Sidecars[^1].BindVersion(sidecars[^1].Version!)));
            Journal.ArchiveTransaction(current = current.AdvanceCreationPublication(ArchiveTransactionPhase.ReadyToPublish, security: sidecars[^1].Version!.Sha256));
            Journal.ArchiveTransaction(current = current.AdvanceCreationPublication(ArchiveTransactionPhase.Publishing));
            Journal.ArchiveTransaction(current = current.AdvanceCreationPublication(ArchiveTransactionPhase.Reconciling, published: target));
            Journal.ArchiveTransaction(Preparation = current.AdvanceCreationPublication(ArchiveTransactionPhase.Cleaning));
            Catalog = ArchiveCreationCleanupFacts.Begin(Preparation, new(target, "parent", "archive.tar", new string('F',64), origin, null, null),
                sidecars.Select(sidecar => new ArchiveContentCleanupArtifact(sidecar.Path,
                    sidecar.Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest,
                    sidecar.Version!, new string('F',64), origin.Digest)).ToArray());
            Journal.ArchiveCreationCleanup(Catalog); ct.ThrowIfCancellationRequested();
        }
        public void Dispose() { Journal.Dispose(); root.Dispose(); }
    }
    private sealed class Lease(Fixture fixture, JobJournal journal, string mode, CancellationTokenSource stop) : IArchiveCreationCleanupLease
    {
        public JobJournal Journal => journal;
        public ArchiveCreationCleanupFacts Catalog => fixture.Catalog;
        public void ReviewTarget(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Fixture.Hash(fixture.Preparation.ArchivePath) != Catalog.Target.Sha256 ||
                mode == "final-review-failure" && Catalog.Artifacts.All(artifact => !File.Exists(artifact.Path))) throw new IOException("Target review failed.");
        }
        public bool HasArtifact(int index) => File.Exists(Catalog.Artifacts[index].Path);
        public void ReviewArtifact(int index, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (Fixture.Hash(Catalog.Artifacts[index].Path) != Catalog.Artifacts[index].Version.Sha256) throw new IOException("Sidecar changed."); }
        public void DeleteArtifact(int index, CancellationToken ct)
        {
            ReviewArtifact(index, ct); File.Delete(Catalog.Artifacts[index].Path);
            if (index == 0 && mode == "unlink-outcome-failure") throw new IOException("Injected failure after exact unlink.");
            if (index == 0 && mode == "cancel-after") stop.Cancel();
        }
        public void RequireArtifactAbsent(int index) { if (HasArtifact(index)) throw new IOException("An occupant remains."); }
        public void Dispose() { }
    }
}
