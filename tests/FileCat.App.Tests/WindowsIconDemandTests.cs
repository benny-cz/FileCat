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
        public bool Disposed;
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

    private static IImage? Picture(object? plan) =>
        (IImage?)plan?.GetType().GetProperty("Image")!.GetValue(plan);

    private static async Task Wait(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "The owned icon load did not reach its checkpoint.");
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
        var loads = new Loads();
        try
        {
            for (int i = 0; i < 5000; i++) Request(source, "eviction|" + i, loads);
            await Wait(() => Volatile.Read(ref loads.Started) >= 4);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            loads.Release.SetResult();
            await Wait(() => Volatile.Read(ref loads.Active) == 0);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            int retained = Count(source, "_perItem");
            TestContext.Current.TestOutputHelper?.WriteLine($"Actual requests 5000; completed starts {loads.Started}; peak active {loads.Peak}; retained {retained}.");
            Assert.InRange(retained, 1, 4096);
        }
        finally { loads.Release.TrySetResult(); await Wait(() => Volatile.Read(ref loads.Active) == 0); }
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
