using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using SkiaSharp;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

/// <summary>V12: a picture feeder owns its actual source call after its window's demand is canceled.</summary>
public sealed class PictureLifetimeTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Closing_a_picture_retires_demand_before_its_active_source_read_returns(bool quickView, bool failRead, bool priorHeaderRead)
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var source = new HeldPictureSource(root, hold: true, failRead);
        // Initial text rendering and encoding detection can each request the header before picture mode starts.
        // A controlled extra header also checks that the fixture holds the feeder rather than the second read.
        if (priorHeaderRead) source.Read(0, new byte[64 * 1024]);
        var pane = new QuickViewPane();
        ViewerWindow? viewer = null;
        try
        {
            if (quickView)
            {
                services.Providers.Register(new PictureProvider(source));
                var tab = vm.ActiveTab!;
                tab.Navigate(new Location("picturefixture", "owned"));
                await WaitFor(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("owned.png"));
                pane.Attach(tab);
                await Load(pane);
            }
            else
            {
                viewer = new ViewerWindow(services, source, "owned.png", hex: false);
                viewer.Show();
            }
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(1, source.FeederReads);
            Assert.True(source.Reads >= (priorHeaderRead ? 3 : 2));
            int readsWhileHeld = source.Reads;
            var clock = Stopwatch.StartNew();
            if (quickView) pane.Attach(null);
            else
            {
                viewer!.Close();
                Assert.NotNull(viewer.PictureLoad);
                await viewer.PictureLoad.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            }
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3));
            Assert.False(source.Release.IsSet);
            Assert.Equal(0, source.Disposals);
            Assert.Equal(0, source.DisposalsDuringRead);
            source.Release.Set();
            await source.ReadExited.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await WaitFor(() => source.Disposals == 1);
            Assert.Equal(readsWhileHeld, source.Reads);
            Assert.Equal(1, source.FeederReads);
            Assert.Equal(0, source.DisposalsDuringRead);
            Assert.Null(viewer?.Picture);
            Assert.Equal(source.OriginalHash, SHA256.HashData(File.ReadAllBytes(source.Path)));
            output.WriteLine($"Quick view {quickView}, read failure {failRead}, prior header {priorHeaderRead}: cancellation precedes release; {readsWhileHeld} total reads; one feeder read; no later reads; one disposal after return; unchanged owned PNG {Convert.ToHexString(source.OriginalHash)}.");
        }
        finally
        {
            pane.Attach(null);
            viewer?.Close();
            source.Release.Set();
            if (source.Entered.Task.IsCompleted)
                await source.ReadExited.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (viewer?.PictureLoad is { } loading) await loading;
            source.Cleanup();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task Closing_a_loaded_picture_releases_its_bitmap_and_source()
    {
        var (services, _, main, root) = AccessibilityTests.OpenMainWindow();
        var source = new HeldPictureSource(root, hold: false);
        var viewer = new ViewerWindow(services, source, "owned.png", hex: false);
        viewer.Show();
        try
        {
            await WaitFor(() => viewer.PictureLoad is not null);
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var picture = Assert.IsType<DecodedPicture>(viewer.Picture);
            Assert.Equal((320, 200, "PNG"), (picture.Width, picture.Height, picture.Format));
            using (picture.Bitmap.Lock()) { } // the returned bitmap is usable before close
            viewer.Close();
            Assert.Null(viewer.Picture);
            Assert.ThrowsAny<Exception>(() => picture.Bitmap.Lock());
            Assert.Equal(1, source.Disposals);
            Assert.Equal(0, source.DisposalsDuringRead);
            Assert.Equal(source.OriginalHash, SHA256.HashData(File.ReadAllBytes(source.Path)));
        }
        finally
        {
            viewer.Close();
            viewer.Picture?.Bitmap.Dispose(); // also release the unchanged-production baseline's leaked bitmap
            source.Cleanup();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task A_completed_quick_picture_keeps_its_dimensions_and_releases_on_close()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var source = new HeldPictureSource(root, hold: false);
        var pane = new QuickViewPane();
        var window = new Window { Content = pane, Width = 600, Height = 400 };
        window.Show();
        try
        {
            services.Providers.Register(new PictureProvider(source));
            var tab = vm.ActiveTab!;
            tab.Navigate(new Location("picturefixture", "owned"));
            await WaitFor(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("owned.png"));
            pane.Attach(tab);
            await Load(pane);
            var image = pane.GetVisualDescendants().OfType<Image>().Single();
            await WaitFor(() => image.Source is not null);
            Assert.True(image.IsVisible);
            Assert.Contains(pane.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("320 × 200", StringComparison.Ordinal) == true);
            Assert.Equal(0, source.Disposals);
            pane.Attach(null);
            Assert.Null(image.Source);
            Assert.Equal(1, source.Disposals);
            Assert.Equal(0, source.DisposalsDuringRead);
            Assert.Equal(source.OriginalHash, SHA256.HashData(File.ReadAllBytes(source.Path)));
        }
        finally
        {
            pane.Attach(null);
            window.Close();
            source.Cleanup();
            AccessibilityTests.Close(services, main, root);
        }
    }

    private static Task Load(QuickViewPane pane)
    {
        ((DispatcherTimer)typeof(QuickViewPane).GetField("_debounce", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!).Stop();
        return (Task)typeof(QuickViewPane).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pane, null)!;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned picture fixture did not reach its checkpoint.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class PictureProvider(IContentSource source) : ResourceProvider
    {
        public override string Scheme => "picturefixture";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override string GetDeviceKey(Location location) => "picturefixture:" + location.Path;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override IContentSource OpenContent(ItemRef item) => source;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch([new EntryData { Name = "owned.png", Kind = EntryKind.File, Size = source.Length }]);
            return Task.CompletedTask;
        }
    }

    private sealed class HeldPictureSource : IContentSource
    {
        private readonly FileContentSource _file;
        private readonly bool _hold, _failRead;
        private int _reads, _feederReads, _disposals, _active, _disposalsDuringRead;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReadExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public readonly byte[] OriginalHash;
        public string Path { get; }
        public HeldPictureSource(string root, bool hold, bool failRead = false)
        {
            _hold = hold; _failRead = failRead;
            Path = System.IO.Path.Combine(root, "held-picture-" + Guid.NewGuid().ToString("N") + ".png");
            using var bitmap = new SKBitmap(320, 200);
            using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Teal);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = png.ToArray();
            File.WriteAllBytes(Path, bytes);
            OriginalHash = SHA256.HashData(bytes);
            _file = new FileContentSource(Path);
        }
        public int Reads => Volatile.Read(ref _reads);
        public int FeederReads => Volatile.Read(ref _feederReads);
        public int Disposals => Volatile.Read(ref _disposals);
        public int DisposalsDuringRead => Volatile.Read(ref _disposalsDuringRead);
        public string DisplayName => "owned.png";
        public long Length => _file.Length;
        public bool CanSeek => true;
        public string? LocalPath => null; // force FileCat's decoder, without a Shell thumbnail
        public ContentRevision? GetRevision() => _file.GetRevision();
        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _reads);
            // The decoder feeds in 1-MiB chunks; both paged header consumers use 64-KiB buffers.
            bool feeder = buffer.Length == 1024 * 1024;
            if (feeder) Interlocked.Increment(ref _feederReads);
            Interlocked.Increment(ref _active);
            try
            {
                if (_hold && feeder)
                {
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new IOException("Owned picture read was not released.");
                }
                int count = _file.Read(offset, buffer);
                if (_failRead && feeder) throw new IOException("Owned picture read failure.");
                return count;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
                if (_hold && feeder) ReadExited.TrySetResult();
            }
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            if (Volatile.Read(ref _active) != 0) Interlocked.Increment(ref _disposalsDuringRead);
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
