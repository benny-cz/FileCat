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

    public PageView(string dataFolder)
    {
        _dataFolder = dataFolder;
    }

    /// <summary>The page could not be shown: why (no engine installed, or none on this display).</summary>
    public event Action<string>? Failed;

    /// <summary>Loading finished, a request was refused, or the title changed: the status line says it anew.</summary>
    public event Action? Changed;

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
        // A display without native controls (a headless test, say) never asks for one: after a moment, say so.
        _noHost?.Dispose();
        _noHost = DispatcherTimer.RunOnce(() =>
        {
            if (_engine is null && IsEffectivelyVisible) Failed?.Invoke("Pages cannot be drawn on this display.");
        }, TimeSpan.FromSeconds(3));
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
        _engine.Changed += () => Dispatcher.UIThread.Post(() => Changed?.Invoke());
        _engine.Loaded += (ok, why) => Dispatcher.UIThread.Post(() =>
        {
            PageLoaded = ok;
            if (!ok && why is not null) Failed?.Invoke("The page could not be shown: " + why);
            Changed?.Invoke();
        });
        _engine.KeyRequested = (key, modifiers) => KeyRequested?.Invoke(key, modifiers) == true;
        Resize();
        if (_page is { } page) _engine.Show(page);
        return _engine.Handle;
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

    private void Resize()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        _engine?.SetSize((int)Math.Ceiling(Bounds.Width * scaling), (int)Math.Ceiling(Bounds.Height * scaling));
    }

    /// <summary>The keyboard to the page.</summary>
    public void FocusPage() => _engine?.Focus();
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
}

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
        unavailable = "Web pages are not drawn on this system yet.";
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
