using System.Reflection;
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
/// Linux. Avalonia's AT-SPI bridge also announces nothing when a window becomes active, so on Linux the current
/// focus is announced again after activation.
/// </summary>
internal sealed class DescendantFocusWindowAutomationPeer : WindowAutomationPeer, IRootProvider
{
    private EventHandler? _focusChanged;
    private IFocusedDescendantPeer? _owner;
    private AutomationPeer? _reported;
    private AtSpiFocusAttachment? _atSpi;

    public DescendantFocusWindowAutomationPeer(Window owner) : base(owner)
    {
        base.FocusChanged += OnControlFocusChanged;
        owner.Activated += OnActivated;
        // A list can already own keyboard focus when this peer is first requested.
        TrackOwner();
        _reported = ResolveFocus();
    }

    AutomationPeer? IRootProvider.GetFocus() => ResolveFocus();
    AutomationPeer? IRootProvider.GetPeerFromPoint(Point p) => GetPeerFromPoint(p);
    ITopLevelImpl? IRootProvider.PlatformImpl => PlatformImpl;

    event EventHandler? IRootProvider.FocusChanged
    {
        add
        {
            _focusChanged += value;
            if (AtSpiFocusAttachment.TryCreate(value) is { } attachment) _atSpi = attachment;
        }
        remove
        {
            _focusChanged -= value;
            if (_atSpi?.IsFor(value) == true) _atSpi = null;
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
        _atSpi?.Attach(this, focus);
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
/// Avalonia 12.1's AT-SPI bridge emits focus only for a node that an assistive client has already reached by
/// enumerating children, and Orca does not enumerate a window before it waits for focus. Before a focus change is
/// raised, this asks the bridge's own child enumeration to attach each node from the window to the focused peer.
/// It uses only the bridge members named below, found on the actual subscriber of the window's focus event. If any
/// is missing or fails, it disables itself and Avalonia's unmodified behavior remains.
/// </summary>
internal sealed class AtSpiFocusAttachment
{
    internal const string RootNodeType = "Avalonia.FreeDesktop.AtSpi.RootAtSpiNode";
    internal const string ServerProperty = "Server";
    internal const string AttachedNodeMethod = "TryGetAttachedNode";
    internal const string EnsureChildrenMethod = "EnsureChildren";
    private const int MaximumDepth = 64;
    private static bool s_reported;
    private readonly object _root;
    private readonly object _server;
    private readonly MethodInfo _attachedNode;
    private readonly MethodInfo _ensureChildren;
    private bool _disabled;

    private AtSpiFocusAttachment(object root, object server, MethodInfo attachedNode, MethodInfo ensureChildren)
    {
        _root = root;
        _server = server;
        _attachedNode = attachedNode;
        _ensureChildren = ensureChildren;
    }

    internal static AtSpiFocusAttachment? TryCreate(Delegate? subscriber) =>
        OperatingSystem.IsLinux() && subscriber?.Target is { } root && root.GetType().FullName == RootNodeType ? ForNode(root) : null;

    /// <summary>An attachment for one window's bridge node; it disables itself if the node lacks the expected members.</summary>
    internal static AtSpiFocusAttachment? ForNode(object root)
    {
        try
        {
            if (Members(root.GetType()) is not var (server, attachedNode, ensureChildren) ||
                server.GetValue(root) is not { } instance)
            {
                Disable("the bridge members expected for Avalonia 12.1 were not found");
                return null;
            }
            return new AtSpiFocusAttachment(root, instance, attachedNode, ensureChildren);
        }
        catch (Exception ex)
        {
            Disable(ex.Message);
            return null;
        }
    }

    /// <summary>The bridge members, or null when this Avalonia version does not provide them as expected.</summary>
    internal static (PropertyInfo Server, MethodInfo AttachedNode, MethodInfo EnsureChildren)? Members(Type rootNodeType)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var server = rootNodeType.GetProperty(ServerProperty, instance);
        var ensureChildren = Find(rootNodeType, EnsureChildrenMethod, Type.EmptyTypes);
        var attachedNode = server is null ? null : Find(server.PropertyType, AttachedNodeMethod, [typeof(AutomationPeer)]);
        if (server is null || ensureChildren is null || attachedNode is null ||
            !attachedNode.ReturnType.IsAssignableFrom(rootNodeType) ||
            ensureChildren.ReturnType.GetInterface("System.Collections.IEnumerable") is null)
            return null;
        return (server, attachedNode, ensureChildren);
    }

    private static MethodInfo? Find(Type type, string name, Type[] parameters)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, parameters, null) is { } method)
                return method;
        return null;
    }

    internal bool IsFor(Delegate? subscriber) => ReferenceEquals(subscriber?.Target, _root);

    internal void Attach(AutomationPeer root, AutomationPeer? focused)
    {
        if (_disabled || focused is null || ReferenceEquals(focused, root)) return;
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
            _disabled = true;
            Disable(ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static void Disable(string reason)
    {
        if (s_reported) return;
        s_reported = true;
        Core.Diagnostics.AppLog.Warn("Screen reader focus on Linux uses Avalonia's default announcements: " + reason);
    }
}
