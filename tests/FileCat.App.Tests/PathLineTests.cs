using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// The panel's path as links (after Salamander's directory line): each folder up the path goes there on a click, Ctrl+click
/// copies the path up to it, a middle click opens it in a new tab, and a click on the last part edits the path.
/// </summary>
public sealed class PathLineTests
{
    [AvaloniaFact]
    public async Task Each_folder_up_the_path_is_a_link_that_goes_there_copies_or_opens_a_tab()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string files = Path.Combine(root, "files");
            string deeper = Directory.CreateDirectory(Path.Combine(files, "sub", "deeper")).FullName;
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            tab.Navigate(Location.FileSystem(deeper));
            for (int i = 0; i < 250 && tab.Location?.Path != deeper; i++) await Task.Delay(20, ct);
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            var links = view.PathLinksControl;
            for (int i = 0; i < 100 && links.Text != tab.DisplayPath; i++) await Task.Delay(20, ct);

            // Every folder from the root down is a part: the root first, the folder shown last.
            var paths = links.Segments.Select(s => s.Target.Path.TrimEnd('\\', '/')).ToList();
            Assert.Equal(deeper, paths[^1]);
            Assert.Equal(Path.Combine(files, "sub"), paths[^2]);
            Assert.Equal(files, paths[^3]);
            Assert.Equal(Path.GetPathRoot(deeper)!.TrimEnd('\\', '/'), paths[0]);
            Assert.Equal(links.Text.Length, links.Segments[^1].End);
            Assert.Equal("sub", links.NameOf(links.Segments.Count - 2));

            Point At(int segment) => links.TranslatePoint(links.PointOf(segment)!.Value, window)!.Value;
            int filesPart = links.Segments.Count - 3;

            // Ctrl+click copies the path up to that folder.
            window.MouseDown(At(filesPart), MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(At(filesPart), MouseButton.Left, RawInputModifiers.Control);
            for (int i = 0; i < 100 && vm.Notification is null; i++) await Task.Delay(20, ct);
            Assert.Equal(files, await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            Assert.Equal(deeper, tab.Location!.Path);

            // A middle click opens it in a new tab; the panel's own tab stays.
            int tabs = panel.Tabs.Count;
            window.MouseDown(At(filesPart), MouseButton.Middle);
            window.MouseUp(At(filesPart), MouseButton.Middle);
            Assert.Equal(tabs + 1, panel.Tabs.Count);
            Assert.Equal(files, panel.Tabs[^1].Location!.Path);
            panel.CloseTab(panel.Tabs[^1]);
            panel.ActiveTab = tab;
            for (int i = 0; i < 100 && links.Text != tab.DisplayPath; i++) await Task.Delay(20, ct);

            // A click goes there, with the folder it came from under the cursor. (After a pause: a second click at once
            // would be a double click, which edits the path.)
            await Task.Delay(800, ct);
            window.MouseDown(At(filesPart), MouseButton.Left);
            window.MouseUp(At(filesPart), MouseButton.Left);
            for (int i = 0; i < 250 && !(tab.Location?.Path == files && tab.Listing.State == Core.Listing.ListingState.Complete); i++) await Task.Delay(20, ct);
            Assert.Equal(files, tab.Location!.Path);
            for (int i = 0; i < 100 && tab.Listing.GetVisible(tab.Listing.FocusedIndex).Name != "sub"; i++) await Task.Delay(20, ct);
            Assert.Equal("sub", tab.Listing.GetVisible(tab.Listing.FocusedIndex).Name);

            // A click on the last part edits the path: the box takes the keys, and the links step aside.
            for (int i = 0; i < 100 && links.Text != tab.DisplayPath; i++) await Task.Delay(20, ct);
            await Task.Delay(800, ct);
            window.MouseDown(At(links.Segments.Count - 1), MouseButton.Left);
            window.MouseUp(At(links.Segments.Count - 1), MouseButton.Left);
            await Task.Delay(50, ct);
            var box = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().First(b => b.Name == "PathBox");
            Assert.True(box.IsFocused);
            Assert.False(links.IsVisible);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task The_filter_box_beside_the_path_shows_only_matching_items_on_Enter()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string files = Path.Combine(root, "files");
            File.WriteAllText(Path.Combine(files, "notes.log"), "log");
            Directory.CreateDirectory(Path.Combine(files, "folder"));
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 5); i++) await Task.Delay(20, ct);
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            var box = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().First(b => b.Name == "FilterBox");
            // Everything shows: the box says so the way the platform writes it.
            Assert.Equal(PanelView.AllItemsMask, box.Text);

