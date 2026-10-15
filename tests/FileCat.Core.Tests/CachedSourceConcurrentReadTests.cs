using FileCat.Recovery;

namespace FileCat.Core.Tests;

public sealed class CachedSourceConcurrentReadTests
{
    [Fact]
    public async Task A_different_block_can_load_while_one_source_read_is_held()
    {
        using var source = new IndependentBlocksSource();
        using var cache = new CachedSource(source, blockSize: 16, maxBlocks: 4);
        var first = Task.Factory.StartNew(() =>
        {
            byte[] bytes = new byte[4];
            Assert.Equal(4, cache.Read(0, bytes));
            return bytes;
        }, TaskCreationOptions.LongRunning);
        try
        {
            Assert.True(source.FirstReadStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var second = Task.Factory.StartNew(() =>
            {
                byte[] bytes = new byte[4];
                Assert.Equal(4, cache.Read(16, bytes));
                return bytes;
            }, TaskCreationOptions.LongRunning);
            Assert.Equal(new byte[] { 2, 2, 2, 2 }, await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally { source.ReleaseFirstRead.Set(); }
        Assert.Equal(new byte[] { 1, 1, 1, 1 }, await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_failed_block_read_does_not_poison_the_next_attempt()
    {
        using var source = new FailOnceSource();
        using var cache = new CachedSource(source, blockSize: 16, maxBlocks: 4);
        var bytes = new byte[4];
        Assert.Throws<IOException>(() => cache.Read(0, bytes));
        Assert.Equal(4, cache.Read(0, bytes));
        Assert.Equal(new byte[] { 3, 3, 3, 3 }, bytes);
        Assert.Equal(2, source.ReadCount);
    }

    [Fact]
    public async Task Readers_of_one_uncached_block_receive_one_version_of_its_bytes()
    {
        using var source = new ChangingSource();
        using var cache = new CachedSource(source, blockSize: 16, maxBlocks: 4);
        using var secondStarted = new ManualResetEventSlim();

        static byte[] Read(CachedSource cache)
        {
            byte[] bytes = new byte[4];
            Assert.Equal(bytes.Length, cache.Read(0, bytes));
            return bytes;
        }

        var first = Task.Factory.StartNew(() => Read(cache), TaskCreationOptions.LongRunning);
        try
        {
            Assert.True(source.FirstReadStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "The first block load did not start.");
            var second = Task.Factory.StartNew(() =>
            {
                secondStarted.Set();
                return Read(cache);
            }, TaskCreationOptions.LongRunning);
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "The second reader did not start.");
            // Before the correction, this second source read completes while the first is held.
            // After the correction, it waits on that one in-flight block instead.
            source.SecondReadStarted.Wait(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
            source.ReleaseFirstRead.Set();

            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(1, source.ReadCount);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, results[0]);
            Assert.Equal(results[0], results[1]);
        }
        finally
        {
            source.ReleaseFirstRead.Set();
        }
    }

    private sealed class ChangingSource : IBlockSource
    {
        private int _readCount;

        public ManualResetEventSlim FirstReadStarted { get; } = new();
        public ManualResetEventSlim SecondReadStarted { get; } = new();
        public ManualResetEventSlim ReleaseFirstRead { get; } = new();
        public int ReadCount => Volatile.Read(ref _readCount);
        public string Description => "controlled changing recovery source";
        public long Length => 16;

        public int Read(long offset, Span<byte> buffer)
        {
            Assert.Equal(0, offset);
            int call = Interlocked.Increment(ref _readCount);
            if (call == 1)
            {
                FirstReadStarted.Set();
                if (!ReleaseFirstRead.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The first controlled read was never released.");
                buffer.Fill(0);
                new byte[] { 1, 2, 3, 4 }.CopyTo(buffer);
            }
            else
            {
                SecondReadStarted.Set();
                buffer.Fill(0);
                new byte[] { 5, 6, 7, 8 }.CopyTo(buffer);
            }
            return buffer.Length;
        }

        public void Dispose()
        {
            FirstReadStarted.Dispose();
            SecondReadStarted.Dispose();
            ReleaseFirstRead.Dispose();
        }
    }

    private sealed class IndependentBlocksSource : IBlockSource
    {
        public ManualResetEventSlim FirstReadStarted { get; } = new();
        public ManualResetEventSlim ReleaseFirstRead { get; } = new();
        public string Description => "two independent recovery blocks";
        public long Length => 32;

        public int Read(long offset, Span<byte> buffer)
        {
            if (offset == 0)
            {
                FirstReadStarted.Set();
                if (!ReleaseFirstRead.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The first block remained held.");
                buffer.Fill(1);
            }
            else if (offset == 16) buffer.Fill(2);
            else throw new InvalidOperationException($"Unexpected source offset {offset}.");
            return buffer.Length;
        }

        public void Dispose()
        {
            FirstReadStarted.Dispose();
            ReleaseFirstRead.Dispose();
        }
    }

    private sealed class FailOnceSource : IBlockSource
    {
        private int _readCount;
        public int ReadCount => _readCount;
        public string Description => "recoverable read failure";
        public long Length => 16;
        public int Read(long offset, Span<byte> buffer)
        {
            Assert.Equal(0, offset);
            if (++_readCount == 1) throw new IOException("Controlled first read failure.");
            buffer.Fill(3);
            return buffer.Length;
        }
        public void Dispose() { }
    }
}
