using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.Core.Commands;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// The panel keys as the product owner asked for them (D-49): Space marks (sizing a folder) and moves on, so holding it
/// goes through the folder; in the location menu, a drive letter opens that drive at once, and the drive another panel
/// is on opens at that panel's folder.
/// </summary>
public sealed class PanelKeysTests
{
    private static int Index(Core.Listing.ListingModel listing, string name)
    {
        for (int i = 0; i < listing.VisibleCount; i++)
            if (listing.GetVisible(i).Name == name) return i;
        return -1;
    }

    [AvaloniaFact]
    public async Task Space_marks_and_moves_on_so_holding_it_marks_and_sizes_everything()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllText(Path.Combine(folder, "sub", "inside.bin"), new string('x', 5000));
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 4); i++) await Task.Delay(20, ct);
            listing.SetFocus(0); // ".." first: Space passes over it
            vm.View.FocusActivePanel();
            // Held down, the key repeats: each press marks the item under the cursor and moves on, to the last one.
            for (int i = 0; i < 6; i++) window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(3, listing.MarkedCount);
            Assert.Equal(listing.VisibleCount - 1, listing.FocusedIndex);
            // The folder among them was sized (by a low-priority job: a busy machine gets it done later).
            for (int i = 0; i < 1000 && listing.GetVisible(Index(listing, "sub")).Size != 5000; i++) await Task.Delay(20, ct);
            Assert.Equal(5000, listing.GetVisible(Index(listing, "sub")).Size);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Typing_in_a_panel_goes_to_the_first_name_that_starts_so()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            foreach (var name in new[] { "beta.txt", "bravo.txt", "charlie.txt" }) File.WriteAllText(Path.Combine(folder, name), name);
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 6); i++) await Task.Delay(20, ct);
            listing.SetFocus(0);
            vm.View.FocusActivePanel();
            await Task.Delay(50, ct);
            // Each key press, then the text it types, as the keyboard delivers them. On Windows the text follows only when
            // nothing handled the key press, so a handled letter key would type nothing in the real app.
            bool keyHandled = false;
            window.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (_, e) => keyHandled |= e.Handled, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            void Type(Key key, PhysicalKey physical, string text)
            {
                window.KeyPress(key, RawInputModifiers.None, physical, text);
                if (!keyHandled) window.KeyTextInput(text);
                window.KeyRelease(key, RawInputModifiers.None, physical, text);
            }
            string Focused() => listing.GetVisible(listing.FocusedIndex).Name;

            Type(Key.B, PhysicalKey.B, "b");
            Assert.Equal(("b", "b.txt"), (tab.QuickSearch, Focused()));
            // Further letters narrow the search (they used to end it and start again from the new letter).
            Type(Key.R, PhysicalKey.R, "r");
            Assert.Equal(("br", "bravo.txt"), (tab.QuickSearch, Focused()));
            Assert.False(keyHandled, "A letter's key press was handled, so Windows would not type it.");
            // A letter that matches nothing is refused and said; the search and the item found stay.
            Type(Key.X, PhysicalKey.X, "x");
            Assert.Equal(("br", true, "bravo.txt"), (tab.QuickSearch, tab.QuickSearchNoMatch, Focused()));
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Equal("b", tab.QuickSearch);

            // The search shows in the status line, not over the list, where it would hide the item it found.
            await Task.Delay(50, ct);
            var box = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Border>().First(x => x.Classes.Contains("quicksearch") && x.IsEffectivelyVisible);
            var list = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Controls.FileListControl>().First(x => x.IsEffectivelyVisible);
            var boxTop = Avalonia.VisualExtensions.TranslatePoint(box, new Avalonia.Point(0, 0), window)!.Value.Y;
            var listBottom = Avalonia.VisualExtensions.TranslatePoint(list, new Avalonia.Point(0, list.Bounds.Height), window)!.Value.Y;
            Assert.True(boxTop >= listBottom - 1, $"The quick search box (top {boxTop}) covers the list (bottom {listBottom}).");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Null(tab.QuickSearch);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task The_mouse_back_and_forward_buttons_go_through_the_panels_history()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            string sub = Directory.CreateDirectory(Path.Combine(folder, "sub")).FullName;
            // The right panel goes into a folder; the buttons act on the panel the mouse is over, and make it active.
            var right = vm.Workspace.Panels[1];
            var tab = right.ActiveTab!;
            tab.Navigate(Location.FileSystem(sub));
            for (int i = 0; i < 250 && tab.Location?.Path != sub; i++) await Task.Delay(20, ct);
            vm.Workspace.Activate(vm.Workspace.Panels[0]);
            var views = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Views.PanelView>().ToList();
            var view = views.First(v => ReferenceEquals(v.DataContext, right));
            var middle = Avalonia.VisualExtensions.TranslatePoint(view, new Avalonia.Point(view.Bounds.Width / 2, view.Bounds.Height / 2), window)!.Value;

            window.MouseDown(middle, MouseButton.XButton1);
            window.MouseUp(middle, MouseButton.XButton1);
            for (int i = 0; i < 250 && tab.Location?.Path != folder; i++) await Task.Delay(20, ct);
            Assert.Equal(folder, tab.Location!.Path);
            Assert.Same(right, vm.Workspace.ActivePanel);

            window.MouseDown(middle, MouseButton.XButton2);
            window.MouseUp(middle, MouseButton.XButton2);
            for (int i = 0; i < 250 && tab.Location?.Path != sub; i++) await Task.Delay(20, ct);
            Assert.Equal(sub, tab.Location!.Path);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task The_toolbar_runs_commands_names_their_keys_shows_switches_and_can_be_hidden()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await Task.Delay(50, ct);
            var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>()
                .Where(b => b.Classes.Contains("toolbar")).ToList();
            Assert.True(buttons.Count >= 30, $"{buttons.Count} toolbar buttons");
            // Each shows its command's icon, is named for screen readers, and says its key in its tooltip.
            Assert.All(buttons, b => Assert.IsType<Avalonia.Controls.Image>(b.Content));
            Assert.All(buttons, b => Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(b))));
            foreach (var b in buttons) TestContext.Current.TestOutputHelper?.WriteLine($"{b.Tag}: {Avalonia.Controls.ToolTip.GetTip(b)}");
            // Every tooltip starts with what the button does; one with a key names it.
            Assert.All(buttons, b => Assert.StartsWith(Avalonia.Automation.AutomationProperties.GetName(b)!, Avalonia.Controls.ToolTip.GetTip(b)?.ToString() ?? ""));
            var copy = buttons.Single(b => (string?)b.Tag == CommandIds.Copy);
            Assert.Contains("(F5)", Avalonia.Controls.ToolTip.GetTip(copy)?.ToString());
            // Screen readers get the tooltip's words.
            Assert.Equal(Avalonia.Controls.ToolTip.GetTip(copy)?.ToString(), Avalonia.Automation.AutomationProperties.GetHelpText(copy));

            // A switch: a click flips it, and so does its key, and the button shows the state either way.
            var hidden = buttons.Single(b => (string?)b.Tag == CommandIds.ToggleHidden);
            bool shown = services.Settings.ShowHidden;
            Assert.Equal(shown, hidden.Classes.Contains("checked"));
            hidden.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Task.Delay(50, ct);
            Assert.Equal((!shown, !shown), (services.Settings.ShowHidden, hidden.Classes.Contains("checked")));
            vm.Execute(CommandIds.ToggleHidden);
            await Task.Delay(50, ct);
            Assert.Equal((shown, shown), (services.Settings.ShowHidden, hidden.Classes.Contains("checked")));

            // The menus carry the same icons.
            var menu = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Menu>().First(m => m.Name == "MainMenu");
            var file = Assert.IsType<Avalonia.Controls.MenuItem>(menu.Items[0]);
            var view = Assert.IsType<Avalonia.Controls.MenuItem>(file.Items[0]);
            Assert.IsType<Avalonia.Controls.Image>(view.Icon);
            // Every command in the menus, submenus included, has one.
            IEnumerable<Avalonia.Controls.MenuItem> All(Avalonia.Controls.MenuItem item) =>
                item.Items.OfType<Avalonia.Controls.MenuItem>().SelectMany(child => All(child).Prepend(child));
            var iconless = menu.Items.OfType<Avalonia.Controls.MenuItem>().SelectMany(All).Where(m => m.Icon is null).Select(m => m.Header).ToList();
            Assert.True(iconless.Count == 0, "No icon: " + string.Join(", ", iconless));

            // View → Show the toolbar hides it, and the setting keeps that.
            vm.Execute(CommandIds.ToggleToolbar);
            await Task.Delay(50, ct);
            Assert.False(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Border>().First(x => x.Name == "ToolbarRow").IsVisible);
            Assert.False(services.Settings.ShowToolbar);
            vm.Execute(CommandIds.ToggleToolbar);
            Assert.True(services.Settings.ShowToolbar);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Alt_F1_changes_the_source_and_Alt_F2_the_target_while_the_keyboard_stays()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var panels = vm.Workspace.Panels;
            var dialogs = (Views.OverlayDialogService)vm.Dialogs;
            string? Menu() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBox>()
                .Select(Avalonia.Automation.AutomationProperties.GetName).FirstOrDefault(n => n?.StartsWith("Filter Location", StringComparison.Ordinal) == true);
            // The right panel is the source: Alt+F1 is its menu, whichever side it is on.
            vm.Workspace.Activate(panels[1]);
            ((Views.MainWindow)window).FocusActivePanel();
            window.KeyPress(Key.F1, RawInputModifiers.Alt, PhysicalKey.F1, null);
            for (int i = 0; i < 250 && Menu() is null; i++) await Task.Delay(20, ct);
            Assert.Equal("Filter Location for panel 2 (source)", Menu());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);

            // Alt+F2: the target's menu. What is chosen there opens in the target, and the source stays the source.
            window.KeyPress(Key.F2, RawInputModifiers.Alt, PhysicalKey.F2, null);
            for (int i = 0; i < 250 && Menu() is null; i++) await Task.Delay(20, ct);
            Assert.Equal("Filter Location for panel 1 (target)", Menu());
            await Task.Delay(50, ct);
            // "This PC" ("Computer" elsewhere), by a part that no drive letter begins.
            // Typed as a keyboard types: the key goes down before its text arrives.
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyTextInput(OperatingSystem.IsWindows() ? " PC" : "Computer");
            await Task.Delay(100, ct);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && panels[0].ActiveTab!.Location?.Scheme != Schemes.Computer; i++) await Task.Delay(20, ct);
            Assert.Equal(Schemes.Computer, panels[0].ActiveTab!.Location!.Scheme);
            Assert.Same(panels[1], vm.Workspace.ActivePanel);
            Assert.NotEqual(Schemes.Computer, panels[1].ActiveTab!.Location!.Scheme);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_drive_letter_opens_the_drive_at_once_at_the_folder_another_panel_shows_there()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Drive letters are Windows'.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            var panels = vm.Workspace.Panels;
            // The right panel goes elsewhere; the left (active) one stays in the folder.
            panels[1].ActiveTab!.Navigate(new Location(Schemes.Computer, string.Empty));
            vm.Workspace.Activate(panels[0]);
            var dialogs = (Views.OverlayDialogService)vm.Dialogs;
            vm.Execute(CommandIds.LocationMenuTarget);
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(50, ct);
            // One key, no Enter: the drive the left panel is on, at the left panel's folder.
            char drive = char.ToLowerInvariant(Path.GetPathRoot(folder)![0]);
            window.KeyTextInput(drive.ToString());
            for (int i = 0; i < 250 && (dialogs.IsOpen || panels[1].ActiveTab!.Location?.Path != folder); i++) await Task.Delay(20, ct);
            Assert.False(dialogs.IsOpen);
            Assert.Equal(folder, panels[1].ActiveTab!.Location!.Path);

            // Typed after other text, the same letter only filters.
            vm.Execute(CommandIds.LocationMenuTarget);
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(50, ct);
            window.KeyTextInput("x" + drive);
            await Task.Delay(100, ct);
            Assert.True(dialogs.IsOpen);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
    [AvaloniaFact]
    public async Task Alt_F1_then_a_drive_letter_opens_that_drive_as_the_keyboard_sends_them()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Drive letters are Windows'.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var panels = vm.Workspace.Panels;
            panels[0].ActiveTab!.Navigate(new Location(Schemes.Computer, string.Empty));
            vm.Workspace.Activate(panels[0]);
            vm.View.FocusActivePanel();
            await Task.Delay(100, ct);
            var dialogs = (Views.OverlayDialogService)vm.Dialogs;
            // As the keyboard sends them: Alt down, F1 with Alt, the menu opens while Alt is held, F1 and Alt up.
            window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            window.KeyPress(Key.F1, RawInputModifiers.Alt, PhysicalKey.F1, null);
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(100, ct);
            window.KeyRelease(Key.F1, RawInputModifiers.Alt, PhysicalKey.F1, null);
            window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            await Task.Delay(100, ct);
            string focused = window.FocusManager?.GetFocusedElement()?.GetType().Name ?? "(nothing)";
            // Then the drive's letter, key and text: the text follows only when nothing handled the key (as Windows does).
            char drive = char.ToUpperInvariant(Path.GetPathRoot(root)![0]);
            bool keyHandled = false;
            void Watch(object? _, KeyEventArgs e) => keyHandled |= e.Handled;
            window.AddHandler(InputElement.KeyDownEvent, Watch, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            window.KeyPress(Enum.Parse<Key>(drive.ToString()), RawInputModifiers.None, Enum.Parse<PhysicalKey>(drive.ToString()), drive.ToString().ToLowerInvariant());
            if (!keyHandled) window.KeyTextInput(drive.ToString().ToLowerInvariant());
            window.KeyRelease(Enum.Parse<Key>(drive.ToString()), RawInputModifiers.None, Enum.Parse<PhysicalKey>(drive.ToString()), null);
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.False(dialogs.IsOpen, $"The letter did not open its drive; the focus was on {focused}, the key {(keyHandled ? "was" : "was not")} handled.");
            Assert.StartsWith(drive + ":", panels[0].ActiveTab!.Location!.Path, StringComparison.OrdinalIgnoreCase);

            // Alt pressed and released alone still opens the menu bar, as in any Windows program.
            vm.View.FocusActivePanel();
            await Task.Delay(100, ct);
            window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            await Task.Delay(100, ct);
            Assert.IsType<Avalonia.Controls.MenuItem>(window.FocusManager?.GetFocusedElement());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
