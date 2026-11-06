using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using FileCat.Tests;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed class AnalysisInventoryStageTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    [Fact]
    public async Task NativeUnixStagingRetainsTheWholeContainerAndOriginalIdMapAfterInputAndWorkspaceClose()
    {
        Ordinary(); Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native private staging fixture.");
        await AnalysisInventoryStageCases.NativeAsync((parent, leaf) => new UnixAnalysisPrivateDirectory(parent, leaf));
    }
    [Fact]
    public async Task LargeRecordsAndOriginalIdsSurviveStagingWhileClosedInputDevicesAreNoLongerNeeded()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        var input = new MemoryDirectory { Input = AnalysisInventoryStageCases.Bytes(4200, largeRecord: true) };
        await using var stage = await AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa", () => input, Cancellation);
        Assert.True(input.Closed); Assert.True(input.InputClosed); workspace.Dispose(); Assert.False(scratch.Closed);
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unrelatedInputWork = AnalysisIo.Run(io, "stage-input", _ => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); return true; }, Cancellation);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.Equal(4201UL, stage.Count(AnalysisInventoryFormat.RecordKind.Path));
            Assert.Equal((UInt128)7, (await stage.ReadAsync(AnalysisInventoryFormat.RecordKind.Object, 1, Cancellation).WaitAsync(TimeSpan.FromSeconds(5), Cancellation)).Object!.LogicalLength.Value);
            Assert.Equal(ulong.MaxValue, (await stage.ReadAsync(AnalysisInventoryFormat.RecordKind.Path, 1, Cancellation)).Path!.Id);
            Assert.Equal(1UL, (await stage.ReadAsync(AnalysisInventoryFormat.RecordKind.Path, 4201, Cancellation)).Path!.Id);
            await using var map = await stage.BuildPathMapAsync(Cancellation); await stage.DisposeAsync(); Assert.False(scratch.Closed);
            Assert.Equal(4201UL, await map.ResolveAsync(1, Cancellation)); await map.DisposeAsync(); Assert.True(scratch.Closed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
        }
        finally { release.Set(); await unrelatedInputWork; }
    }
    [Theory]
    [InlineData("checksum")]
    [InlineData("delimiter")]
    [InlineData("extra")]
    public async Task InvalidCompletedOrTruncatedContainersRetireEveryStagedPrefixWithoutChangingInput(string fault)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        byte[] valid = AnalysisInventoryStageCases.Bytes(300); byte[] bytes = fault switch
        {
            "checksum" => CorruptFactWithoutUpdatingTrailer(valid),
            "delimiter" => valid[..^1], _ => [.. valid, (byte)'\n']
        };
        Assert.False(bytes.AsSpan().SequenceEqual(valid));
        var input = new MemoryDirectory { Input = bytes };
        var staging = AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa", () => input, Cancellation);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => staging);
            if (fault == "checksum") Assert.Contains("checksum", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (staging.IsCompletedSuccessfully) await (await staging).DisposeAsync(); }
        Assert.True(input.Closed); Assert.True(input.InputClosed); Assert.Equal(bytes, input.Input); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
    }
    private static byte[] CorruptFactWithoutUpdatingTrailer(byte[] valid)
    {
        var lines = Encoding.UTF8.GetString(valid).TrimEnd('\n').Split('\n');
        int index = Array.FindIndex(lines, line => System.Text.Json.Nodes.JsonNode.Parse(line)!["kind"]!.GetValue<string>() == "storage-fact");
        var fact = System.Text.Json.Nodes.JsonNode.Parse(lines[index])!; fact["measurement"]![1] = "4097"; lines[index] = fact.ToJsonString();
        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }
    [Fact]
    public async Task CancellationWaitsForActualBlockedReadAndKeepsItsPairedOwnersUntilReturn()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        using var release = new ManualResetEventSlim(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new MemoryDirectory { Input = AnalysisInventoryStageCases.Bytes(1), OnRead = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); } };
        var staging = AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa", () => input, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); stop.Cancel(); workspace.Dispose();
            Assert.False(staging.IsCompleted); Assert.False(input.Closed); Assert.False(input.InputClosed); Assert.False(scratch.Closed); Assert.Equal(2, scratch.LiveFiles);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => staging);
            Assert.True(input.Closed); Assert.True(input.InputClosed); Assert.True(scratch.Closed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
        }
        finally { release.Set(); }
    }
    [Fact]
    public async Task ThirdDeviceIsRefusedBeforeAnInputFactoryAndCloseFailureCannotPublishAStage()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch); int calls = 0;
        await Assert.ThrowsAsync<NotSupportedException>(() => AnalysisInventoryFormat.StagePrivateAsync(workspace, io, new("a", "b"), "c", "source.fcdsa", () => { calls++; return new MemoryDirectory(); }, Cancellation));
        Assert.Equal(0, calls); Assert.Equal(0, scratch.LiveFiles);
        var input = new MemoryDirectory { Input = AnalysisInventoryStageCases.Bytes(1), FailInputClose = true };
        await Assert.ThrowsAsync<IOException>(() => AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa", () => input, Cancellation));
        Assert.True(input.Closed); Assert.True(input.InputClosed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
    }
    [Fact]
    public async Task TwoSuspendedImportsKeepTheirReservationsUntilCanceledActualReadsReturn()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var release = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int running = 0, thirdFactory = 0;
        var spaces = new List<AnalysisScratchWorkspace>(); var tasks = new List<Task<AnalysisInventoryStage>>();
        try
        {
            for (int n = 0; n < 3; n++)
            {
                int index = n, seen = 0; var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); spaces.Add(workspace);
                var input = new MemoryDirectory { Input = AnalysisInventoryStageCases.Bytes(1) };
                if (n < 2) input.OnRead = () => { if (Interlocked.Exchange(ref seen, 1) == 0 && Interlocked.Increment(ref running) == 2) entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); };
                tasks.Add(AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output-" + n, "stage-input-" + n, "source.fcdsa",
                    () => { if (index == 2) Interlocked.Increment(ref thirdFactory); return input; }, n == 0 ? stop.Token : Cancellation));
            }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); stop.Cancel();
            Assert.False(tasks[0].IsCompleted); Assert.False(tasks[2].IsCompleted); Assert.Equal(0, Volatile.Read(ref thirdFactory));
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tasks[0]);
            await using var second = await tasks[1]; await using var third = await tasks[2]; Assert.Equal(1, thirdFactory);
        }
        finally
        {
            release.Set();
            try { foreach (var task in tasks) { try { await using var stage = await task; } catch (OperationCanceledException) { } } }
            finally { for (int n = 0; n < spaces.Count; n++) await AnalysisIo.ReleaseScratchAsync(io, "stage-output-" + n, spaces[n]); }
        }
    }
    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal byte[] Input = []; internal bool Closed, InputClosed, FailInputClose; internal int LiveFiles; internal Action? OnRead;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateScratchFile CreateScratch() { LiveFiles++; return new ScratchFile(this); }
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) { Assert.False(Thread.CurrentThread.IsThreadPoolThread); return new InputFile(this); }
        public override void Dispose() { Closed = true; }
    }
    private sealed class InputFile(MemoryDirectory owner) : AnalysisPrivateFile
    {
        private readonly Stream _stream = new InputStream(owner);
        public override Stream Stream => _stream;
        public override void Verify() => ObjectDisposedException.ThrowIf(owner.InputClosed, this);
        public override void Flush() => throw new NotSupportedException();
        public override void PublishNew(string name) => throw new NotSupportedException();
        public override void Dispose() { _stream.Dispose(); owner.InputClosed = true; if (owner.FailInputClose) throw new IOException("Injected input-close failure."); }
    }
    private sealed class InputStream(MemoryDirectory owner) : MemoryStream(owner.Input, writable: false)
    { public override int Read(Span<byte> buffer) { Assert.False(Thread.CurrentThread.IsThreadPoolThread); owner.OnRead?.Invoke(); return base.Read(buffer); } }
    private sealed class ScratchFile(MemoryDirectory owner) : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new(); private bool _closed;
        public override long Length => _bytes.Length;
        public override void Verify() => ObjectDisposedException.ThrowIf(_closed, this);
        public override int ReadAt(Span<byte> buffer, long offset) { _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset) { _bytes.Position = offset; _bytes.Write(buffer); }
        public override void Dispose() { if (_closed) return; _closed = true; _bytes.Dispose(); owner.LiveFiles--; }
    }
}
