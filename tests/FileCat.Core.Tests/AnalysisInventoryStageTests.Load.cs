using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisInventoryStageTests
{
    private static AnalysisSavedEntry Reviewed(byte[] bytes) => new(
        Guid.Parse((string)JsonNode.Parse(Encoding.UTF8.GetString(bytes).Split('\n')[0])!["inventory"]!),
        "source.fcdsa", bytes.LongLength, DateTimeOffset.UnixEpoch, NamespaceCoverage.CompleteUnderPolicy,
        AnalysisTaint.OrdinarySensitive, AnalysisSavedView.Default);

    [Fact]
    public async Task PublicIndexedLoaderRejectsBadSelectionAndThirdDeviceBeforeAnyNativeFactory()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); int factories = 0;
        AnalysisPrivateDirectory Directory() { factories++; return new MemoryDirectory(); }
        AnalysisProfileQuota Quota() { factories++; throw new IOException("This factory must remain uncalled."); }
        var expected = Reviewed(Encode(OpaqueIds())) with { Bytes = 0 };
        await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisIndexedInventory.LoadPrivateAsync(io, "a", "a", "source.fcdsa", Directory, Quota, Directory, expected, Cancellation));
        await Assert.ThrowsAsync<NotSupportedException>(() => AnalysisIndexedInventory.LoadPrivateAsync(io, new("a", "b"), "c", "source.fcdsa", Directory, Quota, Directory, cancellation: Cancellation));
        Assert.Equal(0, factories);
    }

    [Fact]
    public async Task MissingProfileCapacityCannotSilentlyCreateAnUncoordinatedImportWorkspace()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); int inputs = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisIndexedInventory.LoadPrivateAsync(io, "a", "a", "source.fcdsa", () => scratch,
            () => null!, () => { inputs++; return new MemoryDirectory(); }, cancellation: Cancellation));
        Assert.True(scratch.Closed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, inputs);
    }

    [Fact]
    public async Task CatalogAwareIndexedLoadClosesStagingAndRetainsOriginalMetadataAfterItsOwnerCloses()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        byte[] bytes = Encode(OpaqueIds()); var expected = Reviewed(bytes); var input = new MemoryDirectory { Input = bytes };
        await using var inventory = await AnalysisInventoryFormat.LoadPrivateIndexedAsync(workspace, io, "stage-output", "stage-input", expected.Name, () => input, expected, Cancellation);
        Assert.Equal(expected.InventoryId, inventory.InventoryId); Assert.Equal(bytes.LongLength, inventory.SourceBytes); Assert.True(input.Closed); Assert.True(input.InputClosed);
        Assert.Equal(AnalysisOrigin.ImportedInventory, inventory.Descriptor.Origin); Assert.True(inventory.Descriptor.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        await using var lease = await inventory.AcquireAsync(Cancellation); await inventory.DisposeAsync(); workspace.Dispose(); Assert.False(scratch.Closed);
        Assert.Equal(ulong.MaxValue, (await lease.ReadPathAsync(ulong.MaxValue, Cancellation)).Id);
        Assert.Equal(2UL, (await lease.ReadPathAsync(1, Cancellation)).ParentId);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => lease.ReadPathAsync(999, Cancellation));
        await lease.DisposeAsync(); Assert.True(scratch.Closed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(bytes, input.Input);
    }

    [Theory]
    [InlineData("name", false)]
    [InlineData("empty-id", false)]
    [InlineData("zero-bytes", false)]
    [InlineData("invalid-view", false)]
    [InlineData("id", true)]
    [InlineData("bytes", true)]
    [InlineData("coverage", true)]
    [InlineData("taint", true)]
    public async Task ReviewedCatalogMismatchCannotPublishAnInventoryOrLeakItsStaging(string fault, bool opens)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        byte[] bytes = Encode(OpaqueIds()); var expected = Reviewed(bytes); int factories = 0; var input = new MemoryDirectory { Input = bytes };
        expected = fault switch
        {
            "name" => expected with { Name = "another.fcdsa" }, "empty-id" => expected with { InventoryId = Guid.Empty },
            "zero-bytes" => expected with { Bytes = 0 }, "invalid-view" => expected with { View = expected.View with { Chart = "unsupported" } },
            "id" => expected with { InventoryId = Guid.NewGuid() }, "bytes" => expected with { Bytes = bytes.LongLength + 1 },
            "coverage" => expected with { Coverage = NamespaceCoverage.Partial },
            _ => expected with { Taint = AnalysisTaint.OrdinarySensitive | AnalysisTaint.PrivilegedDerived }
        };
        var pending = AnalysisInventoryFormat.LoadPrivateIndexedAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa",
            () => { factories++; return input; }, expected, Cancellation);
        try { await Assert.ThrowsAsync<InvalidDataException>(() => pending); }
        finally { if (pending.IsCompletedSuccessfully) await (await pending).DisposeAsync(); }
        Assert.Equal(opens ? 1 : 0, factories); Assert.Equal(opens, input.Closed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(bytes, input.Input);
    }
}
