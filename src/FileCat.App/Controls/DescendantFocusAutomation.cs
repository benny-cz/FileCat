using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FileCat.App.Controls;

/// <summary>A focused control whose keyboard focus rests on one of its own virtual item peers.</summary>
internal interface IFocusedDescendantPeer
{
    /// <summary>The focused item while the control has keyboard focus; otherwise null.</summary>
    AutomationPeer? FocusedDescendant { get; }

    /// <summary>Raised after the focused item may have changed while the control kept keyboard focus.</summary>
    event EventHandler? FocusedDescendantChanged;
}

/// <summary>
/// Avalonia reports the focused control as a window's focused element. A virtualized list keeps keyboard focus
/// on itself while its focused row changes, so a screen reader announced the list once and then nothing while the
/// user moved between files. This root reports the focused row instead and raises the native focus change whenever
/// that row changes: UI Automation focus on Windows, the focused-element notification on macOS and AT-SPI focus on
/// Linux. Neither Avalonia's AT-SPI nor its Windows bridge announces focus when a window becomes active, so on
/// Linux and Windows the current focus is announced again after activation.
/// </summary>
internal sealed class DescendantFocusWindowAutomationPeer : WindowAutomationPeer, IRootProvider
{
    private EventHandler? _focusChanged;
    private IFocusedDescendantPeer? _owner;
    private AutomationPeer? _reported;
    private AutomationPeer? _valueSource;

    public DescendantFocusWindowAutomationPeer(Window owner) : base(owner)
    {
        base.FocusChanged += OnControlFocusChanged;
        owner.Activated += OnActivated;
        // A list can already own keyboard focus when this peer is first requested.
        TrackOwner();
        _reported = ResolveFocus();
    }

    /// <summary>This window's AT-SPI bridge on Linux, from the bridge's own subscription to its focus.</summary>
    internal AtSpiBridge? AtSpi { get; set; }

    AutomationPeer? IRootProvider.GetFocus() => ResolveFocus();
    AutomationPeer? IRootProvider.GetPeerFromPoint(Point p) => GetPeerFromPoint(p);
    ITopLevelImpl? IRootProvider.PlatformImpl => PlatformImpl;

    event EventHandler? IRootProvider.FocusChanged
    {
        add
        {
            _focusChanged += value;
            if (AtSpiBridge.TryCreate(value) is { } bridge) AtSpi = bridge;
        }
        remove
        {
            _focusChanged -= value;
            if (AtSpi?.IsFor(value) == true) AtSpi = null;
        }
    }

    internal AutomationPeer? ResolveFocus()
    {
        var focus = GetFocus();
        return focus is IFocusedDescendantPeer { FocusedDescendant: { } item } ? item : focus;
    }

    private void OnControlFocusChanged(object? sender, EventArgs e)
    {
        TrackOwner();
        // Avalonia raised a native focus change for every focused-control change; keep doing so.
        Report(always: true);
    }

    private void TrackOwner()
    {
        var owner = GetFocus() as IFocusedDescendantPeer;
        if (ReferenceEquals(owner, _owner)) return;
        if (_owner is not null) _owner.FocusedDescendantChanged -= OnDescendantChanged;
        _owner = owner;
        if (owner is not null) owner.FocusedDescendantChanged += OnDescendantChanged;
    }

