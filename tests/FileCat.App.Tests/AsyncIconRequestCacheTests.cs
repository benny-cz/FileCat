using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class AsyncIconRequestCacheTests
{
    private sealed class Result : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task Full_waiting_queue_does_not_invoke_rejected_load_and_can_retry()
    {
        var cache = new AsyncIconRequestCache<Result>(r => r.Dispose(), capacity: 8, queueCapacity: 2);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var reader = cache.Requests(stop.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        int rejectedStarts = 0;
        Task<Result> Rejected() { rejectedStarts++; return Task.FromResult(new Result()); }
        cache.Get((16, "active"), () => Task.FromResult(new Result()));
        Assert.True(await reader.MoveNextAsync());
        var active = reader.Current;
        cache.Get((16, "waiting-one"), () => Task.FromResult(new Result()));
        cache.Get((16, "waiting-two"), () => Task.FromResult(new Result()));
        Assert.Null(cache.Get((16, "retry"), Rejected));
        Assert.Equal(2, cache.QueuedCount);
        Assert.Equal(3, cache.Count);
        Assert.Equal(0, rejectedStarts);
        Assert.True(cache.Complete(active, await active.Load()));
        for (int i = 0; i < 2; i++)
        {
            Assert.True(await reader.MoveNextAsync());
            Assert.True(cache.Complete(reader.Current, await reader.Current.Load()));
        }
        Assert.Null(cache.Get((16, "RETRY"), Rejected));
        Assert.True(await reader.MoveNextAsync());
        var healthy = await reader.Current.Load();
        Assert.True(cache.Complete(reader.Current, healthy));
        Assert.Same(healthy, cache.Get((16, "retry"), Rejected));
        Assert.Equal(1, rejectedStarts);
        Assert.False(healthy.Disposed);
    }

    [Fact]
    public async Task A_late_failed_answer_cannot_forget_a_same_key_replacement()
    {
        var cache = new AsyncIconRequestCache<Result>(r => r.Dispose(), capacity: 4, queueCapacity: 4);
        await using var reader = cache.Requests(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        cache.Get((16, "C:\\owned\\item"), () => Task.FromResult(new Result()));
        Assert.True(await reader.MoveNextAsync());
        var old = reader.Current;
        cache.Clear();
        cache.Get((16, "c:\\OWNED\\item"), () => Task.FromResult(new Result()));
        Assert.True(await reader.MoveNextAsync());
        var replacement = await reader.Current.Load();
        Assert.True(cache.Complete(reader.Current, replacement));
        var late = await old.Load();
        Assert.False(cache.Complete(old, late, retry: true));
        Assert.True(late.Disposed);
        Assert.Same(replacement, cache.Get((16, "C:\\owned\\ITEM"), () => throw new InvalidOperationException("Cached answer should be reused.")));
        Assert.False(replacement.Disposed);
        Assert.Equal(1, cache.Count);
        Assert.Equal(0, cache.QueuedCount);
    }

    [Fact]
    public async Task Fifty_thousand_completed_icons_keep_recent_and_borrowed_results_within_budget()
    {
        var cache = new AsyncIconRequestCache<Result>(r => r.Dispose(), capacity: 128, queueCapacity: 32);
        await using var reader = cache.Requests(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        async Task<Result> Load(string key)
        {
            Assert.Null(cache.Get((16, key), () => Task.FromResult(new Result())));
            Assert.True(await reader.MoveNextAsync());
            var result = await reader.Current.Load();
            Assert.True(cache.Complete(reader.Current, result));
            return result;
        }
        var borrowed = await Load("first");
        var recent = await Load("recent");
        for (int i = 0; i < 50000; i++)
        {
            await Load("icon:" + i);
            Assert.Same(recent, cache.Get((16, "RECENT"), () => throw new InvalidOperationException("Recent icon should be reused.")));
            Assert.InRange(cache.Count, 1, 128);
        }
        Assert.Equal(128, cache.Count);
        Assert.Equal(0, cache.QueuedCount);
        Assert.False(borrowed.Disposed); // An evicted image can still be displayed by a row.
        Assert.False(recent.Disposed);
    }
}
