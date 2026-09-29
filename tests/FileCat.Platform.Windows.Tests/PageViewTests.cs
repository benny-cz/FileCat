using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Html;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// The viewer's page view on Windows (D-51), for real: WebView2 in a hidden window on a thread of its own. The page shows
/// with its own title (its script does not run), the image beside it is served, and the web and files outside its folder
/// are refused.
/// </summary>
public sealed class PageViewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-page-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        // The browser's processes let go of their folder shortly after the view closes.
        for (int i = 0; i < 20; i++)
        {
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
                return;
            }
            catch (IOException) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) { Thread.Sleep(250); }
        }
    }

    [Fact]
    public void A_page_shows_without_its_scripts_and_gets_nothing_from_outside_its_folder()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "site")).FullName;
        File.WriteAllText(Path.Combine(site, "page.html"),
            "<!doctype html><html><head><title>Plain title</title><link rel=stylesheet href=\"../outside.css\"></head>" +
            "<body><script>document.title = 'the script ran';</script><img src=\"pic.png\"><img src=\"https://example.com/remote.png\"></body></html>");
        File.WriteAllBytes(Path.Combine(site, "pic.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        File.WriteAllText(Path.Combine(_dir, "outside.css"), "body { color: red }");

        var served = new ConcurrentQueue<(string Path, bool Found)>();
        var blocked = new ConcurrentQueue<string>();
        string? title = null;
        (bool Ok, string? Why)? loaded = null;
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            using var source = new FileContentSource(Path.Combine(site, "page.html"));
            using var view = new WebView2Page(parent, Path.Combine(_dir, "webview"));
            var done = new TaskCompletionSource();
            view.Served += (path, found) => served.Enqueue((path, found));
            view.Blocked += blocked.Enqueue;
            view.Loaded += (ok, why) =>
            {
                loaded = (ok, why);
                done.TrySetResult();
            };
            view.SetSize(640, 480);
            view.Show(new HtmlPage(source, Path.Combine(site, "page.html")));
            await done.Task;
            // Requests for images may finish just after the page reports it loaded.
            for (int i = 0; i < 100 && !(served.Any(s => s.Path == "/pic.png") && blocked.Any(b => b.Contains("example.com", StringComparison.Ordinal))); i++) await Task.Delay(50);
            title = view.Title;
            StaPump.DestroyWindow(parent);
        }, TimeSpan.FromSeconds(60));

        Assert.True(loaded is { Ok: true }, $"The page did not load: {loaded?.Why}");
        Assert.Equal("Plain title", title);
        Assert.Contains(served, s => s.Path == "/page.html" && s.Found);
        Assert.Contains(served, s => s.Path == "/pic.png" && s.Found);
        // "../outside.css" cannot climb above the page's folder: it asks for /outside.css there, which is not found.
        Assert.Contains(served, s => s.Path == "/outside.css" && !s.Found);
        Assert.Contains(blocked, b => b.StartsWith("https://example.com/", StringComparison.Ordinal));
    }

    /// <summary>A single-threaded apartment with a message pump and a synchronization context, as a UI thread has.</summary>
    private static class StaPump
    {
        public static void Run(Func<Task> body, TimeSpan timeout)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                var context = new PumpContext();
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    var task = body();
                    var deadline = DateTime.UtcNow + timeout;
                    while (!task.IsCompleted)
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("The page view did not finish in time.");
                        context.RunPending();
                        while (PeekMessageW(out var message, 0, 0, 0, 1))
                        {
                            TranslateMessage(ref message);
                            DispatchMessageW(ref message);
                        }
                        MsgWaitForMultipleObjects(0, 0, false, 20, 0x04FF);
                    }
                    task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new InvalidOperationException("The page view failed: " + error.Message, error);
        }

        public static nint CreateHiddenWindow()
        {
            nint hwnd = CreateWindowExW(0, "Static", "FileCat page test", 0x00CF0000, 0, 0, 800, 600, 0, 0, 0, 0);
            Assert.NotEqual(0, hwnd);
            return hwnd;
        }

        private sealed class PumpContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

            public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

            public void RunPending()
            {
                while (_queue.TryDequeue(out var item)) item.Callback(item.State);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public nint hwnd;
            public uint message;
            public nint wParam;
            public nint lParam;
            public uint time;
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessageW(out MSG message, nint hwnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref MSG message);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessageW(ref MSG message);

        [DllImport("user32.dll")]
        private static extern uint MsgWaitForMultipleObjects(uint count, nint handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds, uint wakeMask);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(nint hwnd);
    }
}