    private void OnDescendantChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _owner)) Report(always: false);
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (OperatingSystem.IsMacOS()) return;
        // After Avalonia's own window activation signals, so the screen reader has an active window to present focus in.
        Dispatcher.UIThread.Post(() =>
        {
            if (Owner.IsActive) Report(always: true);
        }, DispatcherPriority.Background);
    }

    private void Report(bool always)
    {
        var focus = ResolveFocus();
        if (!always && ReferenceEquals(focus, _reported)) return;
        _reported = focus;
        AtSpi?.Attach(this, focus);
        TrackValue(focus);
        _focusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A focused slider's value changes are what screen readers speak as it moves, and Avalonia's AT-SPI bridge emits
    /// none: on Linux each change of the focused element's range value is sent from its own node.
    /// </summary>
    private void TrackValue(AutomationPeer? focus)
    {
        if (ReferenceEquals(focus, _valueSource)) return;
        if (_valueSource is not null) _valueSource.PropertyChanged -= OnFocusedPropertyChanged;
        _valueSource = focus;
        if (focus is not null) focus.PropertyChanged += OnFocusedPropertyChanged;
    }

    private void OnFocusedPropertyChanged(object? sender, AutomationPropertyChangedEventArgs e)
    {
        if (e.Property == RangeValuePatternIdentifiers.ValueProperty && e.NewValue is double value && sender is AutomationPeer peer)
            AtSpi?.ValueChanged(peer, value);
    }
}

/// <summary>
/// Avalonia's AT-SPI bridge names an element without an accessible name after its class, so Orca announced
/// "VisualLayerManager, Grid, Border, DockPanel" before every focused file or dialog field. On Linux, unnamed
/// layout containers have no class name to fall back to; named containers and controls are unchanged.
/// </summary>
internal static class AtSpiLayoutNames
{
    internal static void Install(Application application)
    {
        if (!OperatingSystem.IsLinux()) return;
        foreach (var selector in new Func<Selector?, Selector>[]
                 {
                     s => s.Is<Panel>(), s => s.Is<Decorator>(), s => s.Is<ContentPresenter>(),
                     s => s.OfType<ItemsPresenter>(), s => s.OfType<Viewbox>(),
                 })
            application.Styles.Add(new Style(selector) { Setters = { new Setter(AutomationProperties.ClassNameOverrideProperty, string.Empty) } });
    }
}

/// <summary>
/// Screen readers hear a live region only when a platform bridge raises its live-region event, and each of
/// Avalonia's bridges did so only for an element an assistive client had already visited: the Windows bridge creates
/// an element's UI Automation node, and the macOS bridge its native element, on first visit, while the AT-SPI bridge
/// has no live regions at all. Notices, Find results and validation errors were therefore silent until a reader
/// happened to visit them. When a live control is attached to a window, this makes the bridge track it: Windows gets
/// the element's node, macOS gets native elements from the window down to it, and on Linux each name change is sent
/// as an AT-SPI announcement from the window. A validation message typically gets its new text just before it is
/// shown, so text that changed while its control was hidden is spoken once the shown control is laid out.
/// </summary>
internal static class LiveRegions
{
    /// <summary>A watched control's peer, whether its text changed while hidden, and whether it was just shown.</summary>
    private sealed class Region(AutomationPeer peer)
    {
        public AutomationPeer Peer { get; } = peer;
        public bool Deferred;
        public bool Showing;
    }

    private static readonly ConditionalWeakTable<Control, Region> s_watched = [];

