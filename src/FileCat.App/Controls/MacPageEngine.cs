using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;
using Avalonia.Platform;
using FileCat.Core.Content;

namespace FileCat.App.Controls;

/// <summary>
/// WKWebView (macOS) behind <see cref="PageView"/> (D-51), through the Objective-C runtime. The page and the files in
/// its folder come from a private "filecat:" scheme answered by <see cref="HtmlPage"/>; scripts are off; a compiled
/// content rule list blocks every other load before it is made; navigating away, new windows, and downloads are
/// refused; the data store keeps nothing. Everything runs on the main thread, as AppKit and WebKit require.
/// </summary>
internal sealed unsafe class MacPageEngine : IPageEngine
{
    private const string Scheme = "filecat";
    private const string Rules = """
        [{"trigger":{"url-filter":".*"},"action":{"type":"block"}},
         {"trigger":{"url-filter":"^filecat:"},"action":{"type":"ignore-previous-rules"}},
         {"trigger":{"url-filter":"^data:"},"action":{"type":"ignore-previous-rules"}},
         {"trigger":{"url-filter":"^about:"},"action":{"type":"ignore-previous-rules"}}]
        """;

    /// <summary>Engines by their web view, scheme handler, or delegate object.</summary>
    private static readonly Dictionary<nint, MacPageEngine> ByObject = [];
    private static readonly List<MacPageEngine> AwaitingRules = [];
    private static nint s_rules;
    private static string? s_rulesFailed;
    private static bool s_rulesCompiling;
    private sealed record FindRequest(MacPageEngine Engine, string Text, bool MatchCase, bool Forward,
        long Generation, TaskCompletionSource<PageFindResult?> Done)
    {
        public CancellationTokenRegistration Cancellation { get; set; }
    }
    private static readonly Queue<FindRequest> FindQueue = new();
    private static FindRequest? s_activeFind;
    private static readonly Lazy<string?> s_missing = new(() => ObjC.Load());

    private nint _view, _handler, _delegate;
    private HtmlPage? _page;
    private bool _pageWaits;
    private string? _title;
    private int _blocked;
    private bool _disposed, _observingTitle;
    private long _findGeneration;
    /// <summary>URL scheme tasks being answered, each with a token: a stopped task is never answered.</summary>
    private readonly Dictionary<nint, object> _tasks = [];

    public static string? Unavailable => s_missing.Value;

    private MacPageEngine() { }

    /// <summary>A page view (main thread only); null with why when WebKit cannot be used.</summary>
    public static MacPageEngine? Create(string dataFolder, out string? unavailable)
    {
        unavailable = Unavailable;
        if (unavailable is not null) return null;
        var engine = new MacPageEngine();
        engine.Build(dataFolder);
        return engine;
    }

