using System.Runtime.InteropServices;
using FileCat.Core.Content;
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
        _ = StartAsync();
    }

    /// <summary>The child window the page is drawn in.</summary>
    public nint Handle { get; }

    public string DataFolder { get; }

    /// <summary>The page's title (its title element; scripts do not change it).</summary>
    public event Action<string>? TitleChanged;

    /// <summary>A request or a navigation to somewhere else than the page and its folder was refused.</summary>
    public event Action<string>? Blocked;

    /// <summary>The page finished loading (true), or could not be shown (false, with why).</summary>
    public event Action<bool, string?>? Loaded;

    /// <summary>A key FileCat's viewer may want (Esc, function keys, Ctrl and Alt combinations): the handler says whether it took it.</summary>
    public Func<uint, bool, bool, bool, bool>? KeyPressed { get; set; }

    /// <summary>A request for the page's own address was answered: the path, and whether it was found (diagnostics, tests).</summary>
    public event Action<string, bool>? Served;

    /// <summary>How many requests were refused (the status line counts them).</summary>
    public int BlockedCount { get; private set; }

    public string? Title => _controller?.CoreWebView2.DocumentTitle;

    private async Task StartAsync()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var options = new CoreWebView2EnvironmentOptions
            {
                // Nothing reaches the network: a proxy that refuses, and no background fetching (belt and braces: every
                // request is answered by FileCat or refused before it would get that far).
                AdditionalBrowserArguments = "--proxy-server=http://127.0.0.1:9 --disable-background-networking --disable-component-update --no-pings --disable-domain-reliability",
                AreBrowserExtensionsEnabled = false,
            };
            _environment = await CoreWebView2Environment.CreateAsync(null, DataFolder, options);
            var controllerOptions = _environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.IsInPrivateModeEnabled = true;
            controllerOptions.ProfileName = "FileCatPage";
            _controller = await _environment.CreateCoreWebView2ControllerAsync(Handle, controllerOptions);
            if (_disposed)
            {
                _controller.Close();
                return;
            }
            var core = _controller.CoreWebView2;
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
            core.NavigationCompleted += (_, e) => Loaded?.Invoke(e.IsSuccess, e.IsSuccess ? null : e.WebErrorStatus.ToString());
            _controller.AcceleratorKeyPressed += OnAcceleratorKey;
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, _size.Width, _size.Height);
            _controller.IsVisible = true;
            if (_page is { } page) core.Navigate(page.Address);
        }
        catch (Exception ex) when (ex is COMException or WebView2RuntimeNotFoundException or UnauthorizedAccessException or IOException or InvalidOperationException or ArgumentException)
        {
            Loaded?.Invoke(false, ex.Message);
        }
    }

    /// <summary>A picture of the page as drawn, as PNG (tests and diagnostics: what a reader would see).</summary>
    internal Task CaptureAsync(Stream png) =>
        _controller?.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png) ?? Task.CompletedTask;

    /// <summary>Shows a page (at once, or once the view is ready).</summary>
    public void Show(HtmlPage page)
    {
        _page = page;
        _controller?.CoreWebView2.Navigate(page.Address);
    }

    /// <summary>The view's size in physical pixels (the child window's; the host sizes that window).</summary>
    public void SetSize(int width, int height)
    {
        _size = (Math.Max(1, width), Math.Max(1, height));
        if (_controller is { } controller) controller.Bounds = new System.Drawing.Rectangle(0, 0, _size.Width, _size.Height);
    }

    /// <summary>The keyboard to the page (after a click elsewhere in the viewer).</summary>
    public void Focus() => _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);

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
            Refuse(uri);
            e.Response = environment.CreateWebResourceResponse(null, 403, "Refused by FileCat", "");
            return;
        }
        var deferral = e.GetDeferral();
        try
        {
            string path = new Uri(uri).AbsolutePath;
            var found = await Task.Run(() => page.Resolve(path));
            if (_disposed) return;
            Served?.Invoke(path, found is not null);
            e.Response = found is { } content
                ? environment.CreateWebResourceResponse(new MemoryStream(content.Bytes, writable: false), 200, "OK", $"Content-Type: {content.MimeType}\r\nCache-Control: no-store")
                : environment.CreateWebResourceResponse(null, 404, "Not found", "");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // The view closed meanwhile.
        }
        finally
        {
            try { deferral.Complete(); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { }
        }
    }

    /// <summary>The page may go to other pages in its folder (a link, an anchor); anywhere else is refused.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (OurAddress(e.Uri)) return;
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
        try { _controller?.Close(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        _controller = null;
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandleW(string? name);
    }
}
