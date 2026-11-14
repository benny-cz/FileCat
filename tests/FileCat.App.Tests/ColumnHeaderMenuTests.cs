using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// The file list's column header has a menu of its own: setting up the columns, on Settings' Columns page with the
/// panel's own profile selected; a view with columns of its own says so instead.
/// </summary>
public sealed class ColumnHeaderMenuTests
{
    private static readonly FieldInfo OpenMenu = typeof(PanelView).GetField("_openItemMenu",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ShownColumns = typeof(FileListControl).GetField("_columns",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [AvaloniaFact]
    public async Task Right_clicking_the_column_header_offers_setting_up_the_panels_own_columns_in_Settings()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 2);
            // The panel shows the third profile (Alt+2): Settings opens on that one, not on the first.
            tab.ColumnProfile = 2;
            string profileName = services.Columns.NameOf(2);
            tab.Listing.SetFocus(1);
            var (sort, focus, marks) = (tab.Listing.Sort, tab.Listing.FocusedIndex, tab.Listing.MarkedCount);

            var (menu, setUp) = OpenHeaderMenu(vm, window);
            Assert.Equal("Set up columns…", setUp.Header);
            Assert.NotNull(setUp.Icon);
            Assert.True(setUp.IsEnabled);
            Assert.Contains($"{profileName} (Alt+2)", Assert.IsType<string>(ToolTip.GetTip(setUp)));
            // A right click on a column's title neither sorts by it nor moves the cursor or the marks.
            Assert.Equal((sort, focus, marks), (tab.Listing.Sort, tab.Listing.FocusedIndex, tab.Listing.MarkedCount));

            setUp.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();
            await Until(() => vm.Dialogs.IsOpen);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            var tabs = Assert.Single(card.GetVisualDescendants().OfType<TabControl>());
            Assert.Equal(SettingsDialog.ColumnsPage, Assert.IsType<TabItem>(tabs.SelectedItem).Header);
            var profiles = card.GetVisualDescendants().OfType<ListBox>()
                .Single(box => AutomationProperties.GetName(box) == "Column profiles");
            Assert.Equal(2, profiles.SelectedIndex);
            Assert.Equal(profileName, card.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Profile name").Text);
            // The keyboard starts on that profile's row.
            await Until(() => profiles.ContainerFromIndex(2) is { IsKeyboardFocusWithin: true });

            card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !vm.Dialogs.IsOpen);
            Assert.Equal(2, tab.ColumnProfile);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_view_with_columns_of_its_own_says_its_header_menu_cannot_set_them()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var set = services.ResultSets.Create("Header menu", "local fixture");
            set.Add(ItemRef.ForFileSystemPath(Path.Combine(root, "files", "a.txt"), EntryKind.File), "");
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Location?.Scheme == Schemes.ResultSet && tab.Listing.State == ListingState.Complete);
            Assert.False(tab.UsesColumnProfiles);

            var (menu, setUp) = OpenHeaderMenu(vm, window);
            Assert.False(setUp.IsEnabled);
            string reason = Assert.IsType<string>(ToolTip.GetTip(setUp));
            Assert.Contains("keeps its own columns", reason);
            Assert.True(ToolTip.GetShowOnDisabled(setUp));
            Assert.Equal(reason, AutomationProperties.GetHelpText(setUp));
            menu.Close();
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>
    /// A closed tab reopened beside one on another profile shows its own profile's columns, the one its header menu names
    /// and Settings opens on (the list first took the columns of the tab it was opened beside).
    /// </summary>
    [AvaloniaFact]
    public async Task A_reopened_tab_shows_its_own_profiles_columns_and_its_header_menu_names_that_profile()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var panel = vm.Workspace.ActivePanel!;
            var first = panel.ActiveTab!;
            await Until(() => first.Listing.State == ListingState.Complete);
            first.ColumnProfile = 0;
            var other = panel.OpenTab(first.Location!);
            other.ColumnProfile = 2;
            panel.CloseTab(other);
            Assert.Same(first, panel.ActiveTab);

