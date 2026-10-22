using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>
/// FCUX-010 speech: a screen reader follows the focused file row, not only the list that owns keyboard focus,
/// and Linux AT-SPI nodes on the focused path exist before their focus is announced.
/// </summary>
public sealed class DescendantFocusAutomationTests
{
    [AvaloniaFact]
    public async Task Window_focus_follows_the_focused_row_with_one_native_change_per_move()
    {
        using var f = new FileListFixture(new FileListFixture.ListProvider { Items = ["a", "b", "c"] }, new FocusWindow());
        await f.Load();
        var provider = Root(f.Window);
        var reported = new List<string?>();
        provider.FocusChanged += (_, _) => reported.Add(provider.GetFocus()?.GetName());

        f.List.Focus();
        Dispatcher.UIThread.RunJobs();
        var first = Assert.IsAssignableFrom<AutomationPeer>(provider.GetFocus());
        Assert.Equal(AutomationControlType.ListItem, first.GetAutomationControlType());
        Assert.Equal("a", first.GetName());
        Assert.True(first.HasKeyboardFocus());
        Assert.Equal("a", reported[^1]);

        reported.Clear();
        f.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new string?[] { "b" }, reported);
        Assert.True(provider.GetFocus()!.HasKeyboardFocus());

        // A mark change on the focused row is not a focus move; focus moved by the model is announced once.
        f.Tab.Listing.SetMark(1, true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new string?[] { "b" }, reported);
        f.Tab.Listing.SetFocus(2);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new string?[] { "b", "c" }, reported);

        // Leaving the list reports the newly focused control, never the list's last row.
        f.Other.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Other focus target", reported[^1]);
        Assert.Same(ControlAutomationPeer.CreatePeerForElement(f.Other), provider.GetFocus());
        f.Tab.Listing.SetFocus(0);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Other focus target", reported[^1]);
        Assert.Same(ControlAutomationPeer.CreatePeerForElement(f.Other), provider.GetFocus());
    }

    [AvaloniaFact]
    public async Task Moving_focus_inside_the_visible_rows_keeps_the_exposed_children()
    {
        using var f = new FileListFixture(new FileListFixture.ListProvider { SyntheticCount = 200 }, new FocusWindow());
        await f.Load();
        var list = ListPeer(f.List);
        f.List.Focus();
        Dispatcher.UIThread.RunJobs();
        _ = list.GetChildren();
        int rebuilt = 0;
        list.ChildrenChanged += (_, _) => rebuilt++;

        f.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();
        f.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, rebuilt);
        Assert.Equal("item-0000002", Root(f.Window).GetFocus()!.GetName());

        // Paging past the visible rows scrolls them, which changes the exposed children.
        f.Window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        Dispatcher.UIThread.RunJobs();
        f.Window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(rebuilt > 0);
        var focused = Root(f.Window).GetFocus()!;
        Assert.Contains(list.GetChildren(), child => ReferenceEquals(child, focused));
        Assert.True(focused.HasKeyboardFocus());
    }

    [AvaloniaFact]
    public async Task Parent_navigation_is_focusable_and_never_selectable()
    {
        using var f = new FileListFixture(new FileListFixture.ListProvider { Items = ["a"], HasParent = true }, new FocusWindow());
        await f.Load();
        f.List.Focus();
        Dispatcher.UIThread.RunJobs();
        var parent = Assert.IsAssignableFrom<AutomationPeer>(Root(f.Window).GetFocus());
        Assert.Equal("Go to parent location", parent.GetName());
        Assert.True(parent.HasKeyboardFocus());
        var selection = parent.GetProvider<ISelectionItemProvider>();
        if (OperatingSystem.IsLinux())
        {
            // AT-SPI exposes only selection items as a selection container's children.
            Assert.NotNull(selection);
            Assert.False(selection.IsSelected);
            Assert.Throws<InvalidOperationException>(selection.Select);
            Assert.Throws<InvalidOperationException>(selection.AddToSelection);
            Assert.Throws<InvalidOperationException>(selection.RemoveFromSelection);
            Assert.Equal(0, f.Tab.Listing.MarkedCount);
        }
        else Assert.Null(selection);
        Assert.Contains(ListPeer(f.List).GetChildren(), child => ReferenceEquals(child, parent));
    }

    [Fact]
    public void Avalonia_AtSpi_bridge_still_provides_the_members_used_for_Linux_focus()
    {
        var node = Type.GetType(AtSpiFocusAttachment.RootNodeType + ", Avalonia.FreeDesktop.AtSpi", throwOnError: true)!;
        Assert.NotNull(AtSpiFocusAttachment.Members(node));
    }

    [AvaloniaFact]
    public async Task Linux_attachment_enumerates_each_ancestor_down_to_the_focused_row()
    {
        using var f = new FileListFixture(new FileListFixture.ListProvider { Items = ["a", "b"] }, new FocusWindow());
        await f.Load();
        f.List.Focus();
        f.Tab.Listing.SetFocus(1);
        Dispatcher.UIThread.RunJobs();
        var window = ControlAutomationPeer.CreatePeerForElement(f.Window);
        var row = ((IRootProvider)window).GetFocus()!;
        Assert.Equal("b", row.GetName());
        var server = new FakeBridgeServer();
        var root = new FakeBridgeNode(server, window);
        server.Attached[window] = root;
        var attachment = Assert.IsType<AtSpiFocusAttachment>(AtSpiFocusAttachment.ForNode(root));

        attachment.Attach(window, row);
        Assert.True(server.Attached.ContainsKey(row));
        Assert.Same(window, server.Enumerated[0]);
        Assert.Same(ListPeer(f.List), server.Enumerated[^1]);
        for (int i = 1; i < server.Enumerated.Count; i++)
            Assert.Same(server.Enumerated[i - 1], server.Enumerated[i].GetParent());

        // A peer from another window is not attached through this window's bridge node.
        int enumerated = server.Enumerated.Count;
        using var other = new FileListFixture("x");
        await other.Load();
        attachment.Attach(window, ListPeer(other.List).GetChildren()[0]);
        Assert.Equal(enumerated, server.Enumerated.Count);
        Assert.Null(AtSpiFocusAttachment.ForNode(new object()));
    }

    [AvaloniaFact]
    public async Task Main_window_reports_file_rows_and_dialog_fields_as_the_focused_element()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var listing = vm.ActiveTab!.Listing;
            await FileListFixture.Wait(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            var provider = Root(window);
            Assert.IsType<DescendantFocusWindowAutomationPeer>(provider);
            var reported = new List<AutomationPeer?>();
            provider.FocusChanged += (_, _) => reported.Add(provider.GetFocus());
            window.ActiveList!.Focus();
            listing.SetFocus(1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("a.txt", provider.GetFocus()!.GetName());

            window.KeyPress(Key.Insert, RawInputModifiers.None, PhysicalKey.Insert, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("b.txt", reported[^1]!.GetName());
            Assert.True(listing.IsMarked(listing.GetStoreIndex(1)));

            vm.Execute(CommandIds.MakeDirectory);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await FileListFixture.Wait(() => dialogs.IsOpen);
            Dispatcher.UIThread.RunJobs();
            var field = Assert.IsAssignableFrom<AutomationPeer>(provider.GetFocus());
            Assert.Equal(AutomationControlType.Edit, field.GetAutomationControlType());
            Assert.True(field.HasKeyboardFocus());
            Assert.Same(field, reported[^1]);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await FileListFixture.Wait(() => !dialogs.IsOpen);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("b.txt", provider.GetFocus()!.GetName());
            Assert.Equal("b.txt", reported[^1]!.GetName());
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static IRootProvider Root(Window window) =>
        Assert.IsAssignableFrom<IRootProvider>(ControlAutomationPeer.CreatePeerForElement(window));

    private static FileListAutomationPeer ListPeer(FileListControl list) =>
        Assert.IsType<FileListAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(list));

    private sealed class FocusWindow : Window
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new DescendantFocusWindowAutomationPeer(this);
    }

    /// <summary>Stands in for Avalonia's internal AT-SPI server with the same member shapes.</summary>
    private sealed class FakeBridgeServer
    {
        public Dictionary<AutomationPeer, FakeBridgeNode> Attached { get; } = new(ReferenceEqualityComparer.Instance);
        public List<AutomationPeer> Enumerated { get; } = [];
        internal FakeBridgeNode? TryGetAttachedNode(AutomationPeer? peer) =>
            peer is not null && Attached.TryGetValue(peer, out var node) ? node : null;
    }

    private sealed class FakeBridgeNode(FakeBridgeServer server, AutomationPeer peer)
    {
        public FakeBridgeServer Server { get; } = server;

        internal IReadOnlyList<FakeBridgeNode> EnsureChildren()
        {
            Server.Enumerated.Add(peer);
            var children = new List<FakeBridgeNode>();
            foreach (var child in peer.GetChildren())
            {
                if (!Server.Attached.TryGetValue(child, out var node)) Server.Attached[child] = node = new FakeBridgeNode(Server, child);
                children.Add(node);
            }
            return children;
        }
    }
}
