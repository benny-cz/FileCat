using System.Diagnostics;
using System.Collections.Concurrent;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class QuickViewLifetimeTests(ITestOutputHelper output)
{
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned quick-view fixture did not reach its checkpoint.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    // Use the real loader without waiting for its debounce. This also works against the unchanged baseline.
    private static Task Load(QuickViewPane pane)
    {
        ((DispatcherTimer)typeof(QuickViewPane).GetField("_debounce", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane)!).Stop();
        return (Task)typeof(QuickViewPane).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pane, null)!;
    }

    private static string[] Labels(QuickViewPane pane) => pane.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
    private static bool ShowsText(QuickViewPane pane) => pane.GetVisualDescendants().OfType<TextViewer>().Single().IsVisible;

    [AvaloniaFact]
    public async Task An_old_open_failure_cannot_replace_the_current_preview()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var gate = new ManualResetEventSlim();
        var pane = new QuickViewPane();
        var preview = new Window { Content = pane, Width = 600, Height = 400 };
        Task? old = null;
        int opened = 0;
        try
        {
            var provider = new PreviewProvider(item =>
            {
                if (item.Name == "a.txt")
                {
                    Interlocked.Increment(ref opened);
                    if (!gate.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Fixture release missing.");
                    throw new IOException("old held open failed");
                }
                return new ObservedSource(item.Name);
            });
            services.Providers.Register(provider);
            var tab = vm.ActiveTab!;
            await Navigate(tab, "slow");
            pane.Attach(tab);
            preview.Show();
            old = Load(pane);
            await WaitFor(() => Volatile.Read(ref opened) == 1);
            Assert.True(tab.Listing.FocusName("b.txt"));
            await Load(pane);
            Assert.True(ShowsText(pane));
            gate.Set();
            await old;
            Assert.True(ShowsText(pane), string.Join(" | ", Labels(pane)));
            Assert.Contains("b.txt", Labels(pane));
            Assert.DoesNotContain(Labels(pane), s => s.Contains("old held open failed", StringComparison.Ordinal));
        }
        finally
        {
            gate.Set();
            if (old is not null) await old;
            pane.Attach(null);
            preview.Close();
            gate.Dispose();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task Closing_during_open_discards_the_source_before_its_first_read()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var gate = new ManualResetEventSlim();
        var pane = new QuickViewPane();
        Task? pending = null;
        var source = new ObservedSource("a.txt");
        int opened = 0;
        try
        {
            services.Providers.Register(new PreviewProvider(_ =>
            {
                Interlocked.Increment(ref opened);
                if (!gate.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Fixture release missing.");
                return source;
            }));
            var tab = vm.ActiveTab!;
            await Navigate(tab, "slow");
            pane.Attach(tab);
            pending = Load(pane);
            await WaitFor(() => Volatile.Read(ref opened) == 1);
            pane.Attach(null);
            gate.Set();
            await pending;
            await WaitFor(() => source.Disposals == 1);
            Assert.Equal(0, source.Reads);
            Assert.Equal(0, source.Revisions);
            output.WriteLine($"Closed held open: reads {source.Reads}, revisions {source.Revisions}, disposals {source.Disposals}.");
        }
        finally
        {
            gate.Set();
            if (pending is not null) await pending;
            pane.Attach(null);
            gate.Dispose();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task A_source_is_released_when_its_initial_revision_fails()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var pane = new QuickViewPane();
        var source = new ObservedSource("a.txt", revisionFails: true);
        try
        {
            services.Providers.Register(new PreviewProvider(_ => source));
            await Navigate(vm.ActiveTab!, "slow");
            pane.Attach(vm.ActiveTab);
            await Load(pane);
            Assert.Equal(1, source.Disposals);
            Assert.Equal(0, source.Reads);
        }
        finally { pane.Attach(null); AccessibilityTests.Close(services, main, root); }
    }

    [AvaloniaFact]
    public async Task Returning_to_the_same_name_never_accepts_an_earlier_requests_reader()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var gate = new ManualResetEventSlim();
        var pane = new QuickViewPane();
        Task? old = null;
        int aOpens = 0;
        var earlier = new ObservedSource("a.txt", binary: true);
        var latest = new ObservedSource("a.txt");
        try
        {
            services.Providers.Register(new PreviewProvider(item =>
            {
                if (item.Name != "a.txt") return new ObservedSource(item.Name);
                if (Interlocked.Increment(ref aOpens) > 1) return latest;
                if (!gate.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Fixture release missing.");
                return earlier;
            }));
            var tab = vm.ActiveTab!;
            await Navigate(tab, "slow");
            pane.Attach(tab);
            old = Load(pane);
            await WaitFor(() => Volatile.Read(ref aOpens) == 1);
            Assert.True(tab.Listing.FocusName("b.txt"));
            await Load(pane);
            Assert.True(tab.Listing.FocusName("a.txt"));
            await Load(pane);
            Assert.Equal(1, latest.Reads);
            gate.Set();
            await old;
            Assert.Equal(0, earlier.Reads);
            await WaitFor(() => earlier.Disposals == 1);
            Assert.Equal(0, latest.Disposals);
            pane.Attach(null);
            Assert.Equal(1, latest.Disposals);
        }
        finally
        {
            gate.Set();
            if (old is not null) await old;
            pane.Attach(null);
            gate.Dispose();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task Abandoned_opens_are_bounded_per_device_and_do_not_hold_a_healthy_device()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var gate = new ManualResetEventSlim();
        var pane = new QuickViewPane();
        var pending = new List<Task>();
        var slowSources = new ConcurrentQueue<ObservedSource>();
        var healthy = new ObservedSource("healthy.txt");
        try
        {
            services.Providers.Register(new PreviewProvider(item =>
            {
                if (item.Parent.Path == "healthy") return healthy;
                var source = new ObservedSource(item.Name);
                slowSources.Enqueue(source);
                if (!gate.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Fixture release missing.");
                return source;
            }));
            var tab = vm.ActiveTab!;
            await Navigate(tab, "slow");
            pane.Attach(tab);
            pending.Add(Load(pane));
            await WaitFor(() => slowSources.Count == 1);
            Assert.True(tab.Listing.FocusName("b.txt"));
            pending.Add(Load(pane));
            await WaitFor(() => slowSources.Count == 2);
            for (int i = 2; i < 10; i++)
            {
                Assert.True(tab.Listing.FocusName($"file{i:D2}.txt"));
                pending.Add(Load(pane));
            }
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            Assert.Equal(services.Io.ThreadsPerDevice, slowSources.Count);
            await Navigate(tab, "healthy");
            await Load(pane).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal(1, healthy.Reads);
            Assert.Equal(0, healthy.Disposals);
            gate.Set();
            await Task.WhenAll(pending);
            await WaitFor(() => slowSources.All(s => s.Disposals == 1));
            Assert.Equal(2, slowSources.Count); // canceled queued requests never opened the device
            Assert.All(slowSources, s => Assert.Equal(0, s.Reads));
            pane.Attach(null);
            Assert.Equal(1, healthy.Disposals);
            output.WriteLine("Ten demands: two held opens; eight abandoned queued demands never open; other device completes; released opens perform zero reads.");
        }
        finally
        {
            gate.Set();
            await Task.WhenAll(pending);
            pane.Attach(null);
            gate.Dispose();
            AccessibilityTests.Close(services, main, root);
        }
    }

    [AvaloniaFact]
    public async Task A_reset_to_an_empty_folder_releases_the_previous_preview()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var pane = new QuickViewPane();
        var source = new ObservedSource("a.txt");
        try
        {
            services.Providers.Register(new PreviewProvider(_ => source));
            var tab = vm.ActiveTab!;
            await Navigate(tab, "slow");
            pane.Attach(tab);
            await Load(pane);
            Assert.Equal(1, source.Reads);
            tab.Navigate(new Location("previewfixture", "empty"));
            await WaitFor(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 0);
            await Load(pane);
            Assert.Equal(1, source.Disposals);
        }
        finally { pane.Attach(null); AccessibilityTests.Close(services, main, root); }
    }

    [AvaloniaFact]
    public async Task Closing_during_a_read_releases_demand_at_once_and_source_after_the_read_returns()
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var gate = new ManualResetEventSlim();
        var pane = new QuickViewPane();
        var source = new ObservedSource("a.txt", readGate: gate);
        Task? pending = null;
        try
        {
            services.Providers.Register(new PreviewProvider(_ => source));
            await Navigate(vm.ActiveTab!, "slow");
            pane.Attach(vm.ActiveTab);
            pending = Load(pane);
            await WaitFor(() => source.Reads == 1);
            pane.Attach(null);
            await pending.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.False(gate.IsSet);
            Assert.Equal(0, source.Disposals); // no disposal races the provider's synchronous read
            gate.Set();
            await WaitFor(() => source.Disposals == 1);
            Assert.Equal(1, source.Reads);
            output.WriteLine("Held synchronous read: UI demand canceled before release; source disposed once after return, with no additional read.");
        }
        finally
        {
            gate.Set();
            if (pending is not null) await pending;
            pane.Attach(null);
            gate.Dispose();
            AccessibilityTests.Close(services, main, root);
        }
    }

    private static async Task Navigate(TabViewModel tab, string device)
    {
        tab.Navigate(new Location("previewfixture", device));
        await WaitFor(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 40);
        Assert.True(tab.Listing.FocusName("a.txt"));
    }

    private sealed class PreviewProvider(Func<ItemRef, IContentSource?> open) : ResourceProvider
    {
        public override string Scheme => "previewfixture";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override string GetDeviceKey(Location location) => Scheme + ":" + location.Path;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override IContentSource? OpenContent(ItemRef item) => open(item);
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            if (location.Path == "empty") return Task.CompletedTask;
            sink.AddBatch(Enumerable.Range(0, 40).Select(i => new EntryData { Name = i == 0 ? "a.txt" : i == 1 ? "b.txt" : $"file{i:D2}.txt", Kind = EntryKind.File, Size = 32 }).ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class ObservedSource(string name, bool binary = false, bool revisionFails = false, ManualResetEventSlim? readGate = null) : IContentSource
    {
        private readonly byte[] _bytes = binary ? new byte[32] : System.Text.Encoding.UTF8.GetBytes("current preview\n");
        private int _reads, _disposals, _revisions;
        public int Reads => Volatile.Read(ref _reads);
        public int Disposals => Volatile.Read(ref _disposals);
        public int Revisions => Volatile.Read(ref _revisions);
        public string DisplayName => name;
        public long Length => _bytes.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _reads);
            if (readGate is not null && !readGate.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Fixture read release missing.");
            Assert.Equal(0, Disposals);
            int count = (int)Math.Min(buffer.Length, Math.Max(0, _bytes.Length - offset));
            _bytes.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
        public ContentRevision? GetRevision()
        {
            Interlocked.Increment(ref _revisions);
            if (revisionFails) throw new IOException("owned revision failure");
            return new ContentRevision(Length, 0);
        }
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }
}
