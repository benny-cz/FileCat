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
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Selection;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>FCUX-010: peer patterns, dynamic identities, complete paged marks and real keyboard invocation.</summary>
public sealed class FileListAutomationTests
{
    [AvaloniaFact]
    public async Task Container_identity_is_stable_while_focus_and_marks_have_separate_semantics()
    {
        using var f = new FileListFixture("a", "b", "c");
        await f.Load();
        var peer = Peer(f.List);
        f.Panel.Number = 3;
        f.Panel.IsActive = true;
        string name = peer.GetName();
        Assert.Equal("Panel 3, source, first", name);
        Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
        Assert.False(peer.IsSelectionRequired);
        Assert.True(peer.CanSelectMultiple);
        Assert.Empty(peer.GetSelection());
        var first = Row(peer, "a");
        f.List.Focus();
        Assert.True(first.HasKeyboardFocus());
        Assert.False(((ISelectionItemProvider)first).IsSelected);
        f.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.False(first.HasKeyboardFocus());
        Assert.True(Row(peer, "b").HasKeyboardFocus());
        Assert.Equal(name, peer.GetName());
        Assert.Contains("b, file", peer.Value);
        Assert.Empty(peer.GetSelection());
        ((ISelectionItemProvider)first).AddToSelection();
        Assert.Equal("a", Assert.Single(peer.GetSelection()).GetName());
        Assert.True(Row(peer, "b").HasKeyboardFocus()); // marking is not focus
        Assert.Equal(0, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaFact]
    public async Task Standard_selection_includes_offscreen_and_hidden_marks_and_supports_safe_item_actions()
    {
        using var f = new FileListFixture(Enumerable.Range(0, 100).Select(i => $"item-{i:D3}").ToArray());
        await f.Load();
        var peer = Peer(f.List);
        f.Tab.Listing.SetMark(1, true);
        f.Tab.Listing.SetMark(80, true);
        var selected = peer.GetSelection();
        var far = selected.Single(p => p.GetName() == "item-080");
        Assert.True(far.IsOffscreen());
        Assert.Equal(default, far.GetBoundingRectangle());
        Assert.Same(peer, far.GetParent());
        int before = f.Tab.Listing.FocusedIndex;
        far.BringIntoView();
        Assert.False(far.IsOffscreen());
        Assert.True(far.GetBoundingRectangle().Height > 0);
        Assert.Equal(before, f.Tab.Listing.FocusedIndex);
        far.SetFocus();
        Assert.Equal(80, f.Tab.Listing.FocusedIndex);
        int invoked = 0;
        f.List.OpenRequested += (_, _) => invoked++;
        ((IInvokeProvider)far).Invoke();
        Assert.Equal(1, invoked);

        f.Tab.Listing.Filter = Mask.Parse("*|item-080");
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 99);
        selected = peer.GetSelection();
        Assert.Equal(2, selected.Count);
        Assert.Same(far, selected.Single(p => p.GetName() == "item-080"));
        Assert.True(far.IsOffscreen());
        Assert.Contains("hidden", far.GetHelpText());
        Assert.Contains("1 hidden", peer.GetHelpText());
        Assert.Throws<InvalidOperationException>(far.SetFocus);
        ((ISelectionItemProvider)far).RemoveFromSelection();
        Assert.Equal("item-001", Assert.Single(peer.GetSelection()).GetName());
        Assert.Equal(0, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaFact]
    public async Task Sorting_reuses_item_identity_and_updates_bounds_and_position_without_renaming_it()
    {
        using var f = new FileListFixture("a", "b", "c", "d");
        await f.Load();
        var peer = Peer(f.List);
        var row = Row(peer, "a");
        Rect oldBounds = row.GetBoundingRectangle();
        string id = Assert.IsType<string>(row.GetAutomationId());
        f.Tab.Listing.Sort = new SortSpec(Descending: true);
        await FileListFixture.Wait(() => f.Tab.Listing.GetVisible(0).Name == "d");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(row, Row(peer, "a"));
        Assert.Equal(id, row.GetAutomationId());
        Assert.Equal("a", row.GetName());
        Assert.Contains("4 of 4", row.GetItemStatus());
        Assert.True(row.GetBoundingRectangle().Y > oldBounds.Y);
        Assert.Contains("descending", peer.GetHelpText());
    }

    [AvaloniaFact]
    public async Task Peers_from_a_replaced_store_cannot_mark_focus_or_invoke_another_item()
    {
        using var f = new FileListFixture("a", "b");
        await f.Load();
        var peer = Peer(f.List);
        var old = Row(peer, "a");
        string oldId = Assert.IsType<string>(old.GetAutomationId());
        var store = f.Tab.Listing.Store;
        f.Provider.Items = ["new", "a", "b"];
        f.Tab.Refresh();
        await FileListFixture.Wait(() => !ReferenceEquals(store, f.Tab.Listing.Store) && !f.Tab.Listing.IsRefreshing);
        Dispatcher.UIThread.RunJobs();
        Assert.False(old.IsEnabled());
        Assert.True(old.IsOffscreen());
        Assert.Equal(peer.CachedItemCount, f.List.AutomationOwnerCount);
        Assert.False(((ISelectionItemProvider)old).IsSelected);
        Assert.Throws<InvalidOperationException>(() => ((ISelectionItemProvider)old).AddToSelection());
        Assert.Throws<InvalidOperationException>(old.SetFocus);
        Assert.Throws<InvalidOperationException>(() => ((IInvokeProvider)old).Invoke());
        Assert.Empty(peer.GetSelection());
        Assert.NotEqual(oldId, Row(peer, "a").GetAutomationId());
    }

    [AvaloniaFact]
    public async Task An_item_invoke_cannot_follow_activation_into_another_tabs_files()
    {
        using var f = new FileListFixture("a", "b");
        await f.Load();
        var row = Row(Peer(f.List), "b");
        var second = f.Panel.OpenTab(new Location(FileListFixture.Scheme, "second"));
        await FileListFixture.Wait(() => second.Listing.State == ListingState.Complete);
        int secondFocus = second.Listing.FocusedIndex;
        int opened = 0;
        f.List.ActivateRequested += (_, _) => f.List.Tab = second;
        f.List.OpenRequested += (_, _) => opened++;
        Assert.Throws<InvalidOperationException>(() => ((IInvokeProvider)row).Invoke());
        Assert.Equal(secondFocus, second.Listing.FocusedIndex);
        Assert.Equal(0, opened);
    }

    [AvaloniaFact]
    public async Task Selection_item_select_publishes_one_complete_mark_change_even_for_hidden_items()
    {
        using var f = new FileListFixture("a", "b", "c");
        await f.Load();
        var peer = Peer(f.List);
        var a = Row(peer, "a");
        f.Tab.Listing.SetMark(1, true);
        f.Tab.Listing.SetMark(2, true);
        f.Tab.Listing.Filter = Mask.Parse("*|a");
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 2);
        var changes = new List<string[]>();
        f.Tab.Listing.Changed += (_, change) =>
        {
            if ((change & ListingChange.Marks) != 0) changes.Add(FileListFixture.Marked(f.Tab.Listing));
        };
        ((ISelectionItemProvider)a).Select();
        Assert.Equal(new[] { "a" }, Assert.Single(changes));
        Assert.Equal("a", Assert.Single(peer.GetSelection()).GetName());
        Assert.False(a.HasKeyboardFocus());
    }