            box.Focus();
            box.Text = "*.txt";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 100 && listing.VisibleCount != 4; i++) await Task.Delay(20, ct);
            // The text files, the folder (folders always show), and "..".
            Assert.Equal(["..", "folder", "a.txt", "b.txt"], Enumerable.Range(0, listing.VisibleCount).Select(i => listing.GetVisible(i).Name));
            Assert.Equal("*.txt", tab.FilterText);
            Assert.Contains("active", box.Classes);

            // "*.*" shows everything again.
            box.Focus();
            box.Text = "*.*";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 100 && listing.VisibleCount != 5; i++) await Task.Delay(20, ct);
            Assert.Equal(5, listing.VisibleCount);
            Assert.Null(tab.FilterText);
            Assert.DoesNotContain("active", box.Classes);
            Assert.Equal(PanelView.AllItemsMask, box.Text);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task The_place_buttons_offer_everything_the_location_menu_does()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            for (int i = 0; i < 250 && tab.Location?.Path != Path.Combine(root, "files"); i++) await Task.Delay(20, ct);
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            List<Avalonia.Controls.Button> Buttons() => view.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Where(b => b.Tag is Place).ToList();
            Place PlaceOf(Avalonia.Controls.Button b) => (Place)b.Tag!;
            for (int i = 0; i < 250 && !Buttons().Any(b => PlaceOf(b).Drive is not null); i++) await Task.Delay(20, ct);
            var buttons = Buttons();
            // Everything the location menu (Alt+F1, Alt+F2) lists, in its order: the drives, This PC, working sets, the
            // home and special folders, and a new connection; each named for screen readers, its details in its tooltip.
            Assert.Equal(vm.Places(vm.DriveButtons).Select(p => p.Title), buttons.Select(b => PlaceOf(b).Title));
            Assert.Contains(buttons, b => PlaceOf(b).Location?.Scheme == Core.Resources.Schemes.Computer);
            Assert.Contains(buttons, b => PlaceOf(b).Title == "Working sets");
            Assert.Contains(buttons, b => PlaceOf(b).Title == "Home");
            Assert.True(PlaceOf(buttons[^1]).Connects);
            Assert.All(buttons, b => Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(b))));
            // Groups are set apart.
            Assert.Contains(view.GetVisualDescendants().OfType<Avalonia.Controls.Border>(), b => b.Classes.Contains("placeGap"));

            // A bookmark gets its button, named, as soon as it is saved; it is outlined while the panel is there, as is the drive.
            services.History.Bookmarks.Add(new BookmarkEntry { Name = "Project files", Location = Location.FileSystem(Path.Combine(root, "files")) });
            services.SaveHistory();
            for (int i = 0; i < 250 && !Buttons().Any(b => PlaceOf(b).BarLabel == "Project files"); i++) await Task.Delay(20, ct);
            buttons = Buttons();
            var bookmark = Assert.Single(buttons, b => PlaceOf(b).BarLabel == "Project files");
            Assert.Contains("current", bookmark.Classes);
            var drive = Assert.Single(buttons, b => PlaceOf(b).Drive is not null && b.Classes.Contains("current"));
            Assert.True(root.StartsWith(PlaceOf(drive).Location!.Path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) || PlaceOf(drive).Location!.Path == "/");

            // This PC with one click; its button is then the outlined one.
            var thisPc = buttons.First(b => PlaceOf(b).Location?.Scheme == Core.Resources.Schemes.Computer);
            thisPc.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            for (int i = 0; i < 250 && tab.Location?.Scheme != Core.Resources.Schemes.Computer; i++) await Task.Delay(20, ct);
            Assert.Equal(Core.Resources.Schemes.Computer, tab.Location!.Scheme);
            Assert.Contains("current", thisPc.Classes);
            Assert.DoesNotContain("current", bookmark.Classes);

            // The drive the other panel is on opens at that panel's folder, as in the location menu.
            drive.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            for (int i = 0; i < 250 && tab.Location?.Scheme != Core.Resources.Schemes.FileSystem; i++) await Task.Delay(20, ct);
            Assert.Equal(vm.Workspace.Panels[1].ActiveTab!.Location!.Path, tab.Location!.Path);

            // A button on the other panel makes that panel the source.
            vm.Workspace.Activate(panel);
            var other = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, vm.Workspace.Panels[1]));
            var otherHome = other.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(b => b.Tag is Place { Title: "Home" });
            otherHome.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Same(vm.Workspace.Panels[1], vm.Workspace.ActivePanel);

            // A narrow panel: the row stays one line, and the places it has no room for are on the » button at its end.
            var row = view.GetVisualDescendants().OfType<FileCat.App.Controls.PlaceBarPanel>().Single();
            double rowHeight = row.Bounds.Height;
            window.Width = 520;
            for (int i = 0; i < 100 && row.ShownCount >= row.Children.Count - 1; i++) await Task.Delay(20, ct);
            Assert.True(row.ShownCount < row.Children.Count - 1);
            Assert.NotEmpty(row.Hidden.Select(c => c.Tag).OfType<Place>());
            Assert.Equal(rowHeight, row.Bounds.Height);
            var moreButton = row.Children[^1];
            Assert.Equal("More places", Avalonia.Automation.AutomationProperties.GetName(moreButton));
            Assert.True(moreButton.Bounds.Right <= row.Bounds.Width, $"{moreButton.Bounds} in {row.Bounds}");
            window.Width = 1200;

            // View → Hide the place buttons.
            vm.Execute(Core.Commands.CommandIds.ToggleDriveButtons);
            Assert.False(vm.ShowDriveButtons);
            var bar = view.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First(b => b.Name == "DriveBar");
            Assert.False(bar.IsVisible);
            vm.Execute(Core.Commands.CommandIds.ToggleDriveButtons);
            Assert.True(bar.IsVisible);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_press_anywhere_in_a_panel_makes_it_the_source_and_the_keyboard_follows()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var panels = vm.Workspace.Panels;
            vm.Workspace.Activate(panels[0]);
            ((MainWindow)window).FocusActivePanel();
            var right = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panels[1]));
            var left = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panels[0]));
            Assert.True(left.List.IsFocused);
            // The right panel's number takes no keyboard itself: a press there still makes the panel the source, its number
            // lit and the left one the target, and the keyboard moves there.
            var badge = right.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First(b => b.Name == "NumberBadge");
            var point = badge.TranslatePoint(new Avalonia.Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), window)!.Value;
            window.CaptureRenderedFrame();
            window.MouseDown(point, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            window.MouseUp(point, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            for (int i = 0; i < 100 && !right.List.IsFocused; i++) await Task.Delay(20, ct);
            Assert.Same(panels[1], vm.Workspace.ActivePanel);
            Assert.True(panels[1].IsActive);
            Assert.True(panels[0].IsTarget);
            Assert.Equal("TARGET", panels[0].RoleLabel);
            Assert.True(right.List.IsFocused);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// A folder's name is drawn escaped, as the file list shows names (§18.3, V23 B14): a right-to-left override in it
    /// cannot turn the path around. Its part still goes to the folder itself, whose real name the path keeps.
    /// </summary>
    [AvaloniaFact]
    public async Task A_folders_name_is_drawn_escaped_and_its_part_still_goes_there()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string name = "docs" + (char)0x202E + "fdp.exe";
            string folder = Directory.CreateDirectory(Path.Combine(root, "files", name)).FullName;
            string inner = Directory.CreateDirectory(Path.Combine(folder, "inner")).FullName;
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            tab.Navigate(Location.FileSystem(inner));
            for (int i = 0; i < 250 && tab.Location?.Path != inner; i++) await Task.Delay(20, ct);
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            var links = view.PathLinksControl;
            for (int i = 0; i < 100 && links.Text != tab.DisplayPath; i++) await Task.Delay(20, ct);

            Assert.Contains("docs" + (char)92 + "u202Efdp.exe", links.ShownText);
            // Ordinal: a culture's comparison ignores format characters such as this one, and would always find it.
            Assert.False(links.ShownText.Contains((char)0x202E));
            int part = links.Segments.Count - 2;
            Assert.Equal(name, links.NameOf(part));
            var at = links.TranslatePoint(links.PointOf(part)!.Value, window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            for (int i = 0; i < 250 && tab.Location?.Path != folder; i++) await Task.Delay(20, ct);
            Assert.Equal(folder, tab.Location!.Path);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
