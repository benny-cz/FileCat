using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using FileCat.Core.Content;

namespace FileCat.App.Controls;

/// <summary>
/// The viewer's page view (plan §16.1, D-51): a web page drawn by the system's browser engine in a native control
/// (WebView2 on Windows, WebKitGTK on Linux, WKWebView on macOS), with scripts off and nothing fetched from the web or
/// from outside the page's folder. Where no engine is available, it says why and the viewer shows the page's source.
/// </summary>
public sealed class PageView : NativeControlHost
{
    private readonly string _dataFolder;
    private IPageEngine? _engine;
    private HtmlPage? _page;
    private IDisposable? _noHost;
    private bool _hasKeyboard;
    private TopLevel? _keyboardOwner;

    public PageView(string dataFolder)
    {
        _dataFolder = dataFolder;
        if (OperatingSystem.IsLinux())
        {
            // Orca follows focus into the embedded GTK page, but Avalonia's AT-SPI tree has no socket for its plug
            // (UXBUG-021): readers said nothing while the page itself had the keyboard, before Tab reached anything in
            // it. The page's host takes focus first, so they hear what it is and how to move into it.
            Focusable = true;
            Avalonia.Automation.AutomationProperties.SetHelpText(this, LinuxReaderHelp);
        }
    }

    /// <summary>What a Linux screen reader hears on the page's host, before focus moves into the page.</summary>
    internal const string LinuxReaderHelp = "Tab moves into the page. F4 shows the same file as text.";

    /// <summary>The page could not be shown: why (no engine installed, or none on this display).</summary>
    public event Action<string>? Failed;

    /// <summary>Loading finished, a request was refused, or the title changed: the status line says it anew.</summary>
    public event Action? Changed;

    /// <summary>The current navigation completed; used to keep an older viewer snapshot until replacement loads.</summary>
    public event Action<bool, string?>? LoadCompleted;