    [AvaloniaFact]
    public async Task State_channels_distinguish_loading_filter_empty_partial_failure_and_provider_kinds()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new FileListFixture([], finish, []);
        var peer = Peer(f.List);
        Assert.Contains("Loading", peer.GetItemStatus());
        var stateEvents = new List<AutomationPropertyChangedEventArgs>();
        peer.PropertyChanged += (_, e) => stateEvents.Add(e);
        finish.SetResult();
        await f.Load();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Empty location", peer.GetItemStatus());
        Assert.Contains(stateEvents, e => e.Property == AutomationElementIdentifiers.ItemStatusProperty &&
            e.OldValue is string oldState && oldState.Contains("Loading", StringComparison.Ordinal) &&
            e.NewValue is string newState && newState.Contains("Empty location", StringComparison.Ordinal));

        f.Provider.Entries =
        [
            new EntryData("", EntryKind.RegistryValue),
            new EntryData("drive", EntryKind.Drive),
            new EntryData("unavailable\nfile", EntryKind.File) { Flags = EntryFlags.Offline | EntryFlags.Unavailable },
        ];
        f.Provider.HasParent = true;
        f.Tab.Navigate(new Location(FileListFixture.Scheme, "typed"));
        await FileListFixture.Wait(() => f.Tab.Listing.State == ListingState.Complete && f.Tab.Listing.VisibleCount == 4);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Registry value", Row(peer, "(Default)").GetItemType());
        Assert.Equal("drive", Row(peer, "drive").GetItemType());
        var parent = Row(peer, "Go to parent location");
        Assert.Equal("parent navigation", parent.GetItemType());
        Assert.Null(parent.GetProvider<ISelectionItemProvider>());
        var file = peer.GetChildren().Single(p => p.GetName()?.Contains("unavailable", StringComparison.Ordinal) == true);
        Assert.DoesNotContain('\n', file.GetName());
        Assert.Contains("cloud placeholder", file.GetHelpText());
        Assert.Contains("unavailable", file.GetHelpText());

