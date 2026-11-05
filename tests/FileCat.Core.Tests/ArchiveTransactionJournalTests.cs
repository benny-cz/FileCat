using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveTransactionJournalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complete_creation_or_update_facts_round_trip_and_only_then_allow_normal_closure(bool update)
    {
        using var f = new Fixture(update);
        using (var journal = f.Writer())
        {
            int sequence = 0;
            foreach (var phase in Fixture.SuccessPhases)
            {
                journal.ArchiveTransaction(f.Facts(++sequence, phase));
                Assert.Equal(phase != ArchiveTransactionPhase.Committed, journal.NeedsRecovery);
            }
            var raw = Assert.Single(JobJournal.ReadAll(journal.Path), r => r.Type == "archive-transaction" && r.Get("phase") == "Committed");
            var restored = ArchiveTransactionFacts.Parse(raw.Node);
            Assert.Equal(f.Nonce, restored.Nonce);
            Assert.Equal(f.Stage, restored.Published);
            Assert.Equal(update ? f.Original : null, restored.Retired);
            Assert.Equal(f.ManifestPath, restored.ManifestPath);
            Assert.True(restored.IsResolved);
            journal.Finish(JobState.Completed, "owned complete transaction");
        }
        Assert.Empty(JournalRecovery.Scan(f.Journals, keepFinished: 0));
        Assert.False(File.Exists(f.JournalPath));
        Assert.Equal("owned stage", File.ReadAllText(f.StagePath)); // scanning never performs artifact cleanup.
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Every_nonterminal_phase_survives_a_spurious_end_and_retention_pruning(int transitions)
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
        {
            for (int i = 0; i < transitions; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
            Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "must not hide recovery"));
            Assert.DoesNotContain(JobJournal.ReadAll(journal.Path), r => r.Type == "end");
        }
        File.WriteAllText(JobJournal.ManifestPathOf(f.JournalPath), "owned source evidence");
        Append(f.JournalPath, new JsonObject { ["t"] = "end", ["state"] = "Completed" });
        var job = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.True(job.ArchiveTransactions.RequiresReview);
        Assert.Equal(Fixture.SuccessPhases[transitions - 1], Assert.Single(job.ArchiveTransactions.Transactions).Phase);
        Assert.True(JournalRecovery.IsInterrupted(job));
        byte[] before = File.ReadAllBytes(f.JournalPath);
        Assert.False(JournalRecovery.TryClose(job, "generic review is insufficient"));
        Assert.Equal(before, File.ReadAllBytes(f.JournalPath));
        Assert.True(File.Exists(JobJournal.ManifestPathOf(f.JournalPath)));
        Assert.Equal("owned manifest", File.ReadAllText(f.ManifestPath));
        Assert.Equal("owned stage", File.ReadAllText(f.StagePath));
    }

    [Fact]
    public void A_committed_cleanup_warning_names_the_retained_artifact_and_stays_visible()
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
        {
            for (int i = 0; i < 7; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
            var artifacts = new List<string> { f.RetainedPath };
            var facts = f.Facts(8, ArchiveTransactionPhase.CommittedWithRetainedArtifacts, artifacts);
            artifacts.Clear();
            journal.ArchiveTransaction(facts);
            Assert.Equal(f.RetainedPath, Assert.Single(facts.UnresolvedArtifacts));
            Assert.True(journal.NeedsRecovery);
        }
        var job = Assert.Single(JournalRecovery.Scan(f.Journals));
        Assert.Equal(f.RetainedPath, Assert.Single(Assert.Single(job.ArchiveTransactions.Transactions).UnresolvedArtifacts));
        Assert.True(job.ArchiveTransactions.RequiresReview);
        Assert.False(JournalRecovery.TryClose(job, "must use archive-specific review"));
    }

    [Fact]
    public void A_known_prepublication_failure_can_close_but_does_not_release_an_unrelated_recovery_hold()
    {
        using var f = new Fixture();
        using var journal = f.Writer();
        journal.ArchiveTransaction(f.Facts(1, ArchiveTransactionPhase.Reserved));
        journal.RequireRecovery();
        journal.ArchiveTransaction(f.Facts(2, ArchiveTransactionPhase.NotCommitted));
        Assert.True(journal.NeedsRecovery);
        Assert.Throws<IOException>(() => journal.Finish(JobState.Failed, "other temporary items remain"));
    }

    [Fact]
    public void A_known_prepublication_failure_releases_only_its_own_archive_hold()
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
        {
            journal.ArchiveTransaction(f.Facts(1, ArchiveTransactionPhase.Reserved));
            journal.ArchiveTransaction(f.Facts(2, ArchiveTransactionPhase.NotCommitted));
            Assert.False(journal.NeedsRecovery);
            journal.Finish(JobState.Failed, "no publication occurred; owned stage was cleaned");
        }
        Assert.Empty(JournalRecovery.Scan(f.Journals));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("sequence")]
    [InlineData("stage")]
    [InlineData("parent")]
    [InlineData("unknown-field")]
    [InlineData("unknown-type")]
    [InlineData("too-many-artifacts")]
    [InlineData("replay")]
    [InlineData("torn")]
    [InlineData("bad-crc")]
    [InlineData("nested-duplicate")]
    [InlineData("top-level-duplicate")]
    public void Damaged_unsupported_replayed_or_changed_facts_cannot_be_hidden_by_a_later_valid_commit(string mutation)
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
            for (int i = 0; i < 4; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
        var node = f.Facts(5, ArchiveTransactionPhase.Publishing).ToJson();
        switch (mutation)
        {
            case "version": node["v"] = 999; break;
            case "sequence": node["sequence"] = 6; break;
            case "stage": node["stageIdentity"] = "different-object"; node["verified"]!["identity"] = "different-object"; break;
            case "parent": node["parent"] = "different-parent"; break;
            case "unknown-field": node["unrecognized"] = true; break;
            case "unknown-type": node["t"] = "archive-future-version"; break;
            case "too-many-artifacts": node["unresolved"] = new JsonArray(Enumerable.Range(0, 17).Select(i => (JsonNode)JsonValue.Create(f.StagePath + i)!).ToArray()); break;
            case "replay": node = f.Facts(4, ArchiveTransactionPhase.ReadyToPublish).ToJson(); break;
        }
        if (mutation == "torn") File.AppendAllText(f.JournalPath, "01234567 {\"t\":\"archive-transaction\"\n");
        else if (mutation == "bad-crc") File.AppendAllText(f.JournalPath, "00000000 " + node.ToJsonString() + "\n");
        else if (mutation == "nested-duplicate")
            AppendJson(f.JournalPath, node.ToJsonString().Replace("\"format\":1", "\"format\":1,\"format\":1", StringComparison.Ordinal));
        else if (mutation == "top-level-duplicate")
            AppendJson(f.JournalPath, node.ToJsonString().Replace("\"t\":\"archive-transaction\"", "\"t\":\"archive-transaction\",\"t\":\"archive-transaction\"", StringComparison.Ordinal));
        else Append(f.JournalPath, node);
        for (int i = 4; i < Fixture.SuccessPhases.Length; i++) Append(f.JournalPath, f.Facts(i + 1, Fixture.SuccessPhases[i]).ToJson());
        Append(f.JournalPath, new JsonObject { ["t"] = "end" });
        var job = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0));
        Assert.NotNull(job.ArchiveTransactions.Problem);
        Assert.True(JournalRecovery.IsInterrupted(job));
        Assert.False(JournalRecovery.TryClose(job, "not reconciled"));
        Assert.True(File.Exists(f.JournalPath));
    }

    [Fact]
    public void Archive_records_without_a_begin_record_are_retained_as_an_unknown_interruption()
    {
        using var f = new Fixture();
        using (var journal = f.Writer()) journal.ArchiveTransaction(f.Facts(1, ArchiveTransactionPhase.Reserved));
        File.WriteAllLines(f.JournalPath, File.ReadAllLines(f.JournalPath).Skip(1));
        var job = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0));
        Assert.NotNull(job.ArchiveTransactions.Problem);
        Assert.True(job.ArchiveTransactions.RequiresReview);
        Assert.False(JournalRecovery.TryClose(job, "missing header"));
    }

    [Fact]
    public void Generic_closure_rechecks_the_current_journal_instead_of_trusting_a_stale_scan()
    {
        using var f = new Fixture();
        using (f.Writer(explicitProfile: false)) { }
        var stale = Assert.Single(JournalRecovery.Scan(f.Journals));
        Assert.False(stale.ArchiveTransactions.HasRecords);
        Append(f.JournalPath, f.Facts(1, ArchiveTransactionPhase.Reserved).ToJson());
        Assert.False(JournalRecovery.TryClose(stale, "stale generic review"));
        Assert.True(JournalRecovery.IsInterrupted(stale));
    }

    [Theory]
    [InlineData("torn")]
    [InlineData("bad-type")]
    [InlineData("missing-type")]
    [InlineData("unknown-type")]
    public void The_header_version_protects_an_unreadable_first_archive_fact_from_finished_journal_pruning(string mutation)
    {
        using var f = new Fixture();
        using (f.Writer()) { }
        if (mutation == "torn") File.AppendAllText(f.JournalPath, "01234567 {\"t\":\"archive-transaction\"\n");
        else Append(f.JournalPath, mutation switch
        {
            "bad-type" => new JsonObject { ["t"] = 7 },
            "missing-type" => new JsonObject { ["v"] = 1 },
            _ => new JsonObject { ["t"] = "future-stage-transition" },
        });
        Append(f.JournalPath, new JsonObject { ["t"] = "end" });
        var job = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0));
        Assert.True(job.ArchiveTransactions.RequiresReview);
        Assert.NotNull(job.ArchiveTransactions.Problem);
        Assert.False(JournalRecovery.TryClose(job, "first fact is damaged"));
    }

    [Fact]
    public void An_explicit_profile_refused_before_any_archive_fact_can_finish_normally()
    {
        using var f = new Fixture();
        using (var journal = f.Writer()) journal.Finish(JobState.Failed, "configuration refused before mutation");
        Assert.Empty(JournalRecovery.Scan(f.Journals, keepFinished: 0));
        Assert.False(File.Exists(f.JournalPath));
    }

    [Fact]
    public void Invalid_transitions_are_rejected_before_a_durable_append()
    {
        using var f = new Fixture();
        using var journal = f.Writer();
        journal.ArchiveTransaction(f.Facts(1, ArchiveTransactionPhase.Reserved));
        long before = new FileInfo(journal.Path).Length;
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(f.Facts(3, ArchiveTransactionPhase.Verifying)));
        Assert.Equal(before, new FileInfo(journal.Path).Length);
        Assert.True(journal.NeedsRecovery);
    }

    [Fact]
    public void A_disposed_journal_cannot_acknowledge_an_archive_transition()
    {
        using var f = new Fixture();
        var journal = f.Writer(); journal.Dispose();
        Assert.Throws<ObjectDisposedException>(() => journal.ArchiveTransaction(f.Facts(1, ArchiveTransactionPhase.Reserved)));
    }

    [Fact]
    public void A_finished_journal_cannot_start_an_archive_transition()
    {
        using var f = new Fixture();
        using var journal = f.Writer();
        journal.Finish(JobState.Completed, "nothing started");
        long before = new FileInfo(journal.Path).Length;
        Assert.Throws<InvalidOperationException>(() => journal.ArchiveTransaction(f.Facts(1, ArchiveTransactionPhase.Reserved)));
        Assert.Equal(before, new FileInfo(journal.Path).Length);
    }

    [Fact]
    public void Archive_artifacts_are_excluded_from_generic_pathname_cleanup_discovery()
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
        {
            journal.Intent("archive-owned-stage", f.StagePath, null, f.StagePath);
            journal.ArchiveTransaction(f.Facts(1, ArchiveTransactionPhase.Reserved));
        }
        var job = Assert.Single(JournalRecovery.Scan(f.Journals));
        Assert.Empty(JournalRecovery.FindStagedLeftovers(job));
        Assert.Empty(JournalRecovery.FindRenameLeftovers(job));
        Assert.True(JournalRecovery.ReviewCopies(job).LimitReached);
        Assert.Equal("owned stage", File.ReadAllText(f.StagePath));
    }

    [Fact]
    public void A_late_retired_version_cannot_be_claimed_as_a_successfully_reconciled_original()
    {
        using var f = new Fixture();
        Assert.Throws<ArgumentException>(() => f.Facts(8, ArchiveTransactionPhase.Committed, retired: new("different-original", 1, new string('D', 64))));
    }

    [Fact]
    public void Maximum_escaped_fields_and_artifacts_still_fit_one_bounded_durable_record()
    {
        using var f = new Fixture();
        string LongPath(string key)
        {
            string prefix = Path.Combine(f.Journals, key);
            return prefix + new string('\uFFFF', ArchiveWritePolicy.MaxNameChars - prefix.Length);
        }
        string LongIdentity(string key) => key + new string('\uFFFF', 4096 - key.Length);
        var original = new ArchiveObjectVersion(LongIdentity("original"), 1, new string('A', 64));
        var stage = new ArchiveObjectVersion(LongIdentity("stage"), 2, new string('B', 64));
        var manifest = new ArchiveObjectVersion(LongIdentity("manifest"), 3, new string('C', 64));
        var facts = new ArchiveTransactionFacts(f.Nonce, 8, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.CommittedWithRetainedArtifacts,
            LongPath("archive"), LongIdentity("parent"), original, LongPath("stage"), stage.Identity, stage, LongPath("retained"),
            LongPath("manifest"), manifest, new string('E', 64), stage, original,
            Enumerable.Range(0, ArchiveTransactionFacts.MaxArtifacts).Select(i => LongPath("retained-" + i)).ToArray());
        Assert.True(Encoding.UTF8.GetByteCount(facts.ToJson().ToJsonString()) < JobJournal.MaxJournalLineBytes - 10);
    }

    private static void Append(string path, JsonObject record) => AppendJson(path, record.ToJsonString());
    private static void AppendJson(string path, string json)
    {
        uint crc = Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json));
        File.AppendAllText(path, $"{crc:x8} {json}\n", new UTF8Encoding(false));
    }

    private sealed class Fixture(bool update = true) : IDisposable
    {
        private readonly TempDir root = new();
        public static readonly ArchiveTransactionPhase[] SuccessPhases =
        [ArchiveTransactionPhase.Reserved, ArchiveTransactionPhase.Rebuilding, ArchiveTransactionPhase.Verifying,
            ArchiveTransactionPhase.ReadyToPublish, ArchiveTransactionPhase.Publishing, ArchiveTransactionPhase.Reconciling,
            ArchiveTransactionPhase.Cleaning, ArchiveTransactionPhase.Committed];
        public Guid Nonce { get; } = Guid.NewGuid();
        public ArchiveObjectVersion Original { get; } = new("original-object", 1, new string('A', 64));
        public ArchiveObjectVersion Stage { get; } = new("stage-object", 2, new string('B', 64));
        public ArchiveObjectVersion Manifest { get; } = new("manifest-object", 3, new string('C', 64));
        public string StagePath => Path.Combine(root.Path, ".owned-stage");
        public string ManifestPath => Path.Combine(root.Path, ".owned-manifest");
        public string RetainedPath => Path.Combine(root.Path, ".owned-retired");
        public string Journals => root.Dir("journals");
        public string JournalPath { get; private set; } = "";
        public JobJournal Writer(bool explicitProfile = true)
        {
            File.WriteAllText(StagePath, "owned stage"); File.WriteAllText(ManifestPath, "owned manifest");
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = explicitProfile ? new ArchivePlan(Path.Combine(root.Path, "archive.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) : null },
                "owned archive transaction", "owned", [], []);
            var writer = JobJournal.Create(Journals, job); JournalPath = writer.Path; return writer;
        }
        public ArchiveTransactionFacts Facts(int sequence, ArchiveTransactionPhase phase, IReadOnlyList<string>? artifacts = null,
            ArchiveObjectVersion? retired = null) =>
            new(Nonce, sequence, new(ArchiveWriteFormat.Tar), phase, Path.Combine(root.Path, "archive.tar"), "parent-object",
                update ? Original : null, StagePath, Stage.Identity, sequence >= 4 ? Stage : null, update && sequence >= 4 ? RetainedPath : null,
                sequence >= 4 ? ManifestPath : null, sequence >= 4 ? Manifest : null, sequence >= 4 ? new string('E', 64) : null,
                sequence >= 6 ? Stage : null, sequence >= 6 && update ? retired ?? Original : null, artifacts);
        public void Dispose() => root.Dispose();
    }
}
