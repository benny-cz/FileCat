using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
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
        var node = Type.GetType(AtSpiBridge.RootNodeType + ", Avalonia.FreeDesktop.AtSpi", throwOnError: true)!;
        Assert.NotNull(AtSpiBridge.Members(node));
        Assert.NotNull(AtSpiBridge.Announcements(node));
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
        var attachment = Assert.IsType<AtSpiBridge>(AtSpiBridge.ForNode(root));

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
        Assert.Null(AtSpiBridge.ForNode(new object()));
    }

    [Fact]
    public void Avalonia_still_provides_the_members_live_regions_use_on_Windows_and_macOS()
    {
        Assert.NotNull(NativeLiveRegionBridges.UiaNodeMember(Type.GetType(NativeLiveRegionBridges.UiaNodeType, throwOnError: true)));
        Assert.NotNull(NativeLiveRegionBridges.InvalidateChildrenMember());
    }

    [AvaloniaFact]
    public void A_live_region_is_tracked_by_the_Windows_bridge_once_attached()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("UI Automation nodes are Windows'."); return; }
        var status = new TextBlock { Text = "Ready" };
        var window = new FocusWindow { Width = 400, Height = 200, Content = new StackPanel { Children = { status } } };
        window.Show();
        try
        {
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            LiveRegions.Watch(status);
            var peer = ControlAutomationPeer.CreatePeerForElement(status);
            var node = NativeLiveRegionBridges.TrackOnWindows(peer);
            Assert.NotNull(node);
            Assert.Same(node, NativeLiveRegionBridges.TrackOnWindows(peer));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_macOS_bridge_refresh_runs_from_the_window_down_to_the_live_region()
    {
        var status = new TextBlock { Text = "Ready" };
        var bar = new Border { Child = status, IsVisible = false };
        var window = new FocusWindow { Width = 400, Height = 200, Content = new StackPanel { Children = { bar } } };
        window.Show();
        try
        {
            var root = ControlAutomationPeer.CreatePeerForElement(window);
            var peer = ControlAutomationPeer.CreatePeerForElement(status);
            var barPeer = ControlAutomationPeer.CreatePeerForElement(bar);
            // Looked up while the bar is hidden, the bar keeps no parent link once shown, as the notification bar did.
            Assert.Same(barPeer, peer.GetParent());
            Assert.Null(barPeer.GetParent());
            bar.IsVisible = true;
            Assert.Null(barPeer.GetParent());

            // Avalonia 12 hosts the window in a TopLevelHost, above the window's own peer.
            var ancestors = status.GetVisualAncestors().OfType<Control>().TakeWhile(control => control != window).Append(window)
                .Select(ControlAutomationPeer.CreatePeerForElement).Reverse().ToList();
            var refreshed = new List<AutomationPeer>();
            foreach (var ancestor in ancestors) ancestor.ChildrenChanged += (_, _) => refreshed.Add(ancestor);
            NativeLiveRegionBridges.TrackOnMac(root, peer);
            // Every layer from the window down to the bar, in the order the native bridge creates their elements.
            Assert.Same(root, ancestors[0]);
            Assert.Equal(ancestors, refreshed);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Linux_attachment_reaches_a_field_inside_a_container_that_was_hidden()
    {
        var field = new TextBox { Text = "New folder" };
        var card = new Border { Child = field, IsVisible = false };
        var window = new FocusWindow { Width = 400, Height = 200, Content = new StackPanel { Children = { card } } };
        window.Show();
        try
        {
            var root = ControlAutomationPeer.CreatePeerForElement(window);
            var peer = ControlAutomationPeer.CreatePeerForElement(field);
            var cardPeer = ControlAutomationPeer.CreatePeerForElement(card);
            Assert.Same(cardPeer, peer.GetParent());
            Assert.Null(cardPeer.GetParent());
            card.IsVisible = true;
            Assert.Null(cardPeer.GetParent());
            var server = new FakeBridgeServer();
            var node = new FakeBridgeNode(server, root);
            server.Attached[root] = node;

            Assert.IsType<AtSpiBridge>(AtSpiBridge.ForNode(node)).Attach(root, peer);
            Assert.True(server.Attached.ContainsKey(peer));
            Assert.Same(root, server.Enumerated[0]);
            Assert.Same(cardPeer, server.Enumerated[^1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Linux_live_regions_announce_their_new_text_through_the_window_bridge()
    {
        var status = new TextBlock { Text = "Ready" };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var window = new FocusWindow { Width = 400, Height = 200, Content = new StackPanel { Children = { status } } };
        window.Show();
        try
        {
            var root = Assert.IsType<DescendantFocusWindowAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(window));
            var node = new FakeBridgeNode(new FakeBridgeServer(), root);
            root.AtSpi = Assert.IsType<AtSpiBridge>(AtSpiBridge.ForNode(node));
            LiveRegions.Watch(status);

            status.Text = "Cut 1 item to the clipboard";
            Dispatcher.UIThread.RunJobs();
            var polite = Assert.Single(node.EventObjectHandler.Signals);
            Assert.Equal("Announcement", polite.Member);
            Assert.Equal(string.Empty, polite.Body[0]);
            Assert.Equal(1, polite.Body[1]);
            Assert.Equal(0, polite.Body[2]);
            Assert.Equal("Cut 1 item to the clipboard", Assert.IsType<FakeVariant>(polite.Body[3]).Value);
            Assert.Empty(Assert.IsType<Dictionary<string, FakeVariant>>(polite.Body[4]));

            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
            status.Text = "The folder name is invalid";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, node.EventObjectHandler.Signals[^1].Body[1]);

            // A region that stopped being live, or an empty text, announces nothing.
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Off);
            status.Text = "Quiet change";
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            status.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, node.EventObjectHandler.Signals.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_live_region_that_gains_text_while_hidden_speaks_once_when_shown()
    {
        var status = new TextBlock { IsVisible = false };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
        var window = new FocusWindow { Width = 400, Height = 200, Content = new StackPanel { Children = { status } } };
        window.Show();
        try
        {
            var root = Assert.IsType<DescendantFocusWindowAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(window));
            var node = new FakeBridgeNode(new FakeBridgeServer(), root);
            root.AtSpi = Assert.IsType<AtSpiBridge>(AtSpiBridge.ForNode(node));
            LiveRegions.Watch(status);
            string[] Spoken() => node.EventObjectHandler.Signals.Select(s => (string)Assert.IsType<FakeVariant>(s.Body[3]).Value).ToArray();
            // Looked up while hidden, the parent link stays empty once shown; NVDA drops events it cannot place.
            var statusPeer = ControlAutomationPeer.CreatePeerForElement(status);
            Assert.Null(statusPeer.GetParent());
            var parents = new List<AutomationPeer?>();
            statusPeer.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutomationElementIdentifiers.NameProperty && status.IsEffectivelyVisible) parents.Add(statusPeer.GetParent());
            };

            // A validation message gets its text, then is shown: heard once, after its layout.
            status.Text = "The name cannot be empty.";
            Assert.Empty(Spoken());
            status.IsVisible = true;
            Assert.Empty(Spoken());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["The name cannot be empty."], Spoken());
            var panel = ControlAutomationPeer.CreatePeerForElement((Control)window.Content!);
            Assert.Equal([panel], parents);
            status.Text = "The name is too long.";
            Assert.Equal(["The name cannot be empty.", "The name is too long."], Spoken());
            // Hiding and showing the same text, or changing it only while hidden and clearing it, says nothing new.
            status.IsVisible = false;
            status.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            status.IsVisible = false;
            status.Text = "Never shown.";
            status.Text = string.Empty;
            status.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, Spoken().Length);
            LiveRegions.Repeat(status);
            Assert.Equal(2, Spoken().Length);
            status.Text = "Shown again.";
            LiveRegions.Repeat(status);
            // Shown and changed in one step, as bindings may apply both: the final text, once.
            status.IsVisible = false;
            status.IsVisible = true;
            status.Text = "Changed as shown.";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["The name cannot be empty.", "The name is too long.", "Shown again.", "Shown again.", "Changed as shown."], Spoken());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_rejected_prompt_value_is_spoken_described_on_its_field_and_explained_again_on_Enter()
    {
        var host = new Grid();
        var window = new FocusWindow { Content = host, Width = 800, Height = 600 };
        window.Show();
        // As the app does, every control that becomes live is watched; only for this test.
        using var watch = AutomationProperties.LiveSettingProperty.Changed.AddClassHandler<Control>((control, _) => LiveRegions.Watch(control));
        var dialogs = new OverlayDialogService(host, () => null);
        try
        {
            var root = Assert.IsType<DescendantFocusWindowAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(window));
            var node = new FakeBridgeNode(new FakeBridgeServer(), root);
            root.AtSpi = Assert.IsType<AtSpiBridge>(AtSpiBridge.ForNode(node));
            var result = dialogs.PromptAsync(new PromptOptions("Quick filter", "Mask:")
            {
                Validate = t => t.Length == 0 ? "Enter a mask." : Core.Selection.Mask.TryParse(t, out _, out var error) ? null : error,
            });
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var box = host.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.True(box.IsFocused);
            (string, object)[] Spoken() => node.EventObjectHandler.Signals.Select(s => ((string)Assert.IsType<FakeVariant>(s.Body[3]).Value, s.Body[1])).ToArray();

            // Enter before typing anything shows and speaks why nothing happened.
            Assert.Empty(Spoken());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([("Enter a mask.", (object)2)], Spoken());
            Assert.Equal("Enter a mask.", AutomationProperties.GetHelpText(box));
            // TextBox reports a text change from the dispatcher.
            box.Text = "/";
            Dispatcher.UIThread.RunJobs();
            box.Text = "/a";
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            const string unclosed = "Unclosed regular expression in mask.";
            Assert.Equal([("Enter a mask.", (object)2), (unclosed, 2), (unclosed, 2)], Spoken());
            Assert.Equal(unclosed, AutomationProperties.GetHelpText(box));
            Assert.False(result.IsCompleted);

            box.Text = "*.txt";
            Dispatcher.UIThread.RunJobs();
            Assert.Null(AutomationProperties.GetHelpText(box));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("*.txt", Assert.IsType<PromptResult>(result.Result).Text);
            Assert.Equal(3, Spoken().Length);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Linux_hears_each_value_change_of_the_focused_slider_from_its_own_node()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50 };
        var other = new Slider { Minimum = 0, Maximum = 100, Value = 10 };
        var window = new FocusWindow { Width = 400, Height = 200, Content = new StackPanel { Children = { slider, other } } };
        window.Show();
        try
        {
            var root = Assert.IsType<DescendantFocusWindowAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(window));
            var server = new FakeBridgeServer();
            var node = new FakeBridgeNode(server, root);
            server.Attached[root] = node;
            root.AtSpi = Assert.IsType<AtSpiBridge>(AtSpiBridge.ForNode(node));
            slider.Focus();
            Dispatcher.UIThread.RunJobs();
            var sliderNode = server.Attached[ControlAutomationPeer.CreatePeerForElement(slider)];

            slider.Value = 51;
            Assert.Equal([("accessible-value", (object)51.0)], sliderNode.EventObjectHandler.PropertyChanges);
            // Only the focused element is followed.
            other.Value = 20;
            Assert.Single(sliderNode.EventObjectHandler.PropertyChanges);
            other.Focus();
            Dispatcher.UIThread.RunJobs();
            slider.Value = 52;
            Assert.Single(sliderNode.EventObjectHandler.PropertyChanges);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void Every_main_window_notification_is_announced_including_the_first_and_an_error_after_a_notice()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var texts = new[] { "NotificationInfoText", "NotificationErrorText" }.Select(name => window.FindControl<SelectableTextBlock>(name)!).ToArray();
            // The UI Automation and macOS bridges speak every name change of a live element: one per notice.
            int liveChanges = 0;
            foreach (var text in texts)
            {
                LiveRegions.Watch(text);
                var textPeer = ControlAutomationPeer.CreatePeerForElement(text);
                textPeer.PropertyChanged += (_, e) =>
                {
                    if (e.Property == AutomationElementIdentifiers.NameProperty && textPeer.GetLiveSetting() != AutomationLiveSetting.Off) liveChanges++;
                };
            }
            var peer = Assert.IsType<DescendantFocusWindowAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(window));
            var node = new FakeBridgeNode(new FakeBridgeServer(), peer);
            peer.AtSpi = Assert.IsType<AtSpiBridge>(AtSpiBridge.ForNode(node));

            vm.Notify("First notice", false);
            Dispatcher.UIThread.RunJobs();
            vm.Notify("Then an error", true);
            Dispatcher.UIThread.RunJobs();
            vm.Notify("A notice again", false);
            Dispatcher.UIThread.RunJobs();
            // A repeated notice keeps its text but is still a new event for the listener.
            vm.Notify("A notice again", false);
            Dispatcher.UIThread.RunJobs();
            var spoken = node.EventObjectHandler.Signals.Select(s => (Assert.IsType<FakeVariant>(s.Body[3]).Value, s.Body[1])).ToArray();
            Assert.Equal(new (object, object)[] { ("First notice", 1), ("Then an error", 2), ("A notice again", 1), ("A notice again", 1) }, spoken);
            Assert.Equal(4, liveChanges);
        }
        finally { AccessibilityTests.Close(services, window, root); }
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
        internal FakeEventHandler EventObjectHandler { get; } = new();

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

    /// <summary>The event handler's member shapes: a private signal emitter and a variant-typed public signal.</summary>
    private sealed class FakeEventHandler
    {
        internal List<(string Member, object[] Body)> Signals { get; } = [];
        internal List<(string Property, object Value)> PropertyChanges { get; } = [];
        public void EmitPropertyChangeSignal(string propertyName, FakeVariant value) => PropertyChanges.Add((propertyName, value.Value));
        private void EmitSignal(string member, params object[] body) => Signals.Add((member, body));
    }

    private sealed class FakeVariant(object value)
    {
        public object Value { get; } = value;
    }
}