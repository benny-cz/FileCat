using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using FileCat.App.Controls;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// A Low of the review of c0e9cb5a and 97eb0e49: on Linux, WebKitGTK can leave a page waiting with no callback at all when
/// its page process or sandbox does not start, and the page then stayed blank with nothing said.
/// </summary>
public sealed class PageLoadTimeoutTests
{
    private sealed class SilentEngine : IPageEngine
    {
        public IPlatformHandle Handle { get; } = new PlatformHandle(0, "silent-page");
        public string? Title => null;
        public int BlockedCount => 0;
        public event Action? Changed { add { } remove { } }
        public event Action<bool, string?>? Loaded;
        public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
        public void Show(HtmlPage page) { }
        public void Complete() => Loaded?.Invoke(true, null);
        public void SetSize(int width, int height) { }
        public void Focus() { }
        public void Dispose() { }
    }

    [AvaloniaFact]
    public async Task A_page_that_never_finishes_loading_is_reported_and_one_that_finishes_is_not()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-timeout-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var view = new PageView(folder) { LoadTimeout = TimeSpan.FromMilliseconds(200) };
            var failures = new List<string>();
            view.Failed += failures.Add;
            var engine = new SilentEngine();
            view.InstallEngineForTests(engine);
            var page = new HtmlPage(new MemoryContentSource("silent.html", "<html><body>never loads</body></html>"u8.ToArray()), "silent.html");
            view.Show(page);
            for (int attempt = 0; attempt < 150 && failures.Count == 0; attempt++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Contains("did not finish loading", Assert.Single(failures), StringComparison.Ordinal);
            Assert.False(view.PageLoaded);

            failures.Clear();
            view.Show(page);
            engine.Complete();
            await Task.Delay(600, TestContext.Current.CancellationToken);
            Assert.Empty(failures);
            Assert.True(view.PageLoaded);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [AvaloniaFact]
    public void Only_Linux_pages_have_a_load_deadline()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-timeout-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Assert.Equal(OperatingSystem.IsLinux(), new PageView(folder).LoadTimeout is not null);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