    internal static void Install() =>
        AutomationProperties.LiveSettingProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));

    /// <summary>Tracks the control while it is live; its live setting may be switched later.</summary>
    internal static void Watch(Control control)
    {
        if (!s_watched.TryGetValue(control, out var region))
        {
            region = new Region(ControlAutomationPeer.CreatePeerForElement(control));
            s_watched.Add(control, region);
            var watched = region;
            control.AttachedToVisualTree += (_, _) =>
            {
                Track(control, watched.Peer);
                // A dialog can prepare its first result before being attached.
                // Its name change was deferred while it had no window; publish
                // the final text after its native element has a visible parent.
                if (watched.Deferred && control.IsEffectivelyVisible) Shown(control, watched);
            };
            control.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty && control.IsVisible) Shown(control, watched);
            };
            region.Peer.PropertyChanged += (_, e) => OnNameChanged(control, watched, e);
        }
        Track(control, region.Peer);
    }

    /// <summary>Speaks the control's current text again, as for Enter on a value its message rejects.</summary>
    internal static void Repeat(Control control)
    {
        Watch(control);
        if (!control.IsEffectivelyVisible || !s_watched.TryGetValue(control, out var region)) return;
        if (region.Showing) region.Deferred = true;
        else if (Text(region) is { } text) region.Peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, string.Empty, text);
    }

    private static void Track(Control control, AutomationPeer peer)
    {
        if (AutomationProperties.GetLiveSetting(control) == AutomationLiveSetting.Off ||
            TopLevel.GetTopLevel(control) is not Window window)
            return;
        if (OperatingSystem.IsWindows()) NativeLiveRegionBridges.TrackOnWindows(peer);
        else if (OperatingSystem.IsMacOS()) NativeLiveRegionBridges.TrackOnMac(ControlAutomationPeer.CreatePeerForElement(window), peer);
    }

    private static string? Text(Region region) =>
        region.Peer.GetLiveSetting() != AutomationLiveSetting.Off && region.Peer.GetName() is { Length: > 0 } text ? text : null;

    private static void Shown(Control control, Region region)
    {
        if (region.Showing) return;
        region.Showing = true;
        // After layout, as for notifications: NVDA ignored the live element of a message shown a moment before, and
        // the bridges list a control only once it is visible (macOS gets its native element then).
        Dispatcher.UIThread.Post(() =>
        {
            region.Showing = false;
            if (!region.Deferred) return;
            region.Deferred = false;
            if (!control.IsEffectivelyVisible || Text(region) is not { } text) return;
            Link(control, region);
            Track(control, region.Peer);
            region.Peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, string.Empty, text);
        }, DispatcherPriority.Background);
    }

    private static void OnNameChanged(Control control, Region region, AutomationPropertyChangedEventArgs e)
    {
        if (e.Property != AutomationElementIdentifiers.NameProperty || Text(region) is not { } text) return;
        // A change while shown is the platform bridges' own event, raised as it happens; deferring it would repeat it.
        if (!control.IsEffectivelyVisible || TopLevel.GetTopLevel(control) is not Window)
        {
            region.Deferred = true;
            return;
        }
        // Runs before the bridges' own handlers, so their event already carries the restored link.
        Link(control, region);
        // Acts only for a window with an AT-SPI bridge, which exists on Linux.
        if (TopLevel.GetTopLevel(control) is Window window &&
            ControlAutomationPeer.CreatePeerForElement(window) is DescendantFocusWindowAutomationPeer { AtSpi: { } bridge })
            bridge.Announce(text, region.Peer.GetLiveSetting());
    }

    /// <summary>
    /// A peer whose parent was looked up while its control was hidden keeps that empty link once shown, and NVDA drops
    /// the live-region event of a UI Automation element it cannot place in its window. Listing the children from the
    /// window down links the control again.
    /// </summary>
    private static void Link(Control control, Region region)
    {
        if (region.Peer.GetParent() is not null || TopLevel.GetTopLevel(control) is not Window window) return;
        foreach (var ancestor in AutomationPaths.FromWindow(ControlAutomationPeer.CreatePeerForElement(window), region.Peer) ?? [])
            ancestor.GetChildren();
    }
}

/// <summary>
/// The two bridge members live regions need on Windows and macOS. Windows: Avalonia's public
/// <c>AutomationNode.GetOrCreate</c> on its internal UI Automation node type, which subscribes the node to the peer.
/// macOS: the protected <c>ControlAutomationPeer.InvalidateChildren</c>, whose children change makes the native
/// bridge create elements for a peer's children; applied from the window down, it creates the live element. Each
/// disables itself if its member is missing or fails, leaving Avalonia's unmodified behavior.
/// </summary>
internal static class NativeLiveRegionBridges
{
    internal const string UiaNodeType = "Avalonia.Win32.Automation.AutomationNode, Avalonia.Win32.Automation";
    internal const string UiaNodeMethod = "GetOrCreate";
    internal const string InvalidateChildrenMethod = "InvalidateChildren";
    private static readonly Lazy<MethodInfo?> s_uiaNode = new(() => UiaNodeMember(Type.GetType(UiaNodeType)));
    private static readonly MethodInfo? s_invalidateChildren = InvalidateChildrenMember();
    private static bool s_uiaDisabled, s_macDisabled;

