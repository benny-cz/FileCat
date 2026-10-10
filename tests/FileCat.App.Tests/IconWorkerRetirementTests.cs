using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class IconWorkerRetirementTests
{
    public static TheoryData<bool, string, bool, int> Cases
    {
        get
        {
            var cases = new TheoryData<bool, string, bool, int>();
            foreach (bool asynchronous in new[] { false, true })
            foreach (string route in new[] { "clear", "retained" })
            foreach (bool borrower in new[] { false, true })
            foreach (int size in new[] { 16, 32, 64 }) cases.Add(asynchronous, route, borrower, size);
            return cases;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Idle_workers_release_retired_images_and_preserve_real_borrowers(
        bool asynchronous, string route, bool borrower, int size)
    {
        using var worker = new Worker(asynchronous, size);
        await worker.WaitForIdle();
        var held = new Borrower();
        if (borrower) CaptureBorrower(worker, held);
        worker.Retire(route);
        Collect();
        bool beforeRelease = Alive(worker.Image!);
        bool pixelsExact = !borrower || CheckBorrower(held, size);
        bool borrowerUsable = !borrower || CheckUsable(held, size);
        DropBorrower(held);
        Collect();
        bool afterRelease = Alive(worker.Image!);
        TestContext.Current.TestOutputHelper?.WriteLine("ICON_WORKER_RETIREMENT " + JsonSerializer.Serialize(new
        {
            asynchronous, route, borrower, size, pixelBytes = size * size * 4,
            expectedSHA256 = Convert.ToHexString(SHA256.HashData(Pixels(size))),
            beforeRelease, borrowerUsable, pixelsExact, afterRelease,
            cacheCount = worker.Count(), queuedCount = worker.Queued(), completed = worker.Completed,
            actualBlockedEnumerator = true, workerStillLive = !worker.Done.IsCompleted,
            publishedBorrowersAreNotDisposed = true, noVisualOrDesktopInput = true,
            managedBitmapReachabilityNotNativeProcessMemory = true,
        }));
        Assert.True(borrowerUsable);
        Assert.Equal(route == "retained" || borrower, beforeRelease);
        Assert.Equal(route == "retained", afterRelease);
        Assert.Equal(route == "clear" ? 0 : 1, worker.Count());
        Assert.Equal(0, worker.Queued());
        Assert.Equal(1, worker.Completed);
        Assert.False(worker.Done.IsCompleted);
        GC.KeepAlive(worker);
    }

    private sealed class Borrower { internal IImage? Image; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CaptureBorrower(Worker worker, Borrower held) => held.Image = worker.Get();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DropBorrower(Borrower held) => held.Image = null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CheckBorrower(Borrower held, int size)
    {
        var bitmap = Assert.IsType<WriteableBitmap>(held.Image);
        byte[] actual = new byte[size * size * 4];
        var handle = GCHandle.Alloc(actual, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, size, size), handle.AddrOfPinnedObject(), actual.Length, size * 4); }
        finally { handle.Free(); }
        return actual.SequenceEqual(Pixels(size));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CheckUsable(Borrower held, int size)
    {
        var bitmap = Assert.IsType<WriteableBitmap>(held.Image);
        Assert.Equal(new PixelSize(size, size), bitmap.PixelSize);
        using var locked = bitmap.Lock();
        return locked.Address != IntPtr.Zero && locked.RowBytes >= size * 4;
    }

    private static byte[] Pixels(int size) => Enumerable.Range(0, size * size * 4).Select(i => (byte)(i * 37 + 11)).ToArray();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IImage NewImage(int size)
    {
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        byte[] pixels = Pixels(size);
        using var locked = bitmap.Lock();
        for (int row = 0; row < size; row++) Marshal.Copy(pixels, row * size * 4, locked.Address + row * locked.RowBytes, size * 4);
        return bitmap;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Alive(WeakReference<IImage> image) => image.TryGetTarget(out _);

    private static void Collect()
    {
        for (int i = 0; i < 4; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    }

    private sealed class Worker : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread? _thread;
        private readonly IconRequestCache? _sync;
        private readonly AsyncIconRequestCache<IImage>? _async;
        private readonly int _size;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Done => _done.Task;
        internal WeakReference<IImage>? Image;
        internal int Completed;
        internal Func<int> Count;
        internal Func<int> Queued;

        internal Worker(bool asynchronous, int size)
        {
            _size = size;
            if (asynchronous)
            {
                _async = new AsyncIconRequestCache<IImage>(image => (image as IDisposable)?.Dispose(), capacity: 1, queueCapacity: 2);
                _async.Get((size, "owned"), () => Task.FromResult(NewImage(size)));
                Count = () => _async.Count;
                Queued = () => _async.QueuedCount;
                _ = Task.Run(RunAsync);
            }
            else
            {
                _sync = new IconRequestCache(capacity: 1, queueCapacity: 2);
                _sync.Get((size, "owned"));
                Count = () => _sync.Count;
                Queued = () => _sync.QueuedCount;
                _thread = new Thread(RunSync) { IsBackground = true, Name = "FileCat owned icon-retirement control" };
                _thread.Start();
            }
        }

        internal async Task WaitForIdle()
        {
            await _published.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (_thread is not null)
                Assert.True(SpinWait.SpinUntil(() => (_thread.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10)));
            else await _idle.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(Done.IsCompleted);
        }

        internal IImage? Get() => _sync is not null ? _sync.Get((_size, "owned")) : _async!.Get((_size, "owned"), () => throw new InvalidOperationException("An existing publication is reused."));

        internal void Retire(string route)
        {
            if (route == "clear") { _sync?.Clear(); _async?.Clear(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Publish(IconRequestCache.Request request)
        {
            var image = NewImage(_size);
            Image = new WeakReference<IImage>(image);
            Assert.True(_sync!.Complete(request, image));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Publish(AsyncIconRequestCache<IImage>.Request request, IImage image)
        {
            Image = new WeakReference<IImage>(image);
            Assert.True(_async!.Complete(request, image));
        }

        private async Task LoadAndPublish(AsyncIconRequestCache<IImage>.Request request) => Publish(request, await request.Load());

        private void RunSync()
        {
            try
            {
                foreach (var request in _sync!.Requests(_stop.Token))
                {
                    Publish(request);
                    Interlocked.Increment(ref Completed);
                    _published.TrySetResult();
                }
                _done.TrySetResult();
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { _done.TrySetResult(); }
            catch (Exception ex) { _published.TrySetException(ex); _done.TrySetException(ex); }
        }

        private async Task RunAsync()
        {
            try
            {
                await using var reader = _async!.Requests(_stop.Token).GetAsyncEnumerator(_stop.Token);
                while (true)
                {
                    var next = reader.MoveNextAsync();
                    if (!next.IsCompleted) _idle.TrySetResult();
                    if (!await next) break;
                    await LoadAndPublish(reader.Current);
                    Interlocked.Increment(ref Completed);
                    _published.TrySetResult();
                }
                _done.TrySetResult();
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { _done.TrySetResult(); }
            catch (Exception ex) { _published.TrySetException(ex); _done.TrySetException(ex); }
        }

        public void Dispose()
        {
            _stop.Cancel();
            Assert.True(Done.Wait(TimeSpan.FromSeconds(10)));
            _thread?.Join();
            _stop.Dispose();
        }
    }
}
