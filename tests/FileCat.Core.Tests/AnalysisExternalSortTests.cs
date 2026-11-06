using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class AnalysisExternalSortTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");

    [Fact]
    public async Task ReusedInputBuffersMergeExactlyAndOldReadersKeepTheirNativeOwner()
    {
        Ordinary(); var directory = new MemoryDirectory(); var profile = new AnalysisScratchBudget(16 * 1024 * 1024);
        using var workspace = new AnalysisScratchWorkspace(directory, 16 * 1024 * 1024, profile); using var io = new DeviceIoScheduler();
        IEnumerable<AnalysisSortEntry> Source()
        {
            byte[] key = new byte[3];
            for (ulong n = 2000; n > 0; n--) { key[0] = (byte)(n % 37); key[1] = 0xff; key[2] = 0; yield return new(key, n); }
        }
        using var source = Source().GetEnumerator();
        using var index = await AnalysisExternalSort.BuildAsync(workspace, io, "external-sort-fixture", source, Cancellation, runMemoryBytes: 4096);
        Assert.Equal(2000UL, index.Count); Assert.Equal(1, directory.LiveFiles); // all temporary runs retired.
        using var reader = index.Acquire();
        var expected = Enumerable.Range(1, 2000).Select(n => new AnalysisSortEntry([(byte)(n % 37), 0xff, 0], (ulong)n)).OrderBy(x => x, AnalysisExternalSort.Order).ToArray();
        for (ulong n = 1; n <= reader.Count; n++)
        { var actual = reader.Read(n, Cancellation); Assert.Equal(expected[n - 1].Key, actual.Key); Assert.Equal(expected[n - 1].Value, actual.Value); }
        var range = reader.FindRange([7, 0xff, 0], Cancellation); Assert.Equal((ulong)expected.Count(x => x.Key[0] == 7), range.Count);
        Assert.Equal(range, reader.PrefixRange([7], Cancellation)); Assert.Equal(new AnalysisIndexRange(1, 2000), reader.PrefixRange([], Cancellation));
        Assert.Equal(0UL, reader.FindRange([0xff], Cancellation).Count);
        workspace.Dispose(); index.Dispose(); Assert.False(directory.Closed);
        using var retained = reader.Retain(); reader.Dispose(); Assert.Equal(expected[0].Key, retained.Read(1, Cancellation).Key);
        retained.Dispose(); Assert.Equal(0, profile.ReservedBytes); Assert.True(directory.Closed); Assert.Equal(0, directory.LiveFiles);
    }

    [Fact]
    public async Task EmptyInputHasAUsableEmptyIndexAndInvalidEntriesCannotBecomePartialSuccess()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        using var empty = Enumerable.Empty<AnalysisSortEntry>().GetEnumerator();
        using (var index = await AnalysisExternalSort.BuildAsync(workspace, io, "empty-sort", empty, Cancellation))
        using (var reader = index.Acquire()) { Assert.Equal(0UL, reader.Count); Assert.Equal(new AnalysisIndexRange(1, 0), reader.PrefixRange([], Cancellation)); }
        Assert.Equal(0, workspace.ReservedBytes);
        foreach (var invalid in new[] { new AnalysisSortEntry([], 1), new AnalysisSortEntry(new byte[AnalysisExternalSort.MaximumKeyBytes + 1], 1), new AnalysisSortEntry([1], 0) })
        {
            using var source = new[] { invalid }.AsEnumerable().GetEnumerator();
            await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisExternalSort.BuildAsync(workspace, io, "invalid-sort", source, Cancellation));
            Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(0, directory.LiveFiles);
        }
    }

    [Fact]
    public async Task CancelAndPartialNativeWriteReleaseTemporaryArenasWithoutTouchingAnExistingDataRevision()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        using var data = workspace.CreateArena(); var address = data.Append([1, 2, 3], Cancellation); using var reader = data.Capture(); long before = workspace.ReservedBytes;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        IEnumerable<AnalysisSortEntry> Source()
        { for (ulong n = 1; n <= 2000; n++) { if (n == 600) cancel.Cancel(); yield return new([(byte)(n % 100)], n); } }
        using var source = Source().GetEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnalysisExternalSort.BuildAsync(workspace, io, "cancel-sort", source, cancel.Token, 4096));
        Assert.Equal(before, workspace.ReservedBytes); Assert.Equal(new byte[] { 1, 2, 3 }, reader.Read(address, Cancellation));
        directory.FailNextWrite = true; using var faulty = Enumerable.Range(1, 100).Select(n => new AnalysisSortEntry([(byte)n], (ulong)n)).GetEnumerator();
        await Assert.ThrowsAsync<IOException>(() => AnalysisExternalSort.BuildAsync(workspace, io, "fault-sort", faulty, Cancellation, 4096));
        Assert.Equal(before, workspace.ReservedBytes); Assert.Equal(1, directory.LiveFiles); Assert.Equal(new byte[] { 1, 2, 3 }, reader.Read(address, Cancellation));
    }

    [Fact]
    public async Task SparseWorkStepsRemainCancelableAndDoNotCreateIndexEntries()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(directory); using var io = new DeviceIoScheduler();
        IEnumerable<AnalysisSortInput> Source(CancellationTokenSource? stop)
        {
            for (int row = 0; row < 2000; row++)
            { if (row == 600) stop?.Cancel(); yield return new(row == 1999 ? new AnalysisSortEntry([0xff], 17) : null, 64 * 1024); }
        }
        using (var source = Source(null).GetEnumerator())
        using (var index = await AnalysisExternalSort.BuildSparseAsync(workspace, io, "sparse-sort", source, Cancellation))
        using (var reader = index.Acquire())
        { Assert.Equal(1UL, reader.Count); Assert.Equal(17UL, reader.Read(1, Cancellation).Value); }
        Assert.Equal(0, directory.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); using var canceled = Source(stop).GetEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnalysisExternalSort.BuildSparseAsync(workspace, io, "sparse-cancel", canceled, stop.Token));
        Assert.Equal(0, directory.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
    }

    [Fact]
    public async Task SparseInputYieldsTheDeviceWorkerAtItsCapturedByteLimit()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory());
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        const string device = "sparse-byte-scheduling"; int observed = 0; Task<int>? foreground = null;
        IEnumerable<AnalysisSortInput> Source()
        {
            for (int row = 0; row < 1000; row++)
            {
                observed++;
                if (row == 0) foreground = io.Run(device, IoPriority.Interactive, _ => observed);
                yield return new(null, 64 * 1024);
            }
        }
        using var source = Source().GetEnumerator();
        using var result = await AnalysisExternalSort.BuildSparseAsync(workspace, io, device, source, Cancellation);
        Assert.NotNull(foreground); Assert.InRange(await foreground, 1, AnalysisLimits.BatchBytes / (64 * 1024));
        Assert.Equal(0UL, result.Count);
    }

    [Fact]
    public async Task SourceIterationAndCleanupUseAdmittedWorkersAndSourceOwnershipStaysWithCaller()
    {
        Ordinary(); using var workspace = new AnalysisScratchWorkspace(new MemoryDirectory()); using var io = new DeviceIoScheduler();
        int caller = Environment.CurrentManagedThreadId; var threads = new List<int>(); bool disposed = false;
        IEnumerable<AnalysisSortEntry> Source()
        {
            try { for (ulong n = 1; n <= 20; n++) { threads.Add(Environment.CurrentManagedThreadId); Assert.False(Thread.CurrentThread.IsThreadPoolThread); yield return new([(byte)n], n); } }
            finally { disposed = true; }
        }
        using var source = Source().GetEnumerator(); using var result = await AnalysisExternalSort.BuildAsync(workspace, io, "worker-sort", source, Cancellation);
        Assert.All(threads, thread => Assert.NotEqual(caller, thread)); Assert.True(disposed); // natural iterator completion, not a transferred ownership promise.
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
            if (directory.FailNextWrite) { directory.FailNextWrite = false; _bytes.Write(buffer[..Math.Min(buffer.Length, 3)]); throw new IOException("Injected partial run write."); }
            _bytes.Write(buffer);
        }
        public override void Dispose() { if (_closed) return; _closed = true; _bytes.Dispose(); directory.LiveFiles--; }
    }
}
