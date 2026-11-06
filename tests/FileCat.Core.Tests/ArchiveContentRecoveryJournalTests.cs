using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveContentRecoveryJournalTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Retained_recovery_distinguishes_confirmed_deletion_and_absence_and_only_then_releases_the_hold(bool original)
    {
        using var f = new ArchiveContentCleanupJournalTests.Fixture(); string path;
        using (var writer = f.Writer()) { path = writer.Path; f.Catalog(writer); f.Commit(writer); }
        int reviews = 0;
        using var journal = Open(path, Hash(path), () => reviews++);
        var snapshot = journal.RetainedArchiveRecovery(); var content = Assert.Single(snapshot.ContentUpdates); var cleanup = Assert.Single(snapshot.ContentCleanups);
        var target = original ? f.Original : new ArchiveFileObservation(content.PrivateOutput!, "parent", "archive.tar", content.PrivateMetadataSha256!, f.Original.Origin, null, null);
        var current = ArchiveContentRecoveryFacts.Begin(Assert.Single(snapshot.Transactions), content, cleanup, target);
        journal.ArchiveContentRecovery(current); journal.RequireCurrentArchiveContentRecovery(current);
        Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveContentRecovery(ArchiveContentRecoveryFacts.Parse(current.ToJson())));
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentRecovery(current.Advance(ArchiveContentRecoveryPhase.Deleted)));
        for (int i = 0; i < cleanup.Artifacts.Count; i++)
        {
            if (i % 2 == 0) { journal.ArchiveContentRecovery(current = current.Advance(ArchiveContentRecoveryPhase.DeleteIntent)); journal.ArchiveContentRecovery(current = current.Advance(ArchiveContentRecoveryPhase.Deleted)); }
            else journal.ArchiveContentRecovery(current = current.Advance(ArchiveContentRecoveryPhase.Absent));
        }
        Assert.True(journal.NeedsRecovery);
        journal.ArchiveContentRecovery(current = current.Advance(ArchiveContentRecoveryPhase.Completed));
        Assert.Equal(0b010101u, current.DeletedMask); Assert.Equal(0b101010u, current.AbsentMask);
        Assert.False(journal.NeedsRecovery); Assert.Equal(original ? ArchiveContentRecoveryTarget.OriginalIntact : ArchiveContentRecoveryTarget.PublishedIntact, current.TargetState);
        journal.Finish(JobState.Completed, "retained recovery completed"); Assert.True(reviews >= 4);
        var restored = JournalRecovery.ReadArchiveTransactions(path); Assert.Null(restored.Problem); Assert.False(restored.RequiresReview);
        Assert.Equal(current.ToJson().ToJsonString(), Assert.Single(restored.ContentRecoveries).ToJson().ToJsonString());
    }

    [Theory]
    [InlineData("wrong-hash")] [InlineData("torn-line")] [InlineData("bad-crc")] [InlineData("future-record")]
    [InlineData("generic-intent")] [InlineData("end")]
    public void Damaged_mixed_stale_or_finished_histories_never_receive_recovery_append_authority(string mutation)
    {
        using var f = new ArchiveContentCleanupJournalTests.Fixture(); string path;
        using (var writer = f.Writer()) { path = writer.Path; f.Catalog(writer); f.Commit(writer); }
        if (mutation == "torn-line") File.WriteAllText(path, File.ReadAllText(path).TrimEnd('\n'));
        if (mutation == "bad-crc") File.AppendAllText(path, "00000000 {\"t\":\"note\"}\n");
        if (mutation is "future-record" or "generic-intent" or "end")
        {
            var node = new JsonObject { ["t"] = mutation switch { "future-record" => "archive-future", "generic-intent" => "intent", _ => "end" } };
            string json = node.ToJsonString(); File.AppendAllText(path, $"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}\n");
        }
        byte[] before = File.ReadAllBytes(path); string expected = mutation == "wrong-hash" ? new string('A', 64) : Hash(path);
        Assert.Throws<IOException>(() => Open(path, expected, () => { })); Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Recovery_writer_cannot_write_live_preparation_cleanup_or_generic_job_effect_records()
    {
        using var f = new ArchiveContentCleanupJournalTests.Fixture(); string path;
        using (var writer = f.Writer()) { path = writer.Path; f.Catalog(writer); f.Commit(writer); }
        using var journal = Open(path, Hash(path), () => { }); var snapshot = journal.RetainedArchiveRecovery(); byte[] before = Read(path);
        Assert.Throws<InvalidOperationException>(() => journal.ArchiveTransaction(Assert.Single(snapshot.Transactions)));
        Assert.Throws<InvalidOperationException>(() => journal.ArchiveContentUpdate(Assert.Single(snapshot.ContentUpdates)));
        Assert.Throws<InvalidOperationException>(() => journal.ArchiveContentCleanup(Assert.Single(snapshot.ContentCleanups)));
        Assert.Throws<InvalidOperationException>(() => journal.Note("cannot append a generic note"));
        Assert.Equal(before, Read(path));
    }

    [Theory]
    [InlineData("scope")] [InlineData("target")] [InlineData("metadata")] [InlineData("count")]
    [InlineData("overlap")] [InlineData("skip")] [InlineData("unknown")]
    public void Invalid_or_changed_recovery_observations_cannot_advance_a_retained_history(string mutation)
    {
        using var f = new ArchiveContentCleanupJournalTests.Fixture(); string path;
        using (var writer = f.Writer()) { path = writer.Path; f.Catalog(writer); f.Commit(writer); }
        using var journal = Open(path, Hash(path), () => { }); var data = journal.RetainedArchiveRecovery(); var content = Assert.Single(data.ContentUpdates);
        var target = new ArchiveFileObservation(content.PrivateOutput!, "parent", "archive.tar", content.PrivateMetadataSha256!, f.Original.Origin, null, null);
        var initial = ArchiveContentRecoveryFacts.Begin(Assert.Single(data.Transactions), content, Assert.Single(data.ContentCleanups), target);
        journal.ArchiveContentRecovery(initial); var node = initial.Advance(ArchiveContentRecoveryPhase.DeleteIntent).ToJson();
        switch (mutation)
        {
            case "scope": node["content"] = new string('D', 64); break;
            case "target": node["target"]!["identity"] = "other"; break;
            case "metadata": node["metadata"] = new string('D', 64); break;
            case "count": node["count"] = 7; break;
            case "overlap": node["deletedMask"] = 1; node["absentMask"] = 1; break;
            case "skip": node["reviewed"] = 1; node["deletedMask"] = 1; break;
            case "unknown": node["v"] = 2; break;
        }
        Assert.Throws<ArgumentException>(() => journal.ArchiveContentRecovery(ArchiveContentRecoveryFacts.Parse(node)));
        Assert.Equal(1, Assert.Single(journal.RetainedArchiveRecovery().ContentRecoveries).Sequence);
    }
    private static JobJournal Open(string path, string expected, Action review) => JobJournal.ReopenArchiveRecovery(path,
        new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read), expected, review, TestContext.Current.CancellationToken);
    private static byte[] Read(string path)
    { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(Read(path)));
}
