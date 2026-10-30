using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;
using Avalonia.Platform;
using FileCat.Core.Content;

namespace FileCat.App.Controls;

/// <summary>
/// WebKitGTK (Linux) behind <see cref="PageView"/> (D-51). The view lives on the GTK thread Avalonia keeps for its own
/// GTK use (one GTK thread per process), inside a GtkPlug whose X window Avalonia's native control host takes in.
/// The page and the files in its folder come from a private "filecat:" scheme answered by <see cref="HtmlPage"/>;
/// scripts are off; every other request goes to a proxy that refuses it, and is counted; navigating away, new
/// windows, downloads, and permission prompts are refused. The web context is ephemeral: nothing is kept.
/// </summary>
internal sealed unsafe partial class LinuxPageEngine : IPageEngine
{
    private const string Scheme = "filecat";
    private const uint MaxFindMatches = 1024;
    private static readonly ConcurrentDictionary<nint, LinuxPageEngine> Live = new();
    private static long s_nextId;
    private static readonly Lazy<string?> s_missing = new(() => Native.Load());

    private readonly nint _id;
    private nint _context, _view, _window;
    private nuint _plug;
    // GTK thread: GTK's own focus window in the plug, through which the page gets its keys; tries left to give it the
    // keys while the page is not yet on screen; and the last Tab (whether Shift+Tab, and when).
    private nuint _keyTarget;
    private int _focusTries;
    private bool _focusRetrying;
    private bool _tabBackward;
    private long _tabAt = long.MinValue;
    private nint _findController;
    private bool _sandboxEnabled;
    private readonly SemaphoreSlim _findSerial = new(1, 1);
    private volatile TaskCompletionSource<PageFindResult?>? _findPending;
    private string? _findPattern;
    private bool _findCase;
    private bool _findWrap;
    private bool _findFresh;
    private PageFindResult? _findTotal;
    private volatile HtmlPage? _page;
    private volatile string? _title;
    private int _blocked;
    private int _disposed;

    private const string AppArmorProfileMessage =
        "Web page preview is unavailable because this system restricts sandbox user namespaces and FileCat has no AppArmor profile. " +
        "Install FileCat's Debian package, or ask an administrator to load its supplied AppArmor profile.";

    /// <summary>Why WebKitGTK cannot be used here, or null when it can.</summary>
    public static string? Unavailable => s_missing.Value;

    /// <summary>
    /// Ubuntu 24.04 and later can route an unprofiled process into a generic user-namespace profile which denies the
    /// capabilities bubblewrap needs to finish constructing WebKit's sandbox. WebKit still reports the sandbox as
    /// enabled in that state, then never starts its page process. A named unconfined application profile with an
    /// explicit <c>userns</c> grant (the distro's Epiphany pattern) avoids that broken half-start.
    /// </summary>
    internal static bool NeedsAppArmorUserNamespaceProfile(string? restriction, string? currentProfile) =>
        restriction?.Trim() == "1" && (string.IsNullOrWhiteSpace(currentProfile) || currentProfile.Trim() == "unconfined");

    /// <summary>
    /// Where WebKitGTK starts its page process without bubblewrap although its sandbox is enabled, and still reports it
    /// enabled (review finding R-M7): with WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS set to anything but 0, inside Docker,
    /// and inside a Snap (WebKit's own tests for these). FileCat shows no page there rather than claim a sandbox it does
    /// not have. Inside a Flatpak, WebKit uses Flatpak's own sandbox instead.
    /// </summary>
    internal static string? SandboxSkippedHere(Func<string, bool> exists, Func<string, string?> variable)
    {
        if (variable("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS") is { } disabled && disabled != "0")
            return "Web page preview is unavailable because WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS turns WebKitGTK's page sandbox off. Start FileCat without it.";
        if (exists("/.dockerenv"))
            return "Web page preview is unavailable inside a Docker container, where WebKitGTK does not start its page sandbox.";
        if (variable("SNAP") is not null && variable("SNAP_NAME") is not null && variable("SNAP_REVISION") is not null)
            return "Web page preview is unavailable inside a Snap, where WebKitGTK does not start its page sandbox.";
        return null;
    }

    private static string? AppArmorProfileUnavailable()
    {
        const string restrictionPath = "/proc/sys/kernel/apparmor_restrict_unprivileged_userns";
        if (!File.Exists(restrictionPath)) return null;
        try
        {
            string restriction = File.ReadAllText(restrictionPath);
            string? profile = File.Exists("/proc/self/attr/current") ? File.ReadAllText("/proc/self/attr/current") : null;
            return NeedsAppArmorUserNamespaceProfile(restriction, profile) ? AppArmorProfileMessage : null;
        }
        catch (IOException)
        {
            // If the restriction exists but cannot be inspected, fail closed rather than starting a WebKit process
            // whose sandbox can remain stuck without a load-failed callback.
            return AppArmorProfileMessage;
        }
        catch (UnauthorizedAccessException) { return AppArmorProfileMessage; }
    }

    private LinuxPageEngine()
    {
        _id = (nint)Interlocked.Increment(ref s_nextId);
        Live[_id] = this;
    }

    /// <summary>
    /// A page view in a GtkPlug, for the native control host; or, <paramref name="offscreen"/>, in an offscreen window
    /// (tests). Null with why when WebKitGTK or GTK cannot be used.
    /// </summary>
    public static LinuxPageEngine? Create(bool offscreen, out string? unavailable)
    {
        unavailable = Unavailable;
        if (unavailable is not null) return null;
        var engine = new LinuxPageEngine();
        try
        {
            var built = OnGtk(() => engine.Build(offscreen));
            bool completed = built.Wait(TimeSpan.FromSeconds(15));
            if (!completed || !engine._sandboxEnabled || built.Result == 0 && !offscreen)
            {
                unavailable = completed && !engine._sandboxEnabled
                    ? "The web engine (WebKitGTK) could not enable and verify its process sandbox."
                    : "The web engine (WebKitGTK) did not start.";
                engine.Dispose();
                return null;
            }
            engine.Handle = new PlatformHandle(built.Result, "XID");
            return engine;
        }
        catch (AggregateException ex)
        {
            unavailable = "GTK, which WebKitGTK needs, could not start: " + ex.InnerException?.Message;
            engine.Dispose();
            return null;
        }
    }

