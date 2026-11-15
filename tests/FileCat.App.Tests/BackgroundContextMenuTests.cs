using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class BackgroundContextMenuTests
{
    private static readonly FieldInfo OpenMenu = typeof(PanelView).GetField("_openItemMenu",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    [AvaloniaFact]
    public async Task Parent_row_menu_offers_navigation_without_file_mutations()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;
            var child = Location.FileSystem(Path.Combine(root, "files"));
            tab.Navigate(child);
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount > 1);
            tab.Listing.SetFocus(0);
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(v => ReferenceEquals(v.DataContext, panel));
            view.ShowContextMenu(atFocus: true);
            var menu = Assert.IsType<ContextMenu>(OpenMenu.GetValue(null));
            var actions = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>().ToArray();
            Assert.Equal(new[] { "Go to parent", "Open parent in new tab", "Copy parent path" },
                actions.Select(a => a.Header as string));
            Assert.DoesNotContain(actions, a => a.Header as string is "Delete…" or "Cut items to clipboard");
            actions[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => tab.Location == Location.FileSystem(root));
            Assert.Equal(Location.FileSystem(root), tab.Location);
            Assert.True(File.Exists(Path.Combine(root, "files", "a.txt")));
            menu.Close();
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Keyboard_item_context_keeps_the_marked_virtual_item_instead_of_opening_location_actions()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            tab.Navigate(new Location(Schemes.Computer, string.Empty));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount > 0);
            var listing = tab.Listing;
            listing.SetFocus(0);
            listing.ToggleMark(0);
            Assert.Single(listing.GetSelection());
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(panel => ReferenceEquals(panel.DataContext, vm.Workspace.ActivePanel));
            view.FindControl<FileListControl>("List")!.Focus();

            window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
            var menu = Assert.IsType<ContextMenu>(OpenMenu.GetValue(null));
            var titles = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>()
                .Select(item => item.Header as string).ToArray();
            Assert.Contains("Open", titles);
            Assert.DoesNotContain("Copy location name", titles);
            Assert.DoesNotContain("Create folder…", titles);
            Assert.Equal(0, listing.FocusedIndex);
            Assert.Equal(1, listing.MarkedCount);
            menu.Close();
            Assert.Equal(0, listing.FocusedIndex);
            Assert.Equal(1, listing.MarkedCount);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Failed_location_disables_background_writes_and_explains_reread()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;
            var missing = Location.FileSystem(Path.Combine(root, "not-created"));
            tab.Navigate(missing);
            await Until(() => tab.Listing.State == ListingState.Failed);
            var menu = ContextMenuFactory.BuildBackground(vm, panel, tab, missing);
            var actions = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>().ToArray();
            foreach (var name in new[] { "Paste items or go to pasted path", "Create folder…" })
            {
                var item = Assert.Single(actions, action => action.Header as string == name);
                Assert.False(item.IsEnabled);
                Assert.Contains("Reread", AutomationProperties.GetHelpText(item));
            }
            Assert.Contains(actions, item => item.Header as string == "Reread" && item.IsEnabled);
            Assert.DoesNotContain(actions, item => item.Header as string == "Delete…");
            await vm.ExecuteLocationContextAsync(CommandIds.MakeDirectory, panel, tab, missing);
            Assert.False(vm.Dialogs.IsOpen);
            Assert.Contains("Reread", vm.Notification);
            await vm.ExecuteLocationContextAsync(CommandIds.PasteFromClipboard, panel, tab, missing);
            Assert.Contains("Reread", vm.Notification);
            Assert.False(Directory.Exists(missing.Path));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Working_set_background_explains_that_it_holds_references()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var set = services.WorkingSets.Create("Owned context set");
            var location = ResultSetProvider.LocationOf(set);
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;
            tab.Navigate(location);
            await Until(() => tab.Listing.State == ListingState.Complete);
            var menu = ContextMenuFactory.BuildBackground(vm, panel, tab, location);
            var actions = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>().ToArray();
            foreach (var name in new[] { "Paste items or go to pasted path", "Create folder…" })
            {
                var item = Assert.Single(actions, action => action.Header as string == name);
                Assert.False(item.IsEnabled);
                Assert.Contains("working set holds references", AutomationProperties.GetHelpText(item),
                    StringComparison.OrdinalIgnoreCase);
            }
            Assert.Contains(actions, item => item.Header as string == "Reread" && item.IsEnabled);
            Assert.Contains(actions, item => item.Header as string == "Copy location name" && item.IsEnabled);
            Assert.DoesNotContain(actions, item => item.Header as string == "Delete…");
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Right_clicking_listing_whitespace_opens_folder_actions_without_changing_marks(bool empty)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            if (empty)
            {
                string folder = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
                tab.Navigate(Location.FileSystem(folder));
            }
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 1);
            if (!empty)
            {
                tab.Listing.ToggleMark(1);
                tab.Listing.SetFocus(2);
            }
            int focus = tab.Listing.FocusedIndex, marks = tab.Listing.MarkedCount;
            var panel = window.GetVisualDescendants().OfType<PanelView>()
                .Single(view => ReferenceEquals(view.DataContext, vm.Workspace.ActivePanel));
            var list = panel.FindControl<FileListControl>("List")!;
            window.UpdateLayout();
            var point = list.TranslatePoint(new Point(25, list.Bounds.Height - 25), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            var menu = Assert.IsType<ContextMenu>(OpenMenu.GetValue(null));
            var actions = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>().ToArray();
            var titles = actions.Select(item => item.Header as string).ToArray();
            Assert.Equal(PlacementMode.Pointer, menu.Placement);
            Assert.Contains("Paste items or go to pasted path", titles);
            Assert.Contains("Create folder…", titles);
            Assert.Contains("Reread", titles);
            Assert.Contains("Open terminal here", titles);
            Assert.Contains("Copy location path", titles);
            Assert.DoesNotContain("Delete…", titles);
            Assert.DoesNotContain("Copy…", titles);
            Assert.All(actions.Where(item => item.Header as string is "Paste items or go to pasted path" or "Create folder…"),
                item => Assert.True(item.IsEnabled));
            Assert.Equal(focus, tab.Listing.FocusedIndex);
            Assert.Equal(marks, tab.Listing.MarkedCount);
            menu.Close();
            Assert.Equal(focus, tab.Listing.FocusedIndex);
            Assert.Equal(marks, tab.Listing.MarkedCount);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Paste_targets_captured_folder_even_after_another_panel_becomes_active()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string source = Path.Combine(root, "files", "a.txt");
            string target = Directory.CreateDirectory(Path.Combine(root, "paste-target")).FullName;
            string other = Directory.CreateDirectory(Path.Combine(root, "other-panel")).FullName;
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            tab.Navigate(Location.FileSystem(target));
            var otherPanel = vm.Workspace.Panels[1];
            otherPanel.ActiveTab!.Navigate(Location.FileSystem(other));
            var clipboard = ClipboardIntentTests.ControlledClipboard.Create();
            clipboard.ExternalFiles(source);
            vm.View = new ClipboardView(vm.View, clipboard.Api);
            var menu = ContextMenuFactory.BuildBackground(vm, panel, tab, Location.FileSystem(target));
            var paste = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>()
                .Single(item => item.Header as string == "Paste items or go to pasted path");
            vm.Workspace.Activate(otherPanel);

            paste.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => File.Exists(Path.Combine(target, "a.txt")));
            Assert.False(File.Exists(Path.Combine(other, "a.txt")));
            var job = Assert.Single(services.Jobs.Jobs, j => j.Request.Destination?.Path == target);
            Assert.Equal(JobKind.Copy, job.Kind);
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(target, "a.txt")));

            int before = services.Jobs.Jobs.Count;
            tab.Navigate(Location.FileSystem(other));
            await vm.ExecuteLocationContextAsync(CommandIds.PasteFromClipboard, panel, tab, Location.FileSystem(target));
            Assert.Equal(before, services.Jobs.Jobs.Count);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task New_folder_uses_the_captured_panel_location_after_another_panel_is_activated()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string target = Directory.CreateDirectory(Path.Combine(root, "folder-target")).FullName;
            string other = Directory.CreateDirectory(Path.Combine(root, "folder-other")).FullName;
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            tab.Navigate(Location.FileSystem(target));
            var otherPanel = vm.Workspace.Panels[1];
            otherPanel.ActiveTab!.Navigate(Location.FileSystem(other));
            var menu = ContextMenuFactory.BuildBackground(vm, panel, tab, Location.FileSystem(target));
            var createItem = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>()
                .Single(item => item.Header as string == "Create folder…");
            vm.Workspace.Activate(otherPanel);
            createItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => vm.Dialogs.IsOpen);
            window.UpdateLayout();
            var input = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.GetVisualAncestors().OfType<Border>().Any(border => border.Classes.Contains("backdrop")));
            input.Text = "created-here";
            window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "Create" && button.IsEffectivelyVisible)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Directory.Exists(Path.Combine(target, "created-here")));
            Assert.False(Directory.Exists(Path.Combine(other, "created-here")));
            Assert.Contains(services.Jobs.Jobs, job => job.Kind == JobKind.CreateDirectory &&
                job.Request.Destination?.Path == target);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Non_file_location_has_only_safe_location_actions_with_availability_reasons()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;
            var menu = ContextMenuFactory.BuildBackground(vm, panel, tab, new Location(Schemes.Computer, string.Empty));
            var actions = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>().ToArray();
            Assert.All(actions.Where(item => item.Header as string is "Paste items or go to pasted path" or "Create folder…"),
                item =>
                {
                    Assert.False(item.IsEnabled);
                    Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(item) as string));
                    Assert.Equal(ToolTip.GetTip(item) as string, AutomationProperties.GetHelpText(item));
                });
            Assert.DoesNotContain(actions, item => item.Header as string == "Open terminal here");
            Assert.Contains(actions, item => item.Header as string == "Reread" && item.IsEnabled);
            Assert.Contains(actions, item => item.Header as string == "Copy location name" && item.IsEnabled);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>
    /// The headless window has no desktop window of its own: right clicks there keep FileCat's menus, so no test ever
    /// leaves a Shell popup waiting on the desktop.
    /// </summary>
    [AvaloniaFact]
    public void A_window_without_a_desktop_window_never_starts_the_Windows_menu_helper()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            Assert.All(window.GetVisualDescendants().OfType<PanelView>(), view => Assert.False(view.HasNativeWindow()));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Folder_whitespace_opens_the_folders_own_Windows_menu_with_Shift_for_extended_verbs(bool shift)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' own folder menus are Windows-only; elsewhere FileCat's menu opens directly.");
        var calls = new List<(string Folder, bool Extended)>();
        PanelView.NativeBackgroundMenuForTests = (folder, _, _, extended) =>
        {
            calls.Add((folder, extended));
            return Task.FromResult(new WindowsContextMenu.Outcome(WindowsContextMenu.Result.Handled));
        };
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 1);
            RightClickWhitespace(window, vm, shift);
            await Until(() => calls.Count == 1);
            Assert.Equal((tab.Location!.Path, shift), calls[0]);
            // Windows handled it: FileCat's own menu stays closed.
            Assert.Null(OpenMenu.GetValue(null));
        }
        finally
        {
            PanelView.NativeBackgroundMenuForTests = null;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileCat_actions_or_an_unavailable_Windows_menu_open_FileCats_folder_actions(bool warns)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' own folder menus are Windows-only; elsewhere FileCat's menu opens directly.");
        // "FileCat actions…" chosen in Windows' menu, or Windows' menu could not open (which is said once).
        var result = warns ? WindowsContextMenu.Result.Failed : WindowsContextMenu.Result.FileCatActions;
        typeof(PanelView).GetField("_nativeMenuWarningShown", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
        PanelView.NativeBackgroundMenuForTests = (_, _, _, _) => Task.FromResult(new WindowsContextMenu.Outcome(result));
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 1);
            RightClickWhitespace(window, vm, shift: false);
            await Until(() => OpenMenu.GetValue(null) is ContextMenu);
            var menu = (ContextMenu)OpenMenu.GetValue(null)!;
            var titles = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>().Select(item => item.Header as string).ToArray();
            Assert.Contains("Create folder…", titles);
            Assert.Contains("Paste items or go to pasted path", titles);
            Assert.Equal(warns, vm.Notification?.Contains("Windows menu unavailable", StringComparison.Ordinal) == true);
            menu.Close();
        }
        finally
        {
            PanelView.NativeBackgroundMenuForTests = null;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task An_item_the_Shells_New_command_made_is_focused_with_its_name_open_for_editing()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' own folder menus are Windows-only; elsewhere FileCat's menu opens directly.");
        const string made = "New Text Document.txt";
        PanelView.NativeBackgroundMenuForTests = (folder, _, _, _) =>
        {
            // The Shell's New → Text Document, as the helper reports it.
            File.WriteAllText(Path.Combine(folder, made), "");
            return Task.FromResult(new WindowsContextMenu.Outcome(WindowsContextMenu.Result.Created, made));
        };
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 1);
            var list = RightClickWhitespace(window, vm, shift: false);
            await Until(() => list.IsRenaming);
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal(made, focused.Name);
            // As in Explorer, the name without its extension is selected.
            var editor = list.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsVisible);
            Assert.Equal(made, editor.Text);
            Assert.Equal((0, "New Text Document".Length), (editor.SelectionStart, editor.SelectionEnd));
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Until(() => !list.IsRenaming);
            Assert.True(File.Exists(Path.Combine(tab.Location!.Path, made)));
        }
        finally
        {
            PanelView.NativeBackgroundMenuForTests = null;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_late_or_unlisted_new_item_never_starts_an_edit()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' own folder menus are Windows-only; elsewhere FileCat's menu opens directly.");
        PanelView.NativeBackgroundMenuForTests = (_, _, _, _) =>
            // Reported, but not there (a Shell extension's own line could look like a report): nothing to edit.
            Task.FromResult(new WindowsContextMenu.Outcome(WindowsContextMenu.Result.Created, "never made.txt"));
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 1);
            int focus = tab.Listing.FocusedIndex;
            var list = RightClickWhitespace(window, vm, shift: false);
            await Until(() => !tab.Listing.IsRefreshing && tab.Listing.State == ListingState.Complete);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.False(list.IsRenaming);
            Assert.Equal(focus, tab.Listing.FocusedIndex);
        }
        finally
        {
            PanelView.NativeBackgroundMenuForTests = null;
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// The user moved on before the reread listed the new item (here: the cursor to another row): what they did wins, and
    /// no edit takes the keyboard or the cursor from them.
    /// </summary>
    [AvaloniaFact]
    public async Task A_new_item_is_not_offered_for_renaming_once_the_user_moved_the_cursor()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' own folder menus are Windows-only; elsewhere FileCat's menu opens directly.");
        const string made = "New folder";
        PanelView.NativeBackgroundMenuForTests = (folder, _, _, _) =>
        {
            Directory.CreateDirectory(Path.Combine(folder, made));
            return Task.FromResult(new WindowsContextMenu.Outcome(WindowsContextMenu.Result.Created, made));
        };
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 2);
            tab.Listing.SetFocus(0);
            int listed = tab.Listing.TotalCount;
            string? chosen = null;
            void MoveOn(object? sender, ListingChange change)
            {
                // As the reread starts, before it lands: the user goes to another row.
                if (chosen is not null || !tab.Listing.IsRefreshing) return;
                tab.Listing.SetFocus(1);
                chosen = tab.Listing.TryGetFocused(out var row) ? row.Name : "";
            }
            tab.Listing.Changed += MoveOn;
            var list = RightClickWhitespace(window, vm, shift: false);
            await Until(() => !tab.Listing.IsRefreshing && tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == listed + 1);
            tab.Listing.Changed -= MoveOn;
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.NotNull(chosen);
            Assert.False(list.IsRenaming);
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal(chosen, focused.Name);
        }
        finally
        {
            PanelView.NativeBackgroundMenuForTests = null;
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// The user went on to the path box before the reread listed the new item (cursor and marks unchanged): the rename
    /// does not start, so their typing stays in the path box instead of renaming the new item.
    /// </summary>
    [AvaloniaFact]
    public async Task A_new_item_is_not_offered_for_renaming_once_the_keyboard_went_elsewhere()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' own folder menus are Windows-only; elsewhere FileCat's menu opens directly.");
        const string made = "New folder";
        PanelView.NativeBackgroundMenuForTests = (folder, _, _, _) =>
        {
            Directory.CreateDirectory(Path.Combine(folder, made));
            return Task.FromResult(new WindowsContextMenu.Outcome(WindowsContextMenu.Result.Created, made));
        };
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 1);
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(panel => ReferenceEquals(panel.DataContext, vm.Workspace.ActivePanel));
            int listed = tab.Listing.TotalCount;
            bool moved = false;
            void MoveOn(object? sender, ListingChange change)
            {
                // As the reread starts, before it lands: the user goes to the path box.
                if (moved || !tab.Listing.IsRefreshing) return;
                moved = true;
                view.FocusPathBox();
            }
            tab.Listing.Changed += MoveOn;
            var list = RightClickWhitespace(window, vm, shift: false);
            await Until(() => !tab.Listing.IsRefreshing && tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == listed + 1);
            tab.Listing.Changed -= MoveOn;
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.True(moved);
            Assert.False(list.IsRenaming);
            Assert.False(list.IsKeyboardFocusWithin);
        }
        finally
        {
            PanelView.NativeBackgroundMenuForTests = null;
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// The menu was used while the folder was still loading: its load is not restarted (marks made meanwhile would go),
    /// and the new item is offered once the load has ended.
    /// </summary>
    [AvaloniaFact]
    public async Task A_new_item_in_a_folder_still_loading_is_offered_when_its_load_ends()
    {
        const string made = "New Text Document.txt";
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "still-loading")).FullName;
            File.WriteAllText(Path.Combine(folder, "first.txt"), "");
            File.WriteAllText(Path.Combine(folder, made), "");
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;
            var location = Location.FileSystem(folder);
            tab.Navigate(location);
            Assert.Equal(ListingState.Loading, tab.Listing.State);
            int loads = 0;
            void Count(object? sender, ListingChange change)
            {
                if ((change & ListingChange.Reset) != 0) loads++;
            }
            tab.Listing.Changed += Count;
            vm.RenameCreatedItem(panel, tab, location, made, MainViewModel.CursorName(tab.Listing), tab.Listing.MarkedCount);
            var list = window.GetVisualDescendants().OfType<PanelView>().Single(view => ReferenceEquals(view.DataContext, panel))
                .FindControl<FileListControl>("List")!;
            await Until(() => list.IsRenaming);
            tab.Listing.Changed -= Count;
            Assert.Equal(0, loads);
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal(made, focused.Name);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Until(() => !list.IsRenaming);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_folder_whose_listing_failed_keeps_FileCats_menu_with_its_reason()
    {
        int calls = 0;
        PanelView.NativeBackgroundMenuForTests = (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new WindowsContextMenu.Outcome(WindowsContextMenu.Result.Handled));
        };
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            tab.Navigate(Location.FileSystem(Path.Combine(root, "not-created")));
            await Until(() => tab.Listing.State == ListingState.Failed);
            RightClickWhitespace(window, vm, shift: false);
            await Until(() => OpenMenu.GetValue(null) is ContextMenu);
            Assert.Equal(0, calls);
            ((ContextMenu)OpenMenu.GetValue(null)!).Close();
        }
        finally
        {
            PanelView.NativeBackgroundMenuForTests = null;
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>A right click on the active panel's empty space, below its rows; the panel's list.</summary>
    private static FileListControl RightClickWhitespace(Window window, MainViewModel vm, bool shift)
    {
        var panel = window.GetVisualDescendants().OfType<PanelView>()
            .Single(view => ReferenceEquals(view.DataContext, vm.Workspace.ActivePanel));
        var list = panel.FindControl<FileListControl>("List")!;
        window.UpdateLayout();
        var point = list.TranslatePoint(new Point(25, list.Bounds.Height - 25), window)!.Value;
        var modifiers = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
        window.MouseDown(point, MouseButton.Right, modifiers);
        window.MouseUp(point, MouseButton.Right, modifiers);
        return list;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    private sealed class ClipboardView(IViewActions inner, IClipboard clipboard) : IViewActions
    {
        public IClipboard Clipboard => clipboard;
        public TopLevel? TopLevel => inner.TopLevel;
        public void FocusActivePanel() => inner.FocusActivePanel();
        public Task<string?> RenameInlineAsync(PromptOptions options) => inner.RenameInlineAsync(options);
        public void FocusPathBox() => inner.FocusPathBox();
        public void FocusCommandLine() => inner.FocusCommandLine();
        public bool FocusCommandSearch() => inner.FocusCommandSearch();
        public void FocusPanelFilter() => inner.FocusPanelFilter();
        public void CloseWhenIdle() => inner.CloseWhenIdle();
        public void OpenMenuBar() => inner.OpenMenuBar();
        public void ShowContextMenu() => inner.ShowContextMenu();
        public void ShowNotification(string message, bool isError = false) => inner.ShowNotification(message, isError);
        public void ReloadChrome() => inner.ReloadChrome();
    }
}
