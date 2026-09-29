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
            // The folder among them was sized.
            for (int i = 0; i < 250 && listing.GetVisible(Index(listing, "sub")).Size != 5000; i++) await Task.Delay(20, ct);
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
            Assert.All(buttons, b => Assert.StartsWith(Avalonia.Automation.AutomationProperties.GetName(b)!, Avalonia.Controls.ToolTip.GetTip(b) as string ?? ""));
            var copy = buttons.Single(b => (string?)b.Tag == CommandIds.Copy);
            Assert.Contains("(F5)", Avalonia.Controls.ToolTip.GetTip(copy) as string);

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
            vm.Execute(CommandIds.LocationMenuRight);
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(50, ct);
            // One key, no Enter: the drive the left panel is on, at the left panel's folder.
            char drive = char.ToLowerInvariant(Path.GetPathRoot(folder)![0]);
            window.KeyTextInput(drive.ToString());
            for (int i = 0; i < 250 && (dialogs.IsOpen || panels[1].ActiveTab!.Location?.Path != folder); i++) await Task.Delay(20, ct);
            Assert.False(dialogs.IsOpen);
            Assert.Equal(folder, panels[1].ActiveTab!.Location!.Path);

            // Typed after other text, the same letter only filters.
            vm.Execute(CommandIds.LocationMenuRight);
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
}
