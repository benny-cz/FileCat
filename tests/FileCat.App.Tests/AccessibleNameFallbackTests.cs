using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// I653, the class of I583: a list item or a templated button without a name of its own is named by Avalonia after what
/// it holds, so a screen reader announced a record's fields ("KeyboardHelpEntry { Id = app.about, Title = …") or a type
/// ("Avalonia.Controls.PathIcon") instead of what the control shows. These peers are the ones UI Automation (Windows),
/// AX (macOS) and AT-SPI (Linux) read the names from.
/// </summary>
public sealed class AccessibleNameFallbackTests
{
    /// <summary>A name that is implementation text: a type's full name, or a record's generated text.</summary>
    private static readonly Regex Implementation = new(@"^(Avalonia|FileCat|System)\.[\w.+`]+$|^[A-Z]\w* \{ \w+ = ", RegexOptions.CultureInvariant);

    private static string? Name(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();

    private static void ProductStyles(Window window) => window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/"))
        { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });

    /// <summary>Every visible control under <paramref name="root"/> whose accessible name is implementation text.</summary>
    private static IEnumerable<string> ImplementationNamed(Control root, string where) =>
        root.GetVisualDescendants().OfType<Control>().Prepend(root)
            .Where(control => control.IsEffectivelyVisible)
            .Select(control => (control, name: Name(control)))
            .Where(found => found.name is not null && Implementation.IsMatch(found.name))
            .Select(found => $"{where}: {found.control.GetType().Name}{(found.control.Name is { } part ? " " + part : "")} named \"{found.name}\"");

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(), what);
    }

    [AvaloniaFact]
    public async Task Keyboard_reference_rows_are_named_by_their_title_and_shortcut()
    {
        var host = new Grid();
        var window = new Window { Width = 1000, Height = 750, Content = host };
        window.Show();
        try
        {
            var dialogs = new OverlayDialogService(host, () => null);
            _ = dialogs.KeyboardReferenceAsync([
                new KeyboardHelpEntry("file.copy", "Copy", "Files", "F5", "Copy the marked items to the target panel."),
                new KeyboardHelpEntry("app.about", "About FileCat", "Application", null, null),
                new KeyboardHelpEntry("app.settings", "Settings…", "Application", "Ctrl+,", null, false, "A dialog is open."),
            ]);
            ListBox? list = null;
            await WaitFor(() => (list = host.GetVisualDescendants().OfType<ListBox>()
                .FirstOrDefault(l => AutomationProperties.GetName(l) == "Commands and current shortcuts")) is not null &&
                list.GetRealizedContainers().Count() == 3, "the keyboard reference shows its three rows");
            var names = list!.GetRealizedContainers().ToDictionary(item => ((KeyboardHelpEntry)item.DataContext!).Id, Name);
            // What the row shows: its title, and its shortcut or "Unbound" on the right.
            Assert.Equal("Copy, F5", names["file.copy"]);
            Assert.Equal("About FileCat, Unbound", names["app.about"]);
            Assert.Equal("Settings…, Ctrl+,", names["app.settings"]);
            // An unavailable command keeps its reason where a screen reader finds it.
            var unavailable = list.GetRealizedContainers().Single(item => ((KeyboardHelpEntry)item.DataContext!).Id == "app.settings");
            Assert.Equal("Unavailable: A dialog is open.", AutomationProperties.GetHelpText(unavailable));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Spin_buttons_of_every_number_box_in_Settings_are_named_Increase_and_Decrease()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        ProductStyles(window);
        try
        {
            var dialogs = (OverlayDialogService)vm.Dialogs;
            _ = vm.ExecuteAsync(CommandIds.Settings);
            await WaitFor(() => dialogs.IsOpen && window.GetVisualDescendants().OfType<TabControl>().Any(), "Settings is open");
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            var spinners = new Dictionary<string, int>();
            for (int i = 0; i < tabs.ItemCount; i++)
            {
                tabs.SelectedIndex = i;
                await Task.Delay(50, TestContext.Current.CancellationToken);
                window.UpdateLayout();
                string page = (tabs.Items[i] as TabItem)?.Header as string ?? i.ToString();
                foreach (var box in window.GetVisualDescendants().OfType<NumericUpDown>().Where(box => box.IsEffectivelyVisible))
                {
                    var buttons = box.GetVisualDescendants().OfType<RepeatButton>().ToList();
                    string Of(string part) => Name(Assert.Single(buttons, b => b.Name == part)) ?? "(none)";
                    Assert.True(Of("PART_IncreaseButton") == "Increase" && Of("PART_DecreaseButton") == "Decrease",
                        $"{page}, {AutomationProperties.GetName(box)}: the spin buttons are named \"{Of("PART_IncreaseButton")}\" and \"{Of("PART_DecreaseButton")}\"");
                    spinners[page] = spinners.GetValueOrDefault(page) + 1;
                }
            }
            // The pages the report names: Appearance's two font sizes, and Columns' width.
            Assert.Equal(2, spinners.GetValueOrDefault("Appearance"));
            Assert.Equal(1, spinners.GetValueOrDefault("Columns"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Theme_choices_are_named_by_their_theme()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var dialogs = (OverlayDialogService)vm.Dialogs;
            _ = vm.ExecuteAsync(CommandIds.ThemePick);
            ListBox? list = null;
            await WaitFor(() => dialogs.IsOpen && (list = window.GetVisualDescendants().OfType<ListBox>()
                .FirstOrDefault(l => AutomationProperties.GetName(l) == "Themes")) is not null && list.GetRealizedContainers().Any(),
                "the theme choices are shown");
            var items = list!.GetRealizedContainers().OfType<ListBoxItem>().ToList();
            Assert.NotEmpty(items);
            Assert.All(items, item =>
            {
                string theme = Assert.IsType<string>(item.Tag);
                Assert.Equal(ThemeManager.DisplayName(theme), Name(item));
                Assert.Equal(ThemeManager.Description(theme), AutomationProperties.GetHelpText(item));
            });
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitFor(() => !dialogs.IsOpen, "the theme choice closes");
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Registry_search_results_are_named_as_their_row_reads()
    {
        static RegistrySearchDialog.Row Result(string key, string name, EntryKind kind, string folder) =>
            new(new ItemRef(new Location(Schemes.Registry, key), name, kind), folder);
        var list = new ListBox
        {
            ItemTemplate = RegistrySearchDialog.ResultTemplate,
            ItemsSource = new[]
            {
                Result(@"HKCU\Environment", "Path", EntryKind.RegistryValue, @"HKEY_CURRENT_USER\Environment"),
                Result(@"HKCU\Software", "FileCat", EntryKind.RegistryKey, @"HKEY_CURRENT_USER\Software"),
                Result(@"HKCU\Environment", "", EntryKind.RegistryValue, @"HKEY_CURRENT_USER\Environment"),
            },
        };
        var window = new Window { Width = 800, Height = 400, Content = list };
        window.Show();
        try
        {
            await WaitFor(() => list.GetRealizedContainers().Count() == 3, "the results are shown");
            string?[] shown = [@"Value, Path, HKEY_CURRENT_USER\Environment", @"Key, FileCat, HKEY_CURRENT_USER\Software", @"Value, (Default), HKEY_CURRENT_USER\Environment"];
            Assert.Equal(shown, list.GetRealizedContainers().Select(Name).ToArray());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task The_main_window_its_menus_and_dialogs_name_nothing_after_a_type_or_a_record()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        ProductStyles(window);
        var found = new List<string>();
        try
        {
            var listing = vm.ActiveTab!.Listing;
            await WaitFor(() => listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3, "the folder is listed");
            listing.SetFocus(1);
            window.UpdateLayout();
            found.AddRange(ImplementationNamed(window, "main window"));
            var menu = window.FindControl<Menu>("MainMenu")!;
            foreach (var top in menu.Items.OfType<MenuItem>())
            {
                top.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                foreach (var item in Items(top)) found.AddRange(ImplementationNamed(item, $"menu {top.Header}"));
                top.IsSubMenuOpen = false;
                Dispatcher.UIThread.RunJobs();
            }
            var dialogs = (OverlayDialogService)vm.Dialogs;
            foreach (var command in new[] { CommandIds.Settings, CommandIds.Help, CommandIds.ChangeShortcut, CommandIds.ThemePick, CommandIds.Copy,
                         CommandIds.Attributes, CommandIds.BulkRename, CommandIds.MarkSelectMask, CommandIds.TabList, CommandIds.FolderHistory,
                         CommandIds.Bookmarks, CommandIds.ReviewMarks })
            {
                if (command == CommandIds.ReviewMarks) listing.SetMark(1, true);
                _ = vm.ExecuteAsync(command);
                await WaitFor(() => dialogs.IsOpen, command + " opens a dialog");
                await Task.Delay(50, TestContext.Current.CancellationToken);
                window.UpdateLayout();
                found.AddRange(ImplementationNamed(window, command));
                if (command == CommandIds.Settings)
                {
                    var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
                    for (int i = 0; i < tabs.ItemCount; i++)
                    {
                        tabs.SelectedIndex = i;
                        await Task.Delay(50, TestContext.Current.CancellationToken);
                        window.UpdateLayout();
                        found.AddRange(ImplementationNamed(window, $"Settings page {(tabs.Items[i] as TabItem)?.Header}"));
                    }
                }
                for (int tries = 0; tries < 4 && dialogs.IsOpen; tries++)
                {
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    for (int i = 0; i < 50 && dialogs.IsOpen; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
                }
                Assert.False(dialogs.IsOpen, command + " closes");
                if (command == CommandIds.ReviewMarks) listing.SetMark(1, false);
            }
            Assert.True(found.Count == 0, string.Join(Environment.NewLine, found.Distinct()));
        }
        finally { AccessibilityTests.Close(services, window, root); }

        static IEnumerable<Control> Items(MenuItem parent) =>
            parent.Items.OfType<MenuItem>().SelectMany(item => Items(item).Prepend(item));
    }

    /// <summary>
    /// Every choice of every combo box under <paramref name="root"/>, by its realized item, whose accessible name is
    /// implementation text (a ComboBoxItem is named like a list item: its template's text, else its item's ToString).
    /// </summary>
    private static IEnumerable<string> ImplementationNamedChoices(Control root, string where)
    {
        var found = new List<string>();
        foreach (var combo in root.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.ItemCount > 0).ToList())
        {
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            try
            {
                for (int i = 0; i < combo.ItemCount; i++)
                    if (combo.ContainerFromIndex(i) is Control item && Name(item) is { } name && Implementation.IsMatch(name))
                        found.Add($"{where}: {AutomationProperties.GetName(combo)} choice {i + 1} named \"{name}\"");
            }
            finally
            {
                combo.IsDropDownOpen = false;
                Dispatcher.UIThread.RunJobs();
            }
        }
        return found;
    }

    [AvaloniaFact]
    public async Task Separate_windows_and_the_choices_in_dialogs_name_nothing_after_a_type_or_a_record()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1300, height: 860, prepareFiles: folder =>
        {
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            string other = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(folder)!, "other", "sub")).Parent!.FullName;
            File.WriteAllText(Path.Combine(other, "a.txt"), "changed alpha");
            File.WriteAllText(Path.Combine(other, "only-right.txt"), "right");
        });
        ProductStyles(window);
        var found = new List<string>();
        var opened = new List<Window>();
        void Opened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Window w && !ReferenceEquals(w, window)) opened.Add(w);
        }
        using var subscription = Window.WindowOpenedEvent.AddClassHandler(typeof(Window), Opened);
        var ct = TestContext.Current.CancellationToken;
        var dialogs = (OverlayDialogService)vm.Dialogs;
        try
        {
            var tab = vm.ActiveTab!;
            var listing = tab.Listing;
            await WaitFor(() => listing.State == Core.Listing.ListingState.Complete && listing.FindStoreIndex("sub") >= 0, "the folder is listed");
            void Focus(string name) => listing.SetFocus(listing.GetVisibleIndex(listing.FindStoreIndex(name)));

            async Task CloseOverlays()
            {
                for (int tries = 0; tries < 4 && dialogs.IsOpen; tries++)
                {
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    for (int i = 0; i < 50 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
                }
            }

            // Dialogs, with every combo box's choices: where a command opens none on this system, there is nothing to check.
            foreach (var command in new[] { CommandIds.Settings, CommandIds.Copy, CommandIds.Move, CommandIds.MarkUnselectMask, CommandIds.CreateLink,
                         CommandIds.Checksum, CommandIds.ApplyCommand, CommandIds.SftpConnect, CommandIds.RecentNotifications, CommandIds.FindFolder,
                         CommandIds.SaveWorkspace, CommandIds.About, CommandIds.QuickFilter, CommandIds.LocationMenuSource, CommandIds.FocusPanelPicker,
                         CommandIds.ChooseTarget })
            {
                Focus("a.txt");
                _ = vm.ExecuteAsync(command);
                for (int i = 0; i < 40 && !dialogs.IsOpen; i++) await Task.Delay(25, ct);
                if (!dialogs.IsOpen) continue;
                await Task.Delay(50, ct);
                window.UpdateLayout();
                found.AddRange(ImplementationNamed(window, command));
                found.AddRange(ImplementationNamedChoices(window, command));
                if (command == CommandIds.Settings)
                {
                    var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
                    for (int i = 0; i < tabs.ItemCount; i++)
                    {
                        tabs.SelectedIndex = i;
                        await Task.Delay(50, ct);
                        window.UpdateLayout();
                        found.AddRange(ImplementationNamedChoices(window, $"Settings page {(tabs.Items[i] as TabItem)?.Header}"));
                    }
                }
                await CloseOverlays();
                Assert.False(dialogs.IsOpen, command + " closes");
            }

            // Separate windows: each is checked once it has shown what it opened for.
            async Task<Window?> Open(string command, string focus, Func<Window, bool> ready)
            {
                int before = opened.Count;
                Focus(focus);
                _ = vm.ExecuteAsync(command);
                for (int i = 0; i < 200 && opened.Count == before; i++) await Task.Delay(25, ct);
                if (opened.Count == before) return null;
                var w = opened[^1];
                for (int i = 0; i < 200 && !ready(w); i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(25, ct);
                }
                w.UpdateLayout();
                return w;
            }
            var checkedWindows = new List<string>();
            async Task Check(string command, string focus, Func<Window, bool> ready)
            {
                if (await Open(command, focus, ready) is not { } w) return;
                try
                {
                    found.AddRange(ImplementationNamed(w, $"{command} ({w.GetType().Name})"));
                    found.AddRange(ImplementationNamedChoices(w, $"{command} ({w.GetType().Name})"));
                    checkedWindows.Add(w.GetType().Name);
                }
                finally { w.Close(); }
            }

            await Check(CommandIds.View, "a.txt", w => w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("alpha", StringComparison.Ordinal) == true) ||
                w.GetVisualDescendants().OfType<Control>().Count() > 50);
            await Check(CommandIds.HexEdit, "a.txt", w => w.GetVisualDescendants().OfType<Control>().Count() > 50);
            // Find, with its results.
            if (await Open(CommandIds.FindFiles, "a.txt", w => w.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Find" && b.IsEffectivelyEnabled)) is { } find)
            {
                try
                {
                    find.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Find" && b.IsEffectivelyEnabled)
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    for (int i = 0; i < 120 && !find.GetVisualDescendants().OfType<FileCat.App.Controls.FileListControl>().Any(l => l.AutomationListing?.TotalCount > 0); i++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(25, ct);
                    }
                    find.UpdateLayout();
                    found.AddRange(ImplementationNamed(find, "Find results"));
                    found.AddRange(ImplementationNamedChoices(find, "Find"));
                    checkedWindows.Add(find.GetType().Name);
                }
                finally { find.Close(); }
            }
            // Two files compared side by side.
            listing.MarkAll(false);
            listing.SetMark(listing.GetVisibleIndex(listing.FindStoreIndex("a.txt")), true);
            listing.SetMark(listing.GetVisibleIndex(listing.FindStoreIndex("b.txt")), true);
            await Check(CommandIds.CompareFiles, "a.txt", w => w.GetVisualDescendants().OfType<Control>().Count() > 50);
            listing.MarkAll(false);
            // Two folders compared with their subfolders, as a tree of differences.
            var right = vm.Workspace.Panels[1].ActiveTab!;
            right.Navigate(Core.Resources.Location.FileSystem(Path.Combine(root, "other")));
            await WaitFor(() => right.Listing.State == Core.Listing.ListingState.Complete && right.Listing.FindStoreIndex("only-right.txt") >= 0, "the other folder is listed");
            {
                int before = opened.Count;
                _ = vm.ExecuteAsync(CommandIds.CompareDirectories);
                await WaitFor(() => dialogs.IsOpen, "the folder comparison asks what to compare");
                await Task.Delay(50, ct);
                window.UpdateLayout();
                var overlay = window.FindControl<Panel>("OverlayHost")!;
                overlay.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is string s && s.StartsWith("Include subfolders", StringComparison.Ordinal)).IsChecked = true;
                overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await WaitFor(() => opened.Count > before, "the tree of differences opens");
                var differences = opened[^1];
                try
                {
                    for (int i = 0; i < 120 && !differences.GetVisualDescendants().OfType<ListBoxItem>().Any(); i++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(25, ct);
                    }
                    differences.UpdateLayout();
                    found.AddRange(ImplementationNamed(differences, "folder differences"));
                    found.AddRange(ImplementationNamedChoices(differences, "folder differences"));
                    checkedWindows.Add(differences.GetType().Name);
                }
                finally { differences.Close(); }
            }
            Assert.Contains("ViewerWindow", checkedWindows);
            Assert.Contains("FindWindow", checkedWindows);
            Assert.Contains("CompareWindow", checkedWindows);
            Assert.Contains("DirectoryDiffWindow", checkedWindows);
            Assert.True(found.Count == 0, string.Join(Environment.NewLine, found.Distinct()));
        }
        finally
        {
            foreach (var w in opened.ToList()) { try { w.Close(); } catch (InvalidOperationException) { } }
            AccessibilityTests.Close(services, window, root);
        }
    }
}
