using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisInventoryStageTests
{
    [Fact]
    public async Task FailedFlatReadCloseDisposesTheUnreturnedInventoryInsteadOfExhaustingImportMemory()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); byte[] bytes = Encode(OpaqueIds());
        // Each flat reader reserves at least4 MiB. Twenty failed closes would exhaust the
        //64 MiB shared import budget if a successfully parsed, unreturned owner were leaked.
        for (int n = 0; n < 20; n++)
        {
            var input = new MemoryDirectory { Input = bytes, FailInputClose = true };
            await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => AnalysisIo.Run(io, "failed-flat-close-" + n, _ =>
            { using var store = new AnalysisPrivateInventoryStore(input); return store.Load("source.fcdsa", Cancellation); }, Cancellation));
            Assert.True(input.InputClosed); Assert.True(input.Closed);
        }
    }

    [Fact]
    public async Task InputAndRollbackCloseFailuresCannotBecomeOrdinaryParseOrFactoryErrors()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        var input = new MemoryDirectory { Input = Encode(OpaqueIds()), FailInputClose = true };
        await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => AnalysisInventoryFormat.LoadPrivateIndexedAsync(workspace, io,
            "stage-output", "stage-input", "source.fcdsa", () => input, cancellation: Cancellation));
        Assert.True(input.Closed); Assert.True(input.InputClosed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
        var rollback = new ClosingRollbackDirectory(); int inputs = 0;
        var error = await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => AnalysisIndexedInventory.LoadPrivateAsync(io, "rollback", "rollback", "source.fcdsa",
            () => rollback, () => throw new IOException("injected quota-setup refusal"), () => { inputs++; return new MemoryDirectory(); }, cancellation: Cancellation));
        Assert.Equal(1, rollback.Calls); Assert.Equal(0, inputs); Assert.IsType<IOException>(error.InnerException);
    }
    private sealed class ClosingRollbackDirectory : AnalysisPrivateDirectory
    {
        internal int Calls;
        internal override void Verify() => throw new NotSupportedException();
        internal override (UInt128 Capacity, UInt128 Available) Space() => throw new NotSupportedException();
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Calls++; throw new IOException("injected rollback close result"); }
    }
}
