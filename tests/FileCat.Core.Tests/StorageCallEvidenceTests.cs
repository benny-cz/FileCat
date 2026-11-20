using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed partial class StorageCallEvidenceTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private static readonly StorageObservation Before = new(StorageObservationState.Disabled, StorageObservationState.Disabled,
        new(11, "child") { VolumeBinding = "test-volume" }, false, null);

    private static (StorageTransformJournalWriter Writer, string Path) Create(TempDir temp, MemoryStream data)
    {
        Guid id = Guid.NewGuid();
        var header = new StorageJournalHeader(id, DateTime.UnixEpoch, StorageAction.Compress, StorageScope.ExistingDescendants,
            Guid.NewGuid(), [new(0, 0, @"C:\source", Before with { IsDirectory = true })]);
        return (new(data, () => { }, header), System.IO.Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, id)));
    }
    private static StorageJournalPrefix Prefix(StorageTransformJournalWriter writer) => Assert.IsType<StorageJournalPrefix>(writer.Prefix);
    private static void Call(StorageTransformJournalWriter writer, int i, string? path = null)
    {
        var call = writer.FlushIntent(0, path ?? @"C:\source\child-" + i, Before);
        if ((i & 1) == 0) writer.FlushNotAttempted(call, StorageNoCallReason.Paused);
        else
        {
            Assert.True(call.TryAdmit());
            writer.FlushResult(call, new(Before, Before with { Compression = StorageObservationState.Enabled }, StorageEffect.Changed, null)
                { NativeCallAttempted = true });
        }
    }

    [Fact]
    public void Pages_cover_exact_calls_and_pending_intent_from_only_the_captured_prefix_despite_later_appends()
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer;
        for (int i = 1; i <= 130; i++) Call(writer, i);
        var pending = writer.FlushIntent(0, @"C:\source\pending", Before); var prefix = Prefix(writer);
        writer.FlushNotAttempted(pending, StorageNoCallReason.Canceled); Call(writer, 132);
        File.WriteAllBytes(fixture.Path, data.ToArray());
        var first = JournalRecovery.ReadStorageCalls(fixture.Path, prefix, ct: Stop);
        Assert.Equal(64, first.Calls.Length); Assert.True(first.More); Assert.Equal(64, first.NextAfterCall);
        var second = JournalRecovery.ReadStorageCalls(fixture.Path, prefix, first.NextAfterCall, Stop);
        Assert.Equal(64, second.Calls.Length); Assert.True(second.More);
        var third = JournalRecovery.ReadStorageCalls(fixture.Path, prefix, second.NextAfterCall, Stop);
        Assert.False(third.More); Assert.Equal(3, third.Calls.Length);
        var all = first.Calls.Concat(second.Calls).Concat(third.Calls).ToArray();
        Assert.Equal(Enumerable.Range(1, 131).Select(i => (long)i), all.Select(c => c.Call));
        Assert.Equal(StorageEffect.Changed, all[0].Result!.Effect); Assert.True(all[0].Result!.NativeCallAttempted);
        Assert.Equal(StorageCallDisposition.NotAttempted, all[1].Disposition); Assert.Equal(StorageNoCallReason.Paused, all[1].NoCallReason);
        Assert.Null(all[^1].Disposition); Assert.Null(all[^1].Result); Assert.Equal(@"C:\source\pending", all[^1].Path);
        Assert.All(new[] { first, second, third }, page => Assert.Equal(prefix, page.Prefix));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stop); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using (StorageEvidenceLanes.EnterEnvelope(deadline.Token)) { } // no owner survives a page or user wait
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("short")]
    [InlineData("digest")]
    [InlineData("sequence")]
    public void Changed_or_damaged_prefix_never_publishes_tentative_rows(string damage)
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer; for (int i = 1; i <= 70; i++) Call(writer, i);
        var prefix = Prefix(writer); byte[] bytes = data.ToArray();
        switch (damage)
        {
            case "bytes": bytes[^12] ^= 1; break; // after the first tentative 64 rows
            case "short": bytes = bytes[..^10]; break;
            case "digest": prefix = prefix with { Sha256 = new string('0', 64) }; break;
            case "sequence": prefix = prefix with { Sequence = prefix.Sequence + 1 }; break;
        }
        File.WriteAllBytes(fixture.Path, bytes);
        Assert.Throws<IOException>(() => JournalRecovery.ReadStorageCalls(fixture.Path, prefix, ct: Stop));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stop); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using (StorageEvidenceLanes.EnterEnvelope(deadline.Token)) { }
    }

    [Fact]
    public void Retained_character_budget_pages_long_paths_without_omissions_or_a_zero_progress_page()
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer;
        for (int i = 1; i <= 100; i++) Call(writer, i, @"C:\source\" + new string('x', 12_000) + i);
        File.WriteAllBytes(fixture.Path, data.ToArray()); long after = 0; int total = 0, pages = 0;
        while (true)
        {
            var page = JournalRecovery.ReadStorageCalls(fixture.Path, Prefix(writer), after, Stop); pages++;
            Assert.NotEmpty(page.Calls); Assert.InRange(page.Calls.Length, 1, 63);
            Assert.InRange(page.RetainedBytes, 1, JournalRecovery.StorageCallPageBytes);
            Assert.True(page.NextAfterCall > after); total += page.Calls.Length; after = page.NextAfterCall;
            if (!page.More) break;
        }
        Assert.Equal(100, total); Assert.True(pages >= 3);
    }

    [Fact]
    public void Invalid_continuations_cancellation_and_pruned_evidence_are_explicit_failures()
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer; Call(writer, 1); File.WriteAllBytes(fixture.Path, data.ToArray());
        Assert.Throws<ArgumentException>(() => JournalRecovery.ReadStorageCalls(fixture.Path, Prefix(writer), -1, Stop));
        Assert.Throws<IOException>(() => JournalRecovery.ReadStorageCalls(fixture.Path, Prefix(writer), 2, Stop));
        Assert.Throws<ArgumentException>(() => JournalRecovery.ReadStorageCalls(fixture.Path, Prefix(writer) with { JobId = Guid.NewGuid() }, ct: Stop));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => JournalRecovery.ReadStorageCalls(fixture.Path, Prefix(writer), ct: cancel.Token));
        File.Delete(fixture.Path);
        Assert.Throws<FileNotFoundException>(() => JournalRecovery.ReadStorageCalls(fixture.Path, Prefix(writer), ct: Stop));
    }

    [Fact]
    public void Export_copies_the_entire_captured_prefix_including_pending_intent_and_excludes_later_appends()
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer; for (int i = 1; i <= 130; i++) Call(writer, i);
        var pending = writer.FlushIntent(0, @"C:\source\pending", Before); var prefix = Prefix(writer);
        byte[] expected = data.ToArray();
        writer.FlushNotAttempted(pending, StorageNoCallReason.Canceled); Call(writer, 132);
        File.WriteAllBytes(fixture.Path, data.ToArray());
        using var output = new MemoryStream();
        JournalRecovery.CopyStorageCalls(fixture.Path, prefix, () => output, Stop);
        Assert.Equal(expected, output.ToArray());
        Assert.Equal(prefix.ByteLength, output.ToArray().LongLength);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stop); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using (StorageEvidenceLanes.EnterEnvelope(deadline.Token)) { }
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("short")]
    [InlineData("digest")]
    [InlineData("sequence")]
    public void Export_validates_every_record_and_digest_before_creating_any_output(string damage)
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer; for (int i = 1; i <= 70; i++) Call(writer, i);
        var prefix = Prefix(writer); byte[] bytes = data.ToArray();
        switch (damage)
        {
            case "bytes": bytes[^12] ^= 1; break;
            case "short": bytes = bytes[..^10]; break;
            case "digest": prefix = prefix with { Sha256 = new string('0', 64) }; break;
            case "sequence": prefix = prefix with { Sequence = prefix.Sequence + 1 }; break;
        }
        File.WriteAllBytes(fixture.Path, bytes); bool created = false;
        Assert.Throws<IOException>(() => JournalRecovery.CopyStorageCalls(fixture.Path, prefix, () => { created = true; return new MemoryStream(); }, Stop));
        Assert.False(created);
    }

    [Fact]
    public void Export_rechecks_copied_bytes_when_source_changes_after_validation()
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer;
        for (int i = 1; i <= 100; i++) Call(writer, i, @"C:\source\" + new string('x', 1200) + i);
        var prefix = Prefix(writer); File.WriteAllBytes(fixture.Path, data.ToArray());
        Assert.True(prefix.ByteLength > 2 * 64 * 1024);
        using var output = new MemoryStream();
        Assert.Throws<IOException>(() => JournalRecovery.CopyStorageCalls(fixture.Path, prefix, () =>
        {
            using var competing = new FileStream(fixture.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            competing.WriteByte((byte)'!'); competing.Flush(true); return output;
        }, Stop));
        Assert.Equal(prefix.ByteLength, output.ToArray().LongLength); // Tentative staging bytes must not be published.
    }

    [Fact]
    public void Export_cancellation_after_factory_creation_closes_output_and_releases_its_envelope()
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer; Call(writer, 1); File.WriteAllBytes(fixture.Path, data.ToArray());
        using var stop = new CancellationTokenSource(); using var output = new MemoryStream();
        Assert.ThrowsAny<OperationCanceledException>(() => JournalRecovery.CopyStorageCalls(fixture.Path, Prefix(writer), () =>
        { stop.Cancel(); return output; }, stop.Token));
        Assert.Empty(output.ToArray()); Assert.False(output.CanWrite);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stop); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using (StorageEvidenceLanes.EnterEnvelope(deadline.Token)) { }
    }
}
