using Avalonia;
using Avalonia.Media;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class IconRequestCacheTests
{
    private sealed class Image : IImage, IDisposable
    {
        public Size Size => new(16, 16);
        public bool Disposed;
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) { }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void A_stalled_worker_bounds_queued_requests_and_rejected_demand_can_retry()
    {
        var cache = new IconRequestCache(capacity: 4096, queueCapacity: 256);
        for (int i = 0; i < 50000; i++) Assert.Null(cache.Get((16, "extension:" + i)));
        Assert.Equal(256, cache.Count);
        Assert.Equal(256, cache.QueuedCount);
        using var requests = cache.Requests(TestContext.Current.CancellationToken).GetEnumerator();
        Assert.True(requests.MoveNext());
        Assert.True(cache.Complete(requests.Current, null));
        Assert.Null(cache.Get((16, "extension:49999")));
        Assert.Equal(257, cache.Count);
        Assert.Equal(256, cache.QueuedCount);
    }

    [Fact]
    public void Cache_eviction_keeps_recent_icons_without_disposing_a_borrowed_image()
    {
        var cache = new IconRequestCache(capacity: 2, queueCapacity: 2);
        using var requests = cache.Requests(TestContext.Current.CancellationToken).GetEnumerator();
        Image Load(string key)
        {
            Assert.Null(cache.Get((16, key)));
            Assert.True(requests.MoveNext());
            var image = new Image();
            Assert.True(cache.Complete(requests.Current, image));
            return image;
        }
        var a = Load("a");
        var b = Load("b");
        Assert.Same(a, cache.Get((16, "a"))); // b is now the oldest.
        var c = Load("c");
        Assert.Equal(2, cache.Count);
        Assert.Same(a, cache.Get((16, "a")));
        Assert.Same(c, cache.Get((16, "c")));
        Assert.False(b.Disposed);
        Assert.Null(cache.Get((16, "b")));
        Assert.Equal(1, cache.QueuedCount);
    }

    [Fact]
    public void Theme_clear_rejects_the_old_result_even_when_the_same_key_is_requested_again()
    {
        var cache = new IconRequestCache(capacity: 2, queueCapacity: 2);
        using var requests = cache.Requests(TestContext.Current.CancellationToken).GetEnumerator();
        cache.Get((16, "folder"));
        Assert.True(requests.MoveNext());
        var old = requests.Current;
        cache.Get((16, "waiting"));
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.QueuedCount);
        cache.Get((16, "folder"));
        var oldImage = new Image();
        Assert.False(cache.Complete(old, oldImage));
        Assert.True(oldImage.Disposed);
        Assert.Null(cache.Get((16, "folder")));
        Assert.True(requests.MoveNext());
        var currentImage = new Image();
        Assert.True(cache.Complete(requests.Current, currentImage));
        Assert.Same(currentImage, cache.Get((16, "folder")));
        Assert.False(currentImage.Disposed);
    }

    [Fact]
    public void An_evicted_active_request_cannot_repopulate_the_cache()
    {
        var cache = new IconRequestCache(capacity: 1, queueCapacity: 2);
        using var requests = cache.Requests(TestContext.Current.CancellationToken).GetEnumerator();
        cache.Get((16, "old"));
        Assert.True(requests.MoveNext());
        var old = requests.Current;
        cache.Get((16, "new"));
        var stale = new Image();
        Assert.False(cache.Complete(old, stale));
        Assert.True(stale.Disposed);
        Assert.Equal(1, cache.Count);
        Assert.True(requests.MoveNext());
        Assert.Equal("new", requests.Current.Key.Key);
        Assert.True(cache.Complete(requests.Current, null));
        Assert.Null(cache.Get((16, "new")));
        Assert.Equal(0, cache.QueuedCount); // a known unavailable icon is cached, rather than repeatedly loaded.
    }

    [Fact]
    public void Evicted_queued_work_is_skipped_before_native_loading()
    {
        var cache = new IconRequestCache(capacity: 1, queueCapacity: 2);
        cache.Get((16, "old"));
        cache.Get((16, "new"));
        using var requests = cache.Requests(TestContext.Current.CancellationToken).GetEnumerator();
        Assert.True(requests.MoveNext());
        Assert.Equal("new", requests.Current.Key.Key);
        Assert.Equal(0, cache.QueuedCount);
    }

    [Fact]
    public async Task Concurrent_demand_and_worker_completions_keep_both_bounds()
    {
        var cache = new IconRequestCache(capacity: 128, queueCapacity: 32);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int completed = 0;
        var worker = Task.Run(() =>
        {
            try
            {
                foreach (var request in cache.Requests(stop.Token))
                {
                    var image = new Image();
                    bool published = cache.Complete(request, image);
                    Assert.Equal(!published, image.Disposed);
                    Interlocked.Increment(ref completed);
                    Assert.InRange(cache.Count, 0, 128);
                    Assert.InRange(cache.QueuedCount, 0, 32);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }, TestContext.Current.CancellationToken);
        try
        {
            Parallel.For(0, 32, producer =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    cache.Get((16, "producer:" + producer + ":" + i));
                    Assert.InRange(cache.Count, 0, 128);
                    Assert.InRange(cache.QueuedCount, 0, 32);
                }
            });
            Assert.True(SpinWait.SpinUntil(() => cache.QueuedCount == 0, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            stop.Cancel();
            await worker.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        Assert.True(completed > 0);
        Assert.Equal(0, cache.QueuedCount);
        Assert.InRange(cache.Count, 1, 128);
    }
}
