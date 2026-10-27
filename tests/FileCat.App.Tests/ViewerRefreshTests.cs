using System.Reflection;
using System.Text;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;
using SkiaSharp;

namespace FileCat.App.Tests;

public sealed class ViewerRefreshTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaFact]
    public async Task Failed_refresh_banner_uses_one_sentence_separator_after_a_provider_error()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "filecat-refresh-message-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var viewer = new ViewerWindow(services,
            new MemoryContentSource("member.txt", "last good"u8.ToArray()), "member.txt",
            hex: false, deviceKey: "refresh-message", preparedReader: null,
            reopen: _ => Task.FromException<IContentSource?>(new IOException("The archive no longer exists.")));
        try
        {
            viewer.Show();
            await Until(() => Field<Button>(viewer, "_refresh").IsEnabled);
            await viewer.RefreshAsync();
            Assert.Equal("Refresh failed: The archive no longer exists. The previous view remains available.",
                Field<TextBlock>(viewer, "_staleText").Text);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Closing_during_provider_reopen_retires_the_late_source()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-refresh-close-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var opened = new TaskCompletionSource<IContentSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewer = new ViewerWindow(services, new MemoryContentSource("held.txt", "old"u8.ToArray()),
            "held.txt", hex: false, deviceKey: "refresh-close", preparedReader: null, reopen: _ => opened.Task);
        try
        {
            viewer.Show();
            await Until(() => Field<Button>(viewer, "_refresh").IsEnabled);
            Task refreshing = viewer.RefreshAsync();
            Assert.False(refreshing.IsCompleted);
            viewer.Close();
            var late = new TrackedSource("late.txt", "new"u8.ToArray());
            opened.SetResult(late);
            await refreshing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(late.Disposed);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Closing_during_reopened_metadata_waits_to_retire_the_source()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-refresh-metadata-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        using var source = new BlockingMetadataSource();
        var viewer = new ViewerWindow(services, new MemoryContentSource("held.txt", "old"u8.ToArray()),
            "held.txt", hex: false, deviceKey: "refresh-metadata", preparedReader: null,
            reopen: _ => Task.FromResult<IContentSource?>(source));
        try
        {
            viewer.Show();
            await Until(() => Field<Button>(viewer, "_refresh").IsEnabled);
            Task refreshing = viewer.RefreshAsync();
            await Until(() => source.Entered.IsSet);
            viewer.Close();
            Assert.False(source.Disposed);
            source.Release.Set();
            await refreshing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(source.Disposed);
            Assert.False(source.DisposedDuringCall);
        }
        finally
        {
            if (!source.Disposed) source.Release.Set();
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Page_refresh_waits_for_native_load_and_rolls_back_a_failed_replacement()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-refresh-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        int opened = 0;
        Task<IContentSource?> Reopen(CancellationToken _) => Task.FromResult<IContentSource?>(
            new MemoryContentSource("site.html", Encoding.UTF8.GetBytes($"<html><body>new page {++opened}</body></html>")));
        var viewer = new ViewerWindow(services,
            new MemoryContentSource("site.html", "<html><body>old page</body></html>"u8.ToArray()),
            "site.html", hex: false, deviceKey: "page-refresh", preparedReader: null, reopen: Reopen);
        var engine = new ControlledPageEngine();
        var pageView = Field<PageView>(viewer, "_pageView");
        pageView.InstallEngineForTests(engine);
        try
        {
            viewer.Show();
            await Until(() => engine.Pages.Count == 1);
            engine.Complete(true);
            await Until(() => pageView.PageLoaded && Field<Button>(viewer, "_refresh").IsEnabled);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var old = Field<PagedReader>(viewer, "_reader");
            typeof(ViewerWindow).GetField("_sourceGeneration", Private)!.SetValue(viewer, 1L);
            typeof(ViewerWindow).GetMethod("UpdateStatus", Private)!.Invoke(viewer, []);
            Assert.True(Field<Border>(viewer, "_staleBanner").IsVisible);

            Task refreshing = viewer.RefreshAsync();
            await Until(() => engine.Pages.Count == 2);
            Assert.Same(old, Field<PagedReader>(viewer, "_reader"));
            Assert.Equal(1, old.Read(0, new byte[1]));
            Assert.Contains("old page", Encoding.UTF8.GetString(engine.Pages[0].Resolve("/")!.Value.Bytes));
            engine.Complete(true);
            await refreshing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var current = Field<PagedReader>(viewer, "_reader");
            Assert.NotSame(old, current);
            Assert.Throws<ObjectDisposedException>(() => old.WithSource(_ => { }));
            Assert.Contains("new page 1", Encoding.UTF8.GetString(engine.Pages[1].Resolve("/")!.Value.Bytes));
            Assert.False(Field<Border>(viewer, "_staleBanner").IsVisible);

            typeof(ViewerWindow).GetField("_sourceGeneration", Private)!.SetValue(viewer, 3L);
            typeof(ViewerWindow).GetMethod("UpdateStatus", Private)!.Invoke(viewer, []);
            refreshing = viewer.RefreshAsync();
            await Until(() => engine.Pages.Count == 3);
            engine.Complete(false, "native load failed");
            await refreshing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Same(current, Field<PagedReader>(viewer, "_reader"));
            Assert.Equal(4, engine.Pages.Count); // the previous page is restored
            Assert.Same(engine.Pages[1], engine.Pages[3]);
            Assert.Null(engine.Pages[2].Resolve("/"));
            Assert.True(Field<Border>(viewer, "_staleBanner").IsVisible);
            Assert.Contains("Refresh failed", Field<TextBlock>(viewer, "_staleText").Text);
        }
        finally
        {
            viewer.Close();
            engine.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Text_refresh_reopens_content_and_keeps_its_scroll_anchor()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-text-refresh-" + Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Combine(root, "notes.txt");
        File.WriteAllText(path, string.Concat(Enumerable.Range(0, 5000).Select(i => $"old line {i:D4}\n")));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        var viewer = new ViewerWindow(services, new FileContentSource(path), path, hex: false);
        try
        {
            viewer.Show();
            await Until(() => Field<Button>(viewer, "_refresh").IsEnabled);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var text = Field<TextViewer>(viewer, "_text");
            text.ScrollToOffset(20_000);
            text.SetSelection(100, 110);
            long anchor = text.TopOffset;
            Assert.True(anchor > 0);
            var first = Field<PagedReader>(viewer, "_reader");
            File.WriteAllText(path, string.Concat(Enumerable.Range(0, 5000).Select(i => $"new line {i:D4}\n")));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
            await CheckForChanges(viewer);
            viewer.KeyPressQwerty(Avalonia.Input.PhysicalKey.F5, Avalonia.Input.RawInputModifiers.None);
            // The reader is published before Refresh finishes, and the new visible
            // page can still load asynchronously after the action is reenabled.
            // Wait for the restored row layout, then assert its exact anchor range.
            await Until(() => !ReferenceEquals(first, Field<PagedReader>(viewer, "_reader")) &&
                            Field<Button>(viewer, "_refresh").IsEnabled &&
                            text.TopOffset >= anchor - 100 && text.TopOffset <= anchor + 100);
            var current = Field<PagedReader>(viewer, "_reader");
            Assert.NotSame(first, current);
            Assert.InRange(text.TopOffset, anchor - 100, anchor + 100);
            Assert.False(text.HasSelection); // changed bytes cannot inherit an old selection
            var bytes = new byte[8];
            Assert.Equal(8, current.Read(0, bytes));
            Assert.Equal("new line", Encoding.UTF8.GetString(bytes));
            text.SetSelection(100, 110);
            await viewer.RefreshAsync();
            Assert.Equal((100L, 10L), text.Selection);
            var hex = Field<HexView>(viewer, "_hex");
            typeof(ViewerWindow).GetMethod("SetMode", Private)!.Invoke(viewer, [true]);
            hex.GoTo(5, select: true, length: 4);
            await viewer.RefreshAsync();
            Assert.Equal((5L, 4L), hex.Selection);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Info_refresh_reopens_replaced_file_and_failure_keeps_last_report_stale()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-view-refresh-" + Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Combine(root, "picture.png");
        File.WriteAllBytes(path, Png(12, 8));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        int opens = 0;
        Task<IContentSource?> Reopen(CancellationToken _) => ++opens == 1
            ? Task.FromResult<IContentSource?>(new FileContentSource(path))
            : throw new FileNotFoundException("The provider item is no longer available", path);
        var viewer = new ViewerWindow(services, new FileContentSource(path), path, hex: true,
            deviceKey: "local-refresh", preparedReader: null, reopen: Reopen);
        try
        {
            viewer.Show();
            await Until(() => Field<ToggleButton>(viewer, "_modePicture").IsVisible);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            await viewer.ShowInfoAsync();
            Assert.Contains("12 × 8 pixels", viewer.InfoText);
            var first = Field<PagedReader>(viewer, "_reader");

            File.WriteAllBytes(path, Png(16, 10));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
            await CheckForChanges(viewer);
            Assert.Contains("Refresh (F5)", viewer.StatusText);
            Assert.True(Field<Border>(viewer, "_staleBanner").IsVisible);

            await viewer.RefreshAsync();
            Assert.Contains("16 × 10 pixels", viewer.InfoText);
            Assert.DoesNotContain("Refresh (F5)", viewer.StatusText);
            Assert.False(Field<Border>(viewer, "_staleBanner").IsVisible);
            Assert.NotSame(first, Field<PagedReader>(viewer, "_reader"));

            File.WriteAllBytes(path, Png(20, 12));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(20));
            await CheckForChanges(viewer);
            string report = viewer.InfoText;
            var current = Field<PagedReader>(viewer, "_reader");
            await viewer.RefreshAsync();
            Assert.Same(current, Field<PagedReader>(viewer, "_reader"));
            Assert.Equal(report, viewer.InfoText);
            Assert.True(Field<Border>(viewer, "_staleBanner").IsVisible);
            Assert.Contains("Refresh failed", Field<TextBlock>(viewer, "_staleText").Text);
            Assert.Equal(2, opens);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Picture_refresh_preserves_custom_zoom_after_provider_replacement()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-picture-refresh-" + Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Combine(root, "picture.png");
        File.WriteAllBytes(path, Png(1600, 1000));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        var viewer = new ViewerWindow(services, new FileContentSource(path), path, hex: false);
        try
        {
            viewer.Show();
            await Until(() => viewer.PictureLoad is not null);
            await viewer.PictureLoad!.WaitAsync(PictureDecoder.Timeout + TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotNull(viewer.Picture);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var picture = Field<PictureView>(viewer, "_picture");
            picture.SetZoom(1.25);
            Assert.False(picture.Fit);
            double oldCenterX = (picture.Bounds.Width / 2 - picture.Offset.X) / (1600 * picture.Zoom);

            File.WriteAllBytes(path, Png(2000, 1250));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
            await CheckForChanges(viewer);
            await viewer.RefreshAsync();

            Assert.Equal((2000, 1250), (viewer.Picture!.Width, viewer.Picture.Height));
            Assert.False(picture.Fit);
            Assert.Equal(1.25, picture.Zoom, 2);
            double newCenterX = (picture.Bounds.Width / 2 - picture.Offset.X) / (2000 * picture.Zoom);
            Assert.Equal(oldCenterX, newCenterX, 2);
            Assert.DoesNotContain("Refresh (F5)", viewer.StatusLine);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Provider_refresh_uses_a_reopen_factory_for_sources_without_local_paths()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-provider-refresh-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        int opens = 0;
        Task<IContentSource?> Reopen(CancellationToken _) =>
            Task.FromResult<IContentSource?>(new MemoryContentSource("provider/photo.png", Png(++opens == 1 ? 16 : 32, 10)));
        var viewer = new ViewerWindow(services, new MemoryContentSource("provider/photo.png", Png(12, 8)),
            "provider/photo.png", hex: true, deviceKey: "provider-refresh", preparedReader: null, reopen: Reopen);
        try
        {
            viewer.Show();
            await Until(() => Field<ToggleButton>(viewer, "_modePicture").IsVisible);
            await viewer.ShowInfoAsync();
            Assert.Contains("12 × 8 pixels", viewer.InfoText);
            Assert.Null(Field<PagedReader>(viewer, "_reader").Source.LocalPath);
            await viewer.RefreshAsync();
            Assert.Equal(1, opens);
            Assert.Contains("16 × 10 pixels", viewer.InfoText);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static T Field<T>(ViewerWindow viewer, string name) where T : class =>
        Assert.IsType<T>(typeof(ViewerWindow).GetField(name, Private)!.GetValue(viewer));

    private static Task CheckForChanges(ViewerWindow viewer) =>
        (Task)typeof(ViewerWindow).GetMethod("CheckForChanges", Private)!.Invoke(viewer, [])!;

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed class ControlledPageEngine : IPageEngine
    {
        public List<HtmlPage> Pages { get; } = [];
        public IPlatformHandle Handle { get; } = new PlatformHandle(0, "refresh-test");
        public string? Title => "Test page";
        public int BlockedCount => 0;
        public event Action? Changed { add { } remove { } }
        public event Action<bool, string?>? Loaded;
        public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
        public void Show(HtmlPage page) => Pages.Add(page);
        public void Complete(bool ok, string? why = null) => Loaded?.Invoke(ok, why);
        public void SetSize(int width, int height) { }
        public void Focus() { }
        public void Dispose() { }
    }

    private sealed class TrackedSource(string name, byte[] data) : IContentSource
    {
        private readonly MemoryContentSource _inner = new(name, data);
        public bool Disposed { get; private set; }
        public string DisplayName => _inner.DisplayName;
        public long Length => _inner.Length;
        public bool CanSeek => _inner.CanSeek;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => _inner.GetRevision();
        public int Read(long offset, Span<byte> buffer) => _inner.Read(offset, buffer);
        public void Dispose() { Disposed = true; _inner.Dispose(); }
    }

    private sealed class BlockingMetadataSource : IContentSource
    {
        private readonly MemoryContentSource _inner = new("held.txt", "new"u8.ToArray());
        private int _active;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool Disposed { get; private set; }
        public bool DisposedDuringCall { get; private set; }
        public string DisplayName => _inner.DisplayName;
        public long Length
        {
            get
            {
                Interlocked.Increment(ref _active);
                try
                {
                    Entered.Set();
                    if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Metadata gate was not released");
                    return _inner.Length;
                }
                finally { Interlocked.Decrement(ref _active); }
            }
        }
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => _inner.GetRevision();
        public int Read(long offset, Span<byte> buffer) => _inner.Read(offset, buffer);
        public void Dispose()
        {
            if (Disposed) return;
            DisposedDuringCall |= Volatile.Read(ref _active) != 0;
            Disposed = true;
            _inner.Dispose();
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