    public IPlatformHandle Handle { get; private set; } = new PlatformHandle(0, "NSView");
    public string? Title => _title;
    public int BlockedCount => _blocked;
    public event Action? Changed;
    public event Action<bool, string?>? Loaded;
    public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }

    // ---- For the smoke test, which runs the engine without Avalonia on its own main thread ------------------

    /// <summary>An application for WebKit to draw in, as Avalonia starts one.</summary>
    internal static void StartApplication()
    {
        if (Unavailable is not null) return;
        nint app = ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication");
        ObjC.SendNUInt(app, "setActivationPolicy:", 1); // accessory: no Dock icon
        ObjC.Send(app, "finishLaunching");
    }

    /// <summary>The view in a window of its own, as the native control host puts it in the viewer's.</summary>
    internal void ShowInWindow() => ObjC.ShowInWindow(_view);

    /// <summary>Runs the main run loop (and the main queue) for a moment.</summary>
    internal static void RunLoop(TimeSpan time) => ObjC.RunLoop(time.TotalSeconds);

    private void Build(string dataFolder)
    {
        nint configuration = ObjC.New("WKWebViewConfiguration");
        ObjC.Send(configuration, "setWebsiteDataStore:", ObjC.Send(ObjC.Class("WKWebsiteDataStore"), "nonPersistentDataStore"));
        _handler = ObjC.New(ObjC.HandlerClass);
        ByObject[_handler] = this;
        ObjC.Send(configuration, "setURLSchemeHandler:forURLScheme:", _handler, ObjC.String(Scheme));
        nint preferences = ObjC.Send(configuration, "preferences");
        ObjC.SendBool(preferences, "setJavaScriptEnabled:", false);
        ObjC.SendBool(preferences, "setJavaScriptCanOpenWindowsAutomatically:", false);
        if (ObjC.Responds(preferences, "setFraudulentWebsiteWarningEnabled:")) ObjC.SendBool(preferences, "setFraudulentWebsiteWarningEnabled:", false);
        if (ObjC.Responds(configuration, "defaultWebpagePreferences") &&
            ObjC.Send(configuration, "defaultWebpagePreferences") is var webpage and not 0 && ObjC.Responds(webpage, "setAllowsContentJavaScript:"))
            ObjC.SendBool(webpage, "setAllowsContentJavaScript:", false);
        if (ObjC.Responds(configuration, "setMediaTypesRequiringUserActionForPlayback:"))
            ObjC.SendNUInt(configuration, "setMediaTypesRequiringUserActionForPlayback:", nuint.MaxValue);

        _view = ObjC.InitWithFrame(ObjC.Alloc(ObjC.ViewClass), configuration);
        ObjC.Release(configuration);
        ByObject[_view] = this;
        Handle = new PlatformHandle(_view, "NSView");
        _delegate = ObjC.New(ObjC.DelegateClass);
        ByObject[_delegate] = this;
        ObjC.Send(_view, "setNavigationDelegate:", _delegate);
        ObjC.Send(_view, "setUIDelegate:", _delegate);
        // WebKit sets the title from its web content process, at times after the navigation has finished.
        ObjC.AddObserver(_view, _delegate, "title");
        _observingTitle = true;

        if (s_rules != 0) AddRules();
        else
        {
            AwaitingRules.Add(this);
            if (!s_rulesCompiling)
            {
                s_rulesCompiling = true;
                ObjC.CompileRules(Path.Combine(dataFolder, "content-rules"), Rules);
            }
        }
    }

    private bool RulesReady => s_rules != 0 && _view != 0;

    private void AddRules() =>
        ObjC.Send(ObjC.Send(ObjC.Send(_view, "configuration"), "userContentController"), "addContentRuleList:", s_rules);

    public void Show(HtmlPage page)
    {
        _page = page;
        _findGeneration++;
        if (s_rulesFailed is { } why)
        {
            Loaded?.Invoke(false, why);
            return;
        }
        if (!RulesReady)
        {
            _pageWaits = true;
            return;
        }
        Load();
    }

    private void Load()
    {
        if (_page is not { } page || _view == 0) return;
        _findGeneration++;
        nint url = ObjC.Send(ObjC.Class("NSURL"), "URLWithString:", ObjC.String($"{Scheme}://page/{Uri.EscapeDataString(page.Name)}"));
        ObjC.Send(_view, "loadRequest:", ObjC.Send(ObjC.Class("NSURLRequest"), "requestWithURL:", url));
    }

    public void SetSize(int width, int height)
    {
        // The native control host sizes the view.
    }

    public void Focus()
    {
        if (_view == 0) return;
        nint window = ObjC.Send(_view, "window");
        if (window != 0) ObjC.Send(window, "makeFirstResponder:", _view);
    }

    public Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct)
    {
        if (_disposed || _view == 0 || !ObjC.Responds(_view, "findString:withConfiguration:completionHandler:"))
            return Task.FromResult<PageFindResult?>(null);
        var done = new TaskCompletionSource<PageFindResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ct.IsCancellationRequested) { done.SetCanceled(ct); return done.Task; }
        var request = new FindRequest(this, text, matchCase, forward, _findGeneration, done);
        request.Cancellation = ct.Register(() => done.TrySetCanceled(ct));
        FindQueue.Enqueue(request);
        StartNextFind();
        return done.Task;
    }

    private static void StartNextFind()
    {
        if (s_activeFind is not null) return;
        while (FindQueue.TryDequeue(out var request))
        {
            if (request.Done.Task.IsCompleted || request.Engine._disposed || request.Generation != request.Engine._findGeneration)
            {
                request.Cancellation.Dispose();
                request.Done.TrySetResult(null);
                continue;
            }
            s_activeFind = request;
            try { ObjC.BeginFind(request.Engine._view, request.Text, request.MatchCase, request.Forward); }
            catch (Exception ex)
            {
                Core.Diagnostics.AppLog.Warn("Page find: " + ex.Message);
                s_activeFind = null;
                request.Cancellation.Dispose();
                request.Done.TrySetResult(null);
                continue;
            }
            return;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FindBlock(nint block, nint result)
    {
        try
        {
            var request = s_activeFind;
            s_activeFind = null;
            if (request is not null)
            {
                request.Cancellation.Dispose();
                request.Done.TrySetResult(request.Engine._disposed || request.Generation != request.Engine._findGeneration
                    ? null : new PageFindResult(result != 0 && ObjC.SendReturnsBool(result, "matchFound")));
            }
            StartNextFind();
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Page find: " + ex.Message); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _findGeneration++;
        AwaitingRules.Remove(this);
        foreach (var key in new[] { _view, _handler, _delegate }) ByObject.Remove(key);
        foreach (var task in _tasks.Keys) ObjC.Release(task);
        _tasks.Clear();
        if (_view != 0)
        {
            // The view may outlive this engine (its window holds it): it must not call a released observer.
            if (_observingTitle) ObjC.Send(_view, "removeObserver:forKeyPath:", _delegate, ObjC.String("title"));
            _observingTitle = false;
            ObjC.Send(_view, "setNavigationDelegate:", 0);
            ObjC.Send(_view, "setUIDelegate:", 0);
            ObjC.Send(_view, "stopLoading");
            ObjC.Release(_view);
        }
        if (_delegate != 0) ObjC.Release(_delegate);
        if (_handler != 0) ObjC.Release(_handler);
        _view = _delegate = _handler = 0;
    }

    private void Refused()
    {
        _blocked++;
        Changed?.Invoke();
    }

    private static MacPageEngine? Of(nint self) => ByObject.TryGetValue(self, out var engine) && !engine._disposed ? engine : null;

    // ---- Callbacks from WebKit (main thread) --------------------------------------------------------------

    /// <summary>The content rule list compiled: engines waiting for it take it, and their pages load.</summary>
    private static void RulesCompiled(nint list, string? error)
    {
        s_rulesCompiling = false;
        if (list == 0)
        {
            s_rulesFailed = "The page could not be kept off the web" + (error is null ? "." : ": " + error);
            foreach (var engine in AwaitingRules.ToList()) if (engine._pageWaits) engine.Loaded?.Invoke(false, s_rulesFailed);
            AwaitingRules.Clear();
            return;
        }
        s_rules = ObjC.Retain(list);
        foreach (var engine in AwaitingRules.ToList())
        {
            if (engine._disposed) continue;
            engine.AddRules();
            if (engine._pageWaits) engine.Load();
        }
        AwaitingRules.Clear();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StartTask(nint self, nint selector, nint webView, nint task)
    {
        try
        {
            if (Of(self) is not { } engine || engine._page is not { } page)
            {
                ObjC.FailTask(task, "Nothing is served here.");
                return;
            }
            nint url = ObjC.Send(ObjC.Send(task, "request"), "URL");
            string path = ObjC.ToString(ObjC.Send(url, "path")) ?? "/";
            var token = new object();
            engine._tasks[ObjC.Retain(task)] = token;
            _ = Task.Run(() =>
            {
                (byte[] Bytes, string MimeType)? found = null;
                try { found = page.Resolve(path); }
                catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Page resource: " + ex.Message); }
                ObjC.OnMain(() =>
                {
                    // Stopped (or the view closed) meanwhile: WebKit wants no answer.
                    if (!engine._tasks.TryGetValue(task, out var current) || !ReferenceEquals(current, token)) return;
                    engine._tasks.Remove(task);
                    try
                    {
                        if (found is { } served) ObjC.AnswerTask(task, url, served.Bytes, served.MimeType);
                        else
                        {
                            engine.Refused();
                            ObjC.FailTask(task, "Not served: only the page and the files in its folder are.");
                        }
                    }
                    finally { ObjC.Release(task); }
                });
            });
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StopTask(nint self, nint selector, nint webView, nint task)
    {
        try
        {
            if (Of(self) is { } engine && engine._tasks.Remove(task)) ObjC.Release(task);
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
    }

    /// <summary>Only the page's own address may be navigated to, and never into a new window.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DecideAction(nint self, nint selector, nint webView, nint action, nint decisionHandler)
    {
        const long Cancel = 0, Allow = 1;
        long policy = Cancel;
        try
        {
            string uri = ObjC.ToString(ObjC.Send(ObjC.Send(ObjC.Send(action, "request"), "URL"), "absoluteString")) ?? "";
            bool newWindow = ObjC.Send(action, "targetFrame") == 0;
            if (!newWindow && IsOwn(uri))
            {
                policy = Allow;
                if (Of(self) is { } owner) owner._findGeneration++;
            }
            else Of(self)?.Refused();
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
        ObjC.InvokeBlock(decisionHandler, policy);
    }

    /// <summary>What the view cannot show would be downloaded: refused.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DecideResponse(nint self, nint selector, nint webView, nint response, nint decisionHandler)
    {
        const long Cancel = 0, Allow = 1;
        long policy = Cancel;
        try
        {
            if (ObjC.SendReturnsBool(response, "canShowMIMEType")) policy = Allow;
            else Of(self)?.Refused();
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
        ObjC.InvokeBlock(decisionHandler, policy);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Finished(nint self, nint selector, nint webView, nint navigation)
    {
        try
        {
            if (Of(self) is not { } engine) return;
            engine._title = ObjC.ToString(ObjC.Send(webView, "title"));
            engine.Changed?.Invoke();
            engine.Loaded?.Invoke(true, null);
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
    }

    /// <summary>The view's title changed (key-value observing): a title that came after the navigation finished.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Observed(nint self, nint selector, nint keyPath, nint observed, nint change, nint context)
    {
        try
        {
            if (Of(self) is not { } engine || ObjC.ToString(keyPath) != "title") return;
            string? title = ObjC.ToString(ObjC.Send(observed, "title"));
            if (title == engine._title) return;
            engine._title = title;
            engine.Changed?.Invoke();
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Failed(nint self, nint selector, nint webView, nint navigation, nint error)
    {
        try
        {
            Of(self)?.Loaded?.Invoke(false, ObjC.ToString(ObjC.Send(error, "localizedDescription")));
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
    }

    /// <summary>No new windows.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint CreateWebView(nint self, nint selector, nint webView, nint configuration, nint action, nint features)
    {
        try { Of(self)?.Refused(); }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
        return 0;
    }

    /// <summary>The viewer's keys first (Esc, function keys, ⌘ or Ctrl letters); the rest to the page.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void KeyDown(nint self, nint selector, nint keyEvent)
    {
        try
        {
            if (TakeKey(self, keyEvent)) return;
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
        ObjC.SendSuper(self, selector, keyEvent);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte KeyEquivalent(nint self, nint selector, nint keyEvent)
    {
        try
        {
            // AppKit offers key equivalents to every view in the window, not only to the one with the keyboard: the
            // page takes them only while it has it. Escape in the viewer's Find closed the viewer, and ⌘A and ⌘C
            // there selected and copied the page.
            if (HasKeyboard(self) && TakeKey(self, keyEvent)) return 1;
        }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
        return ObjC.SendSuperReturnsBool(self, selector, keyEvent) ? (byte)1 : (byte)0;
    }

    /// <summary>Whether the view, or a view inside it, is its window's first responder.</summary>
    private static bool HasKeyboard(nint view)
    {
        nint window = ObjC.Send(view, "window");
        nint responder = window == 0 ? 0 : ObjC.Send(window, "firstResponder");
        return responder == view || responder != 0 && ObjC.Responds(responder, "isDescendantOf:") &&
            ObjC.SendReturnsBool(responder, "isDescendantOf:", view);
    }

    private static bool TakeKey(nint view, nint keyEvent)
    {
        if (Of(view) is not { KeyRequested: { } handler }) return false;
        ushort code = ObjC.SendReturnsUShort(keyEvent, "keyCode");
        nuint flags = ObjC.SendReturnsNUInt(keyEvent, "modifierFlags");
        Key? key = code switch
        {
            53 => Key.Escape,
            122 => Key.F1, 120 => Key.F2, 99 => Key.F3, 118 => Key.F4, 96 => Key.F5, 97 => Key.F6,
            98 => Key.F7, 100 => Key.F8, 101 => Key.F9, 109 => Key.F10, 103 => Key.F11, 111 => Key.F12,
            _ => ObjC.ToString(ObjC.Send(keyEvent, "charactersIgnoringModifiers")) is { Length: 1 } c && char.IsAsciiLetter(c[0])
                ? Key.A + (char.ToLowerInvariant(c[0]) - 'a') : null,
        };
        if (key is null) return false;
        // ⌘ stands for Ctrl, as everywhere in FileCat on macOS.
        var modifiers = ((flags & (1 << 18)) != 0 || (flags & (1 << 20)) != 0 ? KeyModifiers.Control : 0) |
                        ((flags & (1 << 17)) != 0 ? KeyModifiers.Shift : 0) | ((flags & (1 << 19)) != 0 ? KeyModifiers.Alt : 0);
        if (handler(key.Value, modifiers)) return true;
        // FileCat has no native Edit menu for AppKit to resolve these standard
        // key equivalents. Dispatch them to the displayed WKWebView itself so
        // Page selection and Copy work with the keyboard.
        if (PageEditSelector(key.Value, modifiers) is { } edit && ObjC.Responds(view, edit))
        {
            ObjC.Send(view, edit, view);
            return true;
        }
        return false;
    }

    internal static string? PageEditSelector(Key key, KeyModifiers modifiers) =>
        modifiers == KeyModifiers.Control ? key switch
        {
            Key.A => "selectAll:",
            Key.C => "copy:",
            _ => null,
        } : null;

    private static bool IsOwn(string uri) =>
        uri.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase) || uri == "about:blank";

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RulesBlock(nint block, nint list, nint error)
    {
        try { RulesCompiled(list, error == 0 ? null : ObjC.ToString(ObjC.Send(error, "localizedDescription"))); }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RunOnMain(nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        try { ((Action)handle.Target!)(); }
        catch (Exception ex) { Core.Diagnostics.AppLog.Warn("Web page engine: " + ex.Message); }
        finally { handle.Free(); }
    }

    /// <summary>The Objective-C runtime, AppKit, and WebKit, reached at run time.</summary>
    private static class ObjC
    {
        private const string LibObjC = "/usr/lib/libobjc.A.dylib";
        private static nint s_msgSend, s_msgSendSuper;
        public static nint HandlerClass, DelegateClass, ViewClass;
        private static nint s_mainQueue, s_globalBlock, s_findBlock;
        private static delegate* unmanaged[Cdecl]<nint, nint, delegate* unmanaged[Cdecl]<nint, void>, void> s_dispatchAsync;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public double X, Y, Width, Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Super
        {
            public nint Receiver, SuperClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Block
        {
            public nint Isa;
            public int Flags, Reserved;
            public nint Invoke, Descriptor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlockDescriptor
        {
            public nuint Reserved, Size;
        }

        [DllImport(LibObjC)] private static extern nint objc_getClass(string name);
        [DllImport(LibObjC)] private static extern nint objc_getProtocol(string name);
        [DllImport(LibObjC)] private static extern nint sel_registerName(string name);
        [DllImport(LibObjC)] private static extern nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);
        [DllImport(LibObjC)] private static extern void objc_registerClassPair(nint cls);
        [DllImport(LibObjC)] private static extern byte class_addMethod(nint cls, nint selector, nint implementation, string types);
        [DllImport(LibObjC)] private static extern byte class_addProtocol(nint cls, nint protocol);
        [DllImport(LibObjC)] private static extern nint class_getSuperclass(nint cls);

        public static string? Load()
        {
            if (!OperatingSystem.IsMacOS()) return "Web pages are drawn by WKWebView on macOS only.";
            try
            {
                if (!NativeLibrary.TryLoad("/System/Library/Frameworks/WebKit.framework/WebKit", out _) ||
                    !NativeLibrary.TryLoad(LibObjC, out nint objc) || !NativeLibrary.TryLoad("/usr/lib/libSystem.B.dylib", out nint system))
                    return "WebKit is not available on this Mac.";
                s_msgSend = NativeLibrary.GetExport(objc, "objc_msgSend");
                s_msgSendSuper = NativeLibrary.GetExport(objc, "objc_msgSendSuper");
                s_mainQueue = NativeLibrary.GetExport(system, "_dispatch_main_q");
                s_globalBlock = NativeLibrary.GetExport(system, "_NSConcreteGlobalBlock");
                s_dispatchAsync = (delegate* unmanaged[Cdecl]<nint, nint, delegate* unmanaged[Cdecl]<nint, void>, void>)NativeLibrary.GetExport(system, "dispatch_async_f");
                if (Class("WKWebView") == 0 || Class("WKContentRuleListStore") == 0) return "This macOS's WebKit is too old for FileCat's page view.";

                HandlerClass = objc_allocateClassPair(Class("NSObject"), "FileCatPageSchemeHandler", 0);
                Add(HandlerClass, "webView:startURLSchemeTask:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&StartTask, "v@:@@");
                Add(HandlerClass, "webView:stopURLSchemeTask:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&StopTask, "v@:@@");
                class_addProtocol(HandlerClass, objc_getProtocol("WKURLSchemeHandler"));
                objc_registerClassPair(HandlerClass);

                DelegateClass = objc_allocateClassPair(Class("NSObject"), "FileCatPageDelegate", 0);
                Add(DelegateClass, "webView:decidePolicyForNavigationAction:decisionHandler:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&DecideAction, "v@:@@@?");
                Add(DelegateClass, "webView:decidePolicyForNavigationResponse:decisionHandler:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&DecideResponse, "v@:@@@?");
                Add(DelegateClass, "webView:didFinishNavigation:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&Finished, "v@:@@");
                Add(DelegateClass, "webView:didFailNavigation:withError:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&Failed, "v@:@@@");
                Add(DelegateClass, "webView:didFailProvisionalNavigation:withError:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)&Failed, "v@:@@@");
                Add(DelegateClass, "webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint, nint>)&CreateWebView, "@@:@@@@");
                Add(DelegateClass, "observeValueForKeyPath:ofObject:change:context:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint, void>)&Observed, "v@:@@@^v");
                class_addProtocol(DelegateClass, objc_getProtocol("WKNavigationDelegate"));
                class_addProtocol(DelegateClass, objc_getProtocol("WKUIDelegate"));
                objc_registerClassPair(DelegateClass);

                ViewClass = objc_allocateClassPair(Class("WKWebView"), "FileCatPageWebView", 0);
                Add(ViewClass, "keyDown:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&KeyDown, "v@:@");
                Add(ViewClass, "performKeyEquivalent:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)&KeyEquivalent, "c@:@");
                objc_registerClassPair(ViewClass);
                return HandlerClass == 0 || DelegateClass == 0 || ViewClass == 0 ? "FileCat's page view could not be set up." : null;
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
            {
                return "WebKit could not be reached: " + ex.Message;
            }
        }

        private static void Add(nint cls, string selector, nint implementation, string types) =>
            class_addMethod(cls, sel_registerName(selector), implementation, types);

        public static nint Class(string name) => objc_getClass(name);

        public static nint Send(nint receiver, string selector) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, nint>)s_msgSend)(receiver, sel_registerName(selector));

        public static nint Send(nint receiver, string selector, nint a) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)s_msgSend)(receiver, sel_registerName(selector), a);

        public static nint Send(nint receiver, string selector, nint a, nint b) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint>)s_msgSend)(receiver, sel_registerName(selector), a, b);

        public static void SendBool(nint receiver, string selector, bool value) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, byte, void>)s_msgSend)(receiver, sel_registerName(selector), value ? (byte)1 : (byte)0);

        public static void SendNUInt(nint receiver, string selector, nuint value) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, nuint, void>)s_msgSend)(receiver, sel_registerName(selector), value);

        public static bool SendReturnsBool(nint receiver, string selector) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, byte>)s_msgSend)(receiver, sel_registerName(selector)) != 0;

        public static bool SendReturnsBool(nint receiver, string selector, nint argument) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)s_msgSend)(receiver, sel_registerName(selector), argument) != 0;

        public static ushort SendReturnsUShort(nint receiver, string selector) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, ushort>)s_msgSend)(receiver, sel_registerName(selector));

        public static nuint SendReturnsNUInt(nint receiver, string selector) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, nuint>)s_msgSend)(receiver, sel_registerName(selector));

        /// <summary>Key-value observing of <paramref name="keyPath"/> (new values; no context).</summary>
        public static void AddObserver(nint observed, nint observer, string keyPath) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nuint, nint, void>)s_msgSend)(observed,
                sel_registerName("addObserver:forKeyPath:options:context:"), observer, String(keyPath), 1 /* NSKeyValueObservingOptionNew */, 0);

        public static bool Responds(nint receiver, string selector) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)s_msgSend)(receiver, sel_registerName("respondsToSelector:"), sel_registerName(selector)) != 0;

        public static void SendSuper(nint self, nint selector, nint argument)
        {
            // Observing WKWebView.title can install an NSKVONotifying_ runtime
            // subclass. Its immediate superclass is our override class, so
            // looking up "super" from object_getClass(self) dispatches right
            // back into KeyDown and eventually overflows the process stack.
            var super = new Super { Receiver = self, SuperClass = class_getSuperclass(ViewClass) };
            ((delegate* unmanaged[Cdecl]<Super*, nint, nint, void>)s_msgSendSuper)(&super, selector, argument);
        }

        public static bool SendSuperReturnsBool(nint self, nint selector, nint argument)
        {
            var super = new Super { Receiver = self, SuperClass = class_getSuperclass(ViewClass) };
            return ((delegate* unmanaged[Cdecl]<Super*, nint, nint, byte>)s_msgSendSuper)(&super, selector, argument) != 0;
        }

        public static nint Alloc(nint cls) => Send(cls, "alloc");

        public static nint New(string className) => New(Class(className));

        public static nint New(nint cls) => Send(Alloc(cls), "init");

        public static nint Retain(nint obj) => Send(obj, "retain");

        public static void Release(nint obj)
        {
            if (obj != 0) Send(obj, "release");
        }

        public static nint InitWithFrame(nint view, nint configuration) =>
            ((delegate* unmanaged[Cdecl]<nint, nint, Rect, nint, nint>)s_msgSend)(view, sel_registerName("initWithFrame:configuration:"),
                new Rect { Width = 400, Height = 300 }, configuration);

        public static nint String(string text)
        {
            fixed (byte* utf8 = Encoding.UTF8.GetBytes(text + "\0"))
                return Send(Class("NSString"), "stringWithUTF8String:", (nint)utf8);
        }

        public static string? ToString(nint nsString) =>
            nsString == 0 ? null : Marshal.PtrToStringUTF8(Send(nsString, "UTF8String"));

        public static void AnswerTask(nint task, nint url, byte[] bytes, string mimeType)
        {
            nint response = ((delegate* unmanaged[Cdecl]<nint, nint, nint, nint, long, nint, nint>)s_msgSend)(
                Alloc(Class("NSURLResponse")), sel_registerName("initWithURL:MIMEType:expectedContentLength:textEncodingName:"),
                url, String(mimeType), bytes.Length, 0);
            Send(task, "didReceiveResponse:", response);
            Release(response);
            fixed (byte* data = bytes)
                Send(task, "didReceiveData:", ((delegate* unmanaged[Cdecl]<nint, nint, nint, nuint, nint>)s_msgSend)(
                    Class("NSData"), sel_registerName("dataWithBytes:length:"), (nint)data, (nuint)bytes.Length));
            Send(task, "didFinish");
        }

        public static void FailTask(nint task, string message)
        {
            nint info = Send(Class("NSDictionary"), "dictionaryWithObject:forKey:", String(message), String("NSLocalizedDescription"));
            nint error = ((delegate* unmanaged[Cdecl]<nint, nint, nint, long, nint, nint>)s_msgSend)(
                Class("NSError"), sel_registerName("errorWithDomain:code:userInfo:"), String("filecat-page"), 403, info);
            Send(task, "didFailWithError:", error);
        }

        /// <summary>Calls a block WebKit handed over with one integer argument (a policy).</summary>
        public static void InvokeBlock(nint block, long value)
        {
            if (block == 0) return;
            var invoke = (delegate* unmanaged[Cdecl]<nint, long, void>)((Block*)block)->Invoke;
            invoke(block, value);
        }

        /// <summary>A single app-owned completion block; page searches are serialized across views.</summary>
        public static void BeginFind(nint view, string text, bool matchCase, bool forward)
        {
            if (Class("WKFindConfiguration") == 0) throw new InvalidOperationException("This WebKit has no page find configuration.");
            if (s_findBlock == 0)
            {
                var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
                descriptor->Size = (nuint)sizeof(Block);
                var block = (Block*)NativeMemory.AllocZeroed((nuint)sizeof(Block));
                block->Isa = s_globalBlock;
                block->Flags = 1 << 28; // BLOCK_IS_GLOBAL; one block for the process lifetime
                block->Invoke = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&FindBlock;
                block->Descriptor = (nint)descriptor;
                s_findBlock = (nint)block;
            }
            nint configuration = New("WKFindConfiguration");
            try
            {
                SendBool(configuration, "setBackwards:", !forward);
                SendBool(configuration, "setCaseSensitive:", matchCase);
                SendBool(configuration, "setWraps:", false);
                ((delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)s_msgSend)(view,
                    sel_registerName("findString:withConfiguration:completionHandler:"), String(text), configuration, s_findBlock);
            }
            finally { Release(configuration); }
        }

        /// <summary>Compiles the content rule list into a store of FileCat's own; <see cref="RulesCompiled"/> gets it.</summary>
        public static void CompileRules(string folder, string json)
        {
            Directory.CreateDirectory(folder);
            nint url = ((delegate* unmanaged[Cdecl]<nint, nint, nint, byte, nint>)s_msgSend)(
                Class("NSURL"), sel_registerName("fileURLWithPath:isDirectory:"), String(folder), 1);
            nint store = Send(Class("WKContentRuleListStore"), "storeWithURL:", url);
            // A global block: never copied or freed, so it lives in memory of its own for good.
            var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
            descriptor->Size = (nuint)sizeof(Block);
            var block = (Block*)NativeMemory.AllocZeroed((nuint)sizeof(Block));
            block->Isa = s_globalBlock;
            block->Flags = 1 << 28; // BLOCK_IS_GLOBAL
            block->Invoke = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&RulesBlock;
            block->Descriptor = (nint)descriptor;
            ((delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void>)s_msgSend)(store,
                sel_registerName("compileContentRuleListForIdentifier:encodedContentRuleList:completionHandler:"),
                String("filecat-page-rules"), String(json), (nint)block);
        }

        public static void ShowInWindow(nint view)
        {
            nint window = ((delegate* unmanaged[Cdecl]<nint, nint, Rect, nuint, nuint, byte, nint>)s_msgSend)(
                Alloc(Class("NSWindow")), sel_registerName("initWithContentRect:styleMask:backing:defer:"),
                new Rect { X = 100, Y = 100, Width = 640, Height = 480 }, 1 /* titled */, 2 /* buffered */, 0);
            Send(window, "setContentView:", view);
            Send(window, "orderFrontRegardless");
        }

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSourceHandled);

        public static void RunLoop(double seconds)
        {
            nint cf = NativeLibrary.Load("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation");
            nint mode = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFRunLoopDefaultMode"));
            CFRunLoopRunInMode(mode, seconds, 0);
        }

        /// <summary>Runs work on the main thread (the main dispatch queue), where WebKit wants its answers.</summary>
        public static void OnMain(Action work)
        {
            var handle = GCHandle.Alloc(work);
            s_dispatchAsync(s_mainQueue, GCHandle.ToIntPtr(handle), &RunOnMain);
        }
    }
}
