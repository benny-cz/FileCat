using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>A panel's tabs: the active one stays in sight, and "⋯" lists them all when they do not fit.</summary>
public sealed class TabStripTests
{
    [AvaloniaFact]
    public void Rejected_last_tab_middle_click_preserves_real_recent_history()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[0];
            vm.Workspace.Activate(panel);
            var only = panel.ActiveTab!;
            var real = panel.OpenTab(FileCat.Core.Resources.Location.FileSystem(Path.Combine(root, "files")));
            panel.CloseTab(real);
            Assert.Same(only, panel.ActiveTab);
            var history = vm.Workspace.RecentlyClosed.ToArray();
            Assert.Single(history);

            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(v => ReferenceEquals(v.DataContext, panel));
            window.CaptureRenderedFrame();
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Tag, only));
            Point center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            for (int i = 0; i < 20; i++)
            {
                window.MouseDown(center, MouseButton.Middle);
                window.MouseUp(center, MouseButton.Middle);
            }

            Assert.Single(panel.Tabs);
            Assert.Same(only, panel.ActiveTab);
            Assert.Equal(history, vm.Workspace.RecentlyClosed);
            Assert.False(vm.GetAvailability(CommandIds.CloseTab).Enabled);
            Assert.False(view.BuildTabMenu(panel, only).Items.OfType<MenuItem>()
                .Single(item => item.Header as string == "Close tab").IsEnabled);
            vm.Execute(CommandIds.CloseTab);
            Assert.Equal(history, vm.Workspace.RecentlyClosed);

            vm.Workspace.ReopenClosed();
            Assert.Equal(2, panel.Tabs.Count);
            Assert.Equal(real.Location, panel.ActiveTab!.Location);
            Assert.Empty(vm.Workspace.RecentlyClosed);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Eligible_close_paths_record_one_real_tab_and_follow_panel_moves()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var source = vm.Workspace.Panels[0];
            var target = vm.Workspace.Panels[1];
            vm.Workspace.Activate(source);
            var moved = source.ActiveTab!;
            vm.Workspace.MoveTabToPanel(moved, target);
            Assert.False(source.CanCloseTab(source.ActiveTab!));
            Assert.True(target.CanCloseTab(moved));
            Assert.False(source.CanCloseTab(moved));
            vm.Workspace.Activate(target);
            Assert.True(vm.GetAvailability(CommandIds.CloseTab).Enabled);

            var original = moved.ToState();
            vm.Execute(CommandIds.CloseTab);
            Assert.Single(vm.Workspace.RecentlyClosed);
            Assert.Equal(original.Location, vm.Workspace.RecentlyClosed[0].Location);
            vm.Workspace.ReopenClosed();
            Assert.Equal(original.Location, target.ActiveTab!.Location);
            Assert.Empty(vm.Workspace.RecentlyClosed);

            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(v => ReferenceEquals(v.DataContext, target));
            window.CaptureRenderedFrame();
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Tag, target.ActiveTab));
            Point center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Right);
            window.MouseUp(center, MouseButton.Right);
            var menu = view.BuildTabMenu(target, target.ActiveTab!);
            var close = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Close tab");
            Assert.True(close.IsEnabled);
            close.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Single(vm.Workspace.RecentlyClosed);
            Assert.Equal(original.Location, vm.Workspace.RecentlyClosed[0].Location);
            vm.Workspace.ReopenClosed();
            Assert.Empty(vm.Workspace.RecentlyClosed);

            window.CaptureRenderedFrame();
            button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Tag, target.ActiveTab));
            center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Middle);
            window.MouseUp(center, MouseButton.Middle);
            Assert.Single(vm.Workspace.RecentlyClosed);
            Assert.Equal(original.Location, vm.Workspace.RecentlyClosed[0].Location);
            vm.Workspace.ReopenClosed();
            Assert.Equal(original.Location, target.ActiveTab!.Location);
            Assert.Empty(vm.Workspace.RecentlyClosed);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Colliding_titles_use_the_shortest_distinct_parent_provider_or_archive_cue()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[0];
            TabViewModel Add(string path)
            {
                var tab = new TabViewModel(services, panel) { Title = "build", DisplayPath = path };
                panel.AttachTab(tab);
                return tab;
            }

            var clientA = Add(@"C:\ClientA\build");
            var clientB = Add(@"D:\ClientB\build");
            var remote = Add("sftp://alex@server/home/build");
            var archive = Add("zip://archive.zip!/home/build");
            Assert.Equal("build · ClientA", clientA.TabHeader);
            Assert.Equal("build · ClientB", clientB.TabHeader);
            Assert.Equal("build · alex@server / home", remote.TabHeader);
            Assert.Equal("build · archive.zip! / home", archive.TabHeader);

            var duplicate = Add(@"C:\ClientA\build");
            Assert.NotEqual(clientA.TabHeader, duplicate.TabHeader);
            Assert.Contains("#1", clientA.TabHeader);
            Assert.Contains("#2", duplicate.TabHeader);
            panel.MoveTab(duplicate, 0);
            Assert.NotEqual(clientA.TabHeader, duplicate.TabHeader);
            panel.CloseTab(duplicate);
            Assert.Equal("build · ClientA", clientA.TabHeader);

            remote.Title = "other";
            archive.Title = "other";
            clientB.Title = "other";
            Assert.Equal("build", clientA.TabHeader);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Lock_modes_have_distinct_visible_labels_and_full_keyboard_accessible_meanings()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            string path = tab.DisplayPath;
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(v => ReferenceEquals(v.DataContext, panel));
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Tag, tab));

            tab.IsLocked = true;
            tab.ReturnToRoot = false;
            Assert.StartsWith("🔒 ", tab.TabHeader);
            Assert.Contains("Navigation lock", tab.TabTooltip);
            Assert.Contains("creates a new tab", AutomationProperties.GetHelpText(button));
            Assert.Contains(path, AutomationProperties.GetName(button));

            tab.ReturnToRoot = true;
            tab.LockedRoot = FileCat.Core.Resources.Location.FileSystem(path);
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("⌂ ", tab.TabHeader);
            Assert.Contains("revisiting returns to", tab.TabTooltip);
            Assert.Contains(path, AutomationProperties.GetHelpText(button));
            Assert.Contains("Root lock", AutomationProperties.GetName(button));

            tab.IsLocked = false;
            Assert.DoesNotContain("lock", tab.TabHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Unlocked tab", AutomationProperties.GetHelpText(button));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Returning_to_a_locked_root_updates_the_tab_identity_and_keeps_its_saved_root()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string saved = Directory.CreateDirectory(Path.Combine(root, "saved-root")).FullName;
            string child = Directory.CreateDirectory(Path.Combine(saved, "child")).FullName;
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            var savedLocation = FileCat.Core.Resources.Location.FileSystem(saved);
            tab.Navigate(savedLocation);
            await FileListFixture.Wait(() => tab.DisplayPath == saved);
            tab.IsLocked = true;
            tab.ReturnToRoot = true;
            tab.LockedRoot = savedLocation;
            tab.Navigate(FileCat.Core.Resources.Location.FileSystem(child));
            await FileListFixture.Wait(() => tab.DisplayPath == child);
            Assert.Contains("child", tab.TabHeader);

            panel.OpenTab(FileCat.Core.Resources.Location.FileSystem(Path.Combine(root, "files")));
            panel.ActiveTab = tab;
            await FileListFixture.Wait(() => tab.DisplayPath == saved);
            Assert.Equal(savedLocation, tab.LockedRoot);
            Assert.Contains("saved-root", tab.TabHeader);
            Assert.DoesNotContain("child", tab.TabHeader);
            Assert.Contains(saved, tab.TabTooltip);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Visually_equal_escaped_names_get_safe_distinct_qualifiers()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[0];
            var hidden = new TabViewModel(services, panel)
            {
                Title = "docs\u202E.exe",
                DisplayPath = "sftp://serverA/ClientA/docs\u202E.exe",
            };
            var literal = new TabViewModel(services, panel)
            {
                Title = "docs\\u202E.exe",
                DisplayPath = "sftp://serverB/ClientB/docs\\u202E.exe",
            };
            panel.AttachTab(hidden);
            panel.AttachTab(literal);
            Assert.StartsWith("docs\\u202E.exe · ", hidden.TabHeader);
            Assert.StartsWith("docs\\u202E.exe · ", literal.TabHeader);
            Assert.NotEqual(hidden.TabHeader, literal.TabHeader);
            Assert.DoesNotContain('\u202E', hidden.TabHeader);
            Assert.Contains("ClientA", hidden.TabHeader);
            Assert.Contains("ClientB", literal.TabHeader);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task The_active_tab_stays_in_sight_and_overflowing_tabs_offer_the_list()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var panel = vm.Workspace.Panels[0];
            vm.Workspace.Activate(panel);
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            await Task.Delay(100, ct);
            Assert.False(view.TabStripState().Overflowing); // one tab fits

            for (int i = 0; i < 12; i++) vm.Execute(CommandIds.NewTab);
            for (int i = 0; i < 100 && view.TabStripState() != (true, true); i++) await Task.Delay(20, ct);
            Assert.Equal(13, panel.Tabs.Count);
            Assert.Equal((true, true), view.TabStripState()); // the newest tab, at the end, is shown

            panel.ActiveTab = panel.Tabs[0];
            for (int i = 0; i < 100 && !view.TabStripState().ActiveTabVisible; i++) await Task.Delay(20, ct);
            Assert.True(view.TabStripState().ActiveTabVisible);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// A folder's name shows escaped wherever FileCat shows it, as in the file list (§18.3, V23 B14): its tab, and the
    /// path beside the command line; the path itself, which the edit box and every operation use, stays as it is.
    /// </summary>
    [AvaloniaFact]
    public async Task A_folders_name_cannot_turn_its_tab_or_path_around()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string name = "docs" + (char)0x202E + "fdp.exe";
            string folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            var tab = vm.Workspace.Panels[0].ActiveTab!;
            tab.Navigate(Core.Resources.Location.FileSystem(folder));
            for (int i = 0; i < 100 && tab.DisplayPath != folder; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            string escaped = "docs" + (char)92 + "u202Efdp.exe";
            Assert.Equal(escaped, tab.TabHeader);
            Assert.EndsWith(escaped, tab.ShownPath, StringComparison.Ordinal);
            Assert.Equal(folder, tab.DisplayPath);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
