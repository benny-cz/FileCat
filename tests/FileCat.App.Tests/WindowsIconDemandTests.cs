using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Platform;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>V12: actual Windows icon source demand and late completion, without a drawn desktop.</summary>
public sealed class WindowsIconDemandTests
{
    private sealed class Image : IImage, IDisposable
    {
        public Size Size => new(16, 16);
        public volatile bool Disposed;
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) { }
        public void Dispose() => Disposed = true;
    }

    private sealed class Loads
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started, Active, Peak;
        public Image? Image;
        public bool AskAgain;
    }

    private static NativeIconSource Source()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Native Windows icon demand needs Windows.");
        return (NativeIconSource)NativeIconSource.TryCreate(new PortableShellServices())!;
    }

    // Invoke the existing production request path. Only the native load's result is controlled, so admission,
    // cache eviction, size changes and completion publication all execute the real NativeIconSource code.
    private static object? Request(NativeIconSource source, string key, Loads loads)
    {
        var method = typeof(NativeIconSource).GetMethod("PerItem", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var planType = method.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments()[0];
        var factory = typeof(WindowsIconDemandTests).GetMethod(nameof(Factory), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(planType).Invoke(null, [loads]);
        return method.Invoke(source, [key, factory]);
    }

    private static Func<Task<T>> Factory<T>(Loads loads) => async () =>
    {
        Interlocked.Increment(ref loads.Started);
        int active = Interlocked.Increment(ref loads.Active), peak;
        do { peak = Volatile.Read(ref loads.Peak); }
        while (active > peak && Interlocked.CompareExchange(ref loads.Peak, active, peak) != peak);
        try
        {
            await loads.Release.Task;
            return (T)Activator.CreateInstance(typeof(T), [loads.Image, null, loads.AskAgain])!;
        }
        finally { Interlocked.Decrement(ref loads.Active); }
    };

    private static int Count(NativeIconSource source, string field)
    {
        var cache = typeof(NativeIconSource).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
        return (int)cache.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(cache)!;
    }

    private static bool Contains(NativeIconSource source, string key)
    {
        var cache = typeof(NativeIconSource).GetField("_perItem", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
        var gate = cache.GetType().GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
        lock (gate)
        {
            var entries = cache.GetType().GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
            return (bool)entries.GetType().GetMethod("ContainsKey")!.Invoke(entries, [(source.PixelSize, key)])!;
        }
    }

    private static IImage? Picture(object? plan) =>
        (IImage?)plan?.GetType().GetProperty("Image")!.GetValue(plan);

    private static async Task Wait(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            // Accept the observed sample. A second predicate call can start more demand and invalidate it.
            if (condition()) return;
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "The owned icon load did not reach its checkpoint.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task A_checkpoint_accepts_one_idle_sample_before_new_icon_demand_starts()
    {
        var source = Source(); var next = new Loads(); int samples = 0;
        try
        {
            await Wait(() =>
            {
                int sample = ++samples;
                bool idle = Volatile.Read(ref next.Active) == 0;
                if (sample == 1)
                {
                    Request(source, "checkpoint-next", next);
                    Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref next.Active) == 1,
                        TimeSpan.FromSeconds(3)), "Owned follow-up icon demand did not enter");
                }
                TestContext.Current.TestOutputHelper?.WriteLine("ICON_CHECKPOINT_SAMPLE " +
                    System.Text.Json.JsonSerializer.Serialize(new { sample, idle,
                        StartedAfterSample = Volatile.Read(ref next.Started), ActiveAfterSample = Volatile.Read(ref next.Active),
                        ActualNativeIconSource = true, ControlledLoadResult = true,
                        NativeDesktop = false, PhysicalSource = false, NativeHelperExercised = false }));
                return idle;
            });
            Assert.Equal(1, samples); Assert.Equal(1, next.Started); Assert.Equal(1, next.Active);
        }
        finally { next.Release.TrySetResult(); await Wait(() => Volatile.Read(ref next.Active) == 0); }
    }

    [AvaloniaFact]
    public void Fifty_thousand_type_requests_keep_the_native_shared_cache_bounded()
    {
        var source = Source();
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 50000; i++)
            source.GetIcon(new EntryData { Kind = EntryKind.File, Name = "item.filecat-budget-" + i });
        int retained = Count(source, "_shared");
        TestContext.Current.TestOutputHelper?.WriteLine($"Actual type requests 50000; retained {retained}; request time {timer.Elapsed}.");
        Assert.InRange(retained, 1, 4096);
    }

    [AvaloniaFact]
    public async Task Held_per_item_demand_starts_at_most_four_loads_and_overflow_can_retry()
    {
        var source = Source();
        var held = new Loads();
        try
        {
            for (int i = 0; i < 512; i++) Request(source, "held|" + i, held);
            await Wait(() => Volatile.Read(ref held.Started) >= 4);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            TestContext.Current.TestOutputHelper?.WriteLine($"Held native loads {held.Active}; retained requests {Count(source, "_perItem")}.");
            Assert.Equal(4, Volatile.Read(ref held.Active));
            Assert.InRange(Count(source, "_perItem"), 4, 260);
        }
        finally { held.Release.TrySetResult(); await Wait(() => Volatile.Read(ref held.Active) == 0); }
        var retry = new Loads { Image = new Image() };
        retry.Release.SetResult();
        await Wait(() => Picture(Request(source, "held|511", retry)) is not null);
        Assert.Same(retry.Image, Picture(Request(source, "held|511", retry)));
        Assert.Equal(1, retry.Started);
        Assert.False(retry.Image.Disposed);
        Assert.InRange(held.Peak, 1, 4);
    }

    [AvaloniaFact]
    public async Task Old_load_cannot_return_after_pixel_size_changes_away_and_back()
    {
        var source = Source();
        var old = new Loads { Image = new Image() };
        var fresh = new Loads { Image = new Image() };
        fresh.Release.SetResult();
        try
        {
            Request(source, "same", old);
            await Wait(() => Volatile.Read(ref old.Active) == 1);
            source.SetPixelSize(32);
            source.SetPixelSize(16);
            await Wait(() => Picture(Request(source, "same", fresh)) is not null);
            Assert.Same(fresh.Image, Picture(Request(source, "same", fresh)));
            old.Release.SetResult();
            await Wait(() => Volatile.Read(ref old.Active) == 0);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Same(fresh.Image, Picture(Request(source, "same", fresh)));
            Assert.True(old.Image.Disposed);
            Assert.False(fresh.Image.Disposed);
        }
        finally { old.Release.TrySetResult(); await Wait(() => Volatile.Read(ref old.Active) == 0); }
    }

    [AvaloniaFact]
    public async Task Completing_evicted_requests_cannot_repopulate_more_than_the_cache_budget()
    {
        var source = Source();
        var held = new Loads { Image = new Image() };
        var published = new Loads(); published.Release.SetResult();
        try
        {
            Request(source, "evicted-held", held);
            await Wait(() => Volatile.Read(ref held.Active) == 1);
            // A held four-worker queue admits at most 260 entries, so flooding it never reaches the 4096-entry
            // eviction boundary. Publish each fresh key while one older load remains held to force real eviction.
            for (int i = 0; i < 4096; i++)
            {
                string key = "published|" + i;
                await Wait(() => Request(source, key, published) is not null);
            }
            int before = Count(source, "_perItem"); bool heldRetainedBefore = Contains(source, "evicted-held");
            Assert.Equal(4096, before); Assert.Equal(4096, published.Started);
            Assert.False(heldRetainedBefore); Assert.False(held.Image.Disposed);
            held.Release.SetResult();
            await Wait(() => held.Image.Disposed && Volatile.Read(ref held.Active) == 0);
            int after = Count(source, "_perItem"); bool heldRetainedAfter = Contains(source, "evicted-held");
            TestContext.Current.TestOutputHelper?.WriteLine("ICON_REAL_EVICTION " +
                System.Text.Json.JsonSerializer.Serialize(new { PublishedStarts = published.Started,
                    HeldStarts = held.Started, before, after, heldRetainedBefore, heldRetainedAfter,
                    UnpublishedImageDisposed = held.Image.Disposed, DefaultCapacity = 4096,
                    ActualNativeIconSource = true, ControlledLoadResult = true, NativeDesktop = false,
                    PhysicalSource = false, NativeHelperExercised = false }));
            Assert.Equal(4096, after); Assert.False(heldRetainedAfter); Assert.Equal(1, held.Started);
        }
        finally { held.Release.TrySetResult(); await Wait(() => Volatile.Read(ref held.Active) == 0); }
    }

    [AvaloniaFact]
    public async Task Failed_helper_answer_is_forgotten_and_a_fresh_answer_can_be_borrowed()
    {
        var source = Source();
        var failed = new Loads { AskAgain = true };
        failed.Release.SetResult();
        Request(source, "retry", failed);
        await Wait(() => failed.Started == 1 && failed.Active == 0);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var healthy = new Loads { Image = new Image() };
        healthy.Release.SetResult();
        await Wait(() => Picture(Request(source, "retry", healthy)) is not null);
        Assert.Same(healthy.Image, Picture(Request(source, "retry", healthy)));
        Assert.Equal(1, healthy.Started);
        Assert.False(healthy.Image.Disposed);
    }
}