            vm.Workspace.ReopenClosed();
            var reopened = panel.ActiveTab!;
            Assert.NotSame(first, reopened);
            Assert.Equal(2, reopened.ColumnProfile);
            var list = window.GetVisualDescendants().OfType<PanelView>()
                .Single(view => ReferenceEquals(view.DataContext, panel)).FindControl<FileListControl>("List")!;
            Assert.Same(reopened, list.Tab);
            Assert.Same(services.Columns.Profiles[2].Columns, ShownColumns.GetValue(list));
            await Until(() => reopened.Listing.State == ListingState.Complete);

            var (menu, setUp) = OpenHeaderMenu(vm, window);
            Assert.Contains($"{services.Columns.NameOf(2)} (Alt+2)", Assert.IsType<string>(ToolTip.GetTip(setUp)));
            menu.Close();
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Settings_opened_from_the_header_menu_is_not_stacked_over_another_dialog()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 680);
        try
        {
            var first = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.Dialogs.IsOpen);
            // Refused at once: nothing is stacked to wait on.
            Assert.True(vm.ShowSettingsAsync(SettingsDialog.ColumnsPage, 1).IsCompletedSuccessfully);
            window.UpdateLayout();
            Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            window.GetVisualDescendants().OfType<DialogCard>().Single().GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Read_only_settings_opened_on_a_profile_keep_every_column_action_off_while_it_is_looked_through()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-header-menu", Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Resolve(overrideRoot: root);
        Directory.CreateDirectory(paths.SettingsDirectory);
        File.WriteAllText(paths.SettingsFile, "{\"SchemaVersion\":999,\"Theme\":\"Classic\"}"); // written by a newer FileCat
        var services = AppServices.CreateForPaths(paths);
        var vm = new MainViewModel(services);
        var window = new MainWindow(vm, null) { Width = 1200, Height = 800 };
        try
        {
            vm.Initialize(null);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(services.SettingsReadOnly);
            var showing = vm.ShowSettingsAsync(SettingsDialog.ColumnsPage, 2);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            var page = Assert.IsAssignableFrom<Control>(Assert.IsType<TabItem>(
                Assert.Single(card.GetVisualDescendants().OfType<TabControl>()).SelectedItem).Content);
            var profiles = page.GetLogicalDescendants().OfType<ListBox>().Single(box => AutomationProperties.GetName(box) == "Column profiles");
            var columns = page.GetLogicalDescendants().OfType<ListBox>().Single(box => AutomationProperties.GetName(box) == "Columns of the selected profile");
            Assert.Equal(2, profiles.SelectedIndex);
            var actions = page.GetLogicalDescendants().OfType<Control>().Where(c => c is Button or ComboBox or NumericUpDown).ToList();
            Assert.NotEmpty(actions);
            // Looking through other profiles and columns (the lists stay usable) turns no action on.
            foreach (int index in new[] { 0, 1, 2 })
            {
                profiles.SelectedIndex = index;
                columns.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                Assert.All(actions, action => Assert.False(action.IsEnabled, $"{action.GetType().Name} {AutomationProperties.GetName(action)} on profile {index}"));
            }
            Assert.True(page.GetLogicalDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Profile name").IsReadOnly);

            card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>Right-clicks the active panel's header row over the Name column's title: the menu and its one item.</summary>
    private static (ContextMenu Menu, MenuItem Item) OpenHeaderMenu(MainViewModel vm, Window window)
    {
        var list = window.GetVisualDescendants().OfType<PanelView>()
            .Single(view => ReferenceEquals(view.DataContext, vm.Workspace.ActivePanel))
            .FindControl<FileListControl>("List")!;
        window.UpdateLayout();
        var point = list.TranslatePoint(new Point(60, 6), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        var menu = Assert.IsType<ContextMenu>(OpenMenu.GetValue(null));
        Assert.True(menu.IsOpen);
        return (menu, Assert.Single(((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>()));
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