    /// <summary>Keys the page would otherwise keep (Esc, function keys, Ctrl and Alt combinations): whether the viewer took one.</summary>
    public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }

    public string? Title => _engine?.Title;

    public int BlockedCount => _engine?.BlockedCount ?? 0;

    /// <summary>Whether the page finished loading.</summary>
    public bool PageLoaded { get; private set; }

    /// <summary>Shows a page, now or once the native view exists.</summary>
    public void Show(HtmlPage page)
    {
        _page = page;
        PageLoaded = false;
        _engine?.Show(page);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (OperatingSystem.IsLinux() && TopLevel.GetTopLevel(this) is { } top)
        {
            _keyboardOwner = top;
            top.AddHandler(GotFocusEvent, OnWindowGotFocus, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            if (top is WindowBase window) window.Activated += OnWindowActivated;
        }
        // A display without native controls (a headless test, say) never asks for one: after a moment, say so.
        _noHost?.Dispose();
        _noHost = DispatcherTimer.RunOnce(() =>
        {
            if (_engine is null && IsEffectivelyVisible) Failed?.Invoke("Pages cannot be drawn on this display.");
        }, TimeSpan.FromSeconds(3));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_keyboardOwner is { } top)
        {
            top.RemoveHandler(GotFocusEvent, OnWindowGotFocus);
            if (top is WindowBase window) window.Activated -= OnWindowActivated;
            _keyboardOwner = null;
        }
        _hasKeyboard = false;
        base.OnDetachedFromVisualTree(e);
    }

    // Linux: the X keyboard focus follows Avalonia's focus between the page and the rest of its window (see
    // LinuxPageEngine.Focus). Another control taking focus, such as Find after F7, takes the keys back from the page;
    // the window coming back with the page focused gives them to the page again.
    private void OnWindowGotFocus(object? sender, Avalonia.Input.FocusChangedEventArgs e)
    {
        if (!_hasKeyboard || ReferenceEquals(e.Source, this)) return;
        _hasKeyboard = false;
        _engine?.ReturnFocus(_keyboardOwner?.TryGetPlatformHandle()?.Handle ?? 0);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!IsFocused) return;
        _hasKeyboard = true;
        _engine?.Focus();
    }

    // Linux: however the host gets focus (the viewer focusing the page, or Tab from the controls around it), the keys
    // go into the page next, so the following Tab moves into it as the host's help says.
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (!OperatingSystem.IsLinux()) return;
        _hasKeyboard = true;
        _engine?.Focus();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _noHost?.Dispose();
        try
        {
            _engine = PageEngines.Create(parent, _dataFolder, out string? unavailable);
            if (_engine is null)
            {
                Dispatcher.UIThread.Post(() => Failed?.Invoke(unavailable ?? "No web engine is available here."));
                return base.CreateNativeControlCore(parent);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Dispatcher.UIThread.Post(() => Failed?.Invoke("The web engine could not start: " + ex.Message));
            return base.CreateNativeControlCore(parent);
        }
        ConnectEngine(_engine);
        Resize();
        if (_page is { } page) _engine.Show(page);
        return _engine.Handle;
    }

    internal void InstallEngineForTests(IPageEngine engine)
    {
        _engine = engine;
        ConnectEngine(engine);
        if (_page is { } page) engine.Show(page);
    }

    private void ConnectEngine(IPageEngine engine)
    {
        engine.Changed += () => Dispatcher.UIThread.Post(() => Changed?.Invoke());
        engine.Loaded += (ok, why) => Dispatcher.UIThread.Post(() =>
        {
            PageLoaded = ok;
            LoadCompleted?.Invoke(ok, why);
            if (!ok && why is not null) Failed?.Invoke("The page could not be shown: " + why);
            Changed?.Invoke();
        });
        engine.KeyRequested = (key, modifiers) => KeyRequested?.Invoke(key, modifiers) == true;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_engine is { } engine && ReferenceEquals(control, engine.Handle))
        {
            _engine = null;
            engine.Dispose();
            return;
        }
        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Resize();
    }

    // Avalonia gives a native control host an interop child for backends that embed native accessibility; Linux's
    // cannot follow it into the GTK plug and showed an unnamed, unknown node. There the host itself is the page.
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        OperatingSystem.IsLinux() ? new LinuxPageHostPeer(this) : base.OnCreateAutomationPeer();

    private sealed class LinuxPageHostPeer(PageView owner) : Avalonia.Automation.Peers.ControlAutomationPeer(owner)
    {
        protected override Avalonia.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            Avalonia.Automation.Peers.AutomationControlType.Group;

        protected override IReadOnlyList<Avalonia.Automation.Peers.AutomationPeer>? GetChildrenCore() => null;
    }

    private void Resize()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        _engine?.SetSize((int)Math.Ceiling(Bounds.Width * scaling), (int)Math.Ceiling(Bounds.Height * scaling));
    }

    /// <summary>The keyboard to the page; on Linux through its host, which screen readers can announce.</summary>
    public void FocusPage()
    {
        if (OperatingSystem.IsLinux())
        {
            if (!IsFocused && Focus()) return; // OnGotFocus gives the page the keys.
            _hasKeyboard = true;
        }
        _engine?.Focus();
    }

    /// <summary>Finds visible text in the current rendered page. Null means no page or no find capability.</summary>
    public async Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct)
    {
        var engine = _engine;
        var page = _page;
        if (!PageLoaded || engine is null || page is null) return null;
        var found = await engine.FindAsync(text, matchCase, forward, ct);
        ct.ThrowIfCancellationRequested();
        return ReferenceEquals(engine, _engine) && ReferenceEquals(page, _page) && PageLoaded ? found : null;
    }

    public void ClearFind() => _engine?.ClearFind();
}

