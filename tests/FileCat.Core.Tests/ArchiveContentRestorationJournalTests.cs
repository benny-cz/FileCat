using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveContentRestorationJournalTests
{
    [Fact]
    public void Restoration_links_a_fresh_job_without_rewriting_old_facts_and_cleanup_binds_the_current_restored_metadata()
    {
        using var f = new ArchiveContentCleanupJournalTests.Fixture(); string path;
        using (var live = f.Writer()) { path = live.Path; f.Catalog(live); f.Commit(live); }
        using var journal = Open(path); var old = journal.RetainedArchiveRecovery(); var content = Assert.Single(old.ContentUpdates);
        var catalog = Assert.Single(old.ContentCleanups); var preparation = Assert.Single(old.Transactions);
        var intent = ArchiveContentRestorationFacts.Parse(Intent(old, path)); journal.ArchiveContentRestoration(intent);
        Assert.Equal(content.ToJson().ToJsonString(), Assert.Single(journal.RetainedArchiveRecovery().ContentUpdates).ToJson().ToJsonString());
        var current = new ArchiveFileObservation(intent.Restored, "parent", "archive.tar", intent.RestoredMetadataSha256,
            ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), null, null);
        Assert.Throws<ArgumentException>(() => ArchiveContentRecoveryFacts.Begin(preparation, content, catalog, current));
        // A flushed restore intent plus a fresh exact observation is enough to classify bytes, not historical commit.
        Assert.Equal(ArchiveContentRecoveryTarget.RestoredIntact, ArchiveContentRecoveryFacts.Begin(preparation, content, catalog, current, intent).TargetState);
        var committed = intent.Advance(ArchiveContentRestorationPhase.Committed, current); journal.ArchiveContentRestoration(committed);
        var reconciliation = ArchiveContentRecoveryFacts.Begin(preparation, content, catalog, current, committed); journal.ArchiveContentRecovery(reconciliation);
        Assert.Equal(2, reconciliation.RecordVersion); Assert.Equal(committed.Sha256, reconciliation.RestorationSha256);
        Assert.Throws<InvalidOperationException>(() => journal.ArchiveContentRestoration(committed));
        for (int i = 0; i < catalog.Artifacts.Count; i++) journal.ArchiveContentRecovery(reconciliation = reconciliation.Advance(ArchiveContentRecoveryPhase.Absent));
        journal.ArchiveContentRecovery(reconciliation.Advance(ArchiveContentRecoveryPhase.Completed));
        journal.Finish(JobState.Completed, "restored state freshly classified and all private names accounted for");
        Assert.False(JournalRecovery.ReadArchiveTransactions(path).RequiresReview);
    }

    [Fact]
    public void An_uncertain_restore_retry_requires_a_new_job_and_transaction_and_never_replays_the_old_intent()
    {
        using var f = new ArchiveContentCleanupJournalTests.Fixture(); string path;
        using (var live = f.Writer()) { path = live.Path; f.Catalog(live); f.Commit(live); }
        using var journal = Open(path); var old = journal.RetainedArchiveRecovery(); var node = Intent(old, path);
        var first = ArchiveContentRestorationFacts.Parse(node); journal.ArchiveContentRestoration(first);
        var held = first.Advance(ArchiveContentRestorationPhase.NeedsReview); journal.ArchiveContentRestoration(held);
        node["sequence"] = 3; node["attempt"] = 2;
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentRestoration(ArchiveContentRestorationFacts.Parse(node)));
        node["restoreNonce"] = Guid.NewGuid().ToString("N"); node["transaction"] = Guid.NewGuid().ToString("N");
        journal.ArchiveContentRestoration(ArchiveContentRestorationFacts.Parse(node));
        Assert.Equal(2, Assert.Single(journal.RetainedArchiveRecovery().ContentRestorations).Attempt);
        Assert.True(journal.NeedsRecovery); Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "an intent never proves publication"));
    }

    [Theory]
    [InlineData("content")] [InlineData("cleanup")] [InlineData("before-id")] [InlineData("before-metadata")]
    [InlineData("before-origin")] [InlineData("restored-data")] [InlineData("restored-id")] [InlineData("future")]
    [InlineData("extra-key")] [InlineData("sequence")] [InlineData("same-nonce")] [InlineData("empty-transaction")]
    [InlineData("outcome-first")] [InlineData("unknown-phase")]
    public void Corrupt_or_unbound_restoration_history_never_receives_append_authority(string mutation)
    {
        using var f = new ArchiveContentCleanupJournalTests.Fixture(); string path;
        using (var live = f.Writer()) { path = live.Path; f.Catalog(live); f.Commit(live); }
        using var journal = Open(path); var old = journal.RetainedArchiveRecovery(); var node = Intent(old, path); string before = Hash(path);
        switch (mutation)
        {
            case "content": node["content"] = new string('D', 64); break;
            case "cleanup": node["cleanup"] = new string('D', 64); break;
            case "before-id": node["before"]!["identity"] = "foreign"; break;
            case "before-metadata": node["beforeMetadata"] = new string('D', 64); break;
            case "before-origin": node["beforeOrigin"] = new string('D', 64); break;
            case "restored-data": node["restored"]!["sha256"] = new string('D', 64); break;
            case "restored-id": node["restored"]!["identity"] = "foreign"; break;
            case "future": node["v"] = 2; break;
            case "extra-key": node["future"] = 1; break;
            case "sequence": node["sequence"] = 2; break;
            case "same-nonce": node["restoreNonce"] = node["nonce"]!.GetValue<string>(); break;
            case "empty-transaction": node["transaction"] = Guid.Empty.ToString("N"); break;
            case "outcome-first": node["phase"] = "Committed"; break;
            case "unknown-phase": node["phase"] = "0"; break;
        }
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentRestoration(ArchiveContentRestorationFacts.Parse(node)));
        Assert.Equal(before, Hash(path)); Assert.Empty(journal.RetainedArchiveRecovery().ContentRestorations);
    }
    [Fact]
    public void Version_one_cleanup_history_remains_readable_and_cannot_acquire_restoration_authority()
    {
        using var f = new ArchiveContentCleanupJournalTests.Fixture(); string path;
        using (var live = f.Writer()) { path = live.Path; f.Catalog(live); f.Commit(live); }
        using var journal = Open(path); var old = journal.RetainedArchiveRecovery();
        var node = ArchiveContentRecoveryFacts.Begin(Assert.Single(old.Transactions), Assert.Single(old.ContentUpdates), Assert.Single(old.ContentCleanups), f.Original).ToJson();
        node["v"] = 1; node.Remove("restoration"); var legacy = ArchiveContentRecoveryFacts.Parse(node); journal.ArchiveContentRecovery(legacy);
        Assert.Equal(1, legacy.RecordVersion); Assert.False(legacy.ToJson().ContainsKey("restoration"));
        Assert.Throws<InvalidOperationException>(() => journal.ArchiveContentRestoration(ArchiveContentRestorationFacts.Parse(Intent(old, path))));
        Assert.Equal(1, legacy.Advance(ArchiveContentRecoveryPhase.Absent).RecordVersion);
    }

    private static JsonObject Intent(ArchiveTransactionRecovery old, string path)
    {
        var content = Assert.Single(old.ContentUpdates); var cleanup = Assert.Single(old.ContentCleanups);
        return new()
        {
            ["t"] = "archive-content-restoration", ["v"] = 1, ["nonce"] = content.Nonce.ToString("N"), ["sequence"] = 1, ["attempt"] = 1,
            ["phase"] = "CommitIntent", ["content"] = Digest(content.ToJson()), ["cleanup"] = Digest(cleanup.ToJson()),
            ["restoreNonce"] = Guid.NewGuid().ToString("N"), ["restoreJournal"] = Path.Combine(Path.GetDirectoryName(path)!, "fresh-restore.jsonl"),
            ["transaction"] = Guid.NewGuid().ToString("N"), ["before"] = Version(content.PrivateOutput!), ["beforeMetadata"] = content.PrivateMetadataSha256,
            ["beforeOrigin"] = content.RequiredOriginSha256, ["restored"] = Version(new(content.PrivateOutput!.Identity, content.Snapshot!.Length, content.Snapshot.Sha256)),
            ["restoredMetadata"] = content.PrivateMetadataSha256, ["restoredOrigin"] = content.RequiredOriginSha256,
        };
    }
    private static JsonObject Version(ArchiveObjectVersion value) => new() { ["identity"] = value.Identity, ["length"] = value.Length, ["sha256"] = value.Sha256 };
    private static string Digest(JsonObject node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
    private static JobJournal Open(string path) => JobJournal.ReopenArchiveRecovery(path, new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read), Hash(path), () => { }, TestContext.Current.CancellationToken);
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(SHA256.HashData(stream)); }
}
