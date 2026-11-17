using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class StorageReviewAcknowledgmentTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private static byte[] Journal(bool clean = false)
    {
        var before = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(8, "reviewed-object"), false, null);
        var header = new StorageJournalHeader(Guid.NewGuid(), DateTime.UnixEpoch, StorageAction.Compress, StorageScope.SelectedItems,
            Guid.NewGuid(), [new(0, 0, @"C:\source\file", before)]);
        using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        if (clean) writer.Finish(new(header.Action, header.Scope, header.ReviewRevision, new(NotProcessed: 1))
        { Canceled = true, Roots = [new(0, before.Identity, StorageCompleteness.Incomplete, false)] });
        else writer.FlushIntent(0, @"C:\source\file", before);
        return data.ToArray();
    }
    private static MemoryEvidence Evidence(bool clean = false)
    {
        byte[] bytes = Journal(clean);
        using var read = new MemoryStream(bytes);
        var facts = StorageTransformJournalReader.Read(read, ct: Stop);
        return new(bytes, StorageJournalPolicy.FileName(DateTime.UnixEpoch, facts.Collector.Header!.JobId));
    }
    private static StorageReviewStatus Acknowledge(MemoryEvidence evidence, string text = "Reviewed the recorded storage change and its unresolved effect.") =>
        JournalRecovery.AcknowledgeStorageReview(evidence, JournalRecovery.ReadStorageReview(evidence, Stop), text,
            StorageReviewChoice.AcknowledgeReviewedHistory, Stop);

    [Fact]
    public void Exact_evidence_export_preserves_every_retained_byte_and_refuses_a_stale_snapshot()
    {
        using var evidence = Evidence(); var reviewed = JournalRecovery.ReadStorageReview(evidence, Stop);
        using var exported = new MemoryStream(); JournalRecovery.CopyStorageReview(evidence, reviewed, exported, Stop);
        Assert.Equal(reviewed.Evidence.ByteLength, exported.Length);
        Assert.Equal(reviewed.Evidence.Sha256, Convert.ToHexString(SHA256.HashData(exported.ToArray())));
        Assert.Contains("storage-intent", Encoding.UTF8.GetString(exported.ToArray()));
        Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending); Assert.Empty(evidence.Files);
        evidence.Journal.Position = evidence.Journal.Length; evidence.Journal.WriteByte((byte)'!');
        using var rejected = new MemoryStream();
        Assert.Throws<IOException>(() => JournalRecovery.CopyStorageReview(evidence, reviewed, rejected, Stop)); Assert.Equal(0, rejected.Length);
    }

    [Fact]
    public void Stopped_evidence_export_never_acknowledges_history()
    {
        using var evidence = Evidence(); var reviewed = JournalRecovery.ReadStorageReview(evidence, Stop);
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var output = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => JournalRecovery.CopyStorageReview(evidence, reviewed, output, stop.Token));
        Assert.Empty(evidence.Files); Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
    }

    [Fact]
    public void Exact_report_and_acknowledgment_clear_review_but_preserve_historical_uncertainty()
    {
        using var evidence = Evidence();
        var before = JournalRecovery.ReadStorageReviewStatus(evidence, Stop);
        Assert.True(before.ReviewPending); Assert.True(before.Current.History.EffectUncertain);
        var status = Acknowledge(evidence);
        Assert.False(status.ReviewPending); Assert.True(status.Current.History.EffectUncertain); Assert.True(status.Current.History.ReviewPending);
        Assert.NotNull(status.Acknowledgment); Assert.Equal(status.Current.Evidence, status.Acknowledgment.Reviewed.Evidence);
        Assert.Equal(2, evidence.Files.Count); Assert.Equal(1, evidence.MaximumOpenArtifacts);
        Assert.False(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
        Assert.All(evidence.Events.Where(e => e.StartsWith("publish", StringComparison.Ordinal)), e => Assert.Contains("owned", e));
    }

    [Fact]
    public void Repeated_acknowledgment_is_idempotent_and_a_revised_report_is_a_new_immutable_pair()
    {
        using var evidence = Evidence();
        var first = Acknowledge(evidence, "first explicit review");
        string reportLeaf = first.Acknowledgment!.ReportLeaf;
        byte[] original = evidence.Files[reportLeaf].Bytes.ToArray();
        var again = Acknowledge(evidence, "first explicit review");
        Assert.Equal(first.Acknowledgment, again.Acknowledgment); Assert.Equal(2, evidence.Created);
        var revised = Acknowledge(evidence, "revised explicit review");
        Assert.NotEqual(first.Acknowledgment.ReportId, revised.Acknowledgment!.ReportId);
        Assert.Equal(4, evidence.Created); Assert.Equal(2, evidence.Files.Count); Assert.False(evidence.Files.ContainsKey(reportLeaf));
        Assert.True(evidence.Events.FindLastIndex(e => e.StartsWith("published-ack", StringComparison.Ordinal)) < evidence.Events.FindIndex(e => e.StartsWith("delete-artifact", StringComparison.Ordinal)));
        Assert.NotEmpty(original); Assert.False(revised.ReviewPending);
    }

    [Fact]
    public void A_changed_journal_or_replaced_report_cannot_reuse_an_old_acknowledgment()
    {
        using var evidence = Evidence(); var status = Acknowledge(evidence);
        var report = evidence.Files[status.Acknowledgment!.ReportLeaf]; report.Identity = Guid.NewGuid().ToString("N");
        Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
        Assert.False(JournalRecovery.PruneStorageJournal(evidence, status.Current, Stop)); Assert.False(evidence.JournalDeleted);
        report.Identity = status.Acknowledgment.ReportEvidence.Identity;
        var reviewed = JournalRecovery.ReadStorageReview(evidence, Stop);
        evidence.Journal.Position = evidence.Journal.Length; evidence.Journal.WriteByte((byte)'!');
        int created = evidence.Created;
        Assert.Throws<IOException>(() => JournalRecovery.AcknowledgeStorageReview(evidence, reviewed, "stale click",
            StorageReviewChoice.AcknowledgeReviewedHistory, Stop));
        Assert.Equal(created, evidence.Created); Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
    }

    [Fact]
    public void Complete_digest_covers_bytes_beyond_a_damaged_parsed_prefix_and_unknown_names_have_a_stable_association()
    {
        byte[] bytes = new byte[200 * 1024]; Encoding.UTF8.GetBytes("00000000 invalid history\n").CopyTo(bytes, 0);
        using var evidence = new MemoryEvidence(bytes, "job-storage-unreadable.fcj");
        var reviewed = JournalRecovery.ReadStorageReview(evidence, Stop);
        Assert.True(reviewed.History.Damaged); Assert.True(reviewed.History.EffectUncertain);
        Assert.True(reviewed.Evidence.ByteLength > reviewed.History.Prefix.ByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), reviewed.Evidence.Sha256);
        Assert.NotEqual(Guid.Empty, reviewed.AssociationId);
        Assert.Equal(reviewed.AssociationId, JournalRecovery.ReadStorageReview(evidence, Stop).AssociationId);
        evidence.Journal.Position = evidence.Journal.Length - 1; evidence.Journal.WriteByte(71);
        var changed = JournalRecovery.ReadStorageReview(evidence, Stop);
        Assert.Equal(reviewed.History.Prefix, changed.History.Prefix); Assert.NotEqual(reviewed.Evidence, changed.Evidence);
        Assert.Throws<IOException>(() => JournalRecovery.AcknowledgeStorageReview(evidence, reviewed, "prefix alone is insufficient",
            StorageReviewChoice.AcknowledgeReviewedHistory, Stop));
        Assert.False(Acknowledge(evidence).ReviewPending); Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).Current.History.Damaged);
    }

    [Theory]
    [InlineData("flush-report")]
    [InlineData("publish-report")]
    [InlineData("published-report")]
    [InlineData("flush-ack")]
    [InlineData("publish-ack")]
    [InlineData("published-ack")]
    public void Interrupted_publication_retains_review_and_reconciles_known_orphans_before_retry(string phase)
    {
        using var evidence = Evidence(); bool fail = true;
        evidence.At = p => { if (p == phase && fail) { fail = false; throw new IOException("publication fixture"); } };
        Assert.Throws<IOException>(() => Acknowledge(evidence));
        Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending); Assert.True(evidence.IsOwned);
        evidence.At = null;
        Assert.False(Acknowledge(evidence).ReviewPending); Assert.Equal(2, evidence.Files.Count);
        Assert.DoesNotContain(evidence.Files.Keys, leaf => leaf.StartsWith(".", StringComparison.Ordinal));
    }

    [Fact]
    public void A_readable_ack_after_ambiguous_publication_and_failed_cleanup_does_not_clear_review()
    {
        using var evidence = Evidence();
        evidence.At = phase => { if (phase is "published-ack" or "delete-artifact") throw new IOException("ambiguous namespace fixture"); };
        Assert.Throws<IOException>(() => Acknowledge(evidence));
        Assert.Contains(evidence.Files.Keys, name => name.Contains("-ack-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal));
        Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
        evidence.At = null;
        Assert.False(Acknowledge(evidence).ReviewPending); Assert.Equal(2, evidence.Files.Count);
    }

    [Fact]
    public void Missing_tampered_unknown_schema_and_external_report_references_never_validate_a_pair()
    {
        using var evidence = Evidence(); var status = Acknowledge(evidence);
        string ackLeaf = evidence.Files.Keys.Single(k => k.Contains("-ack-", StringComparison.Ordinal));
        var blob = evidence.Files[ackLeaf]; byte[] original = blob.Bytes.ToArray();
        foreach (string field in new[] { "reportLeaf", "version", "reportEvidence", "reviewed", "choice" })
        {
            var json = JsonNode.Parse(original)!;
            json[field] = field switch
            { "reportLeaf" => JsonValue.Create(@"C:\outside\report.json"), "version" => JsonValue.Create(99), "choice" => JsonValue.Create("unrecognizedChoice"), _ => null };
            blob.Replace(Encoding.UTF8.GetBytes(json.ToJsonString()));
            Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
            Assert.DoesNotContain(evidence.Opened, name => name.Contains("outside", StringComparison.Ordinal));
        }
        blob.Replace(original);
        var report = evidence.Files[status.Acknowledgment!.ReportLeaf]; report.Bytes.Position = report.Bytes.Length - 1; report.Bytes.WriteByte((byte)'!');
        Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
    }

    [Fact]
    public void Pruning_requires_exact_inactive_proof_and_never_releases_report_dependencies_when_journal_delete_fails()
    {
        using var evidence = Evidence(); var status = Acknowledge(evidence); evidence.Events.Clear();
        evidence.At = phase => { if (phase == "delete-journal") throw new IOException("journal deletion fixture"); };
        Assert.Throws<IOException>(() => JournalRecovery.PruneStorageJournal(evidence, status.Current, Stop));
        Assert.Equal(2, evidence.Files.Count); Assert.DoesNotContain(evidence.Events, e => e.StartsWith("delete-artifact", StringComparison.Ordinal));
        evidence.At = null; evidence.Owned = false;
        Assert.Throws<IOException>(() => JournalRecovery.PruneStorageJournal(evidence, status.Current, Stop));
        evidence.Owned = true;
        Assert.True(JournalRecovery.PruneStorageJournal(evidence, status.Current, Stop)); Assert.True(evidence.JournalDeleted);
        Assert.Equal(2, evidence.Files.Count); // report retention follows only after successful journal removal
        using var clean = Evidence(clean: true); var review = JournalRecovery.ReadStorageReview(clean, Stop);
        Assert.True(JournalRecovery.PruneStorageJournal(clean, review, Stop)); Assert.Empty(clean.Files);
    }

    [Fact]
    public void Failed_orphan_deletion_is_charged_and_cannot_make_retries_grow_unbounded()
    {
        using var evidence = Evidence();
        evidence.At = phase => { if (phase is "flush-ack" or "delete-artifact") throw new IOException("disk fixture"); };
        string text = new('x', 300_000);
        for (int i = 0; i < 10; i++) Assert.Throws<IOException>(() => Acknowledge(evidence, text));
        int count = evidence.Files.Count, created = evidence.Created;
        Assert.InRange(evidence.Files.Values.Sum(b => b.Bytes.Length), 1, StorageJournalPolicy.ReviewWriteBytes);
        Assert.Throws<IOException>(() => Acknowledge(evidence, text)); Assert.Equal(count, evidence.Files.Count); Assert.Equal(created, evidence.Created);
        Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
    }

    [Fact]
    public void Retained_pair_survives_journal_pruning_and_stays_protected_while_a_journal_exists()
    {
        using var evidence = Evidence(); var acknowledged = Acknowledge(evidence); using var retained = new Retained(evidence);
        string leaf = evidence.Files.Keys.Single(k => k.Contains("-ack-", StringComparison.Ordinal));
        var blob = evidence.Files[leaf]; var info = new StorageReviewArtifactInfo(leaf, blob.Identity, blob.Bytes.Length);
        var before = JournalRecovery.ReadRetainedStorageReview(retained, info, Stop)!;
        Assert.True(before.JournalPresent); Assert.False(JournalRecovery.PruneRetainedStorageReview(retained, before, Stop)); Assert.Equal(2, evidence.Files.Count);
        Assert.True(JournalRecovery.PruneStorageJournal(evidence, acknowledged.Current, Stop));
        var after = JournalRecovery.ReadRetainedStorageReview(retained, info, Stop)!;
        Assert.False(after.JournalPresent); Assert.True(after.Report.Reviewed.History.EffectUncertain);
        Assert.Equal(acknowledged.Current.Evidence, after.Report.Reviewed.Evidence);
        Assert.True(JournalRecovery.PruneRetainedStorageReview(retained, after, Stop)); Assert.Empty(evidence.Files);
    }

    [Fact]
    public void A_reappearing_journal_and_failed_retained_report_delete_preserve_the_pair()
    {
        using var evidence = Evidence(); var acknowledged = Acknowledge(evidence);
        Assert.True(JournalRecovery.PruneStorageJournal(evidence, acknowledged.Current, Stop)); using var retained = new Retained(evidence);
        string leaf = evidence.Files.Keys.Single(k => k.Contains("-ack-", StringComparison.Ordinal));
        var blob = evidence.Files[leaf]; var info = new StorageReviewArtifactInfo(leaf, blob.Identity, blob.Bytes.Length);
        var snapshot = JournalRecovery.ReadRetainedStorageReview(retained, info, Stop)!; retained.ReappearBeforeDelete = true;
        Assert.Throws<IOException>(() => JournalRecovery.PruneRetainedStorageReview(retained, snapshot, Stop)); Assert.Equal(2, evidence.Files.Count);
        retained.ReappearBeforeDelete = false; retained.ExtraJournal = false;
        evidence.At = phase => { if (phase == "delete-artifact") throw new IOException("retained report deletion fixture"); };
        Assert.Throws<IOException>(() => JournalRecovery.PruneRetainedStorageReview(retained, snapshot, Stop)); Assert.Equal(2, evidence.Files.Count);
        Assert.NotNull(JournalRecovery.ReadRetainedStorageReview(retained, info, Stop));
    }

    private sealed class Retained(MemoryEvidence store) : StorageRetainedReviewEvidence
    {
        internal bool ExtraJournal, ReappearBeforeDelete;
        internal override string DirectoryIdentity => store.DirectoryIdentity;
        internal override bool IsOwned => store.Owned;
        internal override IReadOnlyList<StorageReviewArtifactInfo> Artifacts(Guid association, int maximum, CancellationToken ct) => store.Artifacts(association, maximum, ct);
        internal override StorageReviewArtifact OpenArtifact(StorageReviewArtifactInfo expected, CancellationToken ct) => store.OpenArtifact(expected, ct);
        internal override bool JournalExists(string historicalLeaf, Guid association, CancellationToken ct) => !store.JournalDeleted || ExtraJournal;
        internal override void DeleteArtifact(StorageReviewArtifact artifact, StorageEvidenceVersion expected, CancellationToken ct) => throw new IOException("Absence proof required.");
        internal override void DeleteUnreferenced(StorageReviewArtifact artifact, StorageEvidenceVersion expected, string historicalLeaf, Guid association, CancellationToken ct)
        {
            if (ReappearBeforeDelete) ExtraJournal = true;
            if (JournalExists(historicalLeaf, association, ct)) throw new IOException("A journal still requires this report.");
            store.DeleteArtifact(artifact, expected, ct);
        }
        public override void Dispose() { }
    }

    private sealed class Blob(byte[] bytes)
    {
        internal string Identity = Guid.NewGuid().ToString("N");
        internal MemoryStream Bytes = Make(bytes);
        internal void Replace(byte[] bytes) { Bytes.Dispose(); Bytes = Make(bytes); }
        private static MemoryStream Make(byte[] bytes) { var value = new MemoryStream(); value.Write(bytes); value.Position = 0; return value; }
    }
    private sealed class MemoryEvidence(byte[] journal, string leaf) : StorageReviewEvidence
    {
        private readonly Blob _journal = new(journal);
        internal readonly Dictionary<string, Blob> Files = new(StringComparer.Ordinal);
        internal readonly List<string> Events = [], Opened = [];
        internal Action<string>? At;
        internal bool Owned = true, JournalDeleted;
        internal int Created, OpenArtifacts, MaximumOpenArtifacts;
        internal override string DirectoryIdentity => "private-directory-identity";
        internal override string JournalLeaf => leaf;
        internal override string JournalIdentity => _journal.Identity;
        internal override Stream Journal => _journal.Bytes;
        internal override bool IsOwned => Owned && !JournalDeleted;
        private void Event(string phase) { Assert.True(Owned); Events.Add(phase + " owned"); At?.Invoke(phase); }
        internal override IReadOnlyList<StorageReviewArtifactInfo> Artifacts(Guid association, int maximum, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Files.Select(p => new StorageReviewArtifactInfo(p.Key, p.Value.Identity, p.Value.Bytes.Length)).Take(maximum + 1).ToArray(); }
        internal override StorageReviewArtifact OpenArtifact(StorageReviewArtifactInfo expected, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Opened.Add(expected.Leaf);
            if (!Files.TryGetValue(expected.Leaf, out var blob) || blob.Identity != expected.Identity || blob.Bytes.Length != expected.ByteLength) throw new IOException("Artifact identity/length mismatch.");
            blob.Bytes.Position = 0; return Open(expected.Leaf, blob);
        }
        internal override StorageReviewArtifact CreateArtifact(string temporaryLeaf, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.True(StorageReviewNames.TryParse(temporaryLeaf, out _, out var kind, out _, out var temporary)); Assert.True(temporary);
            Event("create-" + kind); var blob = new Blob([]); Files.Add(temporaryLeaf, blob); Created++; return Open(temporaryLeaf, blob);
        }
        private Artifact Open(string name, Blob blob)
        { OpenArtifacts++; MaximumOpenArtifacts = Math.Max(MaximumOpenArtifacts, OpenArtifacts); Assert.Equal(1, OpenArtifacts); return new(this, name, blob); }
        internal override void PublishNew(StorageReviewArtifact artifact, string publishedLeaf, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); var owned = Assert.IsType<Artifact>(artifact);
            Assert.True(StorageReviewNames.TryParse(publishedLeaf, out _, out var kind, out _, out var temporary)); Assert.False(temporary);
            Event("publish-" + kind); Assert.False(Files.ContainsKey(publishedLeaf)); Files.Add(publishedLeaf, owned.Blob); Files.Remove(owned.Name); owned.Name = publishedLeaf;
            Event("published-" + kind);
        }
        internal override void DeleteArtifact(StorageReviewArtifact artifact, StorageEvidenceVersion expected, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Event("delete-artifact");
            Assert.Equal(expected, JournalRecovery.InspectStorageEvidence(artifact.Stream, () => artifact.Identity, ct));
            Assert.True(Files.Remove(artifact.Leaf));
        }
        internal override void DeleteJournal(StorageEvidenceVersion expected, CancellationToken ct)
        { Event("delete-journal"); Assert.Equal(expected, JournalRecovery.InspectStorageEvidence(Journal, () => JournalIdentity, ct)); JournalDeleted = true; }
        public override void Dispose() { Owned = false; _journal.Bytes.Dispose(); foreach (var blob in Files.Values) blob.Bytes.Dispose(); }
        private sealed class Artifact(MemoryEvidence owner, string name, Blob blob) : StorageReviewArtifact
        {
            internal string Name = name;
            internal Blob Blob => blob;
            private bool _closed;
            internal override string Leaf => Name;
            internal override string Identity => blob.Identity;
            internal override Stream Stream => blob.Bytes;
            internal override void FlushDurable(CancellationToken ct)
            { ct.ThrowIfCancellationRequested(); Assert.True(StorageReviewNames.TryParse(Name, out _, out var kind, out _, out _)); owner.Event("flush-" + kind); }
            public override void Dispose() { if (!_closed) { _closed = true; owner.OpenArtifacts--; } }
        }
    }
}
