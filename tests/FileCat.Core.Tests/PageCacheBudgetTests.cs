using System.Runtime.CompilerServices;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Release issue I06 (plan §21.2: one content cache budget of 64 MiB shared by every view): the page caches of viewers,
/// editors, the quick view and comparisons count against one budget, the least recently used pages go first whichever
/// reader holds them, and the account stays exact through disposal, collection and concurrent use.
/// </summary>
public sealed class PageCacheBudgetTests
{
    private const int Page = PagedReader.PageSize;

    /// <summary>Content of any length that costs nothing to hold; <see cref="Changed"/> makes it read as changed.</summary>
    private sealed class Pattern(long length) : IContentSource
    {
        private long _version;
        public string DisplayName => "pattern";
        public long Length => length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(length, Interlocked.Read(ref _version));
        public void Changed() => Interlocked.Increment(ref _version);
        public void Dispose() { }

        public int Read(long offset, Span<byte> buffer)
        {
            int n = (int)Math.Clamp(length - offset, 0, buffer.Length);
            buffer[..n].Fill((byte)(offset / Page));
            return n;
        }
    }

    private static void ReadPages(PagedReader reader, int first, int count)
    {
        var buffer = new byte[16];
        for (int p = first; p < first + count; p++) Assert.Equal(16, reader.Read((long)p * Page, buffer));
    }

    private static long Cached(IEnumerable<PagedReader> readers) => readers.Sum(r => (long)r.CachedPages * Page);

    [Fact]
    public void Open_viewers_together_stay_within_the_shared_budget()
    {
        // Five viewers and three hex editors (256 pages each), two comparisons (two readers of 64 pages) and the quick
        // view (64 pages), every one read through more than its own limit.
        int[] limits = [256, 256, 256, 256, 256, 256, 256, 256, 64, 64, 64, 64, 64];
        var alone = limits.Select(l => new PagedReader(new Pattern(40L << 20), l, new PageCacheBudget(long.MaxValue))).ToList();
        foreach (var reader in alone) ReadPages(reader, 0, 640);
        // Each reader's own limit is all that bounds it: 148 MiB together.
        Assert.Equal(148L << 20, Cached(alone));
        alone.ForEach(r => r.Dispose());

        var budget = new PageCacheBudget();
        var shared = limits.Select(l => new PagedReader(new Pattern(40L << 20), l, budget)).ToList();
        foreach (var reader in shared) ReadPages(reader, 0, 640);
        Assert.Equal(PageCacheBudget.DefaultLimitBytes, budget.UsedBytes);
        Assert.Equal(budget.UsedBytes, Cached(shared));
        // The last reader read most recently: it kept its own limit, the oldest pages of the others went first.
        Assert.Equal(64, shared[^1].CachedPages);
        shared.ForEach(r => r.Dispose());
        Assert.Equal(0, budget.UsedBytes);
        Assert.Equal(0, budget.Readers);
    }

