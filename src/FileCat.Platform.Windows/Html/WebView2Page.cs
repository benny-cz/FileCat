using System.Runtime.InteropServices;
using System.Globalization;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace FileCat.Platform.Windows.Html;

/// <summary>
/// The viewer's page view on Windows (plan §16.1, D-51): Microsoft Edge WebView2 in a child window of FileCat's, with
/// scripts, the web, downloads, new windows, permission prompts, and reputation lookups off. The page is loaded from a
/// reserved address that FileCat answers itself (<see cref="HtmlPage"/>): the page and, for a page on disk, the files in
/// its folder. Every other request is refused and reported, never fetched. InPrivate: nothing is kept between pages.
/// Must be used on the UI thread that owns the parent window.
/// </summary>
public sealed class WebView2Page : IDisposable
{
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private HtmlPage? _page;
    private (int Width, int Height) _size = (1, 1);
    private bool _disposed;
    private bool _starting;
    private string? _startupError;
    private bool _focusPending;
    private bool _nativeHasFocus;
    private long _navigationGeneration;
    private int _ownRequestsStarted, _canceledNavigations;
    private CoreWebView2ProcessFailedKind? _lastProcessFailure;
    private readonly PageLoadTimeline _timeline = new();
    private readonly PageCompletionWatch _watch;
    private SynchronizationContext? _context;
    private bool _watchArmed;
    private long _shownAt;
    private string? _navigationAddress;

    /// <summary>
    /// How long a page may make no progress, with every request it made of FileCat answered, before the view stops
    /// waiting for WebView2 to report its completion (release issue I648): a loaded page then counts as drawn, and one
    /// that never began to load is navigated once more.
    /// </summary>
    internal static readonly TimeSpan CompletionQuiet = TimeSpan.FromSeconds(4);

    /// <summary>A look at the page this much later than planned acts on nothing: the process itself was held up.</summary>
    private static readonly TimeSpan LateLook = TimeSpan.FromMilliseconds(500);