        f.Tab.Listing.Filter = Mask.Parse("no-match;no-match/");
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 1);
        Assert.Contains("No items match", peer.GetItemStatus());
        Assert.Contains("Clear the filter", peer.GetHelpText());
        f.Provider.Failure = new UnauthorizedAccessException("Permission denied for test location");
        f.Tab.Navigate(new Location(FileListFixture.Scheme, "blocked"));
        await FileListFixture.Wait(() => f.Tab.Listing.State == ListingState.Failed);
        Assert.Contains("Not available", peer.GetItemStatus());
        Assert.Contains("Permission denied", peer.GetHelpText());
        Assert.Contains("Refresh", peer.GetHelpText());
    }

    [AvaloniaFact]
    public async Task Stopped_partial_listing_remains_distinct_from_a_complete_or_empty_location()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new FileListFixture(["a"], finish, ["b"]);
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 1);
        var peer = Peer(f.List);
        f.Tab.Listing.CancelLoading();
        Assert.True(f.Tab.Listing.IsPartial);
        Assert.Contains("Listing incomplete", peer.GetItemStatus());
        Assert.Contains("stopped", peer.GetHelpText());
        Assert.Contains("Refresh", peer.GetHelpText());
        Assert.Contains(peer.GetChildren(), p => p.GetName() == "a");
    }

    [AvaloniaFact]
    public async Task Focus_and_mark_events_report_actual_changes_and_background_updates_are_coalesced()
    {
        using var f = new FileListFixture("a", "b", "c", "d");
        await f.Load();
        var peer = Peer(f.List);
        var row = Row(peer, "a");
        f.List.Focus();
        Dispatcher.UIThread.RunJobs();
        var events = new List<AutomationPropertyChangedEventArgs>();
        var rowEvents = new List<AutomationPropertyChangedEventArgs>();
        peer.PropertyChanged += (_, e) => events.Add(e);
        row.PropertyChanged += (_, e) => rowEvents.Add(e);
        f.Tab.Listing.SetFocus(1);
        f.Tab.Listing.SetFocus(2);
        f.Tab.Listing.SetFocus(3);
        f.Tab.Listing.SetMark(0, true);
        for (int i = 0; i < 200; i++) peer.QueueUpdate(ListingChange.State);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(events, e => e.Property == AutomationElementIdentifiers.NameProperty);
        var focus = Assert.Single(events, e => e.Property == ValuePatternIdentifiers.ValueProperty);
        Assert.Contains("a, file", (string)focus.OldValue!);
        Assert.Contains("d, file", (string)focus.NewValue!);
        var mark = Assert.Single(rowEvents, e => e.Property == SelectionItemPatternIdentifiers.IsSelectedProperty);
        Assert.Equal(false, mark.OldValue);
        Assert.Equal(true, mark.NewValue);
        var selection = Assert.Single(events, e => e.Property == SelectionPatternIdentifiers.SelectionProperty);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AutomationPeer>>(selection.OldValue));
        Assert.Equal("a", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AutomationPeer>>(selection.NewValue)).GetName());
        var details = Assert.Single(rowEvents, e => e.Property == AutomationElementIdentifiers.HelpTextProperty);
        Assert.Contains("not marked", Assert.IsType<string>(details.OldValue));
        Assert.Contains(", marked", Assert.IsType<string>(details.NewValue));
        events.Clear();
        rowEvents.Clear();
        f.Tab.Listing.SetMark(0, false);
        f.Tab.Listing.SetMark(0, true); // an unchanged final selected set does not manufacture a change event
        for (int i = 0; i < 200; i++) peer.QueueUpdate(ListingChange.State);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(events);
        Assert.Empty(rowEvents);
    }

    [AvaloniaFact]
    public async Task Refresh_selection_events_keep_the_actual_old_set_when_every_marked_item_disappears()
    {
        using var f = new FileListFixture("a", "b");
        await f.Load();
        f.Tab.Listing.SetMark(1, true);
        var peer = Peer(f.List);
        Dispatcher.UIThread.RunJobs();
        var events = new List<AutomationPropertyChangedEventArgs>();
        peer.PropertyChanged += (_, e) => events.Add(e);
        var store = f.Tab.Listing.Store;
        f.Provider.Items = ["c"];
        f.Tab.Refresh();
        await FileListFixture.Wait(() => !ReferenceEquals(store, f.Tab.Listing.Store) && !f.Tab.Listing.IsRefreshing);
        Dispatcher.UIThread.RunJobs();
        var change = Assert.Single(events, e => e.Property == SelectionPatternIdentifiers.SelectionProperty);
        var oldItem = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AutomationPeer>>(change.OldValue));
        Assert.Equal("b", oldItem.GetName());
        Assert.False(oldItem.IsEnabled());
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AutomationPeer>>(change.NewValue));
    }

    [AvaloniaFact]
    public async Task Partially_visible_row_bounds_are_clipped_to_content_and_exclude_the_scrollbar()
    {
        using var f = new FileListFixture(Enumerable.Range(0, 100).Select(i => $"item-{i:D3}").ToArray());
        await f.Load();
        var peer = Peer(f.List);
        int row = f.List.VisibleRowCapacity;
        var actual = f.List.GetRowBounds(row);
        // Choose a non-integral final row, independent of inherited application font metrics.
        f.Window.Height -= actual.Height / 2;
        Dispatcher.UIThread.RunJobs();
        row = f.List.VisibleRowCapacity;
        actual = f.List.GetRowBounds(row);
        if (actual.Top >= f.List.Bounds.Height)
        {
            f.Window.Height += actual.Height / 3;
            Dispatcher.UIThread.RunJobs();
            row = f.List.VisibleRowCapacity;
            actual = f.List.GetRowBounds(row);
        }
        var item = Row(peer, $"item-{row:D3}");
        var bounds = item.GetBoundingRectangle();
        Assert.False(item.IsOffscreen());
        Assert.InRange(bounds.Height, double.Epsilon, actual.Height - double.Epsilon);
        var listOrigin = f.List.TranslatePoint(default, f.Window)!.Value;
        Assert.True(bounds.Bottom <= listOrigin.Y + f.List.Bounds.Height);
        Assert.True(bounds.Width < f.List.Bounds.Width);
    }

    [AvaloniaFact]
    public async Task Scroll_pattern_has_valid_bounds_and_does_not_change_command_focus()
    {
        using var f = new FileListFixture(Enumerable.Range(0, 100).Select(i => $"item-{i:D3}").ToArray());
        await f.Load();
        var peer = Peer(f.List);
        Assert.True(peer.VerticallyScrollable);
        Assert.False(peer.HorizontallyScrollable);
        int focus = f.Tab.Listing.FocusedIndex;
        peer.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, 100);
        Assert.Equal(100, peer.VerticalScrollPercent);
        peer.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallDecrement);
        Assert.InRange(peer.VerticalScrollPercent, 0, 99.999);
        Assert.Equal(focus, f.Tab.Listing.FocusedIndex);
        Assert.Throws<ArgumentOutOfRangeException>(() => peer.SetScrollPercent(-1, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => peer.SetScrollPercent(-1, 101));
        Assert.Throws<InvalidOperationException>(() => peer.Scroll(ScrollAmount.SmallIncrement, ScrollAmount.NoAmount));
    }

    [AvaloniaFact]
    public async Task Paged_review_reaches_every_mark_with_keyboard_and_keeps_filter_hidden_marks()
    {
        using var f = new FileListFixture(Enumerable.Range(0, 130).Select(i => $"item-{i:D3}").ToArray());
        await f.Load();
        f.Tab.Listing.MarkAll(true);
        f.Tab.Listing.Filter = Mask.Parse("item-00*");
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 10);
        using var review = new MarkedItemsReview(f.Tab);
        f.Window.Content = review;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(AccessibilityTests.Unnamed(review));
        var found = new List<string>();
        found.AddRange(review.CurrentItems.Select(i => i.Name));
        Assert.Equal(64, review.CurrentItems.Count);
        Assert.Contains("120 marks are hidden", review.Summary);
        Assert.Contains(review.CurrentItems, i => i.Hidden);
        var next = review.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Next page"));
        var previous = review.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Previous page"));
        for (int i = 0; i < 2; i++)
        {
            next.Focus();
            f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            found.AddRange(review.CurrentItems.Select(item => item.Name));
        }
        Assert.Equal(130, found.Count);
        Assert.Equal(130, found.Distinct().Count());
        Assert.False(next.IsEnabled);
        previous.Focus();
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal("item-064", review.CurrentItems[0].Name);
        Assert.Equal(130, f.Tab.Listing.MarkedCount);
        Assert.Equal(0, f.Provider.ItemReferencesRequested);

        var store = f.Tab.Listing.Store;
        f.Tab.Refresh();
        await FileListFixture.Wait(() => !ReferenceEquals(store, f.Tab.Listing.Store));
        Assert.Empty(review.CurrentItems);
        Assert.Contains("listing changed", review.Summary);
        Assert.False(next.IsEnabled);
        Assert.False(previous.IsEnabled);
    }

    [AvaloniaFact]
    public async Task Detached_visual_host_retires_cached_item_actions_even_when_its_tab_is_still_alive()
    {
        using var f = new FileListFixture("a", "b");
        await f.Load();
        f.Tab.Listing.SetMark(0, true);
        var peer = Peer(f.List);
        var item = Assert.Single(peer.GetSelection());
        int opens = 0;
        f.List.OpenRequested += (_, _) => opens++;
        f.Window.Content = new Button { Content = "Replacement visual host" };
        Dispatcher.UIThread.RunJobs();
        Assert.False(f.Tab.Listing.IsDisposed);
        Assert.False(peer.IsEnabled());
        Assert.Empty(peer.GetSelection());
        Assert.False(item.IsEnabled());
        Assert.True(item.IsOffscreen());
        Assert.Throws<InvalidOperationException>(() => ((ISelectionItemProvider)item).RemoveFromSelection());
        Assert.Throws<InvalidOperationException>(() => ((ISelectionItemProvider)item).Select());
        Assert.Throws<InvalidOperationException>(() => ((IInvokeProvider)item).Invoke());
        Assert.Throws<InvalidOperationException>(() => item.SetFocus());
        Assert.Equal(0, opens);
        Assert.Equal(1, f.Tab.Listing.MarkedCount);
        Assert.True(f.Tab.Listing.IsMarked(0));
    }

    [AvaloniaFact]
    public async Task Result_set_review_and_peers_distinguish_same_names_by_original_identity_without_provider_reads()
    {
        var archive = new Location(Schemes.Zip, "inner", Location.FileSystem("/original/bundle.zip"));
        using var f = new FileListFixture(new FileListFixture.ListProvider
        {
            Entries =
            [
                new EntryData("same.txt", EntryKind.File, 7) { Tag = new ResultTag(Location.FileSystem("/original/first"), "", EntryKind.File) },
                new EntryData("same.txt", EntryKind.File, 7) { Tag = new ResultTag(Location.FileSystem("/original/second"), "", EntryKind.File) },
                new EntryData("same.txt", EntryKind.File, 7) { Tag = new ResultTag(archive, "", EntryKind.File) },
                new EntryData("same.txt", EntryKind.File, 7) { Tag = new ResultTag(archive, "", EntryKind.File) { Ordinal = 1 } },
                new EntryData("same.txt", EntryKind.File, 7) { Tag = new ResultTag(new Location(Schemes.Sftp, "/shared", session: "profile-a"), "", EntryKind.File), Flags = EntryFlags.Offline },
                new EntryData("same.txt", EntryKind.File, 7) { Tag = new ResultTag(new Location(Schemes.Sftp, "/shared", session: "profile-b"), "", EntryKind.File), Flags = EntryFlags.Unavailable },
            ],
        });
        await f.Load();
        f.Tab.Listing.MarkAll(true);
        var peers = Peer(f.List).GetSelection();
        Assert.Equal(6, peers.Count);
        Assert.Equal(6, peers.Select(p => p.GetHelpText()).Distinct().Count());
        using var review = new MarkedItemsReview(f.Tab);
        Assert.Equal(6, review.CurrentItems.Select(i => i.Label).Distinct().Count());
        Assert.Contains(review.CurrentItems, i => i.Label.Contains("/original/first", StringComparison.Ordinal));
        Assert.Contains(review.CurrentItems, i => i.Label.Contains("/original/second", StringComparison.Ordinal));
        Assert.Contains(review.CurrentItems, i => i.Label.Contains("archive occurrence 2", StringComparison.Ordinal));
        Assert.Contains(review.CurrentItems, i => i.Details.Contains("profile-a", StringComparison.Ordinal) && i.Details.Contains("offline", StringComparison.Ordinal));
        Assert.Contains(review.CurrentItems, i => i.Details.Contains("profile-b", StringComparison.Ordinal) && i.Details.Contains("unavailable", StringComparison.Ordinal));
        Assert.Equal(0, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaFact]
    public async Task Million_item_selection_is_bounded_and_explicitly_routes_to_complete_review()
    {
        using var f = new FileListFixture(new FileListFixture.ListProvider { SyntheticCount = 1_000_000 });
        await f.Load();
        Assert.Equal(1_000_000, f.Tab.Listing.TotalCount);
        f.Tab.Listing.MarkAll(true);
        var peer = Peer(f.List);
        var children = peer.GetChildren();
        Assert.InRange(children.Count, 1, FileListAutomationPeer.VisiblePeerLimit + 4);
        Assert.InRange(peer.CachedItemCount, 1, FileListAutomationPeer.CachedPeerLimit);
        Assert.Equal(peer.CachedItemCount, f.List.AutomationOwnerCount);
        int cachedBefore = peer.CachedItemCount;
        var error = Assert.Throws<NotSupportedException>(() => peer.GetSelection());
        Assert.Contains("Review marked items", error.Message);
        Assert.Equal(cachedBefore, peer.CachedItemCount);
        using var review = new MarkedItemsReview(f.Tab);
        Assert.Equal(64, review.CurrentItems.Count);
        Assert.Contains(1_000_000.ToString("N0"), review.Summary);
        Assert.Equal(0, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaFact]
    public async Task Scrolling_many_virtual_rows_retires_old_actions_and_keeps_native_screen_owners_bounded()
    {
        using var f = new FileListFixture(new FileListFixture.ListProvider { SyntheticCount = 5_000 });
        await f.Load();
        var peer = Peer(f.List);
        // The focused first item stays discoverable while offscreen; use a nonfocused row
        // and visit enough distinct rows to actually exceed the fixed peer cache.
        var original = Row(peer, "item-0000001");
        int capacity = f.List.VisibleRowCapacity;
        int maximumTop = 5_000 - capacity;
        for (int top = 0; top < maximumTop && original.IsEnabled();
             top += Math.Max(1, capacity + 1))
        {
            peer.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, top * 100.0 / maximumTop);
            Dispatcher.UIThread.RunJobs();
            _ = peer.GetChildren();
            Assert.InRange(f.List.AutomationOwnerCount, 1, FileListAutomationPeer.CachedPeerLimit);
            Assert.Equal(peer.CachedItemCount, f.List.AutomationOwnerCount);
        }
        Assert.False(original.IsEnabled());
        Assert.True(original.IsOffscreen());
        Assert.Throws<InvalidOperationException>(() => ((IInvokeProvider)original).Invoke());
        Assert.Equal(0, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaFact]
    public async Task Registered_review_command_opens_an_accessible_complete_scope_and_escape_preserves_marks()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            await FileListFixture.Wait(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            vm.ActiveTab!.Listing.SetMark(1, true);
            window.ActiveList!.Focus();
            window.KeyPress(Key.M, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.M, "m");
            await FileListFixture.Wait(() => vm.Dialogs.IsOpen);
            window.UpdateLayout(); // Realize the modal's scrollable body before querying the review surface.
            Assert.Single(window.GetVisualDescendants().OfType<MarkedItemsReview>());
            Assert.Empty(AccessibilityTests.Unnamed(window));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await FileListFixture.Wait(() => !vm.Dialogs.IsOpen);
            Assert.Equal(1, vm.ActiveTab.Listing.MarkedCount);
            Assert.NotEmpty(services.Keymap.GetChords(CommandIds.ReviewMarks));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static FileListAutomationPeer Peer(FileListControl list) => Assert.IsType<FileListAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(list));
    private static AutomationPeer Row(FileListAutomationPeer list, string name) => list.GetChildren().Single(p => p.GetAutomationControlType() == AutomationControlType.ListItem && p.GetName() == name);
}
