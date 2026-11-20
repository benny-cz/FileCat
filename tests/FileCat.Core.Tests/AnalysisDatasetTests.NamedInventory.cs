using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetTests
{
    [Fact]
    public async Task NamedInventoryStreamsEveryPathAfterProducersCloseAndMapsOnlyInertViewRoots()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory());
        using var capture = Capture(workspace, out var header);
        capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        for (int n = 0; n < 271; n++) capture.Append(Entry($"file-{n:D3}.bin"), 1, "r", "basis", cancellation: Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, Device);
        await using var lease = await dataset.AcquireAsync(Cancellation); capture.Dispose(); workspace.Dispose(); await dataset.DisposeAsync();
        using var output = new AnalysisCatalogTests.MemoryDirectory(); Guid id = Guid.NewGuid();
        var view = new AnalysisSavedView(AnalysisMetric.FileSize, "Table", "name:file-000.bin", "r", "n:1", true, false, AnalysisTableSort.Name, false)
            { ColorDimension = AnalysisColorDimension.ModifiedAge };
        var receipt = await lease.SaveNamedAsync("all.fcdsa", id, view, "output", () => output, Cancellation);
        using var store = new AnalysisPrivateInventoryStore(output); var catalog = new AnalysisSavedCatalog(store);
        using var index = catalog.Read(Cancellation); var entry = Assert.Single(index.Entries);
        Assert.Equal(id, entry.InventoryId); Assert.Equal(receipt.Bytes, entry.Bytes); Assert.Equal(AnalysisSavedCatalog.ViewForInventory(lease.Descriptor, view), entry.View);
        Assert.Equal(lease.Descriptor.Coverage, entry.Coverage); Assert.Equal(lease.Descriptor.Taint, entry.Taint);
        using var loaded = catalog.Load(entry, Cancellation);
        Assert.Equal(272, loaded.Snapshot.Nodes.Length); Assert.Equal(new ulong[] { 1, 2, 272 }, loaded.Snapshot.Nodes.Where(n => n.Id is 1 or 2 or 272).Select(n => n.Id));
        Assert.Equal(271, Assert.Single(loaded.Snapshot.Objects).ObservedAliases.Length); Assert.Equal(AnalysisOrigin.ImportedInventory, loaded.Snapshot.Origin);
        Assert.True(loaded.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        var before = output.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        await Assert.ThrowsAsync<IOException>(() => lease.SaveNamedAsync("ALL.fcdsa", Guid.NewGuid(), view, "output", () => output, Cancellation));
        await Assert.ThrowsAsync<IOException>(() => lease.SaveNamedAsync("new.fcdsa", id, view, "output", () => output, Cancellation));
        Assert.Equal(before.Keys, output.Files.Keys); foreach (var item in before) Assert.Equal(item.Value, output.Files[item.Key]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedInventoryReportsThePublishedReceiptWhenCatalogCompletionFails(bool canceled)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory());
        using var capture = Capture(workspace, out var header); capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, Device); await using var lease = await dataset.AcquireAsync(Cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        using var output = new AnalysisCatalogTests.MemoryDirectory { FailEntry = !canceled, AfterInventory = canceled ? stop.Cancel : null };
        var error = await Assert.ThrowsAsync<AnalysisCatalogUpdateException>(() => lease.SaveNamedAsync("recover.fcdsa", Guid.NewGuid(), AnalysisSavedView.Default, "output", () => output, stop.Token));
        Assert.Equal("recover.fcdsa", error.Receipt.Name);
        using var store = new AnalysisPrivateInventoryStore(output); using var loaded = store.Load(error.Receipt.Name, Cancellation);
        Assert.Equal(error.Receipt.InventoryId, loaded.InventoryId); Assert.Single(loaded.Snapshot.Nodes);
        using var index = new AnalysisSavedCatalog(store).Read(Cancellation); Assert.Empty(index.Entries); Assert.Equal(["recover.fcdsa"], index.UnindexedNames);
    }

    [Fact]
    public async Task NamedInventoryRetainsTheSourceAndWriteLockUntilActualPublicationReturns()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(source);
        using var capture = Capture(workspace, out var header); capture.Append(Entry("source", AnalysisKind.Directory, null), 0, "r", "basis", cancellation: Cancellation);
        await using var dataset = new AnalysisDataset(await Complete(capture, workspace, io, header), io, Device); var lease = await dataset.AcquireAsync(Cancellation);
        capture.Dispose(); workspace.Dispose(); await dataset.DisposeAsync(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var output = new AnalysisCatalogTests.MemoryDirectory { AfterInventory = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(20)); } };
        var saving = lease.SaveNamedAsync("held.fcdsa", Guid.NewGuid(), AnalysisSavedView.Default, "output", () => output, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); stop.Cancel(); var closing = lease.DisposeAsync().AsTask();
            Assert.False(saving.IsCompleted); Assert.False(closing.IsCompleted); Assert.False(source.Closed);
            release.Set(); await Assert.ThrowsAsync<AnalysisCatalogUpdateException>(() => saving); await closing.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); Assert.True(source.Closed);
            output.AfterInventory = null; using var store = new AnalysisPrivateInventoryStore(output);
            new AnalysisSavedCatalog(store).SaveNew("after.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation);
        }
        finally { release.Set(); try { await saving; } catch (AnalysisCatalogUpdateException) { } await lease.DisposeAsync(); }
    }
}