    /// <summary>The installed WebView2 runtime's version, or null when there is none (the view is then unavailable).</summary>
    public static string? RuntimeVersion
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or DllNotFoundException or BadImageFormatException or COMException) { return null; }
        }
    }

    public WebView2Page(nint parent, string dataFolder)
    {
        Handle = Native.CreateHost(parent);
        DataFolder = dataFolder;
        _watch = new PageCompletionWatch(() => Environment.TickCount64, CompletionQuiet);
        _context = SynchronizationContext.Current;
        _timeline.Add($"host window made, visible: {Native.IsWindowVisible(Handle)}");
    }

    /// <summary>The child window the page is drawn in.</summary>
    public nint Handle { get; }

    public string DataFolder { get; }

    /// <summary>The page's title (its title element; scripts do not change it).</summary>
    public event Action<string>? TitleChanged;

    /// <summary>The native browser took keyboard focus, including after a click inside its page.</summary>
    public event Action? FocusGained;

    /// <summary>Whether the browser's native child currently receives keyboard input.</summary>
    public bool HasNativeFocus => _nativeHasFocus;

    /// <summary>A request or a navigation to somewhere else than the page and its folder was refused.</summary>
    public event Action<string>? Blocked;

    /// <summary>The page finished loading (true), or could not be shown (false, with why).</summary>
    public event Action<bool, string?>? Loaded;

    /// <summary>A contained Markdown link clicked in this page; the host opens it in another viewer.</summary>
    public event Action<HtmlPage, LinkedMarkdownTarget>? LinkedMarkdown;

    /// <summary>A key FileCat's viewer may want (Esc, function keys, Ctrl and Alt combinations): the handler says whether it took it.</summary>
    public Func<uint, bool, bool, bool, bool>? KeyPressed { get; set; }

    /// <summary>A request for the page's own address was answered: the path, and whether it was found (diagnostics, tests).</summary>
    public event Action<string, bool>? Served;

    /// <summary>How many requests were refused (the status line counts them).</summary>
    public int BlockedCount { get; private set; }

    public string? Title => _controller?.CoreWebView2.DocumentTitle;

    internal string LoadDiagnostics => $"Environment created: {_environment is not null}. Controller created: {_controller is not null}. " +
        $"Accepted navigations: {_navigationGeneration}. Canceled navigations: {_canceledNavigations}. " +
        $"Page requests started: {_ownRequestsStarted}. Browser process failure: {_lastProcessFailure?.ToString() ?? "none"}.\n" +
        "Timeline:\n" + _timeline.Describe();

    /// <summary>What the view did and saw, with times and threads (I648; tests and the diagnostics log).</summary>
    internal PageLoadTimeline Timeline => _timeline;

    /// <summary>Tests (I648): WebView2's navigation completion reports are dropped, as the engine sometimes never sends one.</summary>
    internal bool WithholdNavigationCompletedForTests { get; set; }

    private static string Describe(Exception ex) => $"{ex.GetType().Name} 0x{ex.HResult:X8}";

    /// <summary>HRESULT_FROM_WIN32(ERROR_BUSY): the folder's browser process is still busy, typically still exiting.</summary>
    private const int ErrorBusy = unchecked((int)0x800700AA);

    /// <summary>
    /// How long the view waits for its folder's previous browser process to exit, and how long it may take to start in
    /// all before it says it could not (I648: on a busy machine, a controller asked for while that process was exiting
    /// came after up to 66 seconds, or failed at once with ERROR_BUSY).
    /// </summary>
    internal static readonly TimeSpan BrowserExitWait = TimeSpan.FromSeconds(10), StartupLimit = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(1);

    private async Task StartAsync()
    {
        long deadline = Environment.TickCount64 + (long)StartupLimit.TotalMilliseconds;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await WaitForExitingBrowserAsync();
                if (_disposed || await StartOnceAsync(attempt, deadline)) return;
                StartupFailed($"WebView2 did not start within {StartupLimit.TotalSeconds:0} seconds", $"no controller after {attempt} starts");
                return;
            }
            catch (COMException ex) when (ex.HResult == ErrorBusy &&
                Environment.TickCount64 + (long)RetryPause.TotalMilliseconds < deadline)
            {
                // The folder's browser is still busy, typically still exiting: it is worth another start.
                _timeline.Add($"start {attempt} failed: {Describe(ex)}");
            }
            catch (Exception ex) when (ex is COMException or WebView2RuntimeNotFoundException or UnauthorizedAccessException or IOException or InvalidOperationException or ArgumentException)
            {
                StartupFailed(ex.Message, Describe(ex));
                return;
            }
            await Task.Delay(RetryPause);
            if (_disposed) return;
        }
    }

    private void StartupFailed(string message, string detail)
    {
        _timeline.Add("startup failed: " + detail);
        _watch.Stop();
        _startupError = message;
        if (!_disposed) Loaded?.Invoke(false, message);
    }

    /// <summary>When the folder's last view closed lately, its browser process is exiting: starting meanwhile is what
    /// stalled (see <see cref="PageBrowserExits"/>).</summary>
    private async Task WaitForExitingBrowserAsync()
    {
        var exit = PageBrowserExits.Exit(DataFolder);
        if (exit.IsCompleted) return;
        _timeline.Add("waiting for the folder's previous browser process to exit");
        long since = Environment.TickCount64;
        if (await Task.WhenAny(exit, Task.Delay(BrowserExitWait)) == exit)
            _timeline.Add($"it exited after {Environment.TickCount64 - since} ms");
        else
        {
            _timeline.Add($"it did not exit within {BrowserExitWait.TotalSeconds:0} s: starting all the same");
            PageBrowserExits.Forget(DataFolder, exit);
        }
    }

    /// <returns>Whether the view started (or closed meanwhile); false when its controller did not come by the deadline.</returns>
    private async Task<bool> StartOnceAsync(int attempt, long deadline)
    {
        Directory.CreateDirectory(DataFolder);
        var options = new CoreWebView2EnvironmentOptions
        {
            // Nothing reaches the network: a proxy that refuses, and no background fetching (belt and braces: every
            // request is answered by FileCat or refused before it would get that far).
            AdditionalBrowserArguments = "--proxy-server=http://127.0.0.1:9 --disable-background-networking --disable-component-update --no-pings --disable-domain-reliability",
            AreBrowserExtensionsEnabled = false,
        };
        _timeline.Add(attempt == 1 ? "environment starting" : $"environment starting, start {attempt}");
        var environment = await CoreWebView2Environment.CreateAsync(null, DataFolder, options);
        _timeline.Add($"environment made, browser {environment.BrowserVersionString}");
        if (_disposed) return true;
        Progressed();
        UseEnvironment(environment);
        var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
        controllerOptions.IsInPrivateModeEnabled = true;
        controllerOptions.ProfileName = "FileCatPage";
        _timeline.Add("controller starting");
        // One controller asked for at a time: on a busy machine, asking again only made more of them, which all came
        // together once the browser had started.
        var creating = environment.CreateCoreWebView2ControllerAsync(Handle, controllerOptions);
        var limit = TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64));
        if (await Task.WhenAny(creating, Task.Delay(limit)) != creating)
        {
            _timeline.Add($"controller did not come within {StartupLimit.TotalSeconds:0} s of the start");
            CloseWhenItComes(creating);
            return false;
        }
        var controller = await creating;
        if (_disposed)
        {
            _timeline.Add("controller made after the view closed");
            controller.Close();
            return true;
        }
        Attach(controller);
        return true;
    }

    /// <summary>The environment outlives this view when other viewers share its browser: Dispose lets go of these.</summary>
    private void UseEnvironment(CoreWebView2Environment environment)
    {
        if (_environment is { } previous && !ReferenceEquals(previous, environment))
        {
            try
            {
                previous.BrowserProcessExited -= OnBrowserProcessExited;
                previous.ProcessInfosChanged -= OnProcessInfosChanged;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        }
        if (ReferenceEquals(_environment, environment)) return;
        _environment = environment;
        environment.BrowserProcessExited += OnBrowserProcessExited;
        environment.ProcessInfosChanged += OnProcessInfosChanged;
    }

    /// <summary>A controller given up on may still come: it is closed then, on the view's thread.</summary>
    private void CloseWhenItComes(Task<CoreWebView2Controller> creating)
    {
        var context = _context;
        long since = Environment.TickCount64;
        _ = creating.ContinueWith(done =>
        {
            if (done.Status != TaskStatus.RanToCompletion)
            {
                _ = done.Exception; // observed: the start was given up on already
                return;
            }
            void Close(object? state)
            {
                _timeline.Add($"a controller given up on came after {Environment.TickCount64 - since} ms more, and was closed");
                try { done.Result.Close(); }
                catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
            }
            if (context is null) Close(null);
            else context.Post(Close, null);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Attach(CoreWebView2Controller controller)
    {
        _controller = controller;
        PageBrowserExits.Attached(DataFolder);
        var core = _controller.CoreWebView2;
        _timeline.Add($"controller made, browser process {core.BrowserProcessId}");
        var settings = core.Settings;
        settings.IsScriptEnabled = false;
        settings.IsWebMessageEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsReputationCheckingRequired = false;
        settings.IsBuiltInErrorPageEnabled = true;
        settings.IsZoomControlEnabled = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnResourceRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (_page is { } page && page.TryLinkedMarkdownTarget(e.Uri, out var target))
            {
                LinkedMarkdown?.Invoke(page, target);
                return;
            }
            Refuse(e.Uri);
        };
        core.DownloadStarting += (_, e) => e.Cancel = true;
        // A link to a scheme a program registered (mailto:, ms-settings:, search-ms:) never reaches that program
        // (release plan B11), whatever the navigation checks above saw of it.
        core.LaunchingExternalUriScheme += (_, e) =>
        {
            e.Cancel = true;
            Refuse(e.Uri);
        };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DocumentTitleChanged += (_, _) => TitleChanged?.Invoke(core.DocumentTitle);
        core.ProcessFailed += (_, e) =>
        {
            _lastProcessFailure = e.ProcessFailedKind;
            _timeline.Add($"process failed: {e.ProcessFailedKind}, reason {e.Reason}, exit code {e.ExitCode}, {e.ProcessDescription}");
            // The page's renderer, or the browser itself, ended: whatever the page drew is gone with it.
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.BrowserProcessExited)
                _watch.ProcessEnded();
            else _watch.Progress();
            ArmWatch();
        };
        core.ContentLoading += (_, e) =>
        {
            _timeline.Add($"navigation {e.NavigationId} content loading{(e.IsErrorPage ? " (error page)" : "")}");
            _watch.Committed(e.NavigationId);
            ArmWatch();
        };
        core.DOMContentLoaded += (_, e) =>
        {
            _timeline.Add($"navigation {e.NavigationId} DOM content loaded");
            _watch.ContentLoaded(e.NavigationId);
            ArmWatch();
        };
        core.WebResourceResponseReceived += (_, e) =>
        {
            try { _timeline.Add($"response received: {e.Response.StatusCode}", e.Request.Uri); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
            {
                _timeline.Add("response received, unreadable: " + Describe(ex));
            }
            Progressed();
        };
        core.NavigationCompleted += (_, e) =>
        {
            _timeline.Add($"navigation {e.NavigationId} completed: {(e.IsSuccess ? "success" : e.WebErrorStatus.ToString())}, HTTP {e.HttpStatusCode}" +
                (WithholdNavigationCompletedForTests ? " (withheld for tests)" : ""));
            if (WithholdNavigationCompletedForTests) return;
            // A refused or host-handled link reports OperationCanceled after the original page has loaded.
            // Treating that cancellation as a page-load failure makes the viewer leave Page mode permanently.
            if (!e.IsSuccess && e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            {
                _canceledNavigations++;
                return;
            }
            // A navigation a newer one replaced (a Refresh, a retry) reports nothing about the page shown now; a
            // page already counted as drawn was reported (I648). A failure of the page shown is always reported.
            switch (_watch.Completed(e.NavigationId, e.IsSuccess))
            {
                case PageCompletionWatch.Report.Superseded:
                    _timeline.Add($"navigation {e.NavigationId} completion not passed on: a newer navigation replaced it");
                    return;
                case PageCompletionWatch.Report.AlreadyDrawn:
                    _timeline.Add($"navigation {e.NavigationId} completion not passed on: the page already counts as drawn");
                    return;
            }
            Loaded?.Invoke(e.IsSuccess, e.IsSuccess ? null : e.WebErrorStatus.ToString());
        };
        _controller.AcceleratorKeyPressed += OnAcceleratorKey;
        _controller.GotFocus += (_, _) =>
        {
            _nativeHasFocus = true;
            FocusGained?.Invoke();
        };
        _controller.LostFocus += (_, _) => _nativeHasFocus = false;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, _size.Width, _size.Height);
        _controller.IsVisible = true;
        _timeline.Add($"controller visible, {_size.Width}x{_size.Height}, host window visible: {Native.IsWindowVisible(Handle)}");
        Progressed();
        if (_page is { } page)
        {
            _timeline.Add("navigate", page.Address);
            core.Navigate(page.Address);
        }
        if (_focusPending)
        {
            _focusPending = false;
            _controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }
    }

    /// <summary>A picture of the page as drawn, as PNG (tests and diagnostics: what a reader would see).</summary>
    internal Task CaptureAsync(Stream png) =>
        _controller?.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png) ?? Task.CompletedTask;

    /// <summary>Test-only host action: activate the first rendered link without enabling page-authored scripts.</summary>
    internal Task ClickLinkForTestsAsync(int index) =>
        _controller?.CoreWebView2.ExecuteScriptAsync($"document.querySelectorAll('a')[{index}]?.click()") ?? Task.CompletedTask;

    /// <summary>Shows a page (at once, or once the view is ready).</summary>
    public void Show(HtmlPage page)
    {
        if (_disposed) return;
        _page = page;
        _shownAt = _timeline.Mark();
        _timeline.Add($"show ({(_startupError is not null ? "startup failed" : _controller is not null ? "navigate" : _starting ? "still starting" : "start")}), host window visible: {Native.IsWindowVisible(Handle)}", page.Address);
        _context ??= SynchronizationContext.Current;
        _navigationAddress = null;
        if (_startupError is null) _watch.Begin();
        if (_startupError is { } failure) Loaded?.Invoke(false, failure);
        else if (_controller is { } controller)
        {
            // A browser that ended for good refuses new navigations: say so rather than wait for nothing.
            try { controller.CoreWebView2.Navigate(page.Address); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                _timeline.Add("navigate failed: " + Describe(ex));
                _watch.Stop();
                Loaded?.Invoke(false, "WebView2 could not load the page: " + ex.Message);
            }
        }
        else if (!_starting)
        {
            // Show follows the host's event registration, including for synchronous startup failures.
            _starting = true;
            _ = StartAsync();
        }
        ArmWatch();
    }

    private void Progressed()
    {
        _watch.Progress();
        ArmWatch();
    }

    /// <summary>Looks at the shown page again once it may have gone quiet (on the UI thread, through its context).</summary>
    private void ArmWatch(TimeSpan? after = null)
    {
        if (_watchArmed || _disposed || !_watch.Active || _context is not { } context) return;
        _watchArmed = true;
        var delay = after ?? _watch.Quiet;
        long due = Environment.TickCount64 + (long)delay.TotalMilliseconds;
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            long fired = Environment.TickCount64;
            context.Post(_ => OnWatch(due, fired), null);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void OnWatch(long due, long fired)
    {
        _watchArmed = false;
        if (_disposed) return;
        // A late look says this process was held up, not WebView2: its timer, or the view's thread, where WebView2's
        // events arrive too. What WebView2 sent meanwhile may still wait to be handled (on a busy machine its completion
        // came a few milliseconds after such a look), so look again in a moment rather than act on stale knowledge.
        long now = Environment.TickCount64;
        if (now - due > (long)LateLook.TotalMilliseconds)
        {
            _timeline.Add($"watch looked {now - due} ms late (its timer {fired - due} ms, the view's thread {now - fired} ms): looking again");
            ArmWatch(TimeSpan.FromMilliseconds(250));
            return;
        }
        var decision = _watch.Check(_controller is not null, out var again);
        switch (decision)
        {
            case PageCompletionWatch.Decision.TreatAsDrawn:
                Note("no completion came, but the page's content loaded and every request it made of FileCat was answered: it counts as drawn");
                Loaded?.Invoke(true, null);
                break;
            case PageCompletionWatch.Decision.NavigateAgain:
                string? address = _navigationAddress ?? _page?.Address;
                Note("the page did not begin to load, or its page process ended: it is navigated once more");
                try
                {
                    if (address is not null) _controller?.CoreWebView2.Navigate(address);
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
                {
                    _timeline.Add("navigating once more failed: " + Describe(ex));
                    _watch.Stop();
                    Loaded?.Invoke(false, "WebView2 could not load the page again: " + ex.Message);
                    return;
                }
                break;
            case PageCompletionWatch.Decision.Fail:
                Note("the page's process ended again: it could not be shown");
                Loaded?.Invoke(false, $"WebView2's page process ended ({_lastProcessFailure?.ToString() ?? "unknown"})");
                break;
            case PageCompletionWatch.Decision.GiveUp:
                Note("navigating once more did not help: the page waits, and Refresh stays available");
                break;
        }
        if (again is { } wait) ArmWatch(wait);
    }

    private void OnBrowserProcessExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs e) =>
        _timeline.Add($"browser process {e.BrowserProcessId} exited: {e.BrowserProcessExitKind}");

    private void OnProcessInfosChanged(object? sender, object e)
    {
        try
        {
            if (_environment is { } environment)
                _timeline.Add("processes: " + string.Join(", ", environment.GetProcessInfos().Select(p => $"{p.Kind} {p.ProcessId}")));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            _timeline.Add("processes unknown: " + Describe(ex));
        }
    }

    /// <summary>What the watch decided, in the timeline and in the diagnostics log (addresses hashed there).</summary>
    private void Note(string decision)
    {
        _timeline.Add("watch: " + decision);
        AppLog.Warn($"Page view (I648): {decision}. WebView2 {_environment?.BrowserVersionString ?? "not started"}; " +
            $"{_watch.Quiet.TotalSeconds:0.#} s without progress. Timeline: " +
            _timeline.Describe(hashNames: true, since: _shownAt).TrimEnd('\n').Replace("\n", " | ", StringComparison.Ordinal));
    }

    /// <summary>The view's size in physical pixels (the child window's; the host sizes that window).</summary>
    public void SetSize(int width, int height)
    {
        var size = (Math.Max(1, width), Math.Max(1, height));
        // While a page loads (a window being resized would otherwise crowd out what the page did).
        if (size != _size && (_watch.Active || _controller is null)) _timeline.Add($"size {size.Item1}x{size.Item2}");
        _size = size;
        if (_controller is { } controller) controller.Bounds = new System.Drawing.Rectangle(0, 0, _size.Width, _size.Height);
    }

    /// <summary>The keyboard to the page (after a click elsewhere in the viewer).</summary>
    public void Focus()
    {
        if (_disposed) return;
        if (_controller is { } controller) controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        else _focusPending = true;
    }

    /// <summary>The viewer took logical focus from the page. Its native window must take Win32 keyboard focus too,
    /// otherwise the next typed characters still go into WebView2 despite Find appearing focused.</summary>
    public void ReturnFocus(nint window)
    {
        _focusPending = false;
        if (window != 0) Native.SetFocus(window);
    }

    /// <summary>Scrolls a rendered page when its parent window, rather than WebView2, received a Page key.</summary>
    public async Task<bool> ScrollByPageAsync(int direction)
    {
        if (_disposed || _controller is not { } controller || direction is not (-1 or 1)) return false;
        try
        {
            await controller.CoreWebView2.ExecuteScriptAsync(
                $"window.scrollBy(0, {direction} * Math.max(1, window.innerHeight - 32))");
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { return false; }
    }

    /// <summary>Reads the page's actual viewport, including mouse-wheel and scrollbar movement.</summary>
    public async Task<(double X, double Y)?> CaptureScrollPositionAsync(CancellationToken ct)
    {
        if (_disposed || _controller is not { } controller) return null;
        long navigation = _navigationGeneration;
        ct.ThrowIfCancellationRequested();
        try
        {
            string result = await controller.CoreWebView2.ExecuteScriptAsync("[window.scrollX, window.scrollY]");
            ct.ThrowIfCancellationRequested();
            if (_disposed || navigation != _navigationGeneration) return null;
            double[]? coordinates = JsonSerializer.Deserialize<double[]>(result);
            return coordinates is { Length: 2 } &&
                   double.IsFinite(coordinates[0]) && coordinates[0] >= 0 &&
                   double.IsFinite(coordinates[1]) && coordinates[1] >= 0
                ? (coordinates[0], coordinates[1]) : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Reapplies the prior viewport; the browser clamps it if the new page is shorter.</summary>
    public async Task<bool> RestoreScrollPositionAsync((double X, double Y) position, CancellationToken ct)
    {
        if (_disposed || _controller is not { } controller ||
            !double.IsFinite(position.X) || position.X < 0 ||
            !double.IsFinite(position.Y) || position.Y < 0) return false;
        long navigation = _navigationGeneration;
        ct.ThrowIfCancellationRequested();
        try
        {
            string x = position.X.ToString("R", CultureInfo.InvariantCulture);
            string y = position.Y.ToString("R", CultureInfo.InvariantCulture);
            await controller.CoreWebView2.ExecuteScriptAsync($"window.scrollTo({x}, {y})");
            ct.ThrowIfCancellationRequested();
            return !_disposed && navigation == _navigationGeneration;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Searches the rendered document using host-controlled script; page-authored scripts remain disabled.</summary>
    public Task<bool?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct) =>
        FindAsync(text, matchCase, forward, ct, wrap: false);

    public async Task<bool?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct, bool wrap)
    {
        if (_disposed || _controller is not { } controller) return null;
        long navigation = _navigationGeneration;
        ct.ThrowIfCancellationRequested();
        // JSON encoding keeps even quotes, backslashes and line breaks inside one string literal.
        string script = $"window.find({JsonSerializer.Serialize(text)}, {JsonSerializer.Serialize(matchCase)}, {JsonSerializer.Serialize(!forward)}, {JsonSerializer.Serialize(wrap)}, false, false, false)";
        string result;
        try { result = await controller.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { return null; }
        ct.ThrowIfCancellationRequested();
        if (_disposed || navigation != _navigationGeneration) return null;
        return result == "true" ? true : result == "false" ? false : null;
    }

    public void ClearFind()
    {
        if (_disposed || _controller is not { } controller) return;
        _ = ClearFindAsync(controller);
    }

    private static async Task ClearFindAsync(CoreWebView2Controller controller)
    {
        try { await controller.CoreWebView2.ExecuteScriptAsync("window.getSelection().removeAllRanges()"); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { }
    }

    private bool OurAddress(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps && string.Equals(parsed.Host, HtmlPage.Host, StringComparison.OrdinalIgnoreCase);

    private void Refuse(string uri)
    {
        BlockedCount++;
        Blocked?.Invoke(uri);
    }

    /// <summary>Every request: the page's own address is answered from the page (off the UI thread); the rest is refused.</summary>
    private async void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_environment is not { } environment) return;
        string uri = e.Request.Uri;
        if (!OurAddress(uri) || _page is not { } page)
        {
            _timeline.Add($"request refused ({e.ResourceContext})", uri);
            Refuse(uri);
            e.Response = environment.CreateWebResourceResponse(null, 403, "Refused by FileCat", "");
            Progressed();
            return;
        }
        var deferral = e.GetDeferral();
        int request = ++_ownRequestsStarted;
        string path = new Uri(uri).AbsolutePath;
        _timeline.Add($"request {request} started ({e.ResourceContext})", path);
        _watch.RequestStarted();
        ArmWatch();
        string step = "resolving";
        try
        {
            var found = await Task.Run(() => page.Resolve(path));
            if (_disposed)
            {
                _timeline.Add($"request {request} abandoned: the view closed");
                return;
            }
            _timeline.Add($"request {request} resolved: {(found is { } resolved ? resolved.Bytes.Length + " bytes" : "not found")}");
            Served?.Invoke(path, found is not null);
            step = "answering";
            e.Response = found is { } content
                ? environment.CreateWebResourceResponse(new MemoryStream(content.Bytes, writable: false), 200, "OK", $"Content-Type: {content.MimeType}\r\nCache-Control: no-store")
                : environment.CreateWebResourceResponse(null, 404, "Not found", "");
            _timeline.Add($"request {request} answered");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // The view closed meanwhile.
            _timeline.Add($"request {request} {step} failed: {Describe(ex)}");
        }
        finally
        {
            try
            {
                deferral.Complete();
                _timeline.Add($"request {request} deferral completed");
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
            {
                _timeline.Add($"request {request} deferral completion failed: {Describe(ex)}");
            }
            _watch.RequestEnded();
            ArmWatch();
        }
    }

    /// <summary>The page may go to other pages in its folder (a link, an anchor); anywhere else is refused.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_page is { } page && page.TryLinkedMarkdownTarget(e.Uri, out var target))
        {
            _timeline.Add($"navigation {e.NavigationId} starting: a linked Markdown file, opened elsewhere", e.Uri);
            e.Cancel = true;
            LinkedMarkdown?.Invoke(page, target);
            return;
        }
        if (_page?.IsLinkedMarkdownRequest(e.Uri) == true)
        {
            _timeline.Add($"navigation {e.NavigationId} starting: a linked Markdown file, refused", e.Uri);
            e.Cancel = true;
            Refuse(e.Uri);
            return;
        }
        if (OurAddress(e.Uri))
        {
            _timeline.Add($"navigation {e.NavigationId} starting: accepted{(e.IsRedirected ? ", redirected" : "")}, host window visible: {Native.IsWindowVisible(Handle)}", e.Uri);
            _navigationGeneration++;
            _navigationAddress = e.Uri;
            _watch.Started(e.NavigationId);
            ArmWatch();
            return;
        }
        _timeline.Add($"navigation {e.NavigationId} starting: refused", e.Uri);
        e.Cancel = true;
        Refuse(e.Uri);
    }

    private void OnAcceleratorKey(object? sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
    {
        if (e.KeyEventKind is not (CoreWebView2KeyEventKind.KeyDown or CoreWebView2KeyEventKind.SystemKeyDown) || KeyPressed is not { } handler) return;
        bool ctrl = Native.IsDown(0x11), shift = Native.IsDown(0x10), alt = e.KeyEventKind == CoreWebView2KeyEventKind.SystemKeyDown || Native.IsDown(0x12);
        if (handler(e.VirtualKey, ctrl, shift, alt)) e.Handled = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timeline.Add("closed");
        _nativeHasFocus = false;
        // Before the controller closes: when it is the folder's last, the next view waits for the browser to exit.
        if (_controller is not null && _environment is { } exiting) PageBrowserExits.Detaching(DataFolder, exiting);
        try { _controller?.Close(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        _controller = null;
        if (_environment is { } environment)
        {
            try
            {
                environment.BrowserProcessExited -= OnBrowserProcessExited;
                environment.ProcessInfosChanged -= OnProcessInfosChanged;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        }
        Native.DestroyWindow(Handle);
    }

    private static class Native
    {
        private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
        private static ushort s_class;

        /// <summary>A plain child window for the view (its window procedure is the system's default).</summary>
        public static nint CreateHost(nint parent)
        {
            var instance = GetModuleHandleW(null);
            if (s_class == 0)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = NativeLibrary.GetExport(NativeLibrary.Load("user32.dll"), "DefWindowProcW"),
                    hInstance = instance,
                    lpszClassName = "FileCatPageHost",
                };
                s_class = RegisterClassExW(ref wc);
            }
            var hwnd = CreateWindowExW(0, "FileCatPageHost", "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS, 0, 0, 1, 1, parent, 0, instance, 0);
            if (hwnd == 0) throw new InvalidOperationException("The page view's window could not be made: error " + Marshal.GetLastPInvokeError());
            return hwnd;
        }

        public static bool IsDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(nint window);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            public nint lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public nint hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(nint hwnd);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

        [DllImport("user32.dll")]
        public static extern nint SetFocus(nint window);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandleW(string? name);
    }
}
