using System.Security.Cryptography;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisMaintenanceTests
{
    [Fact]
    public async Task PreparedSnapshotRemainsAnonymousAndImmutableUntilAFreshPublicationDecision()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler();
        using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var devices = new AnalysisIoDevices("prepared-snapshot"); Guid id = Guid.NewGuid(); string name = AnalysisPrivateNames.ReplacementIncoming(id, Guid.NewGuid());
        var snapshot = AnalysisAccountingTests.Golden(); using var expected = new MemoryStream(); AnalysisInventoryFormat.Write(expected, snapshot, id, Cancellation);
        var output = await AnalysisInventoryFormat.PrepareSnapshotReplacementAsync(snapshot, io, devices, store.BorrowDirectory, name, id, preparation.Token);
        try
        {
            Assert.Empty(directory.Files); Assert.True(directory.Leases > 0); preparation.Cancel();
            Assert.Throws<InvalidOperationException>(() => { _ = output.Stream; });
            var inspected = await AnalysisInventoryFormat.InspectContainerAsync(output.PreparedStream, output.Verify, io, devices, Cancellation);
            Assert.Equal(id, inspected.Id); Assert.Equal(expected.Length, inspected.Bytes); Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(expected.ToArray())), inspected.Sha256);
            var receipt = await AnalysisIo.Run(io, devices, _ => output.PublishPrepared(id, Cancellation), Cancellation);
            Assert.Equal(id, receipt.InventoryId); Assert.Equal(expected.ToArray(), directory.Files[name].Bytes);
            await AnalysisIo.Run(io, devices, _ =>
            { Assert.Throws<InvalidOperationException>(() => output.PublishPrepared(id, Cancellation)); return true; }, Cancellation);
        }
        finally { await AnalysisIo.ReleaseScratchAsync(io, devices, output); }
        Assert.Equal(0, directory.Leases);
    }

    [Fact]
    public async Task DiscardingPreparedSnapshotClosesTheAnonymousOutputWithoutPublishing()
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
        var devices = new AnalysisIoDevices("discard-prepared"); Guid id = Guid.NewGuid();
        var output = await AnalysisInventoryFormat.PrepareSnapshotReplacementAsync(AnalysisAccountingTests.Golden(), io, devices,
            store.BorrowDirectory, AnalysisPrivateNames.ReplacementIncoming(id, Guid.NewGuid()), id, Cancellation);
        await AnalysisIo.ReleaseScratchAsync(io, devices, output);
        Assert.Empty(directory.Files); Assert.Equal(0, directory.Leases); Assert.Throws<ObjectDisposedException>(() => { _ = output.PreparedStream; });
    }

    [Fact]
    public async Task PreparedWriterSpaceFailureReleasesItsBoundedProducerAndActualStagingLease()
    {
        Ordinary(); using var directory = new Directory { LowSpace = true }; using var io = new DeviceIoScheduler(); using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
        var snapshot = AnalysisAccountingTests.Golden(); snapshot = snapshot with
        { Nodes = snapshot.Nodes.AddRange(Enumerable.Range(0, 1800).Select(i => new AnalysisNode((ulong)i + 7, "r", 1, "extra-" + i + new string('x', 160), AnalysisKind.File, "X"))) };
        Guid id = Guid.NewGuid(); var devices = new AnalysisIoDevices("prepared-space");
        await Assert.ThrowsAsync<IOException>(() => AnalysisInventoryFormat.PrepareSnapshotReplacementAsync(snapshot, io, devices,
            store.BorrowDirectory, AnalysisPrivateNames.ReplacementIncoming(id, Guid.NewGuid()), id, Cancellation));
        Assert.Empty(directory.Files); Assert.Equal(0, directory.Leases);
        directory.LowSpace = false;
        var output = await AnalysisInventoryFormat.PrepareSnapshotReplacementAsync(AnalysisAccountingTests.Golden(), io, devices,
            store.BorrowDirectory, AnalysisPrivateNames.ReplacementIncoming(id, Guid.NewGuid()), id, Cancellation);
        await AnalysisIo.ReleaseScratchAsync(io, devices, output); Assert.Equal(0, directory.Leases);
    }

    [Theory]
    [InlineData("ordinary.fcdsa")]
    [InlineData("private-state.json")]
    [InlineData("../outside.payload")]
    [InlineData("incoming-not-generated.payload")]
    public async Task PreparedWriterCannotPublishIntoTheUserInventoryOrControlNamespace(string name)
    {
        Ordinary(); using var directory = new Directory(); using var io = new DeviceIoScheduler(); using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
        var devices = new AnalysisIoDevices("prepared-namespace");
        await Assert.ThrowsAsync<ArgumentException>(() => AnalysisInventoryFormat.PrepareSnapshotReplacementAsync(AnalysisAccountingTests.Golden(), io, devices,
            store.BorrowDirectory, name, Guid.NewGuid(), Cancellation));
        Assert.Empty(directory.Files); Assert.Equal(0, directory.Leases);
    }
}