    internal static MethodInfo? UiaNodeMember(Type? nodeType) =>
        nodeType?.GetMethod(UiaNodeMethod, BindingFlags.Public | BindingFlags.Static, [typeof(AutomationPeer)]) is { } method &&
        method.ReturnType == nodeType ? method : null;

    internal static MethodInfo? InvalidateChildrenMember() =>
        typeof(ControlAutomationPeer).GetMethod(InvalidateChildrenMethod, BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);

    /// <summary>Creates the peer's UI Automation node, so its live-region event is raised; returns the node.</summary>
    internal static object? TrackOnWindows(AutomationPeer peer)
    {
        if (s_uiaDisabled) return null;
        try
        {
            if (s_uiaNode.Value is not { } getOrCreate) throw new MissingMethodException(UiaNodeType, UiaNodeMethod);
            return getOrCreate.Invoke(null, [peer]);
        }
        catch (Exception ex)
        {
            s_uiaDisabled = true;
            Core.Diagnostics.AppLog.Warn("Live regions on Windows fall back to Avalonia's default behavior: " + (ex.InnerException?.Message ?? ex.Message));
            return null;
        }
    }

    /// <summary>Refreshes children from the window down to the live peer, so the native bridge creates its element.</summary>
    internal static void TrackOnMac(AutomationPeer root, AutomationPeer peer)
    {
        if (s_macDisabled) return;
        try
        {
            if (s_invalidateChildren is not { } invalidate) throw new MissingMethodException(nameof(ControlAutomationPeer), InvalidateChildrenMethod);
            // A layer the native bridge does not expose has no native element; refreshing it does nothing.
            foreach (var ancestor in AutomationPaths.FromWindow(root, peer) ?? [])
                if (ancestor is ControlAutomationPeer control) invalidate.Invoke(control, null);
        }
        catch (Exception ex)
        {
            s_macDisabled = true;
            Core.Diagnostics.AppLog.Warn("Live regions on macOS fall back to Avalonia's default behavior: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }
}

/// <summary>
/// Reaches two parts of Avalonia 12.1's AT-SPI bridge that FileCat needs but Avalonia does not expose. Focus: the
/// bridge emits focus only for a node that an assistive client has already reached by enumerating children, and Orca
/// does not enumerate a window before it waits for focus, so before a focus change is raised the bridge's own child
/// enumeration attaches each node from the window to the focused peer. Announcements: the bridge never emits AT-SPI
/// announcements, so a live region's text is sent as one from the window's node. Only the members named below are
/// used, found on the actual subscriber of the window's focus event; each capability disables itself if a member is
/// missing or fails, leaving Avalonia's unmodified behavior.
/// </summary>
internal sealed class AtSpiBridge
{
    internal const string RootNodeType = "Avalonia.FreeDesktop.AtSpi.RootAtSpiNode";
    internal const string ServerProperty = "Server";
    internal const string AttachedNodeMethod = "TryGetAttachedNode";
    internal const string EnsureChildrenMethod = "EnsureChildren";
    internal const string EventHandlerProperty = "EventObjectHandler";
    internal const string EmitSignalMethod = "EmitSignal";
    internal const string PropertyChangeMethod = "EmitPropertyChangeSignal";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool s_reported;
    private readonly object _root;
    private readonly object _server;
    private readonly MethodInfo _attachedNode;
    private readonly MethodInfo _ensureChildren;
    private readonly AnnouncementMembers? _announce;
    private bool _focusDisabled, _announceDisabled, _valueDisabled;

    private AtSpiBridge(object root, object server, MethodInfo attachedNode, MethodInfo ensureChildren, AnnouncementMembers? announce)
    {
        _root = root;
        _server = server;
        _attachedNode = attachedNode;
        _ensureChildren = ensureChildren;
        _announce = announce;
    }

    internal static AtSpiBridge? TryCreate(Delegate? subscriber) =>
        OperatingSystem.IsLinux() && subscriber?.Target is { } root && root.GetType().FullName == RootNodeType ? ForNode(root) : null;

    /// <summary>A bridge for one window's node; null if the node lacks the members expected for focus.</summary>
    internal static AtSpiBridge? ForNode(object root)
    {
        try
        {
            if (Members(root.GetType()) is not var (server, attachedNode, ensureChildren) ||
                server.GetValue(root) is not { } instance)
            {
                Disable("the bridge members expected for Avalonia 12.1 were not found");
                return null;
            }
            var announce = Announcements(root.GetType());
            if (announce is null) Disable("the bridge has no announcement members");
            return new AtSpiBridge(root, instance, attachedNode, ensureChildren, announce);
        }
        catch (Exception ex)
        {
            Disable(ex.Message);
            return null;
        }
    }

    /// <summary>The focus members, or null when this Avalonia version does not provide them as expected.</summary>
    internal static (PropertyInfo Server, MethodInfo AttachedNode, MethodInfo EnsureChildren)? Members(Type rootNodeType)
    {
        var server = rootNodeType.GetProperty(ServerProperty, Instance);
        var ensureChildren = Find(rootNodeType, EnsureChildrenMethod, Type.EmptyTypes);
        var attachedNode = server is null ? null : Find(server.PropertyType, AttachedNodeMethod, [typeof(AutomationPeer)]);
        if (server is null || ensureChildren is null || attachedNode is null ||
            !attachedNode.ReturnType.IsAssignableFrom(rootNodeType) ||
            ensureChildren.ReturnType.GetInterface("System.Collections.IEnumerable") is null)
            return null;
        return (server, attachedNode, ensureChildren);
    }

    internal sealed record AnnouncementMembers(PropertyInfo Handler, MethodInfo Emit, MethodInfo PropertyChange, ConstructorInfo Variant, Type Properties);

    /// <summary>The announcement members, or null when this Avalonia version does not provide them as expected.</summary>
    internal static AnnouncementMembers? Announcements(Type rootNodeType)
    {
        PropertyInfo? handler = null;
        for (var t = rootNodeType; t is not null && handler is null; t = t.BaseType)
            handler = t.GetProperty(EventHandlerProperty, Instance | BindingFlags.DeclaredOnly);
        if (handler is null) return null;
        var emit = Find(handler.PropertyType, EmitSignalMethod, [typeof(string), typeof(object[])]);
        var propertyChange = handler.PropertyType.GetMethods(Instance)
            .FirstOrDefault(m => m.Name == PropertyChangeMethod && m.GetParameters() is [{ ParameterType: var name }, _] && name == typeof(string));
        var variantType = propertyChange?.GetParameters()[1].ParameterType;
        var variant = variantType?.GetConstructor(Instance, [typeof(object)]);
        if (emit is null || propertyChange is null || variantType is null || variant is null) return null;
        return new AnnouncementMembers(handler, emit, propertyChange, variant, typeof(Dictionary<,>).MakeGenericType(typeof(string), variantType));
    }

    private static MethodInfo? Find(Type type, string name, Type[] parameters)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.GetMethod(name, Instance | BindingFlags.DeclaredOnly, null, parameters, null) is { } method)
                return method;
        return null;
    }

    internal bool IsFor(Delegate? subscriber) => ReferenceEquals(subscriber?.Target, _root);

    internal void Attach(AutomationPeer root, AutomationPeer? focused)
    {
        if (_focusDisabled || focused is null || ReferenceEquals(focused, root)) return;
        try
        {
            // A peer outside this window keeps Avalonia's behavior.
            if (AutomationPaths.FromWindow(root, focused) is not { } path) return;
            path.Add(focused);
            object node = _root;
            for (int i = 1; i < path.Count; i++)
            {
                _ensureChildren.Invoke(node, null);
                // A layout layer the bridge does not expose is skipped; its children are the node's own.
                if (_attachedNode.Invoke(_server, [path[i]]) is { } attached) node = attached;
            }
        }
        catch (Exception ex)
        {
            _focusDisabled = true;
            Disable(ex.InnerException?.Message ?? ex.Message);
        }
    }

    /// <summary>Sends an AT-SPI announcement from the window, as Orca presents live regions (polite or assertive).</summary>
    internal void Announce(string text, AutomationLiveSetting setting)
    {
        if (_announce is not { } members || _announceDisabled || setting == AutomationLiveSetting.Off) return;
        try
        {
            // The node replaces its event handler when the bridge re-registers it, so read the current one.
            if (members.Handler.GetValue(_root) is not { } handler) return;
            int politeness = setting == AutomationLiveSetting.Assertive ? 2 : 1;
            var properties = Activator.CreateInstance(members.Properties, StringComparer.Ordinal)!;
            object[] body = [string.Empty, politeness, 0, members.Variant.Invoke([text]), properties];
            members.Emit.Invoke(handler, ["Announcement", body]);
        }
        catch (Exception ex)
        {
            _announceDisabled = true;
            Disable(ex.InnerException?.Message ?? ex.Message);
        }
    }

    /// <summary>Tells AT-SPI clients that an attached node's range value changed, from that node.</summary>
    internal void ValueChanged(AutomationPeer peer, double value)
    {
        if (_announce is not { } members || _valueDisabled) return;
        try
        {
            if (_attachedNode.Invoke(_server, [peer]) is not { } node || members.Handler.GetValue(node) is not { } handler) return;
            members.PropertyChange.Invoke(handler, ["accessible-value", members.Variant.Invoke([value])]);
        }
        catch (Exception ex)
        {
            _valueDisabled = true;
            Disable(ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static void Disable(string reason)
    {
        if (s_reported) return;
        s_reported = true;
        Core.Diagnostics.AppLog.Warn("Screen reader support on Linux falls back to Avalonia's default behavior: " + reason);
    }
}

/// <summary>
/// The peers from a window down to a target, for bridges that create native elements one level at a time. A peer's
/// parent link can be missing below the window: Avalonia links a peer to its parent only while the parent lists it
/// as a child, and keeps a link it looked up while the control was hidden, so the notification bar, hidden until its
/// first notice, had no parent. Where a control's link is missing its visual parent continues the path, which
/// always reaches the window. The path can hold layout layers a bridge does not expose; each bridge skips those.
/// </summary>
internal static class AutomationPaths
{
    private const int MaximumDepth = 256;

    /// <summary>The window's peer first, then each ancestor of the target; null when the target is outside the window.</summary>
    internal static List<AutomationPeer>? FromWindow(AutomationPeer root, AutomationPeer target)
    {
        var path = new List<AutomationPeer>();
        for (var peer = ParentOf(target); peer is not null && path.Count < MaximumDepth; peer = ParentOf(peer))
        {
            path.Add(peer);
            if (!ReferenceEquals(peer, root)) continue;
            path.Reverse();
            return path;
        }
        return null;
    }

    private static AutomationPeer? ParentOf(AutomationPeer peer)
    {
        if (peer.GetParent() is { } parent) return parent;
        if (peer is not ControlAutomationPeer { Owner: var owner }) return null;
        for (var visual = owner.GetVisualParent(); visual is not null; visual = visual.GetVisualParent())
            if (visual is Control control) return ControlAutomationPeer.CreatePeerForElement(control);
        return null;
    }
}
