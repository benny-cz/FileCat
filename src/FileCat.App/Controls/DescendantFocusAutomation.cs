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
        _focusChanged?.Invoke(this, EventArgs.Empty);
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
/// Avalonia's Windows and macOS bridges raise a live-region event when a live element's accessible name changes;
/// its AT-SPI bridge has none, so Orca heard no notification, Find result or validation error. On Linux every
/// control with a live setting announces its new name through its window's AT-SPI bridge, polite or assertive as
/// declared, at the same moment the other platforms raise their live-region event.
/// </summary>
internal static class AtSpiLiveRegions
{
    private static readonly ConditionalWeakTable<Control, AutomationPeer> s_watched = [];

    internal static void Install()
    {
        if (!OperatingSystem.IsLinux()) return;
        AutomationProperties.LiveSettingProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));
    }

    /// <summary>Announces the control's name changes while it is live; its live setting may be switched later.</summary>
    internal static void Watch(Control control)
    {
        if (s_watched.TryGetValue(control, out _)) return;
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        s_watched.Add(control, peer);
        peer.PropertyChanged += (_, e) =>
        {
            if (e.Property != AutomationElementIdentifiers.NameProperty) return;
            var setting = peer.GetLiveSetting();
            if (setting == AutomationLiveSetting.Off || peer.GetName() is not { Length: > 0 } text) return;
            if (TopLevel.GetTopLevel(control) is Window window &&
                ControlAutomationPeer.CreatePeerForElement(window) is DescendantFocusWindowAutomationPeer { AtSpi: { } bridge })
                bridge.Announce(text, setting);
        };
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
    private const int MaximumDepth = 64;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool s_reported;
    private readonly object _root;
    private readonly object _server;
    private readonly MethodInfo _attachedNode;
    private readonly MethodInfo _ensureChildren;
    private readonly AnnouncementMembers? _announce;
    private bool _focusDisabled, _announceDisabled;

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

    internal sealed record AnnouncementMembers(PropertyInfo Handler, MethodInfo Emit, ConstructorInfo Variant, Type Properties);

    /// <summary>The announcement members, or null when this Avalonia version does not provide them as expected.</summary>
    internal static AnnouncementMembers? Announcements(Type rootNodeType)
    {
        PropertyInfo? handler = null;
        for (var t = rootNodeType; t is not null && handler is null; t = t.BaseType)
            handler = t.GetProperty(EventHandlerProperty, Instance | BindingFlags.DeclaredOnly);
        if (handler is null) return null;
        var emit = Find(handler.PropertyType, EmitSignalMethod, [typeof(string), typeof(object[])]);
        var variantType = handler.PropertyType.GetMethods(Instance)
            .FirstOrDefault(m => m.Name == PropertyChangeMethod && m.GetParameters().Length == 2)?.GetParameters()[1].ParameterType;
        var variant = variantType?.GetConstructor(Instance, [typeof(object)]);
        if (emit is null || variantType is null || variant is null) return null;
        return new AnnouncementMembers(handler, emit, variant, typeof(Dictionary<,>).MakeGenericType(typeof(string), variantType));
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
            var chain = new List<AutomationPeer>();
            for (var peer = focused; !ReferenceEquals(peer, root); peer = peer.GetParent())
            {
                // A peer outside this window, or an unexpectedly deep tree, keeps Avalonia's behavior.
                if (peer is null || chain.Count == MaximumDepth) return;
                chain.Add(peer);
            }
            object? node = _root;
            for (int i = chain.Count - 1; i >= 0 && node is not null; i--)
            {
                _ensureChildren.Invoke(node, null);
                node = _attachedNode.Invoke(_server, [chain[i]]);
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

    private static void Disable(string reason)
    {
        if (s_reported) return;
        s_reported = true;
        Core.Diagnostics.AppLog.Warn("Screen reader support on Linux falls back to Avalonia's default behavior: " + reason);
    }
}
