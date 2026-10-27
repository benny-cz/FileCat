using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace PageEngineFaultHarness;

/// <summary>Native acceptance fixture: the replacement fails only when the browser asks for it.</summary>
internal sealed class FaultingPageSource(byte[] bytes) : IContentSource
{
    private bool _disposed;
    public int ReadCalls { get; private set; }
    public int FailureReadCall { get; private set; }
    public string DisplayName => "owned.md";
    public long Length => bytes.Length;
    public bool CanSeek => true;
    public string? LocalPath => null;
    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReadCalls++;
        // Refresh reads a prefix once, then the preflight page read and its
        // EOF probe. The fourth read is the native browser's scheme request.
        if (ReadCalls == 4)
        {
            FailureReadCall = ReadCalls;
            throw new IOException("Owned replacement page read failed during native load.");
        }
        if (offset >= bytes.Length) return 0;
        int count = (int)Math.Min(buffer.Length, bytes.Length - offset);
        bytes.AsSpan((int)offset, count).CopyTo(buffer);
        return count;
    }
    public ContentRevision? GetRevision() => new(bytes.Length, 1);
    public void Dispose() => _disposed = true;
}

internal sealed class HarnessApp : Application
{
    internal static string ReportPath = "";
    internal static string ValidationRoot => Path.Combine(
        Environment.GetFolderPath(OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.UserProfile), "FileCatValidation");
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = ExecuteAsync(desktop);
        }
    }

    private static async Task ExecuteAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Let the desktop lifetime finish startup before assigning its first
        // window; otherwise it can try to show a closed test window again.
        await Task.Yield();
        string validation = ValidationRoot;
        string root = Path.Combine(validation, "PageEngineFault", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var result = new Dictionary<string, object?>
        {
            ["passed"] = false, ["stage"] = "startup", ["fixture"] = root,
            ["platform"] = OperatingSystem.IsWindows() ? "Windows WebView2" : "Linux WebKitGTK",
            ["appDllSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "FileCat.dll")))).ToLowerInvariant(),
            ["harnessDllSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "PageEngineFaultHarness.dll")))).ToLowerInvariant()
        };
        void Save() => File.WriteAllText(ReportPath, JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        AppServices? services = null;
        ViewerWindow? viewer = null;
        try
        {
            services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            var old = new MemoryContentSource("owned.md", Encoding.UTF8.GetBytes("# Old page\n\noldpagetoken\n"));
            var replacement = new FaultingPageSource(Encoding.UTF8.GetBytes("# New page\n\nnewpagetoken\n"));
            Func<CancellationToken, Task<IContentSource?>> reopen = _ => Task.FromResult<IContentSource?>(replacement);
            var constructor = typeof(ViewerWindow).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                [typeof(AppServices), typeof(IContentSource), typeof(string), typeof(bool), typeof(string),
                 typeof(FileCat.Core.Content.PagedReader), typeof(Func<CancellationToken, Task<IContentSource?>>)],
                modifiers: null) ?? throw new InvalidOperationException("Viewer test constructor is unavailable");
            viewer = (ViewerWindow)constructor.Invoke([services, old, "owned.md", false, "page-fault", null, reopen]);
            desktop.MainWindow = viewer;
            viewer.Show();
            var page = (PageView)(typeof(ViewerWindow).GetField("_pageView", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(viewer) ?? throw new InvalidOperationException("Page view is unavailable"));
            string? pageFailure = null;
            page.Failed += why => pageFailure = why;
            result["stage"] = "initial native page";
            Save();
            try { await Until(() => page.PageLoaded, TimeSpan.FromSeconds(20)); }
            catch (TimeoutException)
            {
                result["initialPageFailure"] = pageFailure;
                result["initialViewerStatus"] = viewer.StatusLine;
                result["initialPageMode"] = viewer.IsPageShown;
                result["initialPageVisible"] = page.IsEffectivelyVisible;
                throw;
            }
            result["stage"] = "initial native page loaded";
            Save();
            var oldFind = await page.FindAsync("oldpagetoken", false, true, CancellationToken.None);
            if (oldFind is not { Found: true }) throw new InvalidOperationException("Native engine did not render the old page");
            result["oldPageFound"] = true;
            result["stage"] = "initial native page searched";
            Save();
            var refresh = typeof(ViewerWindow).GetMethod("RefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Viewer Refresh is unavailable");
            result["stage"] = "native replacement load failure";
            Save();
            await Until(() => viewer.IsVisible && page.PageLoaded, TimeSpan.FromSeconds(10));
            await ((Task)refresh.Invoke(viewer, null)!).WaitAsync(TimeSpan.FromSeconds(25));
            result["stage"] = "native Refresh returned";
            Save();
            result["replacementReadCalls"] = replacement.ReadCalls;
            result["failureReadCall"] = replacement.FailureReadCall;
            await Until(() => page.PageLoaded, TimeSpan.FromSeconds(20));
            page.ClearFind();
            var restoredFind = await page.FindAsync("oldpagetoken", false, true, CancellationToken.None);
            result["oldPageFoundAfterFailure"] = restoredFind?.Found == true;
            page.ClearFind();
            var newFind = await page.FindAsync("newpagetoken", false, true, CancellationToken.None);
            result["newPageFoundAfterFailure"] = newFind?.Found == true;
            var banner = (TextBlock?)(typeof(ViewerWindow).GetField("_staleText", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(viewer));
            result["failureBanner"] = banner?.Text;
            result["pageModeRetained"] = (bool)(typeof(ViewerWindow).GetProperty("IsPageShown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(viewer) ?? false);
            result["passed"] = replacement.FailureReadCall == 4 && restoredFind?.Found == true &&
                newFind?.Found != true && (bool)result["pageModeRetained"]! &&
                (banner?.Text?.StartsWith("Refresh failed:", StringComparison.Ordinal) ?? false);
            result["stage"] = "complete";
            Save();
        }
        catch (Exception ex)
        {
            result["error"] = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            result["cleanupStage"] = "closing viewer";
            Save();
            desktop.MainWindow = null;
            viewer?.Close();
            result["cleanupStage"] = "disposing services";
            Save();
            services?.Dispose();
            result["cleanupStage"] = "removing owned fixture";
            Save();
            try
            {
                await Task.Run(() => Directory.Delete(root, recursive: true))
                    .WaitAsync(TimeSpan.FromSeconds(5));
                result["cleanupStage"] = "owned fixture removed";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                // WebView2 can keep its private profile files open after the
                // controller closes. The caller can remove this exact owned
                // directory after the helper process has exited.
                result["cleanupStage"] = "deferred until helper exit";
                result["cleanupError"] = ex.GetType().Name + ": " + ex.Message;
            }
            Save();
            desktop.Shutdown((bool)result["passed"]! ? 0 : 1);
        }
    }

    private static async Task Until(Func<bool> check, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (check()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Native page state did not arrive within " + timeout);
    }
}

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Usage: PageEngineFaultHarness REPORT.json");
        HarnessApp.ReportPath = Path.GetFullPath(args[0]);
        string validation = HarnessApp.ValidationRoot;
        if (Path.GetDirectoryName(HarnessApp.ReportPath) != validation || File.Exists(HarnessApp.ReportPath))
            throw new InvalidOperationException("Use a fresh report in the isolated validation directory");
        return AppBuilder.Configure<HarnessApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }
}
