using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class PagedReaderCoalescingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false, "complete")]
    [InlineData(false, true, "complete")]
    [InlineData(true, false, "complete")]
    [InlineData(true, true, "complete")]
    [InlineData(false, false, "close")]
    [InlineData(false, true, "close")]
    [InlineData(true, false, "close")]
    [InlineData(true, true, "close")]
    [InlineData(false, false, "error")]
    [InlineData(false, true, "error")]
    [InlineData(true, false, "error")]
    [InlineData(true, true, "error")]
    public async Task Blocking_and_visible_requests_share_one_active_source_read(bool backgroundOwner, bool backgroundPeer, string outcome)
    {
        var source = new Source(outcome == "error"); using var io = new DeviceIoScheduler(TimeSpan.FromMinutes(1), threadsPerDevice: 1, maxThreadsPerDevice: 1);
        var budget = new PageCacheBudget(); using var reader = new PagedReader(source, io, "owned-shared-page", budget: budget);
        Task<int>? owner = null, peer = null; var first = new byte[32]; var second = new byte[32]; Thread? peerThread = null;
        try
        {
            if (backgroundOwner) Assert.False(reader.TryRead(0, first, out _)); else owner = Task.Factory.StartNew(() => reader.Read(0, first), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (backgroundPeer) Assert.False(reader.TryRead(0, second, out _));
            else peer = Task.Factory.StartNew(() => { peerThread = Thread.CurrentThread; return reader.Read(0, second); }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (peer is not null) await Wait(() => peer.IsCompleted || peerThread is not null && (peerThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0);
            else await Task.Delay(100, TestContext.Current.CancellationToken); // asynchronous request is already admitted by TryRead
            int before = source.Reads; bool ownerPending = owner is null || !owner.IsCompleted;
            if (outcome == "close") reader.Dispose();
            int disposalWhileHeld = source.Disposals; source.Release.Set(); if (owner is not null) await owner.WaitAsync(TimeSpan.FromSeconds(10)); if (peer is not null) await peer.WaitAsync(TimeSpan.FromSeconds(10)); await Wait(() => reader.PendingLoads == 0);
            if (outcome == "complete")
            {
                if (owner is null) { Assert.True(reader.TryRead(0, first, out int count)); Assert.Equal(first.Length, count); }
                if (peer is null) { Assert.True(reader.TryRead(0, second, out int count)); Assert.Equal(second.Length, count); }
                Assert.Equal(source.Bytes[..32], first); Assert.Equal(first, second);
            }
            if (outcome == "error") { if (owner is not null) Assert.Equal(0, await owner); if (peer is not null) Assert.Equal(0, await peer); Assert.NotNull(reader.ReadError); }
            reader.Dispose();
            output.WriteLine("PAGE_COALESCING " + JsonSerializer.Serialize(new { backgroundOwner, backgroundPeer, outcome, before, ownerPending, disposalWhileHeld, source.Reads, source.PeakActive, source.Disposals, source.DisposedDuringRead, Calls = source.Calls.ToArray(), BudgetBytes = budget.UsedBytes, OwnedFileSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.Path))), ExactOriginalSHA256 = Convert.ToHexString(SHA256.HashData(source.Bytes)), NativeDesktopOrPhysicalOrCandidateQualified = false }));
            Assert.True(ownerPending); Assert.Equal(0, disposalWhileHeld); Assert.Equal(1, before); Assert.Equal(1, source.Reads); Assert.Equal(1, source.PeakActive); Assert.Equal(1, source.Disposals); Assert.False(source.DisposedDuringRead); Assert.Equal(0, budget.UsedBytes);
        }
        finally { source.Release.Set(); if (owner is not null) await owner; if (peer is not null) await peer; reader.Dispose(); await Wait(() => reader.PendingLoads == 0); source.Cleanup(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_different_page_or_refreshed_generation_can_finish_while_the_old_page_is_held(bool refresh)
    {
        var source = new Source(false); using var reader = new PagedReader(source); var bytes = new byte[32]; var owner = Task.Factory.StartNew(() => reader.Read(0, bytes), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); long offset = PagedReader.PageSize;
            if (refresh) { var changed = source.Bytes.ToArray(); changed[0] = 123; File.WriteAllBytes(source.Path, changed); File.SetLastWriteTimeUtc(source.Path, File.GetLastWriteTimeUtc(source.Path).AddMinutes(2)); Assert.True(reader.Refresh()); offset = 0; }
            var current = new byte[32]; var other = Task.Factory.StartNew(() => reader.Read(offset, current), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default); Assert.Equal(32, await other.WaitAsync(TimeSpan.FromSeconds(10))); Assert.False(owner.IsCompleted);
            Assert.Equal(File.ReadAllBytes(source.Path).AsSpan((int)offset, 32).ToArray(), current); source.Release.Set(); await owner; reader.Dispose();
            output.WriteLine("PAGE_INDEPENDENCE " + JsonSerializer.Serialize(new { refresh, source.Reads, source.PeakActive, source.Disposals, source.DisposedDuringRead, Calls = source.Calls.ToArray(), CurrentBytes = Convert.ToHexString(current), NativeOrAtomicContentSnapshotQualified = false }));
            Assert.Equal(2, source.Reads); Assert.Equal(2, source.PeakActive); Assert.Equal(1, source.Disposals); Assert.False(source.DisposedDuringRead);
        }
        finally { source.Release.Set(); await owner; reader.Dispose(); source.Cleanup(); }
    }

    [Fact]
    public async Task A_cancelled_waiter_waits_for_the_shared_call_and_never_opens_a_second_source_read()
    {
        var source = new Source(false); using var reader = new PagedReader(source); using var cancellation = new CancellationTokenSource(); Task<int>? peer = null; Thread? thread = null;
        var owner = Task.Factory.StartNew(() => reader.Read(0, new byte[32]), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); peer = Task.Factory.StartNew(() => { thread = Thread.CurrentThread; return reader.Read(0, new byte[32], cancellation.Token); }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await Wait(() => peer.IsCompleted || thread is not null && (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0); cancellation.Cancel(); bool pending = !peer.IsCompleted; source.Release.Set(); await owner;
            Exception? error = null; try { await peer; } catch (OperationCanceledException ex) { error = ex; }
            reader.Dispose(); output.WriteLine("PAGE_CANCELLED_WAITER " + JsonSerializer.Serialize(new { pending, Error = error?.GetType().FullName, source.Reads, source.Disposals, source.DisposedDuringRead, Calls = source.Calls.ToArray(), OwnedFilesOnly = true }));
            Assert.True(pending); Assert.IsAssignableFrom<OperationCanceledException>(error); Assert.Equal(1, source.Reads); Assert.Equal(1, source.Disposals); Assert.False(source.DisposedDuringRead);
        }
        finally { source.Release.Set(); await owner; if (peer is not null) try { await peer; } catch (OperationCanceledException) { } reader.Dispose(); source.Cleanup(); }
    }

    [Fact]
    public async Task A_visible_page_waiter_yields_the_device_worker_to_another_page()
    {
        var source = new Source(false); using var io = new DeviceIoScheduler(TimeSpan.FromMinutes(1), threadsPerDevice: 2, maxThreadsPerDevice: 2);
        using var reader = new PagedReader(source, io, "owned-device-page"); Task<int>? healthy = null;
        var owner = io.Run("owned-device-page", IoPriority.Interactive, _ => reader.Read(0, new byte[32]));
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(reader.TryRead(0, new byte[1], out _));
            var bytes = new byte[32]; healthy = io.Run("owned-device-page", IoPriority.Interactive, _ => reader.Read(PagedReader.PageSize, bytes));
            bool completedWhileHeld; try { Assert.Equal(32, await healthy.WaitAsync(TimeSpan.FromSeconds(2))); completedWhileHeld = true; } catch (TimeoutException) { completedWhileHeld = false; }
            bool ownerPending = !owner.IsCompleted, waiterPending = reader.PendingLoads == 1; int callsBeforeRelease = source.Reads;
            source.Release.Set(); await owner; await healthy; await Wait(() => reader.PendingLoads == 0); reader.Dispose();
            output.WriteLine("PAGE_DEVICE_YIELD " + JsonSerializer.Serialize(new { completedWhileHeld, ownerPending, waiterPending, callsBeforeRelease, source.Reads, source.PeakActive, source.Disposals, source.DisposedDuringRead, Calls = source.Calls.ToArray(), ExactOtherPageBytes = Convert.ToHexString(bytes), OwnedFilesOnly = true, NativeDesktopOrPhysicalOrCandidateQualified = false }));
            Assert.True(completedWhileHeld); Assert.True(ownerPending); Assert.True(waiterPending); Assert.Equal(2, callsBeforeRelease); Assert.Equal(2, source.Reads); Assert.Single(source.Calls, c => c.Offset == 0); Assert.Equal(source.Bytes.AsSpan(PagedReader.PageSize, 32).ToArray(), bytes); Assert.Equal(1, source.Disposals); Assert.False(source.DisposedDuringRead);
        }
        finally { source.Release.Set(); await owner; if (healthy is not null) await healthy; reader.Dispose(); await Wait(() => reader.PendingLoads == 0); source.Cleanup(); }
    }

    [Fact]
    public async Task A_failed_shared_page_can_be_read_again_after_the_source_recovers()
    {
        var source = new Source(true); using var reader = new PagedReader(source); source.Release.Set();
        try
        {
            Assert.Equal(0, reader.Read(0, new byte[32])); source.FailReads = false; var bytes = new byte[32]; Assert.Equal(32, reader.Read(0, bytes)); Assert.Equal(source.Bytes[..32], bytes); reader.Dispose();
            output.WriteLine("PAGE_RETRY " + JsonSerializer.Serialize(new { source.Reads, source.Disposals, source.DisposedDuringRead, Calls = source.Calls.ToArray(), ExactRecoveredBytes = Convert.ToHexString(bytes), OwnedFilesOnly = true })); Assert.Equal(2, source.Reads); Assert.Equal(1, source.Disposals);
        }
        finally { reader.Dispose(); source.Cleanup(); }
    }

    private static async Task Wait(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned page-sharing checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private sealed record ReadCall(long Offset, int Bytes, string Thread);
    private sealed class Source : IContentSource
    {
        private readonly FileContentSource _inner; public bool FailReads; private int _reads, _active, _peak, _disposals;
        public readonly string Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "filecat-page-sharing-" + Guid.NewGuid().ToString("N"));
        public readonly byte[] Bytes = Enumerable.Range(0, 2 * PagedReader.PageSize).Select(i => (byte)((i * 37) % 251)).ToArray();
        public readonly ConcurrentQueue<ReadCall> Calls = new(); public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); public readonly ManualResetEventSlim Release = new(); public bool DisposedDuringRead;
        public Source(bool fail) { FailReads = fail; File.WriteAllBytes(Path, Bytes); _inner = new(Path); }
        public int Reads => Volatile.Read(ref _reads); public int PeakActive => Volatile.Read(ref _peak); public int Disposals => Volatile.Read(ref _disposals);
        public string DisplayName => Path; public long Length => _inner.Length; public bool CanSeek => true; public string? LocalPath => Path; public ContentRevision? GetRevision() => _inner.GetRevision();
        public int Read(long offset, Span<byte> buffer)
        {
            int read = Interlocked.Increment(ref _reads), active = Interlocked.Increment(ref _active); Calls.Enqueue(new(offset, buffer.Length, Thread.CurrentThread.Name ?? "")); int peak;
            do { peak = Volatile.Read(ref _peak); if (active <= peak) break; } while (Interlocked.CompareExchange(ref _peak, active, peak) != peak);
            try { if (read == 1) { Entered.TrySetResult(); if (!Release.Wait(TimeSpan.FromSeconds(12))) throw new IOException("Owned page release expired."); } if (FailReads) throw new IOException("Owned page failure."); return _inner.Read(offset, buffer); }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose() { Interlocked.Increment(ref _disposals); DisposedDuringRead |= Volatile.Read(ref _active) != 0; _inner.Dispose(); }
        public void Cleanup() { _inner.Dispose(); Release.Dispose(); File.Delete(Path); }
    }
}
