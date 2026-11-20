using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class AnalysisInventoryPathMapTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly AnalysisIoDevices Device = "inventory-map-fixture";
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");

    [Fact]
    public async Task UnixNativeOriginalIdMapOutlivesItsWorkspaceOwnerAndClosesAllAnonymousFiles()
    {
        Ordinary(); Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native storage fixture.");
        await FileCat.Tests.AnalysisInventoryPathMapCases.NativeAsync((parent, leaf) => new UnixAnalysisPrivateDirectory(parent, leaf));
    }

    [Fact]
    public async Task ArbitraryOriginalIdsStayDistinctFromRowsAcrossExternalMergeAndOwnerClosure()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        static ulong Id(ulong row) => row == 5201 ? 1 : ulong.MaxValue - (row - 1) * 7919;
        IEnumerable<AnalysisInventoryPathIdentity> Source()
        { for (ulong row = 1; row <= 5201; row++) yield return new(Id(row), row == 1 ? 0 : Id(row / 2)); }
        using var source = Source().GetEnumerator();
        await using var map = await AnalysisInventoryPathMap.BuildAsync(workspace, io, Device, source, Cancellation, 4096);
        Assert.Equal(5201UL, map.Count); Assert.Equal(2, directory.LiveFiles); // original rows and exact ID index; merge spools closed.
        workspace.Dispose(); Assert.False(directory.Closed);
        foreach (ulong row in Enumerable.Range(1, 5201).Where(n => n <= 20 || n % 97 == 0 || n == 5201).Select(n => (ulong)n))
        {
            Assert.Equal(row, await map.ResolveAsync(Id(row), Cancellation));
            var value = await map.ReadAsync(row, Cancellation); Assert.Equal(new AnalysisInventoryPathMapping(row, Id(row), row == 1 ? 0 : Id(row / 2), row / 2), value);
        }
        Assert.Null(await map.ResolveAsync(2, Cancellation));
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = map.ResolveAsync(0, Cancellation); });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => map.ReadAsync(0, Cancellation));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => map.ReadAsync(5202, Cancellation));
        await map.DisposeAsync(); Assert.True(directory.Closed); Assert.Equal(0, directory.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await map.ReadAsync(1, Cancellation));
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("duplicate")]
    [InlineData("self")]
    [InlineData("missing")]
    [InlineData("forward")]
    public async Task InvalidIdsAndParentOrderCannotPublishAMapOrTouchAnExistingRevision(string fault)
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        using var protectedArena = workspace.CreateArena(); var address = protectedArena.Append([9, 8, 7], Cancellation); using var protectedReader = protectedArena.Capture();
        long before = workspace.ReservedBytes;
        AnalysisInventoryPathIdentity[] rows = fault switch
        {
            "zero" => [new(0, 0)], "duplicate" => [new(99, 0), new(99, 0)], "self" => [new(99, 99)],
            "missing" => [new(99, 0), new(7, 88)], _ => [new(99, 7), new(7, 0)]
        };
        using var source = rows.AsEnumerable().GetEnumerator();
        await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisInventoryPathMap.BuildAsync(workspace, io, Device, source, Cancellation, 1024));
        Assert.Equal(before, workspace.ReservedBytes); Assert.Equal(1, directory.LiveFiles); Assert.Equal(new byte[] { 9, 8, 7 }, protectedReader.Read(address, Cancellation));
    }

    [Fact]
    public async Task EmptyInputIsAnEmptyMapAndFailedWritesLeaveNoTemporaryFiles()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        using var empty = Enumerable.Empty<AnalysisInventoryPathIdentity>().GetEnumerator();
        await using (var map = await AnalysisInventoryPathMap.BuildAsync(workspace, io, Device, empty, Cancellation))
        { Assert.Equal(0UL, map.Count); Assert.Null(await map.ResolveAsync(ulong.MaxValue, Cancellation)); }
        Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(0, directory.LiveFiles);
        directory.FailNextWrite = true; using var source = new[] { new AnalysisInventoryPathIdentity(ulong.MaxValue, 0) }.AsEnumerable().GetEnumerator();
        await Assert.ThrowsAsync<IOException>(() => AnalysisInventoryPathMap.BuildAsync(workspace, io, Device, source, Cancellation));
        Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(0, directory.LiveFiles);
    }

    [Fact]
    public async Task CancellationKeepsActualInputWorkAndItsStagingAliveUntilItReturns()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IEnumerable<AnalysisInventoryPathIdentity> Source()
        { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); yield return new(ulong.MaxValue, 0); }
        using var source = Source().GetEnumerator(); var building = AnalysisInventoryPathMap.BuildAsync(workspace, io, Device, source, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); stop.Cancel();
            Assert.False(building.IsCompleted); Assert.Equal(1, directory.LiveFiles); Assert.False(directory.Closed);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => building);
            Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(0, directory.LiveFiles);
        }
        finally { release.Set(); }
    }

    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal bool Closed, FailNextWrite; internal int LiveFiles;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateScratchFile CreateScratch() { LiveFiles++; return new MemoryFile(this); }
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Closed = true; }
    }
    private sealed class MemoryFile(MemoryDirectory directory) : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new(); private bool _closed;
        public override long Length => _bytes.Length;
        public override void Verify() { }
        public override int ReadAt(Span<byte> buffer, long offset) { _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset)
        {
            _bytes.Position = offset;
            if (directory.FailNextWrite) { directory.FailNextWrite = false; _bytes.Write(buffer[..Math.Min(buffer.Length, 3)]); throw new IOException("Injected partial map write."); }
            _bytes.Write(buffer);
        }
        public override void Dispose() { if (_closed) return; _closed = true; _bytes.Dispose(); directory.LiveFiles--; }
    }
}