    [Fact]
    public void The_least_recently_used_pages_go_first_whichever_reader_holds_them()
    {
        var budget = new PageCacheBudget(12 * Page);
        using var a = new PagedReader(new Pattern(1L << 30), budget: budget);
        using var b = new PagedReader(new Pattern(1L << 30), budget: budget);
        using var c = new PagedReader(new Pattern(1L << 30), budget: budget);
        ReadPages(a, 0, 6);
        ReadPages(b, 0, 6);
        Assert.Equal(12L * Page, budget.UsedBytes);
        // A is looked at again, pages 2 to 5 (as a view redraws them): its pages 0 and 1 stay the oldest of all.
        for (int p = 2; p < 6; p++) Assert.True(a.TryRead((long)p * Page, new byte[1], out _));
        ReadPages(c, 0, 4);
        // Four pages had to go: A's two oldest, then B's two oldest.
        Assert.Equal(12L * Page, budget.UsedBytes);
        Assert.Equal(new[] { 2, 3, 4, 5 }, Held(a));
        Assert.Equal(new[] { 2, 3, 4, 5 }, Held(b));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Held(c));
    }

    private static int[] Held(PagedReader reader) => [.. Enumerable.Range(0, 8).Where(p => reader.HasPage(p))];

    [Fact]
    public void No_reader_is_trimmed_below_its_floor()
    {
        var budget = new PageCacheBudget(Page);
        var readers = Enumerable.Range(0, 5).Select(_ => new PagedReader(new Pattern(1L << 30), budget: budget)).ToList();
        foreach (var reader in readers) ReadPages(reader, 0, 10);
        // A screen each, past the budget: the floor is the only thing allowed over it.
        Assert.All(readers, r => Assert.Equal(PagedReader.FloorPages, r.CachedPages));
        Assert.Equal(5L * PagedReader.FloorPages * Page, budget.UsedBytes);
        readers.ForEach(r => r.Dispose());
        Assert.Equal(0, budget.UsedBytes);
    }

    [Fact]
    public void Lowering_the_limit_trims_at_once_and_refreshing_releases()
    {
        var budget = new PageCacheBudget();
        using var reader = new PagedReader(new Pattern(1L << 30), budget: budget);
        ReadPages(reader, 0, 100);
        Assert.Equal(100L * Page, budget.UsedBytes);
        budget.LimitBytes = 10L * Page;
        Assert.Equal(10, reader.CachedPages);
        Assert.Equal(10L * Page, budget.UsedBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.LimitBytes = Page - 1);
    }

    [Fact]
    public void A_reader_dropped_without_disposal_is_written_off()
    {
        var budget = new PageCacheBudget();
        Fill(budget);
        Assert.Equal(20L * Page, budget.UsedBytes);
        for (int i = 0; i < 10 && budget.Readers > 0; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.Equal(0, budget.Readers);
        Assert.Equal(0, budget.UsedBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Fill(PageCacheBudget budget) => ReadPages(new PagedReader(new Pattern(1L << 30), budget: budget), 0, 20);

    [Fact]
    public async Task The_account_stays_exact_under_concurrent_reads_refreshes_and_disposal()
    {
        var budget = new PageCacheBudget(32 * Page);
        var ct = TestContext.Current.CancellationToken;
        var readers = Enumerable.Range(0, 6).Select(_ => new PagedReader(new Pattern(256L << 20), maxPages: 16, budget)).ToArray();
        var stop = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        var workers = readers.Select((reader, n) => Task.Run(() =>
        {
            var rng = new Random(n);
            var buffer = new byte[3 * Page];
            while (DateTime.UtcNow < stop)
            {
                long at = rng.NextInt64(reader.Length - buffer.Length);
                switch (rng.Next(10))
                {
                    case 0:
                        // The file changed: the reader drops its pages, and their charge.
                        ((Pattern)reader.Source).Changed();
                        reader.Refresh();
                        break;
                    case < 5:
                        reader.TryRead(at, buffer, out _);
                        break;
                    default:
                        reader.Read(at, buffer);
                        break;
                }
            }
        }, ct)).ToList();
        // Two readers are disposed while they are being read, their background loads still finishing.
        await Task.Delay(700, ct);
        readers[0].Dispose();
        readers[3].Dispose();
        await Task.WhenAll(workers);
        // Background loads started by TryRead finish on the thread pool: wait until nothing changes.
        long last = -1;
        for (int i = 0; i < 50 && budget.UsedBytes != last; i++)
        {
            last = budget.UsedBytes;
            await Task.Delay(100, ct);
        }
        Assert.Equal(0, readers[0].CachedPages);
        Assert.Equal(0, readers[3].CachedPages);
        Assert.Equal(Cached(readers), budget.UsedBytes);
        Assert.True(budget.UsedBytes <= budget.LimitBytes, $"{budget.UsedBytes / Page} pages cached, limit 32");
        foreach (var reader in readers) reader.Dispose();
        Assert.Equal(0, budget.UsedBytes);
    }
}
