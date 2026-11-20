using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class StorageJournalTests
{
    private static readonly StorageObservation Before = new(StorageObservationState.Disabled, StorageObservationState.Disabled,
        new(11, "test-object") { VolumeBinding = "test-volume" }, false, null);
    private static StorageJournalHeader Header(Guid? id = null) => new(id ?? Guid.NewGuid(), DateTime.UnixEpoch, StorageAction.Compress,
        StorageScope.SelectedItems, Guid.NewGuid(), [new(0, 0, @"C:\source\file", Before)]);
    private static StorageMutationResult Changed() => new(Before, Before with { Compression = StorageObservationState.Enabled }, StorageEffect.Changed, null) { NativeCallAttempted = true };
    private static StorageResultSnapshot Summary(StorageJournalHeader header, StorageOutcomeCounts counts, bool uncertain = false) =>
        new(header.Action, header.Scope, header.ReviewRevision, counts)
        {
            Roots = [new(0, Before.Identity, uncertain ? StorageCompleteness.Incomplete : StorageCompleteness.Complete, counts.Changed > 0)],
            Discovery = uncertain ? StorageCompleteness.UnknownRemainder : StorageCompleteness.Complete,
            ReviewPending = uncertain,
        };
    private static StorageJournalReadResult Read(MemoryStream stream) => StorageTransformJournalReader.Read(new MemoryStream(stream.ToArray()), ct: TestContext.Current.CancellationToken);
    private static byte[] Line(string json)
    {
        byte[] data = Encoding.UTF8.GetBytes(json);
        return Encoding.UTF8.GetBytes(Crc32.HashToUInt32(data).ToString("x8") + " " + json + "\n");
    }

    [Fact]
    public void Exact_intent_result_summary_and_terminal_have_one_shared_clean_interpretation()
    {
        using var data = new MemoryStream(); var header = Header();
        using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        var call = writer.FlushIntent(0, @"C:\source\file", Before);
        Assert.True(writer.NeedsRecovery); Assert.NotNull(writer.Collector.Pending);
        Assert.True(call.TryAdmit());
        writer.FlushResult(call, Changed());
        writer.RootSummary(new(0, StorageJournalCounts.From(new(Changed: 1)), StorageCompleteness.Complete, true));
        writer.Finish(Summary(header, new(Changed: 1)));
        Assert.False(writer.NeedsRecovery);
        var read = Read(data); Assert.False(read.Collector.ReviewPending); Assert.False(read.Collector.EffectUncertain); Assert.True(read.Collector.Ended);
        Assert.Equal(1, read.Collector.Summary!.Counts.Changed); Assert.Equal(writer.Prefix, read.Prefix);
        Assert.Throws<IOException>(() => writer.FlushIntent(0, @"C:\source\file", Before));
    }

    [Fact]
    public void Pending_or_uncertain_effects_cannot_be_hidden_by_failed_canceled_or_generic_end_records()
    {
        using var pending = new MemoryStream(); var header = Header();
        using (var writer = new StorageTransformJournalWriter(pending, () => { }, header))
        {
            _ = writer.FlushIntent(0, @"C:\source\file", Before);
            Assert.Throws<IOException>(() => writer.Finish(Summary(header, new()) with { Canceled = true }));
        }
        pending.Write(Line("{\"t\":\"end\",\"state\":\"Failed\"}"));
        var read = Read(pending); Assert.True(read.Collector.ReviewPending); Assert.True(read.Collector.EffectUncertain); Assert.False(read.Collector.Ended);
        using var uncertain = new MemoryStream();
        using var uncertainWriter = new StorageTransformJournalWriter(uncertain, () => { }, header);
        var call = uncertainWriter.FlushIntent(0, @"C:\source\file", Before);
        Assert.True(call.TryAdmit());
        uncertainWriter.FlushResult(call, new(Before, null, StorageEffect.Uncertain,
            new(StorageReasonKind.Unknown, "unknown", "Postcondition unreadable.", true)) { NativeCallAttempted = true });
        uncertainWriter.Finish(Summary(header, new(Uncertain: 1), uncertain: true) with { Canceled = true });
        read = Read(uncertain); Assert.True(read.Collector.ReviewPending); Assert.True(read.Collector.EffectUncertain); Assert.False(read.Collector.Ended);
        Assert.Equal(JobState.Failed, read.Collector.ToSnapshot(read.Collector.Summary!).FinalState());
    }

    [Fact]
    public void Result_flush_failure_retains_pending_review_even_when_all_bytes_later_become_readable()
    {
        using var data = new MemoryStream(); var header = Header(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () => { if (++flushes == 3) throw new IOException("Disk flush fixture."); }, header);
        var call = writer.FlushIntent(0, @"C:\source\file", Before);
        Assert.True(call.TryAdmit());
        var prior = writer.Prefix;
        Assert.Throws<IOException>(() => writer.FlushResult(call, Changed()));
        Assert.Same(prior, writer.Prefix); Assert.True(writer.NeedsRecovery); Assert.False(call.IsSettled);
        Assert.NotNull(writer.Collector.Pending); Assert.Null(writer.Collector.LastOutcome);
        Assert.Throws<IOException>(() => writer.Finish(Summary(header, new(Changed: 1))));
        var read = Read(data); Assert.True(read.Collector.ReviewPending); Assert.False(read.Collector.Ended);
    }

    [Fact]
    public async Task A_result_remains_pending_until_its_flush_has_actually_returned()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var data = new MemoryStream(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () =>
        {
            if (Interlocked.Increment(ref flushes) == 3) { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); }
        }, Header());
        var call = writer.FlushIntent(0, @"C:\source\file", Before); Assert.True(call.TryAdmit()); var prefix = writer.Prefix;
        var flushing = Task.Run(() => writer.FlushResult(call, Changed()), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.NotNull(writer.Collector.Pending); Assert.Null(writer.Collector.LastOutcome); Assert.Same(prefix, writer.Prefix); Assert.False(call.IsSettled);
        }
        finally { release.Set(); }
        await flushing;
        Assert.Null(writer.Collector.Pending); Assert.NotNull(writer.Collector.LastOutcome); Assert.True(call.IsSettled);
    }

    [Fact]
    public void Torn_unknown_duplicate_reordered_and_identity_mismatched_records_fail_closed()
    {
        var header = Header(); byte[] begin = StorageTransformJournalWriter.Encode("storage-begin", 1, header.JobId, header);
        foreach (byte[] tail in new[]
        {
            begin, // second begin/out-of-order sequence
            Line("{\"t\":\"storage-end\",\"v\":99,\"q\":2,\"job\":\"" + header.JobId + "\",\"data\":{}}"),
            Line("{\"t\":\"storage-end\",\"t\":\"storage-begin\",\"v\":1,\"q\":2,\"job\":\"" + header.JobId + "\",\"data\":{}}"),
            Line("{\"t\":\"storage-result\",\"v\":1,\"q\":2,\"job\":\"" + header.JobId + "\",\"data\":{}}"),
            Encoding.UTF8.GetBytes("00000000 {\"t\":\"storage-intent\"}"),
            Encoding.UTF8.GetBytes("00000000 {\"t\":\"storage-intent\"}\n"),
        })
        {
            using var bytes = new MemoryStream(); bytes.Write(begin); bytes.Write(tail);
            Assert.True(Read(bytes).Collector.ReviewPending); Assert.True(Read(bytes).Collector.Damaged);
        }
        using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        var call = writer.FlushIntent(0, @"C:\source\file", Before);
        Assert.True(call.TryAdmit());
        var wrong = Changed() with { After = Before with { Identity = new(11, "replacement"), Compression = StorageObservationState.Enabled } };
        Assert.Throws<IOException>(() => writer.FlushResult(call, wrong)); Assert.True(writer.NeedsRecovery);
        var nullRoot = JsonNode.Parse(Encoding.UTF8.GetString(begin.AsSpan(9, begin.Length - 10)))!;
        nullRoot["data"]!["roots"] = new JsonArray { null };
        using var nullBytes = new MemoryStream(Line(nullRoot.ToJsonString()));
        Assert.True(StorageTransformJournalReader.Read(nullBytes, ct: TestContext.Current.CancellationToken).Collector.Damaged);
    }

    [Fact]
    public void Record_limit_reserves_fixed_closure_and_each_call_atomically_while_noops_have_no_per_object_records()
    {
        using var data = new MemoryStream(); var header = Header();
        using var writer = new StorageTransformJournalWriter(data, () => { }, header, recordLimit: 10);
        var call = writer.FlushIntent(0, @"C:\source\file", Before); Assert.True(call.TryAdmit()); writer.FlushResult(call, Changed());
        long before = data.Length;
        Assert.Throws<StorageJournalLimitException>(() => writer.FlushIntent(0, @"C:\source\file", Before)); Assert.Equal(before, data.Length);
        writer.RootSummary(new(0, StorageJournalCounts.From(new(Changed: 1)), StorageCompleteness.Complete, true));
        writer.Finish(Summary(header, new(Changed: 1))); Assert.False(Read(data).Collector.ReviewPending);
        header = header with { Scope = StorageScope.ExistingDescendants, Roots = [header.Roots[0] with { Observation = Before with { IsDirectory = true } }] };
        using var noop = new MemoryStream(); using var noopWriter = new StorageTransformJournalWriter(noop, () => { }, header, recordLimit: 6);
        noopWriter.RootSummary(new(0, StorageJournalCounts.From(new(AlreadySatisfied: 1_000_000)), StorageCompleteness.Complete, false));
        noopWriter.Finish(Summary(header, new(AlreadySatisfied: 1_000_000)));
        Assert.Equal(4, noopWriter.Prefix!.Sequence); Assert.False(Read(noop).Collector.ReviewPending);
    }

    [Fact]
    public async Task Published_prefix_is_exact_and_can_be_read_while_later_intent_flush_is_blocked()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var data = new MemoryStream(); var header = Header(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () =>
        {
            if (Interlocked.Increment(ref flushes) == 2) { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); }
        }, header);
        var prefix = writer.Prefix!;
        var pending = Task.Run(() => writer.FlushIntent(0, @"C:\source\file", Before), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Same(prefix, writer.Prefix);
            using var snapshot = new MemoryStream(data.ToArray());
            var read = StorageTransformJournalReader.Read(snapshot, prefix.JobId, prefix.ByteLength, prefix.Sha256, TestContext.Current.CancellationToken);
            Assert.Equal(prefix, read.Prefix); Assert.False(read.Collector.Damaged); Assert.Null(read.Collector.Pending);
        }
        finally { release.Set(); }
        var call = await pending; writer.FlushNotAttempted(call, StorageNoCallReason.Canceled);
    }

    [Fact]
    public void Exact_record_and_header_limits_refuse_without_truncating_affected_paths()
    {
        var header = Header();
        string large = @"C:\" + new string('\u4E00', 32000);
        var largeRoots = Enumerable.Range(0, 64).Select(i => new StorageJournalRoot(i, i, large, Before)).ToImmutableArray();
        Assert.Throws<IOException>(() => StorageTransformJournalWriter.Encode("storage-begin", 1, header.JobId, header with { Roots = largeRoots }));
        using var tooLong = new MemoryStream(new byte[StorageJournalPolicy.RecordBytes + 1]);
        var read = StorageTransformJournalReader.Read(tooLong, ct: TestContext.Current.CancellationToken);
        Assert.True(read.LimitReached); Assert.True(read.Collector.ReviewPending);
    }

    [Fact]
    public void Generic_recovery_cannot_close_or_reconstruct_storage_evidence_even_with_a_stray_end()
    {
        using var temp = new TempDir(); string path = Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, Guid.NewGuid()));
        File.WriteAllBytes(path, Line("{\"t\":\"end\",\"state\":\"Canceled\"}"));
        var job = new InterruptedJob(path, "Copy", "Untrusted historical kind", DateTime.UnixEpoch, [@"C:\source\file"], null,
            [new(1, JobJournal.RenameViaOp, @"C:\source\file", @"C:\other", @"C:\stage")], [temp.Path], 0, 1);
        byte[] original = File.ReadAllBytes(path);
        Assert.False(JournalRecovery.TryClose(job, "must not close")); Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(JournalRecovery.FindStagedLeftovers(job)); Assert.Empty(JournalRecovery.FindRenameLeftovers(job));
        Assert.Null(JournalRecovery.LoadSources(job)); Assert.False(job.CanContinueTransfer);
        _ = JournalRecovery.Scan(temp.Path); Assert.True(File.Exists(path)); Assert.Equal(original, File.ReadAllBytes(path));
    }
}
