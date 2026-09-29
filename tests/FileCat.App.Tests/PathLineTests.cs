using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Resources;

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
            Assert.True(box.Classes.Contains("active"));

            // "*.*" shows everything again.
            box.Focus();
            box.Text = "*.*";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 100 && listing.VisibleCount != 5; i++) await Task.Delay(20, ct);
            Assert.Equal(5, listing.VisibleCount);
            Assert.Null(tab.FilterText);
            Assert.False(box.Classes.Contains("active"));
            Assert.Equal(PanelView.AllItemsMask, box.Text);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
