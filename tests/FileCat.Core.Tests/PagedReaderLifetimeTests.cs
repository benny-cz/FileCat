using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

/// <summary>V12: a closed view retires page demand without disposing a provider in the middle of its call.</summary>
public sealed class PagedReaderLifetimeTests
{
    [Fact]
    public async Task Shared_device_page_demand_cancels_queued_reads_and_keeps_active_sources_alive()
    {
        using var io = new DeviceIoScheduler(TimeSpan.FromMinutes(1), threadsPerDevice: 1, maxThreadsPerDevice: 1);
        var active = new HeldSource();
        var queued = new HeldSource(holdRead: false);
        var healthy = new HeldSource(holdRead: false);
        var budget = new PageCacheBudget();
        var activeReader = new PagedReader(active, io, "held", budget: budget);
        var queuedReader = new PagedReader(queued, io, "held", budget: budget);
        var healthyReader = new PagedReader(healthy, io, "healthy", budget: budget);
        try
        {
            Assert.False(activeReader.TryRead(0, new byte[1], out _));
            await active.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(queuedReader.TryRead(0, new byte[1], out _));
            Assert.Equal(1, queuedReader.PendingLoads);
            Assert.False(healthyReader.TryRead(0, new byte[1], out _));
            await Quiescent(healthyReader);
            var bytes = new byte[PagedReader.PageSize];
            Assert.True(healthyReader.TryRead(0, bytes, out int read));
            Assert.Equal(bytes.Length, read);
            Assert.Equal(healthy.Bytes[..bytes.Length], bytes);
            Assert.Equal(0, queued.Reads);

            queuedReader.Dispose();
            await Quiescent(queuedReader);
            Assert.Equal(1, queued.Disposals);
            Assert.Equal(0, queued.Reads);
            activeReader.Dispose();
            Assert.Equal(1, activeReader.PendingLoads);
            Assert.Equal(0, active.Disposals);
            active.Release.Set();
            await Quiescent(activeReader);
            Assert.Equal(1, active.Reads);
            Assert.Equal(1, active.Disposals);
            healthyReader.Dispose();
            Assert.Equal(0, budget.UsedBytes);
            Assert.Equal(0, budget.Readers);
            Assert.All(new[] { active, queued, healthy }, source =>
            {
                Assert.Equal(1, source.Disposals);
                Assert.Equal(0, source.DisposalsDuringCall);
                Assert.Equal(source.OriginalSHA256, SHA256.HashData(File.ReadAllBytes(source.Path)));
            });
        }
        finally
        {
            active.Release.Set();
            activeReader.Dispose();
            queuedReader.Dispose();
            healthyReader.Dispose();
            await Quiescent(activeReader);
            await Quiescent(queuedReader);
            await Quiescent(healthyReader);
            active.Cleanup();
            queued.Cleanup();
            healthy.Cleanup();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Closing_during_a_page_read_releases_the_source_after_the_call_returns(bool background, bool failRead)
    {
        var source = new HeldSource(failRead: failRead);
        var budget = new PageCacheBudget();
        var reader = new PagedReader(source, budget: budget);
        Task<int>? blocking = null;
        try
        {
            if (background) Assert.False(reader.TryRead(0, new byte[1], out _));
            else blocking = Task.Run(() => reader.Read(0, new byte[1]), TestContext.Current.CancellationToken);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            reader.Dispose();
            Assert.Equal(0, source.Disposals);
            Assert.Equal(0, reader.CachedPages);
            Assert.Equal(0, budget.UsedBytes);
            Assert.Equal(0, budget.Readers);
            source.Release.Set();
            if (blocking is not null) Assert.Equal(failRead ? 0 : 1, await blocking);
            await Quiescent(reader);
            Assert.Equal(1, source.Disposals);
            Assert.Equal(0, source.DisposalsDuringCall);
            Assert.Equal(1, source.Reads);
            Assert.Equal(0, reader.CachedPages);
            Assert.Null(reader.ReadError); // an abandoned read's failure is no longer a visible error
            Assert.Equal(source.OriginalSHA256, SHA256.HashData(File.ReadAllBytes(source.Path)));
        }
        finally
        {
            source.Release.Set();
            if (blocking is not null) await blocking;
            await Quiescent(reader);
            reader.Dispose();
            source.Cleanup();
        }
    }

    [Fact]
    public async Task A_multi_page_read_stops_after_close_at_the_first_safe_boundary()
    {
        var source = new HeldSource();
        var reader = new PagedReader(source);
        var bytes = new byte[2 * PagedReader.PageSize];
        var reading = Task.Run(() => reader.Read(0, bytes), TestContext.Current.CancellationToken);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            reader.Dispose();
            source.Release.Set();
            Assert.Equal(PagedReader.PageSize, await reading);
            Assert.Equal(source.Bytes[..PagedReader.PageSize], bytes[..PagedReader.PageSize]);
            Assert.Equal(1, source.Reads);
            Assert.Equal(1, source.Disposals);
            Assert.Equal(0, source.DisposalsDuringCall);
        }
        finally
        {
            source.Release.Set();
            await reading;
            reader.Dispose();
            source.Cleanup();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_closed_reader_does_not_request_another_page(bool background)
    {
        var source = new HeldSource(holdRead: false);
        var reader = new PagedReader(source);
        try
        {
            reader.Dispose();
            if (background)
            {
                Assert.True(reader.TryRead(0, new byte[1], out int read));
                Assert.Equal(0, read);
            }
            else Assert.Equal(0, reader.Read(0, new byte[1]));
            Assert.False(reader.Refresh());
            await Quiescent(reader);
            Assert.Equal(0, source.Reads);
            Assert.Equal(1, source.Revisions); // only construction asked
            Assert.Equal(1, source.Disposals);
        }
        finally
        {
            source.Release.Set();
            await Quiescent(reader);
            reader.Dispose();
            source.Cleanup();
        }
    }

    [Fact]
    public async Task An_inflight_refresh_cannot_change_the_revision_after_close()
    {
        var source = new HeldSource(holdRead: false, holdRefresh: true);
        var budget = new PageCacheBudget();
        var reader = new PagedReader(source, budget: budget);
        var revision = reader.Revision;
        File.SetLastWriteTimeUtc(source.Path, File.GetLastWriteTimeUtc(source.Path).AddSeconds(10));
        var refreshing = Task.Run(() => reader.Refresh(), TestContext.Current.CancellationToken);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            reader.Dispose();
            Assert.Equal(0, source.Disposals);
            source.Release.Set();
            Assert.False(await refreshing);
            Assert.Equal(revision, reader.Revision);
            Assert.Equal(0, budget.Readers);
            Assert.Equal(0, source.DisposalsDuringCall);
            Assert.Equal(1, source.Disposals);
        }
        finally
        {
            source.Release.Set();
            try { await refreshing; } catch (ObjectDisposedException) { } // retain the unchanged-production failure
            reader.Dispose();
            source.Cleanup();
        }
    }

    [Fact]
    public void A_completed_read_keeps_its_bytes_and_cache_charge_until_disposal()
    {
        var source = new HeldSource(holdRead: false);
        var budget = new PageCacheBudget();
        var reader = new PagedReader(source, budget: budget);
        try
        {
            var bytes = new byte[source.Bytes.Length];
            Assert.Equal(bytes.Length, reader.Read(0, bytes));
            Assert.Equal(source.OriginalSHA256, SHA256.HashData(bytes));
            Assert.True(reader.TryRead(0, bytes, out int read));
            Assert.Equal(bytes.Length, read);
            Assert.Equal(2, source.Reads);
            Assert.Equal(2L * PagedReader.PageSize, budget.UsedBytes);
            Assert.Equal(0, source.Disposals);
            reader.Dispose();
            reader.Dispose();
            Assert.Equal(1, source.Disposals);
            Assert.Equal(0, source.DisposalsDuringCall);
            Assert.Equal(0, budget.UsedBytes);
            Assert.Equal(0, budget.Readers);
        }
        finally { reader.Dispose(); source.Cleanup(); }
    }

    private static async Task Quiescent(PagedReader reader)
    {
        var clock = Stopwatch.StartNew();
        while (reader.PendingLoads != 0)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned page read did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class HeldSource : IContentSource
    {
        private readonly FileContentSource _file;
        private readonly bool _holdRead, _failRead, _holdRefresh;
        private int _reads, _revisions, _disposals, _active, _disposalsDuringCall;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public readonly byte[] Bytes = Enumerable.Range(0, 2 * PagedReader.PageSize).Select(i => (byte)((i * 13 + 37) % 251)).ToArray();
        public readonly byte[] OriginalSHA256;
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filecat-page-lifetime-" + Guid.NewGuid().ToString("N"));
        public HeldSource(bool holdRead = true, bool failRead = false, bool holdRefresh = false)
        {
            _holdRead = holdRead; _failRead = failRead; _holdRefresh = holdRefresh;
            File.WriteAllBytes(Path, Bytes);
            OriginalSHA256 = SHA256.HashData(Bytes);
            _file = new FileContentSource(Path);
        }
        public int Reads => Volatile.Read(ref _reads);
        public int Revisions => Volatile.Read(ref _revisions);
        public int Disposals => Volatile.Read(ref _disposals);
        public int DisposalsDuringCall => Volatile.Read(ref _disposalsDuringCall);
        public string DisplayName => Path;
        public long Length => _file.Length;
        public bool CanSeek => true;
        public string? LocalPath => Path;
        public int Read(long offset, Span<byte> buffer)
        {
            int read = Interlocked.Increment(ref _reads);
            Interlocked.Increment(ref _active);
            try
            {
                if (_holdRead && read == 1) Hold();
                int count = _file.Read(offset, buffer);
                if (_failRead) throw new IOException("owned held page read failed");
                return count;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public ContentRevision? GetRevision()
        {
            int revision = Interlocked.Increment(ref _revisions);
            Interlocked.Increment(ref _active);
            try
            {
                if (_holdRefresh && revision > 1) Hold();
                return _file.GetRevision();
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        private void Hold()
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Owned source call was not released.");
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            if (Volatile.Read(ref _active) != 0) Interlocked.Increment(ref _disposalsDuringCall);
            _file.Dispose();
        }
        public void Cleanup()
        {
            _file.Dispose();
            Release.Dispose();
            File.Delete(Path);
        }
    }
}
