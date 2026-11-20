using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class NavigationHistoryTests
{
    private static Location Folder(string root, string name) =>
        Location.FileSystem(Directory.CreateDirectory(Path.Combine(root, name)).FullName);

    private static async Task Until(Func<bool> predicate)
    {
        for (int i = 0; i < 250 && !predicate(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(predicate());
    }

    private static Task At(TabViewModel tab, Location where) =>
        Until(() => tab.Location == where && tab.Listing.State == ListingState.Complete);

    private static Button Button(MainWindow window, string id, string cssClass = "toolbar") =>
        window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, id) && b.Classes.Contains(cssClass));

    private static void Click(MainWindow window, Button button, MouseButton mouseButton)
    {
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        var point = Assert.IsType<Point>(center);
        window.MouseDown(point, mouseButton);
        window.MouseUp(point, mouseButton);
        Dispatcher.UIThread.RunJobs();
    }

    private static void KeyPress(MainWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuItem[] Rows(MainWindow window) =>
        Assert.IsType<MenuItem[]>(Assert.IsType<ContextMenu>(window.HistoryMenu).ItemsSource);

    [AvaloniaFact]
    public async Task A_straight_sequence_of_source_choices_keeps_every_visit_within_the_history_limit()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.HistorySize = 64;
            var panel = vm.Workspace.ActivePanel!;
            var first = Folder(root, "first");
            var tab = panel.OpenTab(first);
            services.Providers.Register(new HistoryProvider(Schemes.Mtp));
            services.Providers.Register(new HistoryProvider(Schemes.Network));
            var sources = new[]
            {
                Folder(root, "disk"),
                new Location(Schemes.Mtp, "Internal storage", session: "phone-one"),
                new Location(Schemes.Network, "server"),
            };
            var visited = new List<Location> { first };
            window.Activate();
            for (int i = 0; i < services.Settings.HistorySize + 3; i++)
            {
                var location = sources[i % sources.Length];
                await vm.OpenPlaceAsync(panel, new Place("Source", null, location, () => null), newTab: false);
                await At(tab, location);
                visited.Add(location);
                if (i is not (2 or 5 or 58 or 66)) continue;

                Click(window, Button(window, CommandIds.Back), MouseButton.Right);
                var expected = visited.Take(visited.Count - 1).TakeLast(services.Settings.HistorySize)
                    .Reverse().Select(services.Providers.Display);
                Assert.Equal(expected, Rows(window).Select(AutomationProperties.GetName));
                KeyPress(window, Key.Escape, PhysicalKey.Escape);
                Assert.False(tab.CanGoForward);
            }

            int limit = services.Settings.HistorySize;
            Assert.True(tab.JumpHistory(false, limit, tab.HistoryRevision));
            await At(tab, visited[^(limit + 1)]);
            Click(window, Button(window, CommandIds.Forward), MouseButton.Right);
            Assert.Equal(visited.TakeLast(limit).Select(services.Providers.Display), Rows(window).Select(AutomationProperties.GetName));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Straight_navigation_preserves_exact_provider_locations_in_the_trail()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HistoryProvider(Schemes.Mtp, compareCaseInsensitively: true);
            services.Providers.Register(provider);
            var first = new Location(Schemes.Mtp, "Photos", session: "phone-one");
            var differentlyCased = first.WithPath("photos");
            var otherSource = Folder(root, "disk");
            var tab = vm.Workspace.ActivePanel!.OpenTab(first);
            await At(tab, first);
            tab.Navigate(differentlyCased);
            await At(tab, differentlyCased);
            tab.Navigate(differentlyCased);
            tab.Navigate(otherSource);
            await At(tab, otherSource);
            Assert.Equal(new[] { first, differentlyCased }, tab.BackHistory);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Toolbar_right_click_and_keyboard_jump_in_both_directions(bool classic)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.ClassicToolbar = classic;
            vm.ApplySettings();
            var a = Folder(root, "same_parent_a");
            var b = Folder(root, "same_parent_b");
            var c = Folder(root, "C");
            var d = Folder(root, "D");
            var tab = vm.Workspace.ActivePanel!.OpenTab(a);
            tab.Navigate(b);
            tab.Navigate(c);
            tab.Navigate(d);
            await At(tab, d);
            window.Activate();
            // Listing completion can precede the toolbar's coalesced refresh.
            await Until(() => Button(window, CommandIds.Back).IsEnabled);
            var back = Button(window, CommandIds.Back);
            Click(window, back, MouseButton.Right);
            Assert.Equal(new[] { c.Path, b.Path, a.Path }, Rows(window).Select(AutomationProperties.GetName));
            Assert.All(Rows(window), row => Assert.NotNull(Assert.IsType<Image>(row.Icon).Source));
            Assert.Equal(d, tab.Location);
            KeyPress(window, Key.Down, PhysicalKey.ArrowDown);
            KeyPress(window, Key.Enter, PhysicalKey.Enter);
            await At(tab, b);
            Assert.Null(window.HistoryMenu);
            Assert.True(window.ActiveList!.IsFocused);
            Assert.Equal(new[] { c, d }, tab.HistoryLocations(forward: true));

            await Until(() => Button(window, CommandIds.Forward).IsEnabled &&
                Button(window, CommandIds.Forward, "historyDropdown").IsEnabled);
            var forward = Button(window, CommandIds.Forward);
            Assert.True(forward.Focus());
            KeyPress(window, Key.F10, PhysicalKey.F10, RawInputModifiers.Shift);
            Assert.Equal(new[] { c.Path, d.Path }, Rows(window).Select(AutomationProperties.GetName));
            KeyPress(window, Key.Escape, PhysicalKey.Escape);
            Assert.Null(window.HistoryMenu);
            Assert.True(forward.IsFocused);

            Click(window, Button(window, CommandIds.Forward, "historyDropdown"), MouseButton.Left);
            KeyPress(window, Key.Down, PhysicalKey.ArrowDown);
            KeyPress(window, Key.Enter, PhysicalKey.Enter);
            await At(tab, d);
            Assert.False(tab.CanGoForward);
            // Navigation commits before the coalesced toolbar refresh is dispatched.
            await Until(() => !Button(window, CommandIds.Forward, "historyDropdown").IsEnabled);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_long_history_can_be_scrolled_by_keyboard_and_the_popup_follows_the_active_tab()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var a = Folder(root, "A");
            var b = Folder(root, "B");
            var oldest = Folder(root, new string('x', 100) + "_oldest");
            var tab = vm.Workspace.ActivePanel!.OpenTab(oldest);
            for (int i = 0; i < 40; i++) tab.Navigate(i % 2 == 0 ? a : b);
            await At(tab, b);
            var button = Button(window, CommandIds.Back);
            window.Activate();
            Click(window, button, MouseButton.Right);
            var menu = window.HistoryMenu!;
            var rows = Rows(window);
            Assert.Equal(40, rows.Length);
            Assert.Equal(oldest.Path, AutomationProperties.GetName(rows[^1]));
            var scroll = menu.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            Assert.NotNull(scroll);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            for (int i = 1; i < rows.Length; i++) KeyPress(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.True(rows[^1].IsFocused);
            Assert.True(scroll.Offset.Y > 0);
            vm.Workspace.ActivePanel!.OpenTab(a);
            Assert.Null(window.HistoryMenu);
            Assert.Equal(b, tab.Location);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Removed_local_visit_uses_its_ancestor_and_locked_tab_opens_a_new_tab()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var parent = Folder(root, "parent");
            var removed = Folder(parent.Path, "removed");
            var here = Folder(root, "here");
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.OpenTab(removed);
            await At(tab, removed);
            tab.Navigate(here);
            await At(tab, here);
            Directory.Delete(removed.Path);
            tab.GoBack();
            await At(tab, parent);
            Assert.Contains("nearest folder", tab.Banner);
            Assert.Equal(here, Assert.Single(tab.ForwardHistory));
            tab.IsLocked = true;
            tab.GoForward();
            Assert.NotSame(tab, panel.ActiveTab);
            await At(panel.ActiveTab!, here);
            Assert.Equal(parent, tab.Location);
            Assert.Equal(here, Assert.Single(tab.ForwardHistory));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Repeated_visits_are_positional_and_a_new_branch_clears_only_forward()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var a = Folder(root, "A");
            var b = Folder(root, "B");
            var c = Folder(root, "C");
            var tab = vm.Workspace.ActivePanel!.OpenTab(a);
            tab.Navigate(b);
            tab.Navigate(a);
            tab.Navigate(c);
            await At(tab, c);
            Assert.Equal(new[] { a, b, a }, tab.HistoryLocations(forward: false));
            long revision = tab.HistoryRevision;
            Assert.True(tab.JumpHistory(false, 3, revision));
            await At(tab, a);
            Assert.Equal(new[] { b, a, c }, tab.HistoryLocations(forward: true));
            Assert.False(tab.JumpHistory(true, 1, revision));
            tab.Navigate(Folder(root, "branch"));
            Assert.False(tab.CanGoForward);
            Assert.Equal(a, Assert.Single(tab.BackHistory));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class HistoryProvider(string scheme = "history-source", bool compareCaseInsensitively = false)
        : FileCat.Core.Resources.ResourceProvider
    {
        public override string Scheme => scheme;
        public override bool IsSameLocation(Location a, Location b) =>
            compareCaseInsensitively && a.Scheme == b.Scheme && a.Session == b.Session &&
            a.Path.Equals(b.Path, StringComparison.OrdinalIgnoreCase) || base.IsSameLocation(a, b);
        public bool Offline { get; set; }
        public TaskCompletionSource<Location>? Preparation { get; set; }
        public CancellationToken PreparationToken { get; private set; }
        public int Reads { get; private set; }
        public override string GetDisplayPath(Location location) => "Source › " + location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Task<Location>? PrepareAsync(Location location, CancellationToken ct)
        {
            PreparationToken = ct;
            return Preparation?.Task;
        }
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            Reads++;
            return Offline ? Task.FromException(new IOException("Source is offline.")) : Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task An_offline_source_can_be_retried_or_crossed_between_providers()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HistoryProvider();
            services.Providers.Register(provider);
            var local = Folder(root, "local");
            var archivePath = Path.Combine(root, "visited.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create))
                archive.CreateEntry("archive.txt");
            var archiveLocation = new Location(Schemes.Zip, string.Empty, Location.FileSystem(archivePath));
            var offline = new Location(provider.Scheme, "mount");
            var computer = new Location(Schemes.Computer, string.Empty);
            var tab = vm.Workspace.ActivePanel!.OpenTab(local);
            tab.Navigate(offline);
            await At(tab, offline);
            tab.Navigate(archiveLocation);
            await At(tab, archiveLocation);
            tab.Navigate(computer);
            await At(tab, computer);
            tab.GoBack();
            await At(tab, archiveLocation);

            provider.Offline = true;
            long revision = tab.HistoryRevision;
            tab.GoBack();
            await Until(() => tab.Banner?.Contains("Source is offline.", StringComparison.Ordinal) == true);
            Assert.Equal(archiveLocation, tab.Location);
            Assert.Equal(revision, tab.HistoryRevision);
            Assert.Equal(new[] { offline, local }, tab.HistoryLocations(forward: false));
            Assert.Equal(computer, Assert.Single(tab.ForwardHistory));

            int reads = provider.Reads;
            Assert.True(tab.JumpHistory(false, 2, tab.HistoryRevision));
            await At(tab, local);
            Assert.Equal(new[] { offline, archiveLocation, computer }, tab.HistoryLocations(forward: true));
            Assert.True(tab.JumpHistory(true, 3, tab.HistoryRevision));
            await At(tab, computer);
            Assert.Equal(reads, provider.Reads);
            Assert.Contains(offline, tab.BackHistory);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Slow_history_preparation_yields_to_new_input_and_keeps_the_current_visit_until_admitted()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HistoryProvider();
            services.Providers.Register(provider);
            var remembered = new Location(provider.Scheme, "remembered");
            var here = Folder(root, "here");
            var after = Folder(root, "after");
            var tab = vm.Workspace.ActivePanel!.OpenTab(remembered);
            await At(tab, remembered);
            tab.Navigate(here);
            await At(tab, here);
            provider.Preparation = new(TaskCreationOptions.RunContinuationsAsynchronously);

            tab.GoBack();
            Assert.Equal(here, tab.Location);
            Assert.Equal(remembered, Assert.Single(tab.BackHistory));
            tab.Navigate(after);
            Assert.True(provider.PreparationToken.IsCancellationRequested);
            provider.Preparation.SetResult(remembered);
            await At(tab, after);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { remembered, here }, tab.BackHistory);
            Assert.False(tab.CanGoForward);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task History_restores_the_cursor_and_top_item_after_sort_changes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var a = Folder(root, "scroll");
            var b = Folder(root, "destination");
            for (int i = 0; i < 200; i++) File.WriteAllText(Path.Combine(a.Path, $"row-{i:000}.txt"), "");
            var tab = vm.Workspace.ActivePanel!.OpenTab(a);
            await At(tab, a);
            var list = window.ActiveList!;
            Assert.True(tab.Listing.FocusName("row-100.txt"));
            list.ScrollToTopRow(90);
            string top = tab.Listing.GetVisible(list.TopRow).Name;
            tab.Navigate(b);
            await At(tab, b);
            tab.Listing.Sort = tab.Listing.Sort with { Descending = true };
            tab.GoBack();
            await At(tab, a);
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal("row-100.txt", focused.Name);
            Assert.Equal(top, tab.Listing.GetVisible(list.TopRow).Name);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
