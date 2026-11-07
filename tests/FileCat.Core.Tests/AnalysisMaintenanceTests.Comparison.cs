using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisMaintenanceTests
{
    private static Guid SaveComparison(Directory directory, string name = "pair.fcdsc", AnalysisSnapshot? previous = null)
    {
        using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation);
        Guid id = Guid.NewGuid(); store.SaveComparisonNew(name, id, previous ?? AnalysisAccountingTests.Golden(), AnalysisAccountingTests.Golden(), new(), null, "Previous", Cancellation); return id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComparisonDeletionRemovesOnlyTheReviewedBundleWithoutAllocatingMoreFiles(bool lowSpace)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); Save(directory); Guid id = SaveComparison(directory); SaveComparison(directory, "other.fcdsc");
        byte[] bytes = directory.Files["pair.fcdsc"].Bytes.ToArray(); var untouched = directory.Files.Where(p => p.Key != "pair.fcdsc").ToArray();
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "comparison-delete", () => directory, Cancellation);
        var review = await owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation);
        Assert.Equal(id, review.ComparisonId); Assert.Equal(bytes.Length, review.Bytes); Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), review.Sha256);
        int publications = directory.Publications; directory.LowSpace = lowSpace;
        var outcome = await owned.DeleteComparisonAsync(review, Cancellation);
        Assert.Equal(OwnedAnalysisDeletionState.Deleted, outcome.State); Assert.True(outcome.RemovalConfirmed); Assert.False(directory.Files.ContainsKey("pair.fcdsc"));
        Assert.Equal(publications, directory.Publications); foreach (var file in untouched) Assert.Same(file.Value, directory.Files[file.Key]);
        Assert.Throws<InvalidOperationException>(() => { _ = owned.DeleteComparisonAsync(review, Cancellation); }); Assert.Equal(0, directory.Leases);
        directory.LowSpace = false; Assert.NotEqual(id, SaveComparison(directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComparisonMaintenanceRefusesActiveReadersDuringPreparationAndExecution(bool execute)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); SaveComparison(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "comparison-reader", () => directory, Cancellation);
        var review = execute ? await owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation) : null;
        using var reader = directory.OpenRead("pair.fcdsc"); byte[] before = directory.Files["pair.fcdsc"].Bytes.ToArray();
        if (execute)
            Assert.Equal(OwnedAnalysisDeletionState.Kept, (await Assert.ThrowsAsync<OwnedAnalysisComparisonDeletionException>(() => owned.DeleteComparisonAsync(review!, Cancellation))).Outcome.State);
        else await Assert.ThrowsAsync<IOException>(() => owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation));
        Assert.Equal(before, directory.Files["pair.fcdsc"].Bytes); reader.Dispose();
        await owned.DeleteComparisonAsync(await owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation), Cancellation); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("valid-bytes")]
    [InlineData("profile")]
    public async Task ComparisonReviewRefusesChangedNativeObjectValidBytesOrProfile(string change)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); SaveComparison(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "comparison-stale", () => directory, Cancellation);
        var review = await owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation);
        if (change == "object") directory.Files["pair.fcdsc"] = new(directory.Files["pair.fcdsc"].Bytes.ToArray());
        else if (change == "valid-bytes") directory.Files["pair.fcdsc"].Bytes = ChangedHeader(directory.Files["pair.fcdsc"].Bytes, h => h["previousLabel"] = "Changed!");
        else directory.Files[AnalysisPrivateNames.PrivateState].Bytes = Encoding.UTF8.GetBytes("{\"schema\":\"1\",\"kind\":\"analysis-private-state\",\"profile\":\"" + Guid.NewGuid().ToString("D") + "\"}");
        byte[] changed = directory.Files["pair.fcdsc"].Bytes.ToArray();
        var failure = await Assert.ThrowsAsync<OwnedAnalysisComparisonDeletionException>(() => owned.DeleteComparisonAsync(review, Cancellation));
        Assert.Equal(OwnedAnalysisDeletionState.Kept, failure.Outcome.State); Assert.Equal(changed, directory.Files["pair.fcdsc"].Bytes); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData("outer-future")]
    [InlineData("inner-future")]
    [InlineData("inner-checksum")]
    [InlineData("outer-checksum")]
    [InlineData("truncated")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    public async Task ComparisonMaintenanceRefusesUnsupportedOrCorruptContainersWithoutImportingOrRepairingThem(string change)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); SaveComparison(directory);
        byte[] original = directory.Files["pair.fcdsc"].Bytes; string text = Encoding.UTF8.GetString(original); int headerEnd = text.IndexOf('\n') + 1;
        byte[] changed;
        if (change == "outer-future") changed = ChangedHeader(original, h => h["schema"] = "9");
        else if (change == "truncated") changed = original[..^1];
        else if (change == "extra") changed = [.. original, (byte)'\n'];
        else if (change == "duplicate") changed = Encoding.UTF8.GetBytes("{\"schema\":\"1\"," + text[1..]);
        else
        {
            int at = change == "inner-future" ? text.IndexOf("\"schema\":\"1\"", headerEnd, StringComparison.Ordinal) + 10
                : (change == "outer-checksum" ? text.LastIndexOf("\"sha256\":\"", StringComparison.Ordinal) : text.IndexOf("\"sha256\":\"", StringComparison.Ordinal)) + 10;
            Assert.True(at >= 10); var chars = text.ToCharArray(); chars[at] = chars[at] == '9' ? '8' : '9'; changed = Encoding.UTF8.GetBytes(chars);
            if (change != "outer-checksum") changed = WithOuterChecksum(changed);
        }
        directory.Files["pair.fcdsc"].Bytes = changed;
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "comparison-corrupt", () => directory, Cancellation);
        await Assert.ThrowsAsync<InvalidDataException>(() => owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation));
        Assert.Equal(changed, directory.Files["pair.fcdsc"].Bytes); Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task ComparisonReviewStreamsLargeInputsAndCannotHideTheirPrivilegedTaintInTheOuterHeader()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler();
        var snapshot = AnalysisAccountingTests.Golden(); snapshot = snapshot with { Taint = snapshot.Taint | AnalysisTaint.PrivilegedDerived,
            Nodes = snapshot.Nodes.AddRange(Enumerable.Range(0, 1800).Select(i => new AnalysisNode((ulong)i + 7, "r", 1, "extra-" + i + new string('x', 160), AnalysisKind.File, "X"))) };
        SaveComparison(directory, previous: snapshot);
        directory.Files["pair.fcdsc"].Bytes = ChangedHeader(directory.Files["pair.fcdsc"].Bytes, h => h["taint"] = ((int)AnalysisTaint.OrdinarySensitive).ToString());
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "comparison-stream", () => directory, Cancellation);
        var review = await owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation);
        Assert.True(review.Taint.HasFlag(AnalysisTaint.PrivilegedDerived)); Assert.True(review.Bytes > AnalysisLimits.BatchBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(directory.Files["pair.fcdsc"].Bytes)), review.Sha256); Assert.InRange(directory.LargestRead, 1, 64 * 1024); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComparisonRemovalReportsPreUnlinkRefusalAndPostUnlinkUncertainty(bool after)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); SaveComparison(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "comparison-fault", () => directory, Cancellation);
        var review = await owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation);
        if (after) directory.AfterRemove = _ => throw new IOException("Post unlink"); else directory.BeforeRemove = _ => throw new IOException("Before unlink");
        var failure = await Assert.ThrowsAsync<OwnedAnalysisComparisonDeletionException>(() => owned.DeleteComparisonAsync(review, Cancellation));
        Assert.Equal(after ? OwnedAnalysisDeletionState.Uncertain : OwnedAnalysisDeletionState.Kept, failure.Outcome.State);
        Assert.Equal(!after, directory.Files.ContainsKey("pair.fcdsc")); Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task ComparisonReviewCannotBeReboundAndCancellationWaitsForTheActualReadAndOwnerClose()
    {
        Ordinary(); using var directory = new Directory(); using var other = new Directory(); using var io = new DeviceIoScheduler(); SaveComparison(directory); SaveComparison(other);
        var owned = await OwnedAnalysisStore.OpenAsync(io, "comparison-lifetime", () => directory, Cancellation);
        await using var second = await OwnedAnalysisStore.OpenAsync(io, "comparison-other", () => other, Cancellation);
        var review = await owned.PrepareComparisonDeleteAsync("pair.fcdsc", Cancellation);
        Assert.Throws<ArgumentException>(() => { _ = second.DeleteComparisonAsync(review, Cancellation); });
        await owned.DeleteComparisonAsync(review, Cancellation); SaveComparison(directory);
        using var release = new ManualResetEventSlim(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.BeforeRead = name => { if (name != "pair.fcdsc") return; directory.BeforeRead = null; entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(20)); };
        var reading = owned.PrepareComparisonDeleteAsync("pair.fcdsc", stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); stop.Cancel(); var closing = owned.DisposeAsync().AsTask();
            Assert.False(reading.IsCompleted); Assert.False(closing.IsCompleted); Assert.True(directory.Leases > 0);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading); await closing.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.Equal(0, directory.Leases); Assert.True(directory.Files.ContainsKey("pair.fcdsc"));
        }
        finally { release.Set(); try { await reading; } catch (OperationCanceledException) { } await owned.DisposeAsync(); }
    }

    [Theory]
    [InlineData("../pair.fcdsc")]
    [InlineData("C:\\pair.fcdsc")]
    [InlineData("private-state.json")]
    [InlineData("pair.fcdsa")]
    public async Task ComparisonMaintenanceCannotSelectPathsControlsOrAnOrdinaryInventory(string name)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); SaveComparison(directory);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "comparison-namespace", () => directory, Cancellation);
        var original = directory.Files.ToArray();
        Assert.Throws<ArgumentException>(() => { _ = owned.PrepareComparisonDeleteAsync(name, Cancellation); });
        await Assert.ThrowsAsync<FileNotFoundException>(() => owned.PrepareComparisonDeleteAsync("absent.fcdsc", Cancellation));
        Assert.Equal(original, directory.Files.ToArray()); Assert.Equal(0, directory.Leases);
    }

    private static byte[] ChangedHeader(byte[] bytes, Action<JsonObject> change)
    {
        int end = Array.IndexOf(bytes, (byte)'\n') + 1; var header = JsonNode.Parse(bytes.AsSpan(0, end))!.AsObject(); change(header);
        return WithOuterChecksum([.. Encoding.UTF8.GetBytes(header.ToJsonString() + "\n"), .. bytes.AsSpan(end).ToArray()]);
    }
    private static byte[] WithOuterChecksum(byte[] bytes)
    {
        int previousLf = bytes.AsSpan(0, bytes.Length - 1).LastIndexOf((byte)'\n'); byte[] prefix = bytes[..(previousLf + 1)];
        return [.. prefix, .. Encoding.UTF8.GetBytes("{\"kind\":\"comparison-trailer\",\"sha256\":\"" + Convert.ToHexStringLower(SHA256.HashData(prefix)) + "\"}\n")];
    }
}