/// <summary>One platform's web engine behind <see cref="PageView"/>.</summary>
internal interface IPageEngine : IDisposable
{
    IPlatformHandle Handle { get; }
    string? Title { get; }
    int BlockedCount { get; }
    event Action? Changed;
    event Action<bool, string?>? Loaded;
    Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
    void Show(HtmlPage page);
    void SetSize(int width, int height);
    void Focus();
    /// <summary>The keyboard back to the window holding the page (its native handle, 0 if unknown), where only the engine can move it.</summary>
    void ReturnFocus(nint window) { }
    Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct) =>
        Task.FromResult<PageFindResult?>(null);
    void ClearFind() { }
}

/// <summary>A directional rendered-page find, with a native total when the engine supplies one.</summary>
public readonly record struct PageFindResult(bool Found, int? MatchCount = null, bool CountComplete = false);

internal static class PageEngines
{
    /// <summary>This platform's engine inside <paramref name="parent"/>, or null with why there is none.</summary>
    public static IPageEngine? Create(IPlatformHandle parent, string dataFolder, out string? unavailable)
    {
        unavailable = null;
        if (OperatingSystem.IsWindows())
        {
            if (Platform.Windows.Html.WebView2Page.RuntimeVersion is null)
            {
                unavailable = "Web pages are drawn by Microsoft Edge WebView2, which is not installed (it comes with Windows 11; for Windows 10 it is a free download from Microsoft).";
                return null;
            }
            return new WindowsPageEngine(parent.Handle, dataFolder);
        }
        if (OperatingSystem.IsLinux())
        {
            if (parent.HandleDescriptor != "XID")
            {
                unavailable = "Web pages are drawn in X11 windows, and this display is not one.";
                return null;
            }
            return LinuxPageEngine.Create(offscreen: false, out unavailable);
        }
        if (OperatingSystem.IsMacOS()) return MacPageEngine.Create(dataFolder, out unavailable);
        unavailable = "Web pages are not drawn on this system.";
        return null;
    }
}

/// <summary>WebView2 (Windows), adapted to <see cref="IPageEngine"/>.</summary>
internal sealed class WindowsPageEngine : IPageEngine
{
    private readonly Platform.Windows.Html.WebView2Page _view;

    public WindowsPageEngine(nint parent, string dataFolder)
    {
        _view = new Platform.Windows.Html.WebView2Page(parent, dataFolder);
        Handle = new PlatformHandle(_view.Handle, "HWND");
        _view.TitleChanged += _ => Changed?.Invoke();
        _view.Blocked += _ => Changed?.Invoke();
        _view.Loaded += (ok, why) => Loaded?.Invoke(ok, why);
        _view.KeyPressed = (virtualKey, ctrl, shift, alt) =>
        {
            if (KeyRequested is not { } handler || ToKey(virtualKey) is not { } key) return false;
            var modifiers = (ctrl ? KeyModifiers.Control : 0) | (shift ? KeyModifiers.Shift : 0) | (alt ? KeyModifiers.Alt : 0);
            return handler(key, modifiers);
        };
    }

    public IPlatformHandle Handle { get; }
    public string? Title => _view.Title;
    public int BlockedCount => _view.BlockedCount;
    public event Action? Changed;
    public event Action<bool, string?>? Loaded;
    public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
    public void Show(HtmlPage page) => _view.Show(page);
    public void SetSize(int width, int height) => _view.SetSize(width, height);
    public void Focus() => _view.Focus();
    public async Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct)
    {
        bool? found = await _view.FindAsync(text, matchCase, forward, ct);
        return found is { } value ? new PageFindResult(value) : null;
    }
    public void ClearFind() => _view.ClearFind();
    public void Dispose() => _view.Dispose();

    /// <summary>The keys the viewer acts on: Esc, the function keys, and letters (with Ctrl or Alt).</summary>
    private static Key? ToKey(uint virtualKey) => virtualKey switch
    {
        0x1B => Key.Escape,
        >= 0x70 and <= 0x7B => Key.F1 + (int)(virtualKey - 0x70),
        >= 0x41 and <= 0x5A => Key.A + (int)(virtualKey - 0x41),
        _ => null,
    };
}