    public IPlatformHandle Handle { get; private set; } = new PlatformHandle(0, "XID");
    public string? Title => _title;
    public int BlockedCount => Volatile.Read(ref _blocked);
    /// <summary>The native WebKit context accepted and reports its process sandbox before any page was loaded.</summary>
    internal bool SandboxEnabled => _sandboxEnabled;
    public event Action? Changed;
    public event Action<bool, string?>? Loaded;
    public event Action<HtmlPage, string>? LinkedMarkdown;

    /// <summary>Raised on the GTK thread when the page is clicked.</summary>
    public event Action? FocusRequested;

    /// <summary>Raised on the GTK thread when Tab, or Shift+Tab (true), goes past the page's last or first item.</summary>
    public event Action<bool>? FocusLeft;

    /// <summary>Called on the GTK thread: the viewer's handler only posts its work to the UI thread.</summary>
    public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }

    public void Show(HtmlPage page)
    {
        _page = page;
        ClearFind();
        string address = $"{Scheme}://page/{Uri.EscapeDataString(page.Name)}";
        _ = OnGtk(() =>
        {
            if (_view == 0) return 0;
            fixed (byte* uri = Utf8(address)) Native.webkit_web_view_load_uri(_view, uri);
            return 0;
        });
    }

    /// <summary>Test-only host navigation through the same native policy callback as a rendered link.</summary>
    internal Task RequestAddressForTestsAsync(string address) => OnGtk(() =>
    {
        if (_view != 0)
            fixed (byte* uri = Utf8(address)) Native.webkit_web_view_load_uri(_view, uri);
        return 0;
    });

    public void SetSize(int width, int height) => _ = OnGtk(() =>
    {
        if (_window == 0 || width <= 0 || height <= 0) return 0;
        // The viewer gives pixels and GTK sizes its windows in its own units: on a 200% desktop GTK made the page
        // twice as wide and high as its room, so its prose ran past the window's right and bottom edges.
        int scale = Native.gtk_widget_get_scale_factor(_window);
        Native.gtk_window_resize(_window, ToGtkUnits(width, scale), ToGtkUnits(height, scale));
        return 0;
    });

    /// <summary>Pixels in GTK's units at its window scale, rounded up so the page fills its room.</summary>
    internal static int ToGtkUnits(int pixels, int scale) => scale <= 1 ? pixels : (pixels + scale - 1) / scale;

    /// <summary>
    /// The keyboard to the page. Avalonia reparents the page's window rather than embedding it (it sends no XEmbed
    /// focus messages), so the X input focus is moved here, to GTK's own focus window in the plug: otherwise the keys
    /// went to the page whenever the pointer rested on it, and to the viewer otherwise, whatever had focus.
    /// </summary>
    public void Focus() => _ = OnGtk(() =>
    {
        if (_view == 0) return 0;
        Native.gtk_widget_grab_focus(_view);
        _focusTries = 0;
        if (_plug == 0) return 0;
        // A GtkPlug takes its window's focus only from the XEmbed messages of its embedder, which Avalonia does not
        // send: without them the page never knew it had focus, and Tab past its last item went round it again.
        Native.SendXEmbed(_plug, Native.XEmbedWindowActivate);
        Native.SendXEmbed(_plug, Native.XEmbedFocusIn);
        if (Native.SetInputFocus(KeyTarget)) return 0;
        // Not on screen yet (the viewer has only just shown it): try again for up to two seconds.
        _focusTries = 40;
        if (!_focusRetrying)
        {
            _focusRetrying = true;
            Native.g_timeout_add(50, (nint)(delegate* unmanaged[Cdecl]<nint, int>)&OnFocusRetry, _id);
        }
        return 0;
    });

    /// <summary>The keys back to the viewer's window (0: its window is not active; only stop giving them to the page).</summary>
    public void ReturnFocus(nint window) => _ = OnGtk(() =>
    {
        _focusTries = 0;
        if (_plug == 0) return 0;
        Native.SendXEmbed(_plug, Native.XEmbedFocusOut);
        if (window != 0) Native.SetInputFocus((nuint)window);
        else Native.SendXEmbed(_plug, Native.XEmbedWindowDeactivate);
        return 0;
    });

    private nuint KeyTarget => _keyTarget != 0 ? _keyTarget : _plug;

    public Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct) =>
        FindCoreAsync(text, matchCase, forward, ct, wrap: false);

    public Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct, bool wrap) =>
        FindCoreAsync(text, matchCase, forward, ct, wrap);

    private int BeginFindOnGtk(string text, bool matchCase, bool forward, bool wrap, TaskCompletionSource<PageFindResult?> done)
    {
        if (_disposed != 0 || _findController == 0) { done.TrySetResult(null); return 0; }
        _findPending = done;
        if (_findPattern == text && _findCase == matchCase && _findWrap == wrap)
        {
            _findFresh = false;
            if (forward) Native.webkit_find_controller_search_next(_findController);
            else Native.webkit_find_controller_search_previous(_findController);
        }
        else
        {
            _findPattern = text;
            _findCase = matchCase;
            _findWrap = wrap;
            _findFresh = true;
            _findTotal = null;
            uint options = (matchCase ? 0u : 1u) | (forward ? 0u : 8u) | (wrap ? 16u : 0u);
            fixed (byte* utf8 = Utf8(text)) Native.webkit_find_controller_search(_findController, utf8, options, MaxFindMatches);
        }
        return 0;
    }

    private int CancelFindOnGtk(TaskCompletionSource<PageFindResult?> done)
    {
        if (ReferenceEquals(_findPending, done))
        {
            _findPending = null;
            _findPattern = null;
            _findFresh = false;
            _findTotal = null;
            if (_findController != 0) Native.webkit_find_controller_search_finish(_findController);
        }
        return 0;
    }

    public void ClearFind() => _ = OnGtk(() =>
    {
        if (_disposed != 0) return 0;
        _findPending?.TrySetResult(null);
        _findPending = null;
        _findPattern = null;
        _findFresh = false;
        _findTotal = null;
        if (_findController != 0) Native.webkit_find_controller_search_finish(_findController);
        return 0;
    });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Live.TryRemove(_id, out _);
        _findPending?.TrySetResult(null);
        _ = OnGtk(() =>
        {
            if (_view != 0) Native.webkit_web_view_stop_loading(_view);
            if (_findController != 0) Native.webkit_find_controller_search_finish(_findController);
            if (_window != 0) Native.gtk_widget_destroy(_window);
            if (_context != 0) Native.g_object_unref(_context);
            _window = _view = _context = _findController = 0;
            _plug = _keyTarget = 0;
            _focusTries = 0;
            return 0;
        });
    }

    private static Task<T> OnGtk<T>(Func<T> work) => Avalonia.X11.Interop.GtkInteropHelper.RunOnGlibThread(work);

    // ---- On the GTK thread ------------------------------------------------------------------------------

    private nint Build(bool offscreen)
    {
        Native.PrepareEnvironment();
        _context = Native.webkit_web_context_new_ephemeral();
        if (_context == 0) return 0;
        if (!Native.EnableAndVerifySandbox(_context)) return 0;
        _sandboxEnabled = true;
        Native.SetCacheModel(_context);
        fixed (byte* scheme = Utf8(Scheme))
            Native.webkit_web_context_register_uri_scheme(_context, scheme, (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnSchemeRequest, _id, 0);
        // Nothing reaches the web: whatever is not the page goes to a proxy that refuses it.
        Native.RefuseTheWeb(_context);
        Connect(_context, "download-started", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnDownload);

        _view = Native.webkit_web_view_new_with_context(_context);
        Native.TurnOffScripts(Native.webkit_web_view_get_settings(_view));
        _findController = Native.webkit_web_view_get_find_controller(_view);
        Connect(_findController, "found-text", (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, void>)&OnFoundText);
        Connect(_findController, "failed-to-find-text", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnFindFailed);
        Connect(_view, "decide-policy", (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, int>)&OnDecidePolicy);
        Connect(_view, "resource-load-started", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&OnResourceStarted);
        Connect(_view, "notify::title", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnTitle);
        Connect(_view, "load-changed", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, void>)&OnLoadChanged);
        Connect(_view, "load-failed", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, nint, nint, int>)&OnLoadFailed);
        Connect(_view, "web-process-terminated", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, void>)&OnProcessTerminated);
        Connect(_view, "permission-request", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnPermission);
        Connect(_view, "button-press-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnPressed);

        _window = offscreen ? Native.gtk_offscreen_window_new() : Native.gtk_plug_new(0);
        // On the window, so the viewer's keys are seen also while nothing in the page has focus.
        Connect(_window, "key-press-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnKey);
        Connect(_window, "set-focus", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnSetFocus);
        Native.gtk_container_add(_window, _view);
        Native.gtk_widget_show_all(_window);
        if (offscreen) return 0;
        _plug = Native.gtk_plug_get_id(_window);
        // Keys reach the page through GTK's focus window, which Focus gives the X focus; the plug itself stops
        // taking keys, so those under the pointer go to the viewer's window while the page does not have focus.
        _keyTarget = Native.FocusWindowOf(_plug);
        if (_keyTarget != 0) Native.StopKeys(_window);
        else Core.Diagnostics.AppLog.Warn("Web page engine: no GTK focus window in the page; its keys follow the pointer.");
        return (nint)_plug;
    }

    private void Connect(nint instance, string signal, nint handler)
    {
        fixed (byte* name = Utf8(signal)) Native.g_signal_connect_data(instance, name, handler, _id, 0, 0);
    }

    private static LinuxPageEngine? Of(nint data) => Live.TryGetValue(data, out var engine) && engine._disposed == 0 ? engine : null;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFoundText(nint controller, uint count, nint data)
    {
        try
        {
            var engine = Of(data);
            if (engine is null) return;
            // WebKit reports the total for a new search, but reports only the
            // directional result count for search_next/search_previous.
            if (engine._findFresh) engine._findTotal = FindResultFromCount(count);
            engine._findPending?.TrySetResult(engine._findTotal ?? new PageFindResult(true));
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Page find: " + ex.Message); }
    }

    internal static PageFindResult FindResultFromCount(uint count) => count switch
    {
        // WebKitGTK uses G_MAXUINT when the actual count exceeds max_match_count.
        uint.MaxValue => new(true, checked((int)MaxFindMatches + 1), false),
        > 0 and <= MaxFindMatches => new(true, (int)count, true),
        _ => new(true), // A successful directional result without a trustworthy total.
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFindFailed(nint controller, nint data)
    {
        try
        {
            var engine = Of(data);
            engine?._findPending?.TrySetResult(engine._findFresh
                ? new PageFindResult(false, 0, true) : new PageFindResult(false));
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Page find: " + ex.Message); }
    }

    /// <summary>A request for filecat://page/…: answered off the GTK thread (a page in an archive is read), then finished on it.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            var engine = Of(data);
            var page = engine?._page;
            if (page is null)
            {
                Native.FinishWithError(request, "Nothing is served here.");
                return;
            }
            string path = Native.String(Native.webkit_uri_scheme_request_get_path(request)) ?? "/";
            Native.g_object_ref(request);
            _ = Task.Run(() =>
            {
                (byte[] Bytes, string MimeType)? found = null;
                try { found = page.Resolve(path); }
                catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Page resource: " + ex.Message); }
                return OnGtk(() =>
                {
                    try
                    {
                        if (found is { } served) Native.Finish(request, served.Bytes, served.MimeType);
                        else
                        {
                            if (engine is not null) engine.Refused();
                            Native.FinishWithError(request, "Not served: only the page and the files in its folder are.");
                        }
                    }
                    finally { Native.g_object_unref(request); }
                    return 0;
                });
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
        }
    }

    /// <summary>Only the page's own address may be navigated to; new windows are refused.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnDecidePolicy(nint view, nint decision, int type, nint data)
    {
        try
        {
            const int NavigationAction = 0, NewWindowAction = 1;
            if (type == NewWindowAction)
            {
                Native.webkit_policy_decision_ignore(decision);
                var owner = Of(data);
                nint newAction = Native.webkit_navigation_policy_decision_get_navigation_action(decision);
                string newUri = Native.String(Native.webkit_uri_request_get_uri(Native.webkit_navigation_action_get_request(newAction))) ?? "";
                if (owner?._page is { } linkedPage && linkedPage.TryLinkedMarkdownPath(newUri, out string? linkedPath) && linkedPath is not null)
                    owner.LinkedMarkdown?.Invoke(linkedPage, linkedPath);
                else owner?.Refused();
                return 1;
            }
            if (type != NavigationAction) return 0;
            nint action = Native.webkit_navigation_policy_decision_get_navigation_action(decision);
            string uri = Native.String(Native.webkit_uri_request_get_uri(Native.webkit_navigation_action_get_request(action))) ?? "";
            var engine = Of(data);
            if (engine?._page is { } page && page.TryLinkedMarkdownPath(uri, out string? path) && path is not null)
            {
                Native.webkit_policy_decision_ignore(decision);
                engine.LinkedMarkdown?.Invoke(page, path);
                return 1;
            }
            if (engine?._page?.IsLinkedMarkdownRequest(uri) == true)
            {
                Native.webkit_policy_decision_ignore(decision);
                engine.Refused();
                return 1;
            }
            if (IsOwn(uri)) Native.webkit_policy_decision_use(decision);
            else
            {
                Native.webkit_policy_decision_ignore(decision);
                Of(data)?.Refused();
            }
            return 1;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
            return 0;
        }
    }

    /// <summary>Everything the page asks for that is not its own goes to the refusing proxy: counted as refused.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnResourceStarted(nint view, nint resource, nint request, nint data)
    {
        try
        {
            string uri = Native.String(Native.webkit_uri_request_get_uri(request)) ?? "";
            if (!IsOwn(uri) && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) Of(data)?.Refused();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
        }
    }

    private static bool IsOwn(string uri) =>
        uri.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase) || uri == "about:blank";

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnTitle(nint view, nint spec, nint data)
    {
        try
        {
            if (Of(data) is not { } engine) return;
            engine._title = Native.String(Native.webkit_web_view_get_title(view));
            engine.Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnLoadChanged(nint view, int loadEvent, nint data)
    {
        try
        {
            const int Finished = 3;
            if (loadEvent == Finished) Of(data)?.Loaded?.Invoke(true, null);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
        }
    }

    /// <summary>The page itself could not load (a refused sub-resource is not this).</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnLoadFailed(nint view, int loadEvent, nint failingUri, nint error, nint data)
    {
        try
        {
            if (Of(data) is not { } engine || !IsOwn(Native.String(failingUri) ?? "")) return 0;
            engine.Loaded?.Invoke(false, Native.ErrorMessage(error));
            return 0;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// The page's own process ended (WebKitWebProcessTerminationReason: crashed 0, over its memory limit 1, ended by
    /// FileCat 2): the page is reported as not shown, where it stayed blank (a Low of the review of c0e9cb5a).
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnProcessTerminated(nint view, int reason, nint data)
    {
        try
        {
            const int EndedByFileCat = 2, OverMemoryLimit = 1;
            if (reason == EndedByFileCat) return;
            Of(data)?.Loaded?.Invoke(false, reason == OverMemoryLimit
                ? "its page process used more memory than it may and was ended"
                : "its page process ended unexpectedly");
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnPermission(nint view, nint request, nint data)
    {
        try
        {
            Native.webkit_permission_request_deny(request);
            return 1;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDownload(nint context, nint download, nint data)
    {
        try
        {
            Native.webkit_download_cancel(download);
            Of(data)?.Refused();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
        }
    }

    /// <summary>The viewer's keys (Esc, function keys, Ctrl or Alt letters), while the page has the keyboard.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnKey(nint widget, nint gdkEvent, nint data)
    {
        try
        {
            if (Of(data) is not { } engine) return 0;
            uint keyval, state;
            if (Native.gdk_event_get_keyval(gdkEvent, &keyval) == 0 || Native.gdk_event_get_state(gdkEvent, &state) == 0) return 0;
            const uint Tab = 0xff09, LeftTab = 0xfe20;
            if (keyval is Tab or LeftTab)
            {
                engine._tabBackward = keyval == LeftTab || (state & 1) != 0;
                engine._tabAt = Environment.TickCount64;
            }
            if (engine.KeyRequested is not { } handler || ToKey(keyval) is not { } key) return 0;
            var modifiers = ((state & 4) != 0 ? KeyModifiers.Control : 0) | ((state & 1) != 0 ? KeyModifiers.Shift : 0) | ((state & 8) != 0 ? KeyModifiers.Alt : 0);
            return handler(key, modifiers) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
            return 0;
        }
    }

    /// <summary>A click in the page: its host takes focus, which gives the page the keys.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnPressed(nint widget, nint gdkEvent, nint data)
    {
        try { Of(data)?.FocusRequested?.Invoke(); }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
        return 0;
    }

    /// <summary>
    /// Nothing in the page has focus any more right after a Tab: it went past the page's last item, or Shift+Tab past
    /// its first (GTK would otherwise go round the page again, and Tab could never leave it).
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSetFocus(nint window, nint widget, nint data)
    {
        try
        {
            if (widget != 0 || Of(data) is not { } engine || Environment.TickCount64 - engine._tabAt > 1000) return;
            engine._tabAt = long.MinValue;
            engine.FocusLeft?.Invoke(engine._tabBackward);
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnFocusRetry(nint data)
    {
        try
        {
            if (Of(data) is not { } engine) return 0;
            if (engine._focusTries > 0 && engine._plug != 0 && !Native.SetInputFocus(engine.KeyTarget))
            {
                if (--engine._focusTries > 0) return 1;
                Core.Diagnostics.AppLog.Warn("Web page engine: the page did not take the keyboard.");
            }
            engine._focusTries = 0;
            engine._focusRetrying = false;
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
        return 0;
    }

    private static Key? ToKey(uint keyval) => keyval switch
    {
        0xff1b => Key.Escape,
        >= 0xffbe and <= 0xffc9 => Key.F1 + (int)(keyval - 0xffbe),
        >= 0x61 and <= 0x7a => Key.A + (int)(keyval - 0x61),
        >= 0x41 and <= 0x5a => Key.A + (int)(keyval - 0x41),
        _ => null,
    };

    private void Refused()
    {
        Interlocked.Increment(ref _blocked);
        Changed?.Invoke();
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

    /// <summary>GLib, GTK 3, and WebKitGTK entry points, found at run time: none of them is needed elsewhere.</summary>
    private static class Native
    {
        public static delegate* unmanaged[Cdecl]<nint, nint> g_object_ref;
        public static delegate* unmanaged[Cdecl]<nint, void> g_object_unref;
        public static delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, nint, int, nuint> g_signal_connect_data;
        public static delegate* unmanaged[Cdecl]<nint, nuint, nint> g_bytes_new;
        public static delegate* unmanaged[Cdecl]<nint, void> g_bytes_unref;
        public static delegate* unmanaged[Cdecl]<byte*, uint> g_quark_from_string;
        public static delegate* unmanaged[Cdecl]<uint, int, byte*, nint> g_error_new_literal;
        public static delegate* unmanaged[Cdecl]<nint, void> g_error_free;
        public static delegate* unmanaged[Cdecl]<nint, nint> g_memory_input_stream_new_from_bytes;
        public static delegate* unmanaged[Cdecl]<nuint, nint> gtk_plug_new;
        public static delegate* unmanaged[Cdecl]<nint, nuint> gtk_plug_get_id;
        public static delegate* unmanaged[Cdecl]<nint> gtk_offscreen_window_new;
        public static delegate* unmanaged[Cdecl]<nint, nint, void> gtk_container_add;
        public static delegate* unmanaged[Cdecl]<nint, void> gtk_widget_show_all;
        public static delegate* unmanaged[Cdecl]<nint, void> gtk_widget_destroy;
        public static delegate* unmanaged[Cdecl]<nint, void> gtk_widget_grab_focus;
        public static delegate* unmanaged[Cdecl]<nint, int, int, void> gtk_window_resize;
        public static delegate* unmanaged[Cdecl]<nint, int> gtk_widget_get_scale_factor;
        public static delegate* unmanaged[Cdecl]<nint, uint*, int> gdk_event_get_keyval;
        public static delegate* unmanaged[Cdecl]<nint, uint*, int> gdk_event_get_state;
        public static delegate* unmanaged[Cdecl]<uint, nint, nint, uint> g_timeout_add;
        // Optional: without them the keyboard stays where the X server has it.
        public static delegate* unmanaged[Cdecl]<nint> gdk_display_get_default;
        public static delegate* unmanaged[Cdecl]<nint, nint> gdk_x11_display_get_xdisplay;
        public static delegate* unmanaged[Cdecl]<nint, void> gdk_x11_display_error_trap_push;
        public static delegate* unmanaged[Cdecl]<nint, int> gdk_x11_display_error_trap_pop;
        public static delegate* unmanaged[Cdecl]<nint, nint> gtk_widget_get_window;
        public static delegate* unmanaged[Cdecl]<nint, int> gdk_window_get_events;
        public static delegate* unmanaged[Cdecl]<nint, int, void> gdk_window_set_events;
        public static delegate* unmanaged[Cdecl]<nint, nuint, int, nuint, int> XSetInputFocus;
        public static delegate* unmanaged[Cdecl]<nint, nuint, nuint*, nuint*, nuint**, uint*, int> XQueryTree;
        public static delegate* unmanaged[Cdecl]<nint, nuint, nuint*, int*, int*, uint*, uint*, uint*, uint*, int> XGetGeometry;
        public static delegate* unmanaged[Cdecl]<void*, int> XFree;
        public static delegate* unmanaged[Cdecl]<nint, byte*, int, nuint> XInternAtom;
        public static delegate* unmanaged[Cdecl]<nint, nuint, int, nint, void*, int> XSendEvent;

        private static nint XDisplay(out nint display)
        {
            display = XSetInputFocus == null ? 0 : gdk_display_get_default();
            return display == 0 ? 0 : gdk_x11_display_get_xdisplay(display);
        }

        /// <summary>The X input focus to a window, on GTK's own connection: false when it cannot take it (not on screen yet, or gone).</summary>
        public static bool SetInputFocus(nuint window)
        {
            nint xdisplay = XDisplay(out nint display);
            if (xdisplay == 0) return false;
            const int RevertToParent = 2;
            gdk_x11_display_error_trap_push(display);
            XSetInputFocus(xdisplay, window, RevertToParent, 0);
            return gdk_x11_display_error_trap_pop(display) == 0;
        }

        /// <summary>
        /// The window GTK keeps in a toplevel for the keyboard: 1 by 1 pixel at -1,-1, so keys sent to it do not depend
        /// on what the pointer is over. 0 when there is none.
        /// </summary>
        public static nuint FocusWindowOf(nuint toplevel)
        {
            nint xdisplay = XDisplay(out nint display);
            if (xdisplay == 0) return 0;
            nuint found = 0, root, parent;
            nuint* children = null;
            uint count = 0;
            gdk_x11_display_error_trap_push(display);
            if (XQueryTree(xdisplay, toplevel, &root, &parent, &children, &count) != 0 && children != null)
            {
                for (uint i = 0; i < count && found == 0; i++)
                {
                    int x, y;
                    uint width, height, border, depth;
                    if (XGetGeometry(xdisplay, children[i], &root, &x, &y, &width, &height, &border, &depth) != 0 &&
                        (x, y, width, height) == (-1, -1, 1u, 1u))
                        found = children[i];
                }
                XFree(children);
            }
            return gdk_x11_display_error_trap_pop(display) == 0 ? found : 0;
        }

        public const int XEmbedWindowActivate = 1, XEmbedWindowDeactivate = 2, XEmbedFocusIn = 4, XEmbedFocusOut = 5;
        private static nuint s_xembed;

        /// <summary>An XEmbed message to a plug, as its embedder would send it (detail 0: focus where it was).</summary>
        public static void SendXEmbed(nuint plug, int message, int detail = 0)
        {
            nint xdisplay = XDisplay(out nint display);
            if (xdisplay == 0) return;
            if (s_xembed == 0)
                fixed (byte* name = Utf8("_XEMBED")) s_xembed = XInternAtom(xdisplay, name, 0);
            // XClientMessageEvent in the 24-long XEvent union: type, serial, send_event, display, window,
            // message_type, format, then data.l[5] = time (CurrentTime), message, detail, data1, data2.
            nint* xevent = stackalloc nint[24];
            new Span<nint>(xevent, 24).Clear();
            const int ClientMessage = 33;
            *(int*)xevent = ClientMessage;
            xevent[3] = xdisplay;
            xevent[4] = (nint)plug;
            xevent[5] = (nint)s_xembed;
            *(int*)(xevent + 6) = 32;
            xevent[8] = message;
            xevent[9] = detail;
            gdk_x11_display_error_trap_push(display);
            XSendEvent(xdisplay, plug, 0, 0, xevent);
            gdk_x11_display_error_trap_pop(display);
        }

        /// <summary>The GTK window takes no keys of its own: they reach it through its focus window only.</summary>
        public static void StopKeys(nint gtkWindow)
        {
            const int KeyPressMask = 1 << 10, KeyReleaseMask = 1 << 11;
            if (gtk_widget_get_window(gtkWindow) is var window && window != 0)
                gdk_window_set_events(window, gdk_window_get_events(window) & ~(KeyPressMask | KeyReleaseMask));
        }
        public static delegate* unmanaged[Cdecl]<nint> webkit_web_context_new_ephemeral;
        public static delegate* unmanaged[Cdecl]<nint, int> webkit_web_context_get_sandbox_enabled;
        public static delegate* unmanaged[Cdecl]<nint, int, void> webkit_web_context_set_sandbox_enabled;
        public static delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, nint, void> webkit_web_context_register_uri_scheme;
        public static delegate* unmanaged[Cdecl]<nint, int, void> webkit_web_context_set_cache_model;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_web_context_get_website_data_manager;
        public static delegate* unmanaged[Cdecl]<byte*, nint, nint> webkit_network_proxy_settings_new;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_network_proxy_settings_free;
        public static delegate* unmanaged[Cdecl]<nint, int, nint, void> webkit_website_data_manager_set_network_proxy_settings;
        public static delegate* unmanaged[Cdecl]<nint, int, nint, void> webkit_web_context_set_network_proxy_settings;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_web_view_new_with_context;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_web_view_get_settings;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_web_view_get_find_controller;
        public static delegate* unmanaged[Cdecl]<nint, byte*, uint, uint, void> webkit_find_controller_search;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_find_controller_search_next;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_find_controller_search_previous;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_find_controller_search_finish;
        public static delegate* unmanaged[Cdecl]<nint, byte*, void> webkit_web_view_load_uri;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_web_view_get_title;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_web_view_stop_loading;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_uri_scheme_request_get_path;
        public static delegate* unmanaged[Cdecl]<nint, nint, long, byte*, void> webkit_uri_scheme_request_finish;
        public static delegate* unmanaged[Cdecl]<nint, nint, void> webkit_uri_scheme_request_finish_error;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_policy_decision_use;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_policy_decision_ignore;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_navigation_policy_decision_get_navigation_action;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_navigation_action_get_request;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_uri_request_get_uri;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_permission_request_deny;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_download_cancel;
        public static delegate* unmanaged[Cdecl]<byte*, byte*, int, int> setenv;

        /// <summary>Settings turned off, where this WebKitGTK has them (setter name, value).</summary>
        private static readonly (string Name, int Value)[] SafeSettings =
        [
            ("webkit_settings_set_enable_javascript", 0),
            ("webkit_settings_set_enable_javascript_markup", 0),
            ("webkit_settings_set_javascript_can_open_windows_automatically", 0),
            ("webkit_settings_set_enable_developer_extras", 0),
            ("webkit_settings_set_enable_html5_local_storage", 0),
            ("webkit_settings_set_enable_html5_database", 0),
            ("webkit_settings_set_enable_offline_web_application_cache", 0),
            ("webkit_settings_set_enable_page_cache", 0),
            ("webkit_settings_set_enable_hyperlink_auditing", 0),
            ("webkit_settings_set_enable_dns_prefetching", 0),
            ("webkit_settings_set_enable_webgl", 0),
            ("webkit_settings_set_enable_webaudio", 0),
            ("webkit_settings_set_enable_media_stream", 0),
            ("webkit_settings_set_enable_mediasource", 0),
            ("webkit_settings_set_enable_encrypted_media", 0),
            ("webkit_settings_set_enable_java", 0),
            ("webkit_settings_set_enable_plugins", 0),
            ("webkit_settings_set_allow_file_access_from_file_urls", 0),
            ("webkit_settings_set_allow_universal_access_from_file_urls", 0),
            ("webkit_settings_set_media_playback_requires_user_gesture", 1),
        ];

        private static readonly Dictionary<string, nint> Setters = [];

        /// <summary>Loads the libraries; why they cannot be used, or null.</summary>
        public static string? Load()
        {
            if (!OperatingSystem.IsLinux()) return "Web pages are drawn by WebKitGTK on Linux only.";
            if (SandboxSkippedHere(File.Exists, Environment.GetEnvironmentVariable) is { } skipped) return skipped;
            if (AppArmorProfileUnavailable() is { } appArmor) return appArmor;
            nint webkit = 0;
            foreach (string name in new[] { "libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37" })
                if (NativeLibrary.TryLoad(name, out webkit)) break;
            if (webkit == 0)
                return "Web pages are drawn by WebKitGTK, which is not installed (the libwebkit2gtk-4.1-0 package, webkit2gtk4.1 on Fedora).";
            if (!NativeLibrary.TryLoad("libgtk-3.so.0", out nint gtk) || !NativeLibrary.TryLoad("libgdk-3.so.0", out nint gdk) ||
                !NativeLibrary.TryLoad("libgobject-2.0.so.0", out nint gobject) || !NativeLibrary.TryLoad("libglib-2.0.so.0", out nint glib) ||
                !NativeLibrary.TryLoad("libgio-2.0.so.0", out nint gio) || !NativeLibrary.TryLoad("libc.so.6", out nint libc))
                return "GTK 3, which WebKitGTK needs, is not installed.";
            try
            {
                g_object_ref = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(gobject, "g_object_ref");
                g_object_unref = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(gobject, "g_object_unref");
                g_signal_connect_data = (delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, nint, int, nuint>)NativeLibrary.GetExport(gobject, "g_signal_connect_data");
                g_bytes_new = (delegate* unmanaged[Cdecl]<nint, nuint, nint>)NativeLibrary.GetExport(glib, "g_bytes_new");
                g_bytes_unref = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(glib, "g_bytes_unref");
                g_quark_from_string = (delegate* unmanaged[Cdecl]<byte*, uint>)NativeLibrary.GetExport(glib, "g_quark_from_string");
                g_error_new_literal = (delegate* unmanaged[Cdecl]<uint, int, byte*, nint>)NativeLibrary.GetExport(glib, "g_error_new_literal");
                g_error_free = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(glib, "g_error_free");
                g_memory_input_stream_new_from_bytes = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(gio, "g_memory_input_stream_new_from_bytes");
                gtk_plug_new = (delegate* unmanaged[Cdecl]<nuint, nint>)NativeLibrary.GetExport(gtk, "gtk_plug_new");
                gtk_plug_get_id = (delegate* unmanaged[Cdecl]<nint, nuint>)NativeLibrary.GetExport(gtk, "gtk_plug_get_id");
                gtk_offscreen_window_new = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(gtk, "gtk_offscreen_window_new");
                gtk_container_add = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(gtk, "gtk_container_add");
                gtk_widget_show_all = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(gtk, "gtk_widget_show_all");
                gtk_widget_destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(gtk, "gtk_widget_destroy");
                gtk_widget_grab_focus = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(gtk, "gtk_widget_grab_focus");
                gtk_window_resize = (delegate* unmanaged[Cdecl]<nint, int, int, void>)NativeLibrary.GetExport(gtk, "gtk_window_resize");
                gtk_widget_get_scale_factor = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(gtk, "gtk_widget_get_scale_factor");
                gdk_event_get_keyval = (delegate* unmanaged[Cdecl]<nint, uint*, int>)NativeLibrary.GetExport(gdk, "gdk_event_get_keyval");
                gdk_event_get_state = (delegate* unmanaged[Cdecl]<nint, uint*, int>)NativeLibrary.GetExport(gdk, "gdk_event_get_state");
                g_timeout_add = (delegate* unmanaged[Cdecl]<uint, nint, nint, uint>)NativeLibrary.GetExport(glib, "g_timeout_add");
                if (NativeLibrary.TryLoad("libX11.so.6", out nint x11) &&
                    NativeLibrary.TryGetExport(gdk, "gdk_display_get_default", out nint defaultDisplay) &&
                    NativeLibrary.TryGetExport(gdk, "gdk_x11_display_get_xdisplay", out nint xdisplay) &&
                    NativeLibrary.TryGetExport(gdk, "gdk_x11_display_error_trap_push", out nint trapPush) &&
                    NativeLibrary.TryGetExport(gdk, "gdk_x11_display_error_trap_pop", out nint trapPop) &&
                    NativeLibrary.TryGetExport(gtk, "gtk_widget_get_window", out nint widgetWindow) &&
                    NativeLibrary.TryGetExport(gdk, "gdk_window_get_events", out nint getEvents) &&
                    NativeLibrary.TryGetExport(gdk, "gdk_window_set_events", out nint setEvents) &&
                    NativeLibrary.TryGetExport(x11, "XSetInputFocus", out nint setFocus) &&
                    NativeLibrary.TryGetExport(x11, "XQueryTree", out nint queryTree) &&
                    NativeLibrary.TryGetExport(x11, "XGetGeometry", out nint geometry) &&
                    NativeLibrary.TryGetExport(x11, "XFree", out nint free) &&
                    NativeLibrary.TryGetExport(x11, "XInternAtom", out nint internAtom) &&
                    NativeLibrary.TryGetExport(x11, "XSendEvent", out nint sendEvent))
                {
                    gdk_display_get_default = (delegate* unmanaged[Cdecl]<nint>)defaultDisplay;
                    gdk_x11_display_get_xdisplay = (delegate* unmanaged[Cdecl]<nint, nint>)xdisplay;
                    gdk_x11_display_error_trap_push = (delegate* unmanaged[Cdecl]<nint, void>)trapPush;
                    gdk_x11_display_error_trap_pop = (delegate* unmanaged[Cdecl]<nint, int>)trapPop;
                    gtk_widget_get_window = (delegate* unmanaged[Cdecl]<nint, nint>)widgetWindow;
                    gdk_window_get_events = (delegate* unmanaged[Cdecl]<nint, int>)getEvents;
                    gdk_window_set_events = (delegate* unmanaged[Cdecl]<nint, int, void>)setEvents;
                    XQueryTree = (delegate* unmanaged[Cdecl]<nint, nuint, nuint*, nuint*, nuint**, uint*, int>)queryTree;
                    XGetGeometry = (delegate* unmanaged[Cdecl]<nint, nuint, nuint*, int*, int*, uint*, uint*, uint*, uint*, int>)geometry;
                    XFree = (delegate* unmanaged[Cdecl]<void*, int>)free;
                    XInternAtom = (delegate* unmanaged[Cdecl]<nint, byte*, int, nuint>)internAtom;
                    XSendEvent = (delegate* unmanaged[Cdecl]<nint, nuint, int, nint, void*, int>)sendEvent;
                    XSetInputFocus = (delegate* unmanaged[Cdecl]<nint, nuint, int, nuint, int>)setFocus;
                }
                else Core.Diagnostics.AppLog.Warn("Web page engine: X11 focus functions are missing; a page's keys follow the pointer.");
                webkit_web_context_new_ephemeral = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(webkit, "webkit_web_context_new_ephemeral");
                webkit_web_context_register_uri_scheme = (delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, nint, void>)NativeLibrary.GetExport(webkit, "webkit_web_context_register_uri_scheme");
                webkit_network_proxy_settings_new = (delegate* unmanaged[Cdecl]<byte*, nint, nint>)NativeLibrary.GetExport(webkit, "webkit_network_proxy_settings_new");
                webkit_network_proxy_settings_free = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_network_proxy_settings_free");
                webkit_web_view_new_with_context = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_web_view_new_with_context");
                webkit_web_view_get_settings = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_web_view_get_settings");
                webkit_web_view_get_find_controller = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_web_view_get_find_controller");
                webkit_find_controller_search = (delegate* unmanaged[Cdecl]<nint, byte*, uint, uint, void>)NativeLibrary.GetExport(webkit, "webkit_find_controller_search");
                webkit_find_controller_search_next = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_find_controller_search_next");
                webkit_find_controller_search_previous = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_find_controller_search_previous");
                webkit_find_controller_search_finish = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_find_controller_search_finish");
                webkit_web_view_load_uri = (delegate* unmanaged[Cdecl]<nint, byte*, void>)NativeLibrary.GetExport(webkit, "webkit_web_view_load_uri");
                webkit_web_view_get_title = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_web_view_get_title");
                webkit_web_view_stop_loading = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_web_view_stop_loading");
                webkit_uri_scheme_request_get_path = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_scheme_request_get_path");
                webkit_uri_scheme_request_finish = (delegate* unmanaged[Cdecl]<nint, nint, long, byte*, void>)NativeLibrary.GetExport(webkit, "webkit_uri_scheme_request_finish");
                webkit_uri_scheme_request_finish_error = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(webkit, "webkit_uri_scheme_request_finish_error");
                webkit_policy_decision_use = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_policy_decision_use");
                webkit_policy_decision_ignore = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_policy_decision_ignore");
                webkit_navigation_policy_decision_get_navigation_action = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_navigation_policy_decision_get_navigation_action");
                webkit_navigation_action_get_request = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_navigation_action_get_request");
                webkit_uri_request_get_uri = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_uri_request_get_uri");
                webkit_permission_request_deny = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_permission_request_deny");
                webkit_download_cancel = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_download_cancel");
                setenv = (delegate* unmanaged[Cdecl]<byte*, byte*, int, int>)NativeLibrary.GetExport(libc, "setenv");
            }
            catch (EntryPointNotFoundException ex)
            {
                return "This WebKitGTK is too old for FileCat's page view: " + ex.Message;
            }
            // Newer and older ways of the same thing, and settings some versions lack.
            if (NativeLibrary.TryGetExport(webkit, "webkit_web_context_set_cache_model", out nint cacheModel))
                webkit_web_context_set_cache_model = (delegate* unmanaged[Cdecl]<nint, int, void>)cacheModel;
            if (NativeLibrary.TryGetExport(webkit, "webkit_web_context_get_website_data_manager", out nint manager) &&
                NativeLibrary.TryGetExport(webkit, "webkit_website_data_manager_set_network_proxy_settings", out nint managerProxy))
            {
                webkit_web_context_get_website_data_manager = (delegate* unmanaged[Cdecl]<nint, nint>)manager;
                webkit_website_data_manager_set_network_proxy_settings = (delegate* unmanaged[Cdecl]<nint, int, nint, void>)managerProxy;
            }
            else if (NativeLibrary.TryGetExport(webkit, "webkit_web_context_set_network_proxy_settings", out nint contextProxy))
                webkit_web_context_set_network_proxy_settings = (delegate* unmanaged[Cdecl]<nint, int, nint, void>)contextProxy;
            else return "This WebKitGTK cannot keep pages off the web (no proxy settings).";
            if (!NativeLibrary.TryGetExport(webkit, "webkit_web_context_get_sandbox_enabled", out nint getSandbox) ||
                !NativeLibrary.TryGetExport(webkit, "webkit_web_context_set_sandbox_enabled", out nint setSandbox))
                return "This WebKitGTK cannot verify and enable its web-process sandbox.";
            webkit_web_context_get_sandbox_enabled = (delegate* unmanaged[Cdecl]<nint, int>)getSandbox;
            webkit_web_context_set_sandbox_enabled = (delegate* unmanaged[Cdecl]<nint, int, void>)setSandbox;
            foreach (var (name, _) in SafeSettings)
                if (NativeLibrary.TryGetExport(webkit, name, out nint setter)) Setters[name] = setter;
            return Setters.ContainsKey("webkit_settings_set_enable_javascript") ? null : "This WebKitGTK cannot turn scripts off.";
        }

        /// <summary>
        /// Software drawing for the web process (read when it starts): accelerated drawing in a window another toolkit
        /// took in, or on a display without a GPU, can leave the page blank. A user's own setting stays.
        /// </summary>
        public static void PrepareEnvironment()
        {
            foreach (string name in new[] { "WEBKIT_DISABLE_DMABUF_RENDERER", "WEBKIT_DISABLE_COMPOSITING_MODE" })
                fixed (byte* key = Utf8(name))
                fixed (byte* one = Utf8("1"))
                    setenv(key, one, 0);
        }

        public static void SetCacheModel(nint context)
        {
            const int DocumentViewer = 0;
            if (webkit_web_context_set_cache_model != null) webkit_web_context_set_cache_model(context, DocumentViewer);
        }

        /// <summary>Require WebKit's native web-process sandbox before constructing a view or loading a page.</summary>
        public static bool EnableAndVerifySandbox(nint context)
        {
            if (webkit_web_context_get_sandbox_enabled(context) == 0)
                webkit_web_context_set_sandbox_enabled(context, 1);
            return webkit_web_context_get_sandbox_enabled(context) != 0;
        }

        public static void RefuseTheWeb(nint context)
        {
            const int Custom = 2;
            nint settings;
            fixed (byte* proxy = Utf8("http://127.0.0.1:9")) settings = webkit_network_proxy_settings_new(proxy, 0);
            if (webkit_website_data_manager_set_network_proxy_settings != null)
                webkit_website_data_manager_set_network_proxy_settings(webkit_web_context_get_website_data_manager(context), Custom, settings);
            else webkit_web_context_set_network_proxy_settings(context, Custom, settings);
            webkit_network_proxy_settings_free(settings);
        }

        public static void TurnOffScripts(nint settings)
        {
            foreach (var (name, value) in SafeSettings)
                if (Setters.TryGetValue(name, out nint setter)) ((delegate* unmanaged[Cdecl]<nint, int, void>)setter)(settings, value);
        }

        public static void Finish(nint request, byte[] bytes, string mimeType)
        {
            nint data;
            fixed (byte* start = bytes) data = g_bytes_new((nint)start, (nuint)bytes.Length);
            nint stream = g_memory_input_stream_new_from_bytes(data);
            g_bytes_unref(data);
            fixed (byte* mime = Utf8(mimeType)) webkit_uri_scheme_request_finish(request, stream, bytes.Length, mime);
            g_object_unref(stream);
        }

        public static void FinishWithError(nint request, string message)
        {
            nint error;
            fixed (byte* domain = Utf8("filecat-page"))
            fixed (byte* text = Utf8(message))
                error = g_error_new_literal(g_quark_from_string(domain), 403, text);
            webkit_uri_scheme_request_finish_error(request, error);
            g_error_free(error);
        }

        public static string? String(nint utf8) => utf8 == 0 ? null : Marshal.PtrToStringUTF8(utf8);

        /// <summary>A GError's message: its third field, after the domain and the code.</summary>
        public static string? ErrorMessage(nint error) => error == 0 ? null : String(Marshal.ReadIntPtr(error, 8));
    }
}
