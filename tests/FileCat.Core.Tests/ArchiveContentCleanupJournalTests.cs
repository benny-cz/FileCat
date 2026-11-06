using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveContentCleanupJournalTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(6)] [InlineData(13)] [InlineData(14)]
    public void Cleanup_prefixes_retain_review_until_every_exact_deletion_and_final_record_are_present(int prefix)
    {
        using var f = new Fixture(); string path;
        using (var journal = f.Writer())
        {
            path = journal.Path; var catalog = f.Catalog(journal); f.Commit(journal);
            var history = History(catalog);
            foreach (var facts in history.Skip(1).Take(prefix - 1)) journal.ArchiveContentCleanup(facts);
            Assert.Equal(prefix != history.Count, journal.NeedsRecovery);
            if (prefix == history.Count) journal.Finish(JobState.Completed, "all owned artifacts deleted");
            else Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "cleanup interrupted"));
        }
        var recovered = JournalRecovery.ReadArchiveTransactions(path);
        Assert.Null(recovered.Problem); Assert.Equal(prefix != 14, recovered.RequiresReview);
        var cleanup = Assert.Single(recovered.ContentCleanups); Assert.Equal(prefix, cleanup.Sequence);
        Assert.Equal(prefix == 14, recovered.IsContentResolved(cleanup.Nonce));
        Assert.Equal(f.Snapshot.MetadataDigest, cleanup.Artifacts[^1].MetadataSha256);
        Assert.Equal(f.Snapshot.Version, cleanup.Artifacts[^1].Version);
    }

    [Fact]
    public void Cataloging_does_not_authorize_deletion_before_confirmed_publication_or_from_a_deserialized_snapshot()
    {
        using var f = new Fixture(); using var journal = f.Writer(); var catalog = f.Catalog(journal);
        journal.RequireCurrentArchiveContentCleanup(catalog);
        Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveContentCleanup(ArchiveContentCleanupFacts.Parse(catalog.ToJson())));
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentCleanup(catalog.Advance(ArchiveContentCleanupPhase.DeleteIntent)));
        f.Commit(journal);
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentCleanup(catalog.Advance(ArchiveContentCleanupPhase.Deleted)));
        var intent = catalog.Advance(ArchiveContentCleanupPhase.DeleteIntent); journal.ArchiveContentCleanup(intent);
        Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveContentCleanup(catalog));
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentCleanup(intent));
    }

    [Theory]
    [InlineData("scope")] [InlineData("path")] [InlineData("kind")] [InlineData("identity")]
    [InlineData("bytes")] [InlineData("metadata")] [InlineData("origin")] [InlineData("count")]
    [InlineData("extra-field")] [InlineData("future-version")]
    public void Changed_catalogs_and_unknown_cleanup_records_cannot_advance_history(string mutation)
    {
        using var f = new Fixture(); using var journal = f.Writer(); var catalog = f.Catalog(journal); f.Commit(journal);
        var node = catalog.Advance(ArchiveContentCleanupPhase.DeleteIntent).ToJson(); var row = node["artifacts"]![0]!;
        switch (mutation)
        {
            case "scope": node["scope"] = new string('D', 64); break;
            case "path": row["path"] = f.Prepared.ArchivePath; break;
            case "kind": row["kind"] = 99; break;
            case "identity": row["version"]!["identity"] = "other"; break;
            case "bytes": row["version"]!["sha256"] = new string('D', 64); break;
            case "metadata": row["metadata"] = new string('D', 64); break;
            case "origin": row["origin"] = new string('D', 64); break;
            case "count": node["deleted"] = 1; break;
            case "extra-field": node["overwrite"] = true; break;
            case "future-version": node["v"] = 2; break;
        }
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentCleanup(ArchiveContentCleanupFacts.Parse(node)));
        Assert.Equal(1, Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).ContentCleanups).Sequence);
    }

    [Fact]
    public void A_later_content_review_hold_is_not_hidden_by_an_earlier_completed_cleanup()
    {
        using var f = new Fixture(); using var journal = f.Writer(); var catalog = f.Catalog(journal); var committed = f.Commit(journal);
        foreach (var facts in History(catalog).Skip(1)) journal.ArchiveContentCleanup(facts);
        Assert.False(journal.NeedsRecovery);
        journal.ArchiveContentUpdate(committed.Advance(ArchiveContentUpdatePhase.NeedsReview));
        Assert.True(journal.NeedsRecovery); Assert.True(JournalRecovery.ReadArchiveTransactions(journal.Path).RequiresReview);
    }

    private static List<ArchiveContentCleanupFacts> History(ArchiveContentCleanupFacts catalog)
    {
        var result = new List<ArchiveContentCleanupFacts> { catalog }; var current = catalog;
        for (int i = 0; i < catalog.Artifacts.Count; i++)
        { result.Add(current = current.Advance(ArchiveContentCleanupPhase.DeleteIntent)); result.Add(current = current.Advance(ArchiveContentCleanupPhase.Deleted)); }
        result.Add(current.Advance(ArchiveContentCleanupPhase.Completed)); return result;
    }
    internal sealed class Fixture : IDisposable
    {
        private readonly TempDir root = new(); private readonly Guid nonce = Guid.NewGuid();
        private readonly ArchiveOriginSnapshot absent = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
        private ArchiveContentUpdateFacts? sealedContent;
        internal ArchiveFileObservation Original => new(new("original", 1024, new string('A', 64)), "parent", "archive.tar", new string('C', 64), absent, null, null);
        internal ArchiveFileObservation Snapshot => new(new("snapshot", Original.Version.Length, Original.Version.Sha256), "parent", "snapshot", new string('F', 64), absent, null, null);
        private ArchiveObjectVersion Stage => new("stage", 2048, new string('B', 64));
        private ArchiveSidecarFacts[] Sidecars => new[] { ArchiveSidecarRole.ExpectedOutput, ArchiveSidecarRole.OriginEvidence, ArchiveSidecarRole.VerifiedOutput, ArchiveSidecarRole.AssessedSource }
            .Select(role => new ArchiveSidecarFacts(role, Path.Combine(root.Path, "." + role), 512, role.ToString(), new(role.ToString(), 512, new string('E', 64)), role == ArchiveSidecarRole.OriginEvidence ? 1 : null)).ToArray();
        internal ArchiveTransactionFacts Prepared => Preparation(5, ArchiveTransactionPhase.Verifying);
        private ArchiveTransactionFacts Preparation(int sequence, ArchiveTransactionPhase phase)
        {
            var sidecars = Sidecars; var expected = sidecars[0]; var origin = sidecars[1];
            return new(nonce, sequence, new(ArchiveWriteFormat.Tar), phase, Path.Combine(root.Path, "archive.tar"), "parent", Original.Version,
                Path.Combine(root.Path, ".stage"), Stage.Identity, sequence == 5 ? Stage : null, manifestPath: expected.Path, manifest: expected.Version,
                originEvidencePath: origin.Path, originEvidenceIdentity: origin.Identity, originEvidenceSources: 1, originEvidence: origin.Version,
                stageMaximumLength: 2048, sidecars: sidecars, finalizedStage: sequence >= 4 ? Stage : null);
        }
        internal JobJournal Writer() => JobJournal.Create(root.Dir("journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(root.Path, "archive.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned cleanup facts", "owned", [], []));
        internal ArchiveContentCleanupFacts Catalog(JobJournal journal)
        {
            foreach (var pair in new[] { (1, ArchiveTransactionPhase.Reserved), (2, ArchiveTransactionPhase.Rebuilding), (3, ArchiveTransactionPhase.Finalizing), (4, ArchiveTransactionPhase.Verifying), (5, ArchiveTransactionPhase.Verifying) })
                journal.ArchiveTransaction(Preparation(pair.Item1, pair.Item2));
            var reservation = new ArchiveArtifactReservation(new object(), nonce, ArchiveArtifactKind.Stage, root.Path, "parent", Original.Version.Length);
            var a = ArchiveContentUpdateFacts.Reserve(Prepared, reservation, Original, absent); journal.ArchiveContentUpdate(a);
            journal.ArchiveContentUpdate(a = a.Advance(ArchiveContentUpdatePhase.SnapshotCreated, snapshotIdentity: "snapshot", creationPolicy: new string('E', 64)));
            journal.ArchiveContentUpdate(a = a.Advance(ArchiveContentUpdatePhase.TransactionOpened, transactionId: Guid.NewGuid()));
            journal.ArchiveContentUpdate(sealedContent = a.Advance(ArchiveContentUpdatePhase.SnapshotSealed, snapshot: Snapshot.Version));
            var rows = new List<ArchiveContentCleanupArtifact> { new(Prepared.StagePath, ArchiveArtifactKind.Stage, Stage, new string('F', 64), absent.Digest) };
            rows.AddRange(Sidecars.Select(sidecar => new ArchiveContentCleanupArtifact(sidecar.Path,
                sidecar.Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest, sidecar.Version!, new string('F', 64), absent.Digest)));
            rows.Add(ArchiveContentCleanupArtifact.Capture(reservation.Path, ArchiveArtifactKind.Stage, Snapshot));
            var catalog = ArchiveContentCleanupFacts.Reserve(Prepared, sealedContent, rows); journal.ArchiveContentCleanup(catalog); return catalog;
        }
        internal ArchiveContentUpdateFacts Commit(JobJournal journal)
        {
            var ready = sealedContent!.Advance(ArchiveContentUpdatePhase.Ready, privateOutput: new(new(Original.Version.Identity, Stage.Length, Stage.Sha256),
                "parent", "archive.tar", new string('F', 64), absent, null, null)); journal.ArchiveContentUpdate(ready);
            var intent = ready.Advance(ArchiveContentUpdatePhase.CommitIntent); journal.ArchiveContentUpdate(intent);
            var committed = intent.Advance(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, published: ready.PrivateOutput); journal.ArchiveContentUpdate(committed); return committed;
        }
        public void Dispose() => root.Dispose();
    }
}
