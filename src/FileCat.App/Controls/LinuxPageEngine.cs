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
internal sealed unsafe class LinuxPageEngine : IPageEngine
{
    private const string Scheme = "filecat";
    private static readonly ConcurrentDictionary<nint, LinuxPageEngine> Live = new();
    private static long s_nextId;
    private static readonly Lazy<string?> s_missing = new(() => Native.Load());

    private readonly nint _id;
    private nint _context, _view, _window;
    private volatile HtmlPage? _page;
    private volatile string? _title;
    private int _blocked;
    private int _disposed;

    /// <summary>Why WebKitGTK cannot be used here, or null when it can.</summary>
    public static string? Unavailable => s_missing.Value;

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
            if (!built.Wait(TimeSpan.FromSeconds(15)) || built.Result == 0 && !offscreen)
            {
                unavailable = "The web engine (WebKitGTK) did not start.";
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
    public event Action? Changed;
    public event Action<bool, string?>? Loaded;

    /// <summary>Called on the GTK thread: the viewer's handler only posts its work to the UI thread.</summary>
    public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }

    public void Show(HtmlPage page)
    {
        _page = page;
        string address = $"{Scheme}://page/{Uri.EscapeDataString(page.Name)}";
        _ = OnGtk(() =>
        {
            if (_view == 0) return 0;
            fixed (byte* uri = Utf8(address)) Native.webkit_web_view_load_uri(_view, uri);
            return 0;
        });
    }

    public void SetSize(int width, int height) => _ = OnGtk(() =>
    {
        if (_window != 0 && width > 0 && height > 0) Native.gtk_window_resize(_window, width, height);
        return 0;
    });

    public void Focus() => _ = OnGtk(() =>
    {
        if (_view != 0) Native.gtk_widget_grab_focus(_view);
        return 0;
    });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Live.TryRemove(_id, out _);
        _ = OnGtk(() =>
        {
            if (_view != 0) Native.webkit_web_view_stop_loading(_view);
            if (_window != 0) Native.gtk_widget_destroy(_window);
            if (_context != 0) Native.g_object_unref(_context);
            _window = _view = _context = 0;
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
        Native.SetCacheModel(_context);
        fixed (byte* scheme = Utf8(Scheme))
            Native.webkit_web_context_register_uri_scheme(_context, scheme, (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnSchemeRequest, _id, 0);
        // Nothing reaches the web: whatever is not the page goes to a proxy that refuses it.
        Native.RefuseTheWeb(_context);
        Connect(_context, "download-started", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnDownload);

        _view = Native.webkit_web_view_new_with_context(_context);
        Native.TurnOffScripts(Native.webkit_web_view_get_settings(_view));
        Connect(_view, "decide-policy", (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, int>)&OnDecidePolicy);
        Connect(_view, "resource-load-started", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&OnResourceStarted);
        Connect(_view, "notify::title", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnTitle);
        Connect(_view, "load-changed", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, void>)&OnLoadChanged);
        Connect(_view, "load-failed", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, nint, nint, int>)&OnLoadFailed);
        Connect(_view, "permission-request", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnPermission);
        Connect(_view, "key-press-event", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnKey);

        _window = offscreen ? Native.gtk_offscreen_window_new() : Native.gtk_plug_new(0);
        Native.gtk_container_add(_window, _view);
        Native.gtk_widget_show_all(_window);
        return offscreen ? 0 : (nint)Native.gtk_plug_get_id(_window);
    }

    private void Connect(nint instance, string signal, nint handler)
    {
        fixed (byte* name = Utf8(signal)) Native.g_signal_connect_data(instance, name, handler, _id, 0, 0);
    }

    private static LinuxPageEngine? Of(nint data) => Live.TryGetValue(data, out var engine) && engine._disposed == 0 ? engine : null;

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
                Of(data)?.Refused();
                return 1;
            }
            if (type != NavigationAction) return 0;
            nint action = Native.webkit_navigation_policy_decision_get_navigation_action(decision);
            string uri = Native.String(Native.webkit_uri_request_get_uri(Native.webkit_navigation_action_get_request(action))) ?? "";
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
            if (Of(data) is not { KeyRequested: { } handler }) return 0;
            uint keyval, state;
            if (Native.gdk_event_get_keyval(gdkEvent, &keyval) == 0 || Native.gdk_event_get_state(gdkEvent, &state) == 0) return 0;
            if (ToKey(keyval) is not { } key) return 0;
            var modifiers = ((state & 4) != 0 ? KeyModifiers.Control : 0) | ((state & 1) != 0 ? KeyModifiers.Shift : 0) | ((state & 8) != 0 ? KeyModifiers.Alt : 0);
            return handler(key, modifiers) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message);
            return 0;
        }
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
        public static delegate* unmanaged[Cdecl]<nint, uint*, int> gdk_event_get_keyval;
        public static delegate* unmanaged[Cdecl]<nint, uint*, int> gdk_event_get_state;
        public static delegate* unmanaged[Cdecl]<nint> webkit_web_context_new_ephemeral;
        public static delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, nint, void> webkit_web_context_register_uri_scheme;
        public static delegate* unmanaged[Cdecl]<nint, int, void> webkit_web_context_set_cache_model;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_web_context_get_website_data_manager;
        public static delegate* unmanaged[Cdecl]<byte*, nint, nint> webkit_network_proxy_settings_new;
        public static delegate* unmanaged[Cdecl]<nint, void> webkit_network_proxy_settings_free;
        public static delegate* unmanaged[Cdecl]<nint, int, nint, void> webkit_website_data_manager_set_network_proxy_settings;
        public static delegate* unmanaged[Cdecl]<nint, int, nint, void> webkit_web_context_set_network_proxy_settings;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_web_view_new_with_context;
        public static delegate* unmanaged[Cdecl]<nint, nint> webkit_web_view_get_settings;
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
                gdk_event_get_keyval = (delegate* unmanaged[Cdecl]<nint, uint*, int>)NativeLibrary.GetExport(gdk, "gdk_event_get_keyval");
                gdk_event_get_state = (delegate* unmanaged[Cdecl]<nint, uint*, int>)NativeLibrary.GetExport(gdk, "gdk_event_get_state");
                webkit_web_context_new_ephemeral = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(webkit, "webkit_web_context_new_ephemeral");
                webkit_web_context_register_uri_scheme = (delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, nint, void>)NativeLibrary.GetExport(webkit, "webkit_web_context_register_uri_scheme");
                webkit_network_proxy_settings_new = (delegate* unmanaged[Cdecl]<byte*, nint, nint>)NativeLibrary.GetExport(webkit, "webkit_network_proxy_settings_new");
                webkit_network_proxy_settings_free = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(webkit, "webkit_network_proxy_settings_free");
                webkit_web_view_new_with_context = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_web_view_new_with_context");
                webkit_web_view_get_settings = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(webkit, "webkit_web_view_get_settings");
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
