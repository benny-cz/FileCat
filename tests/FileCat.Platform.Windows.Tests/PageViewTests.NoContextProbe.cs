using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Html;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class PageViewTests
{
    /// <summary>
    /// Release issue I648, one suspected cause ruled out on the engine at hand: a page's own request answered off the UI
    /// thread, as it would be if WebView2 raised its request event where no synchronization context brings the answer
    /// back. WebView2 refuses the response from that thread (the view's timeline says so), accepts the deferral's
    /// completion all the same, shows its own error page, and reports a failed completion: the page fails, it does not
    /// stall. An opt-in probe of the engine's behaviour (FILECAT_PAGE_PROBE=1), not of FileCat's.
    /// </summary>
    [Fact]
    public void A_request_answered_off_the_UI_thread_fails_the_page_rather_than_stalling_it()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (Environment.GetEnvironmentVariable("FILECAT_PAGE_PROBE") != "1")
            Assert.Skip("Set FILECAT_PAGE_PROBE=1 to probe WebView2 with requests answered off the UI thread (release issue I648).");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "probe")).FullName;
        string readme = Path.Combine(site, "README.md");
        File.WriteAllText(readme, "# Probe\n");
        string? diagnostics = null;
        (bool Ok, string? Why)? loaded = null;
        var thread = new Thread(() =>
        {
            // An STA thread with a message loop but no synchronization context: continuations go to the thread pool.
            SynchronizationContext.SetSynchronizationContext(null);
            nint parent = StaPump.CreateHiddenWindow();
            using var source = new FileContentSource(readme);
            using (var view = new WebView2Page(parent, Path.Combine(_dir, "webview-probe")))
            {
                view.Loaded += (ok, why) => loaded = (ok, why);
                view.SetSize(640, 480);
                view.Show(HtmlPage.ForMarkdown(source, readme));
                for (var deadline = DateTime.UtcNow.AddSeconds(30); DateTime.UtcNow < deadline && loaded is null;)
                {
                    while (PeekMessageW(out var message, 0, 0, 0, 1))
                    {
                        TranslateMessage(ref message);
                        DispatchMessageW(ref message);
                    }
                    Thread.Sleep(10);
                }
                diagnostics = view.LoadDiagnostics;
            }
            StaPump.DestroyWindow(parent);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        TestContext.Current.TestOutputHelper?.WriteLine($"Loaded: {loaded?.ToString() ?? "never"}\n{diagnostics}");
        Assert.True(loaded is { Ok: false }, "The page did not fail: " + diagnostics);
        Assert.Contains("request 1 answering failed: InvalidOperationException", diagnostics, StringComparison.Ordinal);
        Assert.Contains("content loading (error page)", diagnostics, StringComparison.Ordinal);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeMessage
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
    private static extern bool PeekMessageW(out ProbeMessage message, nint hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref ProbeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref ProbeMessage message);
}
