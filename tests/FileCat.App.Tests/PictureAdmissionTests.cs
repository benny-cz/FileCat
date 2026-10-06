using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using SkiaSharp;

namespace FileCat.App.Tests;

/// <summary>Many viewers must not start a decoder for every held picture feed.</summary>
public sealed class PictureAdmissionTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 40)]
    [InlineData(true, 40)]
    public async Task Held_picture_feeds_share_a_worker_limit_and_canceled_waiters_never_read(bool scheduled, int requests)
    {
        // Four feeders and four pipe readers deliberately block together. This serialized fixture tests admission,
        // not the cold pool's thread-injection delay; restore its process-wide minimum after all work has drained.
        using var pool = new BlockingPoolCapacity(output);
        string root = Path.Combine(Path.GetTempPath(), "filecat-picture-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        output.WriteLine($"Owned fixture: {root}");
        var sources = Enumerable.Range(0, requests).Select(i => new Source(root, i, hold: true)).ToArray();
        var readers = sources.Select(s => new PagedReader(s)).ToArray();
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromMinutes(1));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<DecodedPicture>[] pending = sources.Select((s, i) => scheduled
            ? PictureDecoder.DecodeAsync(readers[i], io, "owned-picture-" + i, 64, cancel.Token)
            : PictureDecoder.DecodeAsync(s, 64, cancel.Token)).ToArray();
        try
        {
            var clock = Stopwatch.StartNew();
            while (sources.Count(s => s.Entered.Task.IsCompleted) < 4)
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(10))
                {
                    output.WriteLine($"Checkpoint after {clock.Elapsed}: entered={sources.Count(s => s.Entered.Task.IsCompleted)}, " +
                        $"pool threads={ThreadPool.ThreadCount}, queued={ThreadPool.PendingWorkItemCount}; " +
                        string.Join("; ", pending.Select((p, i) => $"request {i}={p.Status} {p.Exception}")));
                    throw new TimeoutException("Four owned picture feeds did not enter.");
                }
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine($"Four held feeds entered after {clock.Elapsed.TotalMilliseconds:0} ms; pool threads={ThreadPool.ThreadCount}, queued={ThreadPool.PendingWorkItemCount}.");
            // Every started feeder stays held, so later requests cannot complete and free a slot during this observation.
            await Task.Delay(750, TestContext.Current.CancellationToken);
            int entered = sources.Count(s => s.Entered.Task.IsCompleted);
            output.WriteLine($"Scheduled={scheduled}; {requests} simultaneous owned PNG requests; {entered} held feeds entered before release.");
            Assert.Equal(4, entered);
            int refused = Math.Max(0, requests - 36);
            Assert.Equal(refused, pending.Count(p => p.IsFaulted));
            foreach (var p in pending.Where(p => p.IsFaulted))
                Assert.Contains("too many pictures", (await Assert.ThrowsAsync<IOException>(() => p)).Message, StringComparison.Ordinal);
            cancel.Cancel();
            if (scheduled)
                await Task.WhenAll(pending.Where(p => !p.IsFaulted).Select(async p => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p)))
                    .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.All(sources.Where(s => !s.Entered.Task.IsCompleted), s => Assert.Equal(0, s.Reads));
        }
        finally
        {
            cancel.Cancel();
            foreach (var source in sources) source.Release.Set();
            foreach (var p in pending)
            {
                try { (await p.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Bitmap.Dispose(); }
                catch (OperationCanceledException) { }
                catch (IOException ex) when (ex.Message.Contains("too many pictures", StringComparison.Ordinal)) { }
            }
            // Scheduled cancellation retires demand before a held synchronous read has returned.
            var drain = Stopwatch.StartNew();
            while (sources.Any(s => s.ActiveReads != 0))
            {
                if (drain.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Released owned picture feeds did not return.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            foreach (var reader in readers) reader.Dispose();
            foreach (var source in sources)
            {
                Assert.Equal(0, source.ActiveReads);
                Assert.Equal(source.Hash, SHA256.HashData(File.ReadAllBytes(source.Path)));
                source.Dispose();
                source.Release.Dispose();
                File.Delete(source.Path);
            }
            Directory.Delete(root);
        }
        // Freed admission still accepts valid input, with the worker's real decoding and returned bitmap intact.
        using var healthy = new MemoryContentSource("healthy.png", Encode());
        var picture = await PictureDecoder.DecodeAsync(healthy, 64, TestContext.Current.CancellationToken);
        try { Assert.Equal((12, 8, "PNG"), (picture.Width, picture.Height, picture.Format)); }
        finally { picture.Bitmap.Dispose(); }
    }

    private sealed class BlockingPoolCapacity : IDisposable
    {
        private readonly int _workers, _completionPorts;

        public BlockingPoolCapacity(ITestOutputHelper output)
        {
            ThreadPool.GetMinThreads(out _workers, out _completionPorts);
            int reserved = Math.Max(_workers, 12); // eight held calls, plus the runner and cancellation continuations
            if (!ThreadPool.SetMinThreads(reserved, _completionPorts))
                throw new InvalidOperationException("The owned picture fixture cannot reserve its blocking pool capacity.");
            output.WriteLine($"Owned blocking pool minimum: {_workers} -> {reserved}; completion-port minimum {_completionPorts} unchanged.");
        }

        public void Dispose()
        {
            if (!ThreadPool.SetMinThreads(_workers, _completionPorts))
                throw new InvalidOperationException("The owned picture fixture could not restore its pool minimum.");
            ThreadPool.GetMinThreads(out int workers, out int completionPorts);
            Assert.Equal((_workers, _completionPorts), (workers, completionPorts));
        }
    }

    private static byte[] Encode()
    {
        using var bitmap = new SKBitmap(12, 8);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private sealed class Source : IContentSource
    {
        private readonly FileContentSource _file;
        private int _reads, _active;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public string Path { get; }
        public byte[] Hash { get; }
        private readonly bool _hold;
        public Source(string root, int i, bool hold)
        {
            _hold = hold;
            Path = System.IO.Path.Combine(root, $"picture-{i}.png");
            var bytes = Encode();
            File.WriteAllBytes(Path, bytes);
            Hash = SHA256.HashData(bytes);
            _file = new FileContentSource(Path);
        }
        public int Reads => Volatile.Read(ref _reads);
        public int ActiveReads => Volatile.Read(ref _active);
        public string DisplayName => Path;
        public long Length => _file.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => _file.GetRevision();
        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _reads);
            Interlocked.Increment(ref _active);
            try
            {
                Entered.TrySetResult();
                if (_hold && !Release.Wait(TimeSpan.FromSeconds(25))) throw new IOException("Owned picture feed was not released.");
                return _file.Read(offset, buffer);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose() => _file.Dispose();
    }
}
