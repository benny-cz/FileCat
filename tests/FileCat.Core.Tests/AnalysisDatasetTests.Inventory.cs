using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetTests
{
    [Fact]
    public async Task WholeDatasetInventoryKeepsPathIdsAliasesRawNamesAndQualifiedCoverageAfterAllProducersClose()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory());
        using var capture = Capture(workspace, out var header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("first"), 1, "r", "basis", cancellation: Cancellation);
        byte[] raw = [0xff, (byte)'x'];
        capture.Append(Entry(AnalysisRawNames.Decode("unix-bytes", raw)) with { NameEncoding = "unix-bytes", RawName = raw.ToImmutableArray() }, 1, "r", "basis", cancellation: Cancellation);
        capture.Issue("changed-during-observation", 2, MeasurementState.Changed, "r", Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, Device);
        await using var lease = await dataset.AcquireAsync(Cancellation); var descriptor = lease.Descriptor;
        capture.Dispose(); workspace.Dispose(); await dataset.DisposeAsync();
        var output = new InventoryDirectory(); Guid id = Guid.NewGuid();
        var receipt = await lease.SaveNewAsync("whole.fcdsa", id, "inventory-output", () => output, Cancellation);
        Assert.True(output.Closed); Assert.True(output.FileClosed); Assert.Equal(output.Bytes.Length, receipt.Bytes);
        using var imported = AnalysisInventoryFormat.Read(new MemoryStream(output.Bytes), Cancellation);
        Assert.Equal(id, imported.InventoryId); var snapshot = imported.Snapshot;
        Assert.Equal(descriptor.Identity, snapshot.Identity); Assert.Equal(new ulong[] { 1, 2, 3 }, snapshot.Nodes.Select(n => n.Id));
        Assert.Equal(new ulong[] { 0, 1, 1 }, snapshot.Nodes.Select(n => n.ParentId)); Assert.Equal(raw, snapshot.Nodes[2].RawName);
        Assert.Equal(new ulong[] { 2, 3 }, Assert.Single(snapshot.Objects).ObservedAliases); Assert.Single(snapshot.Facts);
        Assert.Equal(AnalysisOrigin.ImportedInventory, snapshot.Origin); Assert.True(snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        Assert.Equal(new AnalysisCoverageQualification(false, 1, true, false), snapshot.CoverageDetails!.Qualification);
        Assert.False(snapshot.CoverageDetails.DenominatorFinal); Assert.Equal(new ulong[] { 2 }, snapshot.CoverageDetails.StaleScopeNodes);
        using var again = new MemoryStream(); AnalysisInventoryFormat.Write(again, snapshot, Guid.NewGuid(), Cancellation); again.Position = 0;
        using var reimported = AnalysisInventoryFormat.Read(again, Cancellation); Assert.Equal(snapshot.CoverageDetails.Qualification, reimported.Snapshot.CoverageDetails!.Qualification);
        foreach (var metric in Enum.GetValues<AnalysisMetric>())
        {
            var expected = await lease.SummarizeAsync(metric, Cancellation); var actual = AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation);
            Assert.Equal(expected.Totals.Known, actual.Totals.Known); Assert.Equal(expected.Totals.MatchingPaths, actual.Totals.MatchingPaths);
        }
    }

    [Theory]
    [InlineData("eligible")]
    [InlineData("unknown-counts")]
    [InlineData("missing-stale")]
    [InlineData("unlocated-complete")]
    [InlineData("extra-field")]
    public async Task QualifiedCoverageContradictionsCannotHideBehindAValidInventoryHash(string fault)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory());
        using var capture = Capture(workspace, out var header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        capture.Append(Entry("first"), 1, "r", "basis", cancellation: Cancellation);
        capture.Issue("changed-during-observation", 2, MeasurementState.Changed, "r", Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, Device); await using var lease = await dataset.AcquireAsync(Cancellation);
        var output = new InventoryDirectory(); await lease.SaveNewAsync("whole.fcdsa", Guid.NewGuid(), "inventory-output", () => output, Cancellation);
        var lines = Encoding.UTF8.GetString(output.Bytes).TrimEnd('\n').Split('\n');
        var row = JsonNode.Parse(lines[^2])!; var q = row["qualification"]!.AsArray();
        switch (fault)
        {
            case "eligible": q[1] = "2"; break;
            case "unknown-counts": row["requested"] = "1"; break;
            case "missing-stale": row["staleNodes"] = new JsonArray(); break;
            case "unlocated-complete": q[3] = true; break;
            default: row["authority"] = true; break;
        }
        lines[^2] = row.ToJsonString(); byte[] prefix = Encoding.UTF8.GetBytes(string.Join('\n', lines[..^1]) + "\n");
        var trailer = JsonNode.Parse(lines[^1])!; trailer["sha256"] = Convert.ToHexStringLower(SHA256.HashData(prefix));
        byte[] changed = [.. prefix, .. Encoding.UTF8.GetBytes(trailer.ToJsonString() + "\n")];
        Assert.Throws<InvalidDataException>(() => AnalysisInventoryFormat.Read(new MemoryStream(changed), Cancellation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveRetainsActualPairedIoThroughCancellationAndDatasetClosure(bool cancel)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(source);
        using var capture = Capture(workspace, out var header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, Device); var lease = await dataset.AcquireAsync(Cancellation);
        capture.Dispose(); workspace.Dispose(); await dataset.DisposeAsync();
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new InventoryDirectory { OnWrite = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); } };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var save = lease.SaveNewAsync("held.fcdsa", Guid.NewGuid(), "inventory-output", () => output, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); if (cancel) stop.Cancel();
            var close = lease.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); Assert.False(save.IsCompleted); Assert.False(output.Closed); Assert.False(source.Closed);
            release.Set();
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save); else await save;
            await close.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); Assert.True(output.Closed); Assert.True(output.FileClosed); Assert.True(source.Closed);
            Assert.Equal(!cancel, output.Published);
        }
        finally { release.Set(); await lease.DisposeAsync(); }
    }

    [Fact]
    public async Task AThirdDeviceIsRefusedBeforeAnyPrivateOutputFactoryIsInvoked()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory());
        using var capture = Capture(workspace, out var header); capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, new(Device, "profile-input"));
        await using var lease = await dataset.AcquireAsync(Cancellation); int calls = 0;
        Assert.Throws<NotSupportedException>(() => { _ = lease.SaveNewAsync("no.fcdsa", Guid.NewGuid(), "third-device", () => { calls++; return new InventoryDirectory(); }, Cancellation); });
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AFailureClosingAnAlreadyPublishedSaveIsExplicitlyUncertainAndNeverRetries()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory());
        using var capture = Capture(workspace, out var header); capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, Device); await using var lease = await dataset.AcquireAsync(Cancellation);
        var output = new InventoryDirectory { FailClose = true }; int calls = 0;
        var error = await Assert.ThrowsAsync<AnalysisPublicationUncertainException>(() => lease.SaveNewAsync("published.fcdsa", Guid.NewGuid(), "inventory-output", () => { calls++; return output; }, Cancellation));
        Assert.Equal("published.fcdsa", error.InventoryName); Assert.Equal(1, calls); Assert.True(output.Published); Assert.True(output.Closed);
        using var imported = AnalysisInventoryFormat.Read(new MemoryStream(output.Bytes), Cancellation); Assert.Single(imported.Snapshot.Nodes);
    }

    [Fact]
    public async Task SuspendedSaveBuffersStayGloballyBoundedUntilCanceledActualWritesReturn()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var release = new ManualResetEventSlim(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var leases = new List<AnalysisDatasetLease>(); var outputs = new List<InventoryDirectory>(); var saves = new List<Task<AnalysisInventoryReceipt>>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int writing = 0, thirdFactory = 0;
        try
        {
            for (int i = 0; i < 3; i++)
            {
                using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var capture = Capture(workspace, out var header);
                capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
                await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, "inventory-input-" + i);
                leases.Add(await dataset.AcquireAsync(Cancellation));
                outputs.Add(new InventoryDirectory { OnWrite = i < 2 ? () => { if (Interlocked.Increment(ref writing) == 2) entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); } : null });
            }
            for (int i = 0; i < 2; i++)
            { int n = i; saves.Add(leases[n].SaveNewAsync("slot.fcdsa", Guid.NewGuid(), "inventory-output-" + n, () => outputs[n], n == 0 ? stop.Token : Cancellation)); }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            saves.Add(leases[2].SaveNewAsync("slot.fcdsa", Guid.NewGuid(), "inventory-output-2", () => { Interlocked.Increment(ref thirdFactory); return outputs[2]; }, Cancellation));
            stop.Cancel(); Assert.False(saves[0].IsCompleted); Assert.False(saves[2].IsCompleted); Assert.Equal(0, Volatile.Read(ref thirdFactory));
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saves[0]);
            await Task.WhenAll(saves.Skip(1)); Assert.Equal(1, thirdFactory); Assert.All(outputs, output => Assert.True(output.Closed));
        }
        finally
        {
            release.Set();
            foreach (var save in saves) try { await save; } catch (OperationCanceledException) { }
            foreach (var lease in leases) await lease.DisposeAsync();
        }
    }

    private sealed class InventoryDirectory : AnalysisPrivateDirectory
    {
        internal bool Closed, FileClosed, Published, FailClose; internal byte[] Bytes = []; internal Action? OnWrite;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateFile CreateStaging() => new InventoryFile(this);
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() => Closed = true;
        private sealed class InventoryFile(InventoryDirectory owner) : AnalysisPrivateFile
        {
            private readonly Output _stream = new(owner);
            public override Stream Stream => _stream;
            public override void Verify() => owner.Verify();
            public override void Flush() { }
            public override void PublishNew(string name) { owner.Bytes = _stream.ToArray(); owner.Published = true; }
            public override void Dispose() { owner.FileClosed = true; _stream.Dispose(); if (owner.FailClose) throw new IOException("injected close failure"); }
        }
        private sealed class Output(InventoryDirectory owner) : MemoryStream
        {
            public override void Write(ReadOnlySpan<byte> bytes) { owner.OnWrite?.Invoke(); base.Write(bytes); }
        }
    }
}
