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
}
