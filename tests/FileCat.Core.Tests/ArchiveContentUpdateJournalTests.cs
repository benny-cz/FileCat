using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveContentUpdateJournalTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Every_interrupted_prefix_retains_the_separate_snapshot_recipe_and_prevents_generic_cleanup_or_closure(int prefix)
    {
        using var fixture = new Fixture(); string path;
        using (var journal = fixture.Writer())
        {
            path = journal.Path; fixture.Prepare(journal);
            foreach (var facts in fixture.History().Take(prefix)) journal.ArchiveContentUpdate(facts);
            Assert.True(journal.NeedsRecovery);
            Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "artifacts have not been reviewed"));
        }
        var recovered = JournalRecovery.ReadArchiveTransactions(path);
        Assert.Null(recovered.Problem); Assert.True(recovered.RequiresReview);
        var content = Assert.Single(recovered.ContentUpdates);
        Assert.Equal(prefix, content.Sequence); Assert.Equal(fixture.Reservation.Path, content.SnapshotPath);
        Assert.Equal(fixture.History()[prefix - 1].ToJson().ToJsonString(), content.ToJson().ToJsonString());
        // Ordinary preparation facts still describe an unpublished stage, never a rename of the snapshot/original.
        Assert.Null(Assert.Single(recovered.Transactions).Published);
        var interrupted = Assert.Single(JournalRecovery.Scan(fixture.Journals, maxAge: TimeSpan.Zero, keepFinished: 0));
        Assert.False(JournalRecovery.TryClose(interrupted, "still needs native review"));
        Assert.Equal("owned original-byte snapshot", File.ReadAllText(fixture.Reservation.Path));
    }

    [Fact]
    public void A_content_recipe_freezes_preparation_and_requires_the_current_live_durable_snapshot()
    {
        using var fixture = new Fixture(); using var journal = fixture.Writer(); fixture.Prepare(journal);
        var history = fixture.History(); journal.ArchiveContentUpdate(history[0]);
        journal.RequireCurrentArchiveContentUpdate(history[0]);
        Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveContentUpdate(ArchiveContentUpdateFacts.Parse(history[0].ToJson())));
        Assert.Throws<InvalidOperationException>(() => journal.ArchiveTransaction(fixture.Prepared.AdvancePreparation(ArchiveTransactionPhase.NeedsReview)));
        journal.ArchiveContentUpdate(history[1]);
        Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveContentUpdate(history[0]));
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentUpdate(history[1]));
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentUpdate(history[3]));
        Assert.Equal(2, Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).ContentUpdates).Sequence);
    }

    [Theory]
    [InlineData("unknown-recipe")] [InlineData("unknown-version")] [InlineData("numeric-phase")]
    [InlineData("missing-field")] [InlineData("unknown-field")] [InlineData("bad-transaction-id")]
    [InlineData("negative-sequence")] [InlineData("bad-digest")]
    public void Unsupported_or_malformed_content_records_are_rejected(string mutation)
    {
        using var fixture = new Fixture(); var node = fixture.History()[3].ToJson();
        switch (mutation)
        {
            case "unknown-recipe": node["recipe"] = "ordinary-overwrite"; break;
            case "unknown-version": node["v"] = 2; break;
            case "numeric-phase": node["phase"] = "3"; break;
            case "missing-field": node.Remove("snapshotMaximum"); break;
            case "unknown-field": node["restore"] = true; break;
            case "bad-transaction-id": node["transactionId"] = Guid.Empty.ToString("N"); break;
            case "negative-sequence": node["sequence"] = -1; break;
            case "bad-digest": node["preparationSha256"] = "unbounded"; break;
        }
        var error = Record.Exception(() => ArchiveContentUpdateFacts.Parse(node));
        Assert.True(error is ArgumentException or ArchiveWriteException, error?.ToString());
    }

    [Theory]
    [InlineData("preparation")] [InlineData("snapshot-path")] [InlineData("snapshot-identity")]
    [InlineData("snapshot-bytes")] [InlineData("private-identity")] [InlineData("private-bytes")]
    [InlineData("early-commit")] [InlineData("changed-origin")]
    public void Constructor_valid_but_inconsistent_history_cannot_grant_publication(string mutation)
    {
        using var fixture = new Fixture(); using var journal = fixture.Writer(); fixture.Prepare(journal);
        var history = fixture.History(); int target = mutation switch
        { "snapshot-bytes" => 3, "private-identity" or "private-bytes" or "early-commit" => 4, _ => 1 };
        foreach (var facts in history.Take(target)) journal.ArchiveContentUpdate(facts);
        var node = history[target].ToJson();
        switch (mutation)
        {
            case "preparation": node["preparationSha256"] = new string('D', 64); break;
            case "snapshot-path": node["snapshotPath"] = fixture.Prepared.StagePath; break;
            case "snapshot-identity": node["snapshotIdentity"] = fixture.Original.Version.Identity; break;
            case "snapshot-bytes": node["snapshot"]!["sha256"] = new string('D', 64); break;
            case "private-identity": node["privateOutput"]!["identity"] = fixture.Prepared.StageIdentity; break;
            case "private-bytes": node["privateOutput"]!["sha256"] = new string('D', 64); break;
            case "early-commit": node["phase"] = "CommitIntent"; break;
            case "changed-origin": node["requiredOrigin"] = new string('D', 64); break;
        }
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentUpdate(ArchiveContentUpdateFacts.Parse(node)));
        Assert.Equal(target, Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).ContentUpdates).Sequence);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Confirmed_private_rollback_remains_a_retained_artifact_outcome(int index)
    {
        using var fixture = new Fixture(); using var journal = fixture.Writer(); fixture.Prepare(journal);
        var history = fixture.History(); foreach (var facts in history.Take(index + 1)) journal.ArchiveContentUpdate(facts);
        journal.ArchiveContentUpdate(history[index].Advance(ArchiveContentUpdatePhase.RolledBackWithRetainedArtifacts));
        var recovered = JournalRecovery.ReadArchiveTransactions(journal.Path);
        Assert.Null(recovered.Problem); Assert.True(recovered.RequiresReview);
        Assert.Equal(ArchiveContentUpdatePhase.RolledBackWithRetainedArtifacts, Assert.Single(recovered.ContentUpdates).Phase);
    }

    [Fact]
    public void A_future_record_keeps_the_last_supported_snapshot_location_in_the_review_report()
    {
        using var fixture = new Fixture(); string path;
        using (var journal = fixture.Writer())
        { path = journal.Path; fixture.Prepare(journal); foreach (var facts in fixture.History()) journal.ArchiveContentUpdate(facts); }
        var future = fixture.History()[^1].ToJson(); future["v"] = 2;
        string json = future.ToJsonString();
        File.AppendAllText(path, $"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}\n", new UTF8Encoding(false));
        var recovered = JournalRecovery.ReadArchiveTransactions(path);
        Assert.NotNull(recovered.Problem); Assert.True(recovered.RequiresReview);
        Assert.Equal(fixture.Reservation.Path, Assert.Single(recovered.ContentUpdates).SnapshotPath);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir root = new(); private readonly Guid nonce = Guid.NewGuid();
        internal string Journals => root.Dir("journals");
        internal ArchiveFileObservation Original { get; }
        internal ArchiveArtifactReservation Reservation { get; }
        internal ArchiveTransactionFacts Prepared => Preparation(4, ArchiveTransactionPhase.Verifying);
        internal Fixture()
        {
            Original = new(new("original", 1024, new string('A', 64)), "parent", "archive.tar", new string('C', 64),
                ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), null, null);
            Reservation = new(new object(), nonce, ArchiveArtifactKind.Stage, root.Path, "parent", Original.Version.Length);
            File.WriteAllText(Reservation.Path, "owned original-byte snapshot");
        }
        internal JobJournal Writer() => JobJournal.Create(Journals, new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(root.Path, "archive.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned content recipe", "owned", [], []));
        internal void Prepare(JobJournal journal)
        {
            journal.ArchiveTransaction(Preparation(1, ArchiveTransactionPhase.Reserved));
            journal.ArchiveTransaction(Preparation(2, ArchiveTransactionPhase.Rebuilding));
            journal.ArchiveTransaction(Preparation(3, ArchiveTransactionPhase.Verifying)); journal.ArchiveTransaction(Prepared);
        }
        private ArchiveTransactionFacts Preparation(int sequence, ArchiveTransactionPhase phase)
        {
            var stage = new ArchiveObjectVersion("stage", 2048, new string('B', 64)); var manifest = new ArchiveObjectVersion("manifest", 512, new string('C', 64));
            var origin = new ArchiveObjectVersion("origin", 512, new string('D', 64));
            return new(nonce, sequence, new(ArchiveWriteFormat.Tar), phase, Path.Combine(root.Path, "archive.tar"), "parent", Original.Version,
                Path.Combine(root.Path, ".stage"), stage.Identity, sequence == 4 ? stage : null,
                manifestPath: sequence == 4 ? Path.Combine(root.Path, ".manifest") : null, manifest: sequence == 4 ? manifest : null,
                originEvidencePath: Path.Combine(root.Path, ".origin"), originEvidenceIdentity: origin.Identity, originEvidenceSources: 1,
                originEvidence: sequence == 4 ? origin : null);
        }
        internal ArchiveContentUpdateFacts[] History()
        {
            var a = ArchiveContentUpdateFacts.Reserve(Prepared, Reservation, Original, Original.Origin);
            var b = a.Advance(ArchiveContentUpdatePhase.SnapshotCreated, snapshotIdentity: "snapshot", creationPolicy: new string('E', 64));
            var c = b.Advance(ArchiveContentUpdatePhase.TransactionOpened, transactionId: new Guid("f2d7d1db-2c18-4b59-9f39-b4ab113e68fa"));
            var d = c.Advance(ArchiveContentUpdatePhase.SnapshotSealed, snapshot: new("snapshot", Original.Version.Length, Original.Version.Sha256));
            var e = d.Advance(ArchiveContentUpdatePhase.Ready, privateOutput: new(new(Original.Version.Identity, Prepared.VerifiedStage!.Length,
                Prepared.VerifiedStage.Sha256), "parent", Original.LeafName, new string('F', 64), Original.Origin, null, null));
            var f = e.Advance(ArchiveContentUpdatePhase.CommitIntent);
            return [a, b, c, d, e, f, f.Advance(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, published: e.PrivateOutput)];
        }
        public void Dispose() => root.Dispose();
    }
}
