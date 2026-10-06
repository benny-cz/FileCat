using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using SkiaSharp;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

/// <summary>V12: picture source calls share the provider's device limit, and a healthy device remains available.</summary>
public sealed class PictureDeviceDemandTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task Rapid_quick_pictures_do_not_start_more_source_calls_on_a_held_device()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        KeepHeldCallsInsideTheFixtureDeadline(services);
        var sources = Enumerable.Range(0, 4).Select(i => new Source(root, i, hold: i < 3)).ToArray();
        var provider = new Provider(sources);
        var pane = new QuickViewPane();
        var window = new Window { Content = pane, Width = 600, Height = 400 };
        window.Show();
        var pending = new List<Task>();
        try
        {
            services.Providers.Register(provider);
            var tab = vm.ActiveTab!;
            await Navigate(tab, "held", "p0.png");
            pane.Attach(tab);
            pending.Add(Load(pane));
            await sources[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(tab.Listing.FocusName("p1.png"));
            pending.Add(Load(pane));
            await sources[1].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var clock = Stopwatch.StartNew();
            Assert.True(tab.Listing.FocusName("p2.png"));
            pending.Add(Load(pane));
            await Task.WhenAny(sources[2].Entered.Task, Task.Delay(1500, TestContext.Current.CancellationToken));
            int activeAtCheckpoint = sources.Take(3).Sum(s => s.ActiveReads);
            await Navigate(tab, "healthy", "p3.png");
            pending.Add(Load(pane));
            await pending[^1].WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            var image = pane.GetVisualDescendants().OfType<Image>().Single();
            await WaitFor(() => image.Source is not null);
            Assert.Contains(pane.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("44 × 30", StringComparison.Ordinal) == true);
            output.WriteLine($"Held reads at checkpoint {activeAtCheckpoint}; third feed entered {sources[2].Entered.Task.IsCompleted}; healthy picture 44 x 30; checkpoint-to-result {clock.Elapsed.TotalMilliseconds:0} ms; per-device workers {services.Io.ThreadsPerDevice}.");
            Assert.Equal(services.Io.ThreadsPerDevice, activeAtCheckpoint);
            Assert.False(sources[2].Entered.Task.IsCompleted);
            Assert.Equal(0, sources[2].Reads); // its abandoned queued initial open never reaches the provider
            pane.Attach(null);
            foreach (var s in sources) s.Release.Set();
            await Task.WhenAll(pending);
            await WaitFor(() => sources.Take(2).All(s => s.Disposals == 1));
            Assert.All(sources.Take(2), s => Assert.Equal(2, s.Reads));
            Assert.Equal(1, sources[3].Disposals);
            Assert.All(sources, s => Assert.Equal(0, s.DisposalsDuringRead));
            Assert.All(sources, s => Assert.Equal(s.OriginalHash, SHA256.HashData(File.ReadAllBytes(s.Path))));
        }
        finally
        {
            pane.Attach(null);
            foreach (var s in sources) s.Release.Set();
            await Task.WhenAll(pending);
            await JoinReads(sources);
            window.Close();
            foreach (var s in sources) s.Cleanup();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task Picture_viewers_share_the_provider_device_limit_and_other_devices_complete()
    {
        var (services, _, main, root) = AccessibilityTests.OpenMainWindow();
        KeepHeldCallsInsideTheFixtureDeadline(services);
        var sources = Enumerable.Range(0, 4).Select(i => new Source(root, i, hold: i < 3)).ToArray();
        var windows = new List<ViewerWindow>();
        try
        {
            services.Providers.Register(new Provider(sources));
            ViewerWindow Open(int i, string device)
            {
                var before = ViewerWindow.OpenWindows.ToHashSet();
                ViewerLauncher.Open(services, new ItemRef(new Location("picturedevicefixture", device), $"p{i}.png", EntryKind.File), sources[i], hex: false);
                var window = Assert.Single(ViewerWindow.OpenWindows.Where(w => !before.Contains(w)));
                windows.Add(window);
                return window;
            }
            Open(0, "held");
            await sources[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Open(1, "held");
            await sources[1].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Open(2, "held");
            await Task.WhenAny(sources[2].Entered.Task, Task.Delay(1500, TestContext.Current.CancellationToken));
            int activeAtCheckpoint = sources.Take(3).Sum(s => s.ActiveReads);
            var healthy = Open(3, "healthy");
            await WaitFor(() => healthy.PictureLoad is not null);
            await healthy.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal((44, 30), (healthy.Picture!.Width, healthy.Picture.Height));
            output.WriteLine($"Held viewer reads at checkpoint {activeAtCheckpoint}; third feed entered {sources[2].Entered.Task.IsCompleted}; separate healthy provider device renders 44 x 30.");
            Assert.Equal(services.Io.ThreadsPerDevice, activeAtCheckpoint);
            Assert.False(sources[2].Entered.Task.IsCompleted);
            Assert.Equal(0, sources[2].Reads); // its queued initial header has not reached the held device
            foreach (var w in windows) w.Close();
            foreach (var s in sources) s.Release.Set();
            await Task.WhenAll(windows.Select(w => w.PictureLoad ?? Task.CompletedTask));
            await WaitFor(() => sources.All(s => s.Disposals == 1));
            Assert.Equal(0, sources[2].Reads); // canceled queued header never reads afterwards
            Assert.All(sources, s => Assert.Equal(0, s.DisposalsDuringRead));
            Assert.All(sources, s => Assert.Equal(s.OriginalHash, SHA256.HashData(File.ReadAllBytes(s.Path))));
        }
        finally
        {
            foreach (var w in windows) w.Close();
            foreach (var s in sources) s.Release.Set();
            await Task.WhenAll(windows.Select(w => w.PictureLoad ?? Task.CompletedTask));
            await JoinReads(sources);
            foreach (var s in sources) s.Cleanup();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task Viewer_headers_share_device_admission_and_canceled_headers_never_read()
    {
        var (services, _, main, root) = AccessibilityTests.OpenMainWindow();
        KeepHeldCallsInsideTheFixtureDeadline(services);
        var sources = Enumerable.Range(0, 4).Select(i => new Source(root, i, hold: i < 3, heldRead: 1)).ToArray();
        var windows = new List<ViewerWindow>();
        try
        {
            services.Providers.Register(new Provider(sources));
            ViewerWindow Open(int i, string device)
            {
                var before = ViewerWindow.OpenWindows.ToHashSet();
                ViewerLauncher.Open(services, new ItemRef(new Location("picturedevicefixture", device), $"p{i}.png", EntryKind.File), sources[i], hex: false);
                var window = Assert.Single(ViewerWindow.OpenWindows, w => !before.Contains(w));
                windows.Add(window);
                return window;
            }
            Open(0, "held");
            await sources[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Open(1, "held");
            await sources[1].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Open(2, "held");
            await Task.WhenAny(sources[2].Entered.Task, Task.Delay(500, TestContext.Current.CancellationToken));
            int activeAtCheckpoint = sources.Take(3).Sum(s => s.ActiveReads);
            var healthy = Open(3, "healthy");
            await WaitFor(() => healthy.PictureLoad is not null);
            await healthy.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal((44, 30), (healthy.Picture!.Width, healthy.Picture.Height));
            output.WriteLine($"Header calls at checkpoint {activeAtCheckpoint}; third header entered {sources[2].Entered.Task.IsCompleted}; healthy device 44 x 30; calls: {string.Join(" | ", sources.Take(3).Select(s => s.HeldCallStack))}");
            Assert.Equal(services.Io.ThreadsPerDevice, activeAtCheckpoint);
            Assert.False(sources[2].Entered.Task.IsCompleted);
            Assert.Equal(0, sources[2].Reads);
            foreach (var window in windows) window.Close();
            foreach (var source in sources) source.Release.Set();
            await JoinReads(sources);
            await WaitFor(() => sources.All(s => s.Disposals == 1));
            Assert.Equal(0, sources[2].Reads);
            Assert.All(sources, s => Assert.Equal(0, s.DisposalsDuringRead));
            Assert.All(sources, s => Assert.Equal(s.OriginalHash, SHA256.HashData(File.ReadAllBytes(s.Path))));
        }
        finally
        {
            foreach (var window in windows) window.Close();
            foreach (var source in sources) source.Release.Set();
            await Task.WhenAll(windows.Select(w => w.PictureLoad ?? Task.CompletedTask));
            await JoinReads(sources);
            await WaitFor(() => sources.All(s => s.Disposals == 1));
            output.WriteLine($"Header fixture cleanup: disposals {string.Join(",", sources.Select(s => s.Disposals))}; disposals during reads {string.Join(",", sources.Select(s => s.DisposalsDuringRead))}; unchanged sources {sources.All(s => s.OriginalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(s.Path))))}.");
            foreach (var source in sources) source.Cleanup();
            AccessibilityTests.Close(services, main, root);
        }
    }

    private static void KeepHeldCallsInsideTheFixtureDeadline(AppServices services)
    {
        // These cases isolate normal per-device admission. Native decoder startup on a busy runner may push the
        // whole case beyond the production eight-second watchdog, which then correctly replaces held workers.
        // Watchdog replacement/hard caps have separate scheduler controls; keep this owned fixture below its own
        // threshold without changing the application's default or its source/lifetime/healthy-device assertions.
        typeof(DeviceIoScheduler).GetField("<HangThreshold>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(services.Io, TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(1), services.Io.HangThreshold);
    }

    private static async Task JoinReads(Source[] sources) => await WaitFor(() => sources.All(s => s.ActiveReads == 0));
    private static async Task Navigate(TabViewModel tab, string device, string name)
    {
        tab.Navigate(new Location("picturedevicefixture", device));
        await WaitFor(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName(name));
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
            if (clock.Elapsed > TimeSpan.FromSeconds(7)) throw new TimeoutException("Owned picture device fixture did not reach its checkpoint.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Provider(Source[] sources) : ResourceProvider
    {
        public override string Scheme => "picturedevicefixture";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override string GetDeviceKey(Location location) => Scheme + ":" + location.Path;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override IContentSource OpenContent(ItemRef item) => sources[int.Parse(item.Name.AsSpan(1,1))];
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            var numbers = location.Path == "held" ? new[] {0,1,2} : [3];
            sink.AddBatch(numbers.Select(i => new EntryData($"p{i}.png", EntryKind.File, sources[i].Length)).ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class Source : IContentSource
    {
        private readonly FileContentSource _file;
        private readonly bool _hold;
        private readonly int _heldRead;
        private int _reads, _active, _disposals, _disposalsDuringRead;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public string Path { get; }
        public byte[] OriginalHash { get; }
        public string? HeldCallStack { get; private set; }
        public Source(string root, int i, bool hold, int heldRead = 2)
        {
            _hold = hold;
            _heldRead = heldRead;
            Path = System.IO.Path.Combine(root, $"held-device-picture-{i}.png");
            using var bitmap = new SKBitmap(hold ? 320 : 44, hold ? 200 : 30);
            using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Teal);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png,100);
            var bytes = png.ToArray();
            OriginalHash = SHA256.HashData(bytes);
            File.WriteAllBytes(Path,bytes);
            _file = new FileContentSource(Path);
        }
        public int Reads => Volatile.Read(ref _reads);
        public int ActiveReads => Volatile.Read(ref _active);
        public int Disposals => Volatile.Read(ref _disposals);
        public int DisposalsDuringRead => Volatile.Read(ref _disposalsDuringRead);
        public string DisplayName => Path;
        public long Length => _file.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => _file.GetRevision();
        public int Read(long offset, Span<byte> buffer)
        {
            int read = Interlocked.Increment(ref _reads);
            Interlocked.Increment(ref _active);
            try
            {
                if (_hold && read == _heldRead)
                {
                    HeldCallStack = Environment.StackTrace;
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new IOException("Owned device picture read was not released.");
                }
                return _file.Read(offset,buffer);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            if (ActiveReads != 0) Interlocked.Increment(ref _disposalsDuringRead);
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
