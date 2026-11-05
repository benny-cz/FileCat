using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveTransactionJournalTests
{
    [Fact]
    public void Completed_schema_two_history_keeps_its_complete_provenance_and_normal_retention_behavior()
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
        {
            for (int i = 0; i < Fixture.SuccessPhases.Length; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
            journal.Finish(JobState.Completed, "completed schema-two fixture");
        }
        Rewrite(f.JournalPath, node =>
        {
            if (node.ContainsKey("archiveJournalVersion")) node["archiveJournalVersion"] = 2;
            if (node["t"]?.GetValue<string>() == "archive-transaction") { node["v"] = 2; node.Remove("stageMaximum"); }
        });
        var recovery = JournalRecovery.ReadArchiveTransactions(f.JournalPath);
        Assert.Null(recovery.Problem); Assert.False(recovery.RequiresReview);
        var facts = Assert.Single(recovery.Transactions); Assert.Equal(2, facts.RecordVersion); Assert.Equal(f.Origin, facts.OriginEvidence);
        Assert.Empty(JournalRecovery.Scan(f.Journals, keepFinished: 0));
        Assert.True(File.Exists(f.StagePath)); Assert.True(File.Exists(f.OriginPath));
    }

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
            Assert.Equal(ArchiveTransactionFacts.SchemaVersion, restored.RecordVersion);
            Assert.Equal(f.OriginPath, restored.OriginEvidencePath); Assert.Equal(f.Origin, restored.OriginEvidence);
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
        Assert.Equal("owned provenance", File.ReadAllText(f.OriginPath));
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

    [Theory]
    [InlineData("missing-path")]
    [InlineData("missing-identity")]
    [InlineData("missing-count")]
    [InlineData("missing-version")]
    [InlineData("missing-field")]
    [InlineData("relative-path")]
    [InlineData("archive-path")]
    [InlineData("stage-path")]
    [InlineData("retired-path")]
    [InlineData("manifest-path")]
    [InlineData("original-identity")]
    [InlineData("stage-identity")]
    [InlineData("manifest-identity")]
    [InlineData("different-version-identity")]
    [InlineData("negative-count")]
    [InlineData("missing-original-count")]
    [InlineData("excess-count")]
    [InlineData("fractional-count")]
    [InlineData("string-count")]
    [InlineData("short-sidecar")]
    [InlineData("short-record-set")]
    [InlineData("large-sidecar")]
    [InlineData("unknown-version-field")]
    public void Publication_refuses_missing_ambiguous_aliased_or_impossible_origin_evidence(string mutation)
    {
        using var f = new Fixture();
        var node = f.Facts(4, ArchiveTransactionPhase.ReadyToPublish).ToJson();
        switch (mutation)
        {
            case "missing-path": node["originPath"] = null; break;
            case "missing-identity": node["originIdentity"] = null; break;
            case "missing-count": node["originSources"] = null; break;
            case "missing-version": node["originEvidence"] = null; break;
            case "missing-field": node.Remove("originEvidence"); break;
            case "relative-path": node["originPath"] = "untrusted-origin"; break;
            case "archive-path": node["originPath"] = node["archive"]!.DeepClone(); break;
            case "stage-path": node["originPath"] = node["stage"]!.DeepClone(); break;
            case "retired-path": node["originPath"] = node["retained"]!.DeepClone(); break;
            case "manifest-path": node["originPath"] = node["manifestPath"]!.DeepClone(); break;
            case "original-identity": SetIdentity("original-object"); break;
            case "stage-identity": SetIdentity("stage-object"); break;
            case "manifest-identity": SetIdentity("manifest-object"); break;
            case "different-version-identity": node["originEvidence"]!["identity"] = "different-object"; break;
            case "negative-count": node["originSources"] = -1; break;
            case "missing-original-count": node["originSources"] = 0; break;
            case "excess-count": node["originSources"] = ArchiveWritePolicy.MaxMembers + 2; break;
            case "fractional-count": node["originSources"] = 1.5; break;
            case "string-count": node["originSources"] = "1"; break;
            case "short-sidecar": node["originEvidence"]!["length"] = 51; break;
            case "short-record-set": node["originEvidence"]!["length"] = 112; break;
            case "large-sidecar": node["originEvidence"]!["length"] = ArchiveWritePolicy.MaxMetadataBytes + 1; break;
            case "unknown-version-field": node["originEvidence"]!["trusted"] = true; break;
        }
        Assert.Throws<ArgumentException>(() => ArchiveTransactionFacts.Parse(node));
        void SetIdentity(string identity) { node["originIdentity"] = identity; node["originEvidence"]!["identity"] = identity; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_count_covers_empty_creation_and_every_added_source_without_an_off_by_one(bool update)
    {
        using var f = new Fixture(update);
        var node = f.Facts(4, ArchiveTransactionPhase.ReadyToPublish).ToJson();
        int minimum = update ? 1 : 0, maximum = ArchiveWritePolicy.MaxMembers + minimum;
        foreach (int count in new[] { minimum, maximum })
        {
            node["originSources"] = count;
            node["originEvidence"]!["length"] = 52L + ArchiveOriginEvidenceWriter.MinFramedRecordBytes * (long)count;
            var facts = ArchiveTransactionFacts.Parse(node);
            Assert.Equal(count, facts.OriginEvidenceSources);
            Assert.Equal(node.ToJsonString(), facts.ToJson().ToJsonString());
        }
        node["originSources"] = maximum + 1;
        Assert.Throws<ArgumentException>(() => ArchiveTransactionFacts.Parse(node));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("identity")]
    [InlineData("count")]
    [InlineData("remove")]
    [InlineData("bytes")]
    [InlineData("length")]
    public void Reserved_or_finalized_origin_evidence_cannot_change_before_a_durable_append(string mutation)
    {
        using var f = new Fixture();
        using var journal = f.Writer();
        bool finalized = mutation is "bytes" or "length";
        int completed = finalized ? 4 : 1;
        for (int i = 0; i < completed; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
        var next = f.Facts(completed + 1, Fixture.SuccessPhases[completed]).ToJson();
        switch (mutation)
        {
            case "path": next["originPath"] = f.OriginPath + "-substitution"; break;
            case "identity": next["originIdentity"] = "substitution"; break;
            case "count": next["originSources"] = 2; break;
            case "remove": next["originPath"] = next["originIdentity"] = next["originSources"] = null; break;
            case "bytes": next["originEvidence"]!["sha256"] = new string('0', 64); break;
            case "length": next["originEvidence"]!["length"] = 513; break;
        }
        byte[] before = ReadLiveJournal(journal.Path);
        Assert.Throws<ArgumentException>(() => journal.ArchiveTransaction(ArchiveTransactionFacts.Parse(next)));
        Assert.Equal(before, ReadLiveJournal(journal.Path));
        Assert.True(journal.NeedsRecovery);
        Assert.Equal("owned provenance", File.ReadAllText(f.OriginPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_facts_stay_readable_and_retained_even_with_complete_commit_and_end_records(bool declaredHeader)
    {
        using var f = new Fixture();
        using (var journal = f.Writer(explicitProfile: declaredHeader))
        {
            for (int i = 0; i < Fixture.SuccessPhases.Length; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
            journal.Finish(JobState.Completed, "historical completed transaction");
        }
        Rewrite(f.JournalPath, record =>
        {
            if (record.ContainsKey("archiveJournalVersion")) record["archiveJournalVersion"] = 1;
            if (record["t"]?.GetValue<string>() == "archive-transaction") MakeLegacy(record);
        });
        File.WriteAllText(JobJournal.ManifestPathOf(f.JournalPath), "owned source evidence");
        byte[] before = File.ReadAllBytes(f.JournalPath);
        var job = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        var facts = Assert.Single(job.ArchiveTransactions.Transactions);
        Assert.Equal(1, facts.RecordVersion); Assert.Equal(ArchiveTransactionPhase.Committed, facts.Phase);
        Assert.Equal(f.StagePath, facts.StagePath); Assert.Equal(f.ManifestPath, facts.ManifestPath);
        Assert.Null(facts.OriginEvidencePath); Assert.Null(facts.OriginEvidenceSources); Assert.Null(facts.OriginEvidence);
        Assert.Contains("legacy", job.ArchiveTransactions.Problem);
        Assert.True(job.ArchiveTransactions.RequiresReview); Assert.True(JournalRecovery.IsInterrupted(job));
        Assert.False(JournalRecovery.TryClose(job, "legacy evidence is incomplete"));
        Assert.Equal(before, File.ReadAllBytes(f.JournalPath));
        Assert.Equal("owned source evidence", File.ReadAllText(JobJournal.ManifestPathOf(f.JournalPath)));
        Assert.Equal("owned stage", File.ReadAllText(f.StagePath));
        Assert.Equal("owned manifest", File.ReadAllText(f.ManifestPath));
        Assert.Equal("owned provenance", File.ReadAllText(f.OriginPath));
    }

    [Fact]
    public void Legacy_facts_cannot_be_written_or_implicitly_upgraded_to_a_new_transaction()
    {
        using var f = new Fixture();
        using var journal = f.Writer();
        var node = f.Facts(1, ArchiveTransactionPhase.Reserved).ToJson(); MakeLegacy(node);
        var legacy = ArchiveTransactionFacts.Parse(node);
        Assert.Equal(node.ToJsonString(), legacy.ToJson().ToJsonString());
        byte[] before = ReadLiveJournal(journal.Path);
        Assert.Throws<InvalidOperationException>(() => journal.ArchiveTransaction(legacy));
        Assert.Equal(before, ReadLiveJournal(journal.Path));
        journal.ArchiveTransaction(f.Facts(1, ArchiveTransactionPhase.Reserved));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("record")]
    [InlineData("mixed")]
    [InlineData("missing-evidence")]
    public void Schema_confusion_or_removed_evidence_cannot_hide_artifacts_behind_a_later_commit(string mutation)
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
        {
            for (int i = 0; i < Fixture.SuccessPhases.Length; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
            journal.Finish(JobState.Completed, "must be rechecked");
        }
        Rewrite(f.JournalPath, record =>
        {
            if (mutation == "header" && record.ContainsKey("archiveJournalVersion")) record["archiveJournalVersion"] = 1;
            if (record["t"]?.GetValue<string>() != "archive-transaction") return;
            if (mutation == "record" || mutation == "mixed" && record["sequence"]!.GetValue<int>() == 5) MakeLegacy(record);
            if (mutation == "missing-evidence" && record["sequence"]!.GetValue<int>() == 5) record["originEvidence"] = null;
        });
        var job = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0));
        Assert.NotNull(job.ArchiveTransactions.Problem); Assert.True(job.ArchiveTransactions.RequiresReview);
        Assert.False(JournalRecovery.TryClose(job, "damaged completed journal"));
        Assert.True(File.Exists(f.JournalPath)); Assert.Equal("owned provenance", File.ReadAllText(f.OriginPath));
    }

    [Fact]
    public void A_retained_provenance_sidecar_keeps_a_completed_archive_visible_for_review()
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
        {
            for (int i = 0; i < 7; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
            journal.ArchiveTransaction(f.Facts(8, ArchiveTransactionPhase.CommittedWithRetainedArtifacts, [f.OriginPath]));
            Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "sidecar cleanup incomplete"));
        }
        Append(f.JournalPath, new JsonObject { ["t"] = "end", ["state"] = "Completed" });
        var job = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.Equal(f.OriginPath, Assert.Single(Assert.Single(job.ArchiveTransactions.Transactions).UnresolvedArtifacts));
        Assert.False(JournalRecovery.TryClose(job, "sidecar not inspected"));
        Assert.Equal("owned provenance", File.ReadAllText(f.OriginPath));
    }

    private static void MakeLegacy(JsonObject node)
    {
        node.Remove("stageMaximum");
        node["v"] = 1;
        foreach (string key in new[] { "originPath", "originIdentity", "originSources", "originEvidence" }) node.Remove(key);
    }

    [Theory]
    [InlineData("duplicate-header")]
    [InlineData("late-header")]
    [InlineData("early-end")]
    [InlineData("duplicate-end")]
    public void Out_of_order_headers_or_end_records_cannot_close_a_completed_archive_history(string mutation)
    {
        using var f = new Fixture();
        using (var journal = f.Writer())
        {
            for (int i = 0; i < Fixture.SuccessPhases.Length; i++) journal.ArchiveTransaction(f.Facts(i + 1, Fixture.SuccessPhases[i]));
            journal.Finish(JobState.Completed, "record ordering must still be checked");
        }
        var records = JobJournal.ReadAll(f.JournalPath).Select(r => (JsonObject)r.Node.DeepClone()).ToList();
        switch (mutation)
        {
            case "duplicate-header": records.Add((JsonObject)records[0].DeepClone()); break;
            case "late-header": var header = records[0]; records.RemoveAt(0); records.Add(header); break;
            case "early-end": records.Insert(2, (JsonObject)records[^1].DeepClone()); break;
            case "duplicate-end": records.Add((JsonObject)records[^1].DeepClone()); break;
        }
        File.WriteAllText(f.JournalPath, "", new UTF8Encoding(false));
        foreach (var record in records) Append(f.JournalPath, record);
        var job = Assert.Single(JournalRecovery.Scan(f.Journals, keepFinished: 0));
        Assert.NotNull(job.ArchiveTransactions.Problem); Assert.True(JournalRecovery.IsInterrupted(job));
        Assert.False(JournalRecovery.TryClose(job, "out-of-order history needs review"));
        Assert.Equal("owned provenance", File.ReadAllText(f.OriginPath));
    }

    private static void Rewrite(string path, Action<JsonObject> change)
    {
        var records = JobJournal.ReadAll(path).Select(r => (JsonObject)r.Node.DeepClone()).ToArray();
        File.WriteAllText(path, "", new UTF8Encoding(false));
        foreach (var record in records) { change(record); Append(path, record); }
    }

    private static byte[] ReadLiveJournal(string path)
    {
        // The durable writer remains open for this before/after assertion. Share its existing write access.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }

    [Fact]
    public void Maximal_fields_share_one_total_line_budget_and_an_overflowing_combination_is_refused()
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
        var origin = new ArchiveObjectVersion(LongIdentity("origin"), 512, new string('F', 64));
        ArchiveTransactionFacts Facts(int artifacts) => new(f.Nonce, 8, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.CommittedWithRetainedArtifacts,
            LongPath("archive"), LongIdentity("parent"), original, LongPath("stage"), stage.Identity, stage, LongPath("retained"),
            LongPath("manifest"), manifest, new string('E', 64), stage, original,
            Enumerable.Range(0, artifacts).Select(i => LongPath("retained-" + i)).ToArray(),
            LongPath("origin"), origin.Identity, 1, origin);
        var facts = Facts(ArchiveTransactionFacts.MaxArtifacts - 1);
        Assert.True(Encoding.UTF8.GetByteCount(facts.ToJson().ToJsonString()) < JobJournal.MaxJournalLineBytes - 10);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => Facts(ArchiveTransactionFacts.MaxArtifacts)).Reason);
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
        public ArchiveObjectVersion Origin { get; } = new("origin-object", 512, new string('F', 64));
        public string StagePath => Path.Combine(root.Path, ".owned-stage");
        public string ManifestPath => Path.Combine(root.Path, ".owned-manifest");
        public string OriginPath => Path.Combine(root.Path, ".owned-origin");
        public string RetainedPath => Path.Combine(root.Path, ".owned-retired");
        public string Journals => root.Dir("journals");
        public string JournalPath { get; private set; } = "";
        public JobJournal Writer(bool explicitProfile = true)
        {
            File.WriteAllText(StagePath, "owned stage"); File.WriteAllText(ManifestPath, "owned manifest"); File.WriteAllText(OriginPath, "owned provenance");
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
                sequence >= 6 ? Stage : null, sequence >= 6 && update ? retired ?? Original : null, artifacts,
                OriginPath, Origin.Identity, update ? 1 : 0, sequence >= 4 ? Origin : null);
        public void Dispose() => root.Dispose();
    }
}
