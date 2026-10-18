using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

/// <summary>The opening header shares an admitted page without occupying a worker while waiting for it.</summary>
public sealed class ViewerHeaderAdmissionTests(ITestOutputHelper output)
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(10);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_header_wait_leaves_device_capacity_for_actual_pointer_page_demand(bool initialHex)
    {
        await using var f = new Fixture(output, large: true, holdNextPage: true);
        f.CreateWindow(initialHex);
        // The constructor's hex snapshot can request page zero even when Text is the initial mode.
        await f.Source.HeaderEntered.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
        Assert.Equal(1, f.Source.Reads);
        f.Window.Show();
        Assert.Equal(initialHex, f.Hex.IsVisible);
        Assert.Equal(!initialHex, f.Text.IsVisible);
        Assert.False(f.Timer.IsEnabled);
        Assert.Equal(-1, f.EncodingBox.SelectedIndex);

        var textPeer = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(f.Text));
        var hexPeer = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(f.Hex));
        for (int i = 0; i < 30; i++)
        {
            Assert.Contains("Loading", textPeer.Value);
            Assert.Contains("not loaded yet", hexPeer.Value);
        }
        Assert.Equal(1, f.Source.Reads);
        // This same-device action must complete before the held source is released. A blocking
        // header waiter would consume the second worker and strand it behind the shared read.
        bool admitted = await f.Services.Io.Run(Fixture.Device, IoPriority.Interactive, _ => true)
            .WaitAsync(Patient, TestContext.Current.CancellationToken);
        Assert.True(admitted);
        Assert.Equal(1, f.Source.Active);
        Assert.False(f.Source.HeaderRelease.IsSet);

        if (!initialHex)
        {
            var mode = Field<ToggleButton>(f.Window, "_modeHex");
            await Until(() => mode.Bounds.Width > 0);
            f.Window.CaptureRenderedFrame()?.Dispose();
            Click(f.Window, mode, new Point(mode.Bounds.Width / 2, mode.Bounds.Height / 2));
            Assert.True(f.Hex.IsVisible);
        }
        await Until(() => f.Hex.ContentViewport.Height > 0);
        f.Window.CaptureRenderedFrame()?.Dispose();
        var local = new Point(f.Hex.CursorBounds(textColumn: false).Center.X, f.Hex.CursorBounds(textColumn: false).Center.Y);
        var point = Assert.IsType<Point>(f.Hex.TranslatePoint(local, f.Window));
        // Real wheel and pointer input reveal and select a different page while the header is held.
        f.Window.MouseWheel(point, new Vector(0, -1400));
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        await f.Source.NextPageEntered.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
        Assert.InRange(f.Hex.CursorOffset, PagedReader.PageSize, 2L * PagedReader.PageSize - 1);
        Assert.Equal(2, f.Source.Reads);
        Assert.Equal(2, f.Source.Active);
        Assert.Equal(f.Services.Io.ThreadsPerDevice, f.Source.PeakActive);
        Assert.Contains("not loaded yet", hexPeer.Value);

        f.Source.HeaderRelease.Set();
        f.Source.NextPageRelease.Set();
        await Until(() => f.Timer.IsEnabled);
        Assert.Equal(Encoding.Unicode.CodePage, f.Text.Encoding.CodePage);
        Assert.Equal(2, f.Text.ContentStart);
        Assert.Contains("byte-order mark", f.EncodingInfo.Text);
        await Until(() => hexPeer.Value?.Contains($"byte {f.Bytes[(int)f.Hex.CursorOffset]:X2} hex", StringComparison.Ordinal) == true);
        Assert.All(f.Source.Calls, call => Assert.False(call.OnUiThread));
        Assert.All(f.Source.Calls, call => Assert.StartsWith("FileCat I/O " + Fixture.Device, call.ThreadName));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_header_cached_before_Show_does_not_need_another_notification_or_read(bool initialHex)
    {
        await using var f = new Fixture(output, holdHeader: false);
        f.CreateWindow(initialHex);
        await Until(() => f.Source.Reads == 1 && f.PendingPages == 0);
        Assert.False(f.Timer.IsEnabled);
        Assert.Equal(-1, f.EncodingBox.SelectedIndex);
        f.Window.Show();
        await Until(() => f.Timer.IsEnabled);
        Assert.Equal(Encoding.Unicode.CodePage, f.Text.Encoding.CodePage);
        Assert.Equal(2, f.Text.ContentStart);
        Assert.Equal(1, f.Source.Reads);
        int latin1 = TextDecoding.Choices.ToList().FindIndex(c => c.Get().CodePage == Encoding.Latin1.CodePage);
        Assert.True(latin1 >= 0);
        f.EncodingBox.SelectedIndex = latin1;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Equal(Encoding.Latin1.CodePage, f.Text.Encoding.CodePage);
        Assert.Equal(0, f.Text.ContentStart);
        Assert.Contains("chosen manually", f.EncodingInfo.Text);
        Assert.Equal(1, f.Source.Reads);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Closing_an_opening_header_retires_publication_and_preserves_the_active_source(bool initialHex, bool completedBeforeClose)
    {
        await using var f = new Fixture(output);
        f.CreateWindow(initialHex);
        f.Window.Show();
        await f.Source.HeaderEntered.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
        Assert.False(f.Timer.IsEnabled);
        if (completedBeforeClose)
        {
            // Complete the actual page/cache callback while the UI dispatcher is held here, then
            // close before its queued encoding continuation or surface notification can publish.
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void PageLoaded() => completed.TrySetResult();
            f.Reader.PageLoaded += PageLoaded;
            try
            {
                f.Source.HeaderRelease.Set();
                Assert.True(completed.Task.Wait(Patient), "The owned page did not finish while the UI was held.");
                Assert.False(f.Timer.IsEnabled);
                Assert.Equal(-1, f.EncodingBox.SelectedIndex);
                f.Window.Close();
            }
            finally { f.Reader.PageLoaded -= PageLoaded; }
        }
        else
        {
            f.Window.Close();
            Assert.Equal(1, f.Source.Active);
            Assert.Equal(0, f.Source.Disposals);
        }
        string? encodingAtClose = f.EncodingInfo.Text;
        string statusAtClose = f.Window.StatusText;
        f.Source.HeaderRelease.Set();
        await f.Source.Disposed.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.False(f.Timer.IsEnabled);
        Assert.Equal(-1, f.EncodingBox.SelectedIndex);
        Assert.Equal(encodingAtClose, f.EncodingInfo.Text);
        Assert.Equal(statusAtClose, f.Window.StatusText);
        Assert.Equal("No content", f.Hex.DescribeCursor());
        Assert.Equal("No content", f.Text.SemanticValue);
        Assert.Null(f.Text.CaptureCopyScope(true));
        Assert.Equal(1, f.Source.Reads);
        Assert.Equal(1, f.Source.Disposals);
        Assert.Equal(0, f.Source.DisposalsDuringRead);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceling_queued_page_admission_finishes_the_header_without_retry_or_late_read(bool initialHex)
    {
        await using var f = new Fixture(output);
        using var entered = new CountdownEvent(f.Services.Io.ThreadsPerDevice);
        using var release = new ManualResetEventSlim();
        var held = Enumerable.Range(0, f.Services.Io.ThreadsPerDevice).Select(i => f.Services.Io.Run(Fixture.Device, IoPriority.Interactive, ct =>
        {
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("The owned device gate was not released.");
        })).ToArray();
        try
        {
            await Task.Run(() => Assert.True(entered.Wait(Patient)), TestContext.Current.CancellationToken);
            f.CreateWindow(initialHex);
            f.Window.Show();
            Assert.Equal(0, f.Source.Reads);
            Assert.False(f.Timer.IsEnabled);
            f.Services.Io.Dispose();
            await Until(() => !f.Window.IsVisible);
            Assert.Equal("Opening stopped.", f.EncodingInfo.Text);
            Assert.Equal(-1, f.EncodingBox.SelectedIndex);
            Assert.False(f.Timer.IsEnabled);
            Assert.Contains("Opening stopped", f.Window.StatusText);
            Assert.DoesNotContain(f.Window, ViewerWindow.OpenWindows);
            Assert.Equal("No content", f.Hex.DescribeCursor());
            Assert.Equal("No content", f.Text.SemanticValue);
            Assert.Null(f.Text.CaptureCopyScope(true));
            await f.Source.Disposed.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
            Assert.Equal(1, f.Source.Disposals);
            Assert.Equal(0, f.Source.DisposalsDuringRead);
            release.Set();
            await Task.WhenAll(held).WaitAsync(Patient, TestContext.Current.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(0, f.Source.Reads);
            Assert.Equal(0, f.PendingPages);
            f.Window.Close();
            await f.Source.Disposed.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
            Assert.Equal(1, f.Source.Disposals);
        }
        finally
        {
            release.Set();
            await Task.WhenAll(held).WaitAsync(Patient);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_page_wakes_the_header_and_preserves_visible_read_error_evidence(bool initialHex)
    {
        await using var f = new Fixture(output, failHeader: true);
        f.CreateWindow(initialHex);
        f.Window.Show();
        await f.Source.HeaderEntered.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
        f.Source.HeaderRelease.Set();
        await Until(() => f.Timer.IsEnabled && f.Hex.DescribeCursor().Contains("owned header read failed", StringComparison.Ordinal));
        Assert.True(f.Window.IsVisible);
        Assert.Equal("owned header read failed", f.Reader.ReadError);
        Assert.Contains("owned header read failed", f.Window.StatusText);
        Assert.Contains("Incomplete reading", f.Hex.DescribeCursor());
        Assert.Contains("owned header read failed", f.Hex.DescribeCursor());
        Assert.Equal(1, f.Source.Reads);
        Assert.Equal(0, f.Source.Active);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_from_an_encoding_publication_cannot_publish_more_state_or_restart_polling(bool fromEncodingSelection)
    {
        await using var f = new Fixture(output);
        f.CreateWindow(initialHex: true);
        bool closed = false;
        string? encodingAtClose = null;
        string? statusAtClose = null;
        void Close()
        {
            if (closed) return;
            closed = true;
            f.Window.Close();
            encodingAtClose = f.EncodingInfo.Text;
            statusAtClose = f.Window.StatusText;
        }
        if (fromEncodingSelection)
            f.EncodingBox.PropertyChanged += (_, e) => { if (e.Property == ComboBox.SelectedIndexProperty && f.EncodingBox.SelectedIndex >= 0) Close(); };
        else
            f.Text.ContentChanged += () => { if (f.Text.Encoding.CodePage == Encoding.Unicode.CodePage) Close(); };
        f.Window.Show();
        await f.Source.HeaderEntered.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
        f.Source.HeaderRelease.Set();
        await Until(() => closed);
        await f.Source.Disposed.Task.WaitAsync(Patient, TestContext.Current.CancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.False(f.Timer.IsEnabled);
        Assert.Equal(encodingAtClose, f.EncodingInfo.Text);
        Assert.Equal(statusAtClose, f.Window.StatusText);
        Assert.Equal("No content", f.Text.SemanticValue);
        Assert.Equal("No content", f.Hex.DescribeCursor());
        Assert.Equal(1, f.Source.Reads);
        Assert.Equal(0, f.Source.DisposalsDuringRead);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Click(Window window, Control control, Point local)
    {
        Point point = Assert.IsType<Point>(control.TranslatePoint(local, window));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
    private static async Task Until(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(clock.Elapsed < Patient, "The owned header checkpoint did not complete.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string Device = "viewer-header-admission";
        private readonly ITestOutputHelper _output;
        private readonly string _root;
        private ViewerWindow? _window;
        internal readonly AppServices Services;
        internal readonly Source Source;
        internal readonly byte[] Bytes;
        internal ViewerWindow Window => _window!;
        internal HexView Hex => Field<HexView>(Window, "_hex");
        internal TextViewer Text => Field<TextViewer>(Window, "_text");
        internal PagedReader Reader => Field<PagedReader>(Window, "_reader");
        internal DispatcherTimer Timer => Field<DispatcherTimer>(Window, "_changeTimer");
        internal ComboBox EncodingBox => Field<ComboBox>(Window, "_encodingBox");
        internal TextBlock EncodingInfo => Field<TextBlock>(Window, "_encodingInfo");
        internal int PendingPages => (int)typeof(PagedReader).GetProperty("PendingLoads", Private)!.GetValue(Reader)!;

        internal Fixture(ITestOutputHelper output, bool large = false, bool holdHeader = true, bool holdNextPage = false, bool failHeader = false)
        {
            _output = output;
            _root = Path.Join(Path.GetTempPath(), "filecat-header-admission-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(_root, "state")));
            // A normal-admission proof must finish before watchdog replacement; no production limit changes.
            typeof(DeviceIoScheduler).GetField("<HangThreshold>k__BackingField", Private)!.SetValue(Services.Io, TimeSpan.FromMinutes(1));
            Assert.Equal(2, Services.Io.ThreadsPerDevice);
            Bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Concat(Enumerable.Repeat("prefix Ω needle\r\n", large ? 7000 : 1)))).ToArray();
            string path = Path.Join(_root, "owned-header.txt");
            File.WriteAllBytes(path, Bytes);
            Source = new Source(path, holdHeader, holdNextPage, failHeader);
        }

        internal void CreateWindow(bool initialHex)
        {
            Assert.Null(_window);
            _window = new ViewerWindow(Services, Source, Source.DisplayName, initialHex, Device);
        }

        public async ValueTask DisposeAsync()
        {
            Source.HeaderRelease.Set();
            Source.NextPageRelease.Set();
            if (_window is not null) _window.Close();
            else Source.Dispose();
            await Source.Disposed.Task.WaitAsync(Patient);
            await Until(() => Source.Active == 0);
            Source.DisposeGates();
            Services.Dispose();
            byte[] after = File.ReadAllBytes(Source.DisplayName);
            _output.WriteLine(JsonSerializer.Serialize(new
            {
                Source.Reads, Source.PeakActive, Source.Disposals, Source.DisposalsDuringRead,
                Calls = Source.Calls.ToArray(), OriginalSHA256 = Convert.ToHexString(SHA256.HashData(Bytes)),
                FinalSHA256 = Convert.ToHexString(SHA256.HashData(after)), ActualFileContentSource = true,
                HeadlessPointerAndSemanticCoverage = true, NativeDeviceAndDesktopAcceptance = false
            }));
            Assert.Equal(Bytes, after);
            Assert.Equal(1, Source.Disposals);
            Assert.Equal(0, Source.DisposalsDuringRead);
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record SourceCall(long Offset, int RequestedBytes, bool OnUiThread, string ThreadName);
    private sealed class Source(string path, bool holdHeader, bool holdNextPage, bool failHeader) : IContentSource
    {
        private readonly FileContentSource _file = new(path);
        private int _header, _nextPage, _reads, _active, _peak, _disposals, _during;
        internal readonly ConcurrentQueue<SourceCall> Calls = new();
        internal readonly TaskCompletionSource HeaderEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource NextPageEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim HeaderRelease = new();
        internal readonly ManualResetEventSlim NextPageRelease = new();
        internal int Reads => Volatile.Read(ref _reads);
        internal int Active => Volatile.Read(ref _active);
        internal int PeakActive => Volatile.Read(ref _peak);
        internal int Disposals => Volatile.Read(ref _disposals);
        internal int DisposalsDuringRead => Volatile.Read(ref _during);
        public string DisplayName => path;
        public string? LocalPath => null;
        public bool CanSeek => true;
        public long Length => _file.Length;
        public ContentRevision? GetRevision() => _file.GetRevision();

        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _reads);
            int active = Interlocked.Increment(ref _active);
            int peak;
            do { peak = Volatile.Read(ref _peak); } while (active > peak && Interlocked.CompareExchange(ref _peak, active, peak) != peak);
            Calls.Enqueue(new(offset, buffer.Length, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
            try
            {
                if (offset == 0 && Interlocked.Exchange(ref _header, 1) == 0)
                {
                    HeaderEntered.TrySetResult();
                    if (holdHeader && !HeaderRelease.Wait(TimeSpan.FromSeconds(30))) throw new IOException("The owned header was not released.");
                    if (failHeader) throw new IOException("owned header read failed");
                }
                if (offset >= PagedReader.PageSize && holdNextPage && Interlocked.Exchange(ref _nextPage, 1) == 0)
                {
                    NextPageEntered.TrySetResult();
                    if (!NextPageRelease.Wait(TimeSpan.FromSeconds(30))) throw new IOException("The owned pointer page was not released.");
                }
                return _file.Read(offset, buffer);
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposals) != 1) return;
            if (Active != 0) Interlocked.Increment(ref _during);
            _file.Dispose();
            Disposed.TrySetResult();
        }

        internal void DisposeGates()
        {
            Assert.Equal(0, Active);
            HeaderRelease.Dispose();
            NextPageRelease.Dispose();
        }
    }
}
