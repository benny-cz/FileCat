using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>The command search finds and runs commands by what people call them (plan §4.4, UX-010).</summary>
public sealed class CommandSearchTests
{
    private static readonly IReadOnlyList<CommandSearch.Entry> Entries = DefaultEntries();

    private static IReadOnlyList<CommandSearch.Entry> DefaultEntries()
    {
        var registry = CommandRegistry.CreateDefault();
        var keymap = new Keymap(registry, new Dictionary<string, string[]>());
        return registry.All.Select(d => new CommandSearch.Entry(d.Id, d.Title, d.Category, keymap.GetGestureText(d.Id),
            MainMenuModel.PathOf(d.Id), d.Keywords)).ToList();
    }

    private static string? First(string query, params string[] recent) => CommandSearch.Rank(query, Entries, recent).FirstOrDefault()?.Id;

    [Theory]
    [InlineData("file recovery", CommandIds.FindDeleted)]
    [InlineData("undelete", CommandIds.FindDeleted)]
    [InlineData("restore deleted", CommandIds.FindDeleted)]
    [InlineData("recovry", CommandIds.FindDeleted)] // misspelled
    [InlineData("new folder", CommandIds.MakeDirectory)]
    [InlineData("mkdir", CommandIds.MakeDirectory)]
    [InlineData("zip", CommandIds.Pack)]
    [InlineData("compres", CommandIds.Pack)] // the start of a keyword
    [InlineData("settings", CommandIds.Settings)]
    [InlineData("preferences", CommandIds.Settings)]
    [InlineData("files find", CommandIds.FindFiles)] // words in any order; the closer title wins
    [InlineData("alt f7", CommandIds.FindFiles)] // by shortcut
    [InlineData("show hidden", CommandIds.ToggleHidden)]
    [InlineData("dark mode", CommandIds.ThemePick)]
    [InlineData("symlink", CommandIds.CreateLink)]
    [InlineData("sha256", CommandIds.Checksum)]
    public void Finds_commands_by_title_synonym_misspelling_and_shortcut(string query, string expected)
    {
        Assert.Equal(expected, First(query));
    }

    [Fact]
    public void Every_word_must_match_and_menu_paths_count()
    {
        Assert.Empty(CommandSearch.Rank("xyzzy", Entries, []));
        Assert.Empty(CommandSearch.Rank("zip xyzzy", Entries, []));
        // "registry" is the File › Registry submenu of the export command.
        Assert.Equal("File › Registry", MainMenuModel.PathOf(CommandIds.RegistryExport));
        Assert.Contains(CommandSearch.Rank("registry export", Entries, []), e => e.Id == CommandIds.RegistryExport);
        Assert.Null(MainMenuModel.PathOf(CommandIds.BookmarkGoPrefix + "1"));
    }

    [Fact]
    public void Nothing_typed_lists_recent_commands_and_recent_use_breaks_ties()
    {
        Assert.Equal(new[] { CommandIds.Settings, CommandIds.Pack },
            CommandSearch.Rank("", Entries, [CommandIds.Settings, "no.such.command", CommandIds.Pack]).Select(e => e.Id));
        Assert.Empty(CommandSearch.Rank("  ", Entries, []));
        Assert.Equal(Entries.Count, CommandSearch.Rank("", Entries, [CommandIds.Pack], int.MaxValue, allWhenEmpty: true).Count);
        Assert.Equal(CommandIds.Pack, CommandSearch.Rank("", Entries, [CommandIds.Pack], int.MaxValue, allWhenEmpty: true)[0].Id);
        // "compare" matches both comparisons equally; the one used last comes first.
        Assert.Equal(CommandIds.CompareFiles, First("compare", CommandIds.CompareFiles));
        Assert.Equal(CommandIds.CompareDirectories, First("compare", CommandIds.CompareDirectories));
    }

    [AvaloniaFact]
    public async Task Ctrl_Shift_P_searches_Enter_runs_and_the_command_is_remembered()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Search commands");
            await WaitFor(() => window.FocusManager?.GetFocusedElement() is FileListControl, ct);
            Assert.True(box.IsVisible);

            window.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P, "P");
            await WaitFor(() => box.IsFocused, ct);
            Assert.True(box.IsFocused);
            Assert.Empty(window.CommandSearch.Shown); // nothing used yet
            Assert.Contains("Type what you want to do", window.CommandSearch.Hint);

            Type(window, "file recovery");
            Assert.Equal(CommandIds.FindDeleted, window.CommandSearch.Selected?.Id);
            Type(window, " qqq"); // every word must match
            Assert.Empty(window.CommandSearch.Shown);
            Assert.Contains("No command matches", window.CommandSearch.Hint);

            box.Text = "";
            Type(window, "settings");
            Assert.Equal(CommandIds.Settings, window.CommandSearch.Selected?.Id);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitFor(() => dialogs.IsOpen, ct);
            Assert.True(dialogs.IsOpen, "Enter ran Settings");
            Assert.Equal("", box.Text);
            Assert.Equal(CommandIds.Settings, services.History.RecentCommands[0]);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitFor(() => !dialogs.IsOpen, ct);

            // Next time, with nothing typed, it is listed first.
            Assert.True(window.FocusCommandSearch());
            Assert.Equal(CommandIds.Settings, window.CommandSearch.Shown.FirstOrDefault()?.Id);

            // Down chooses; Esc returns to the panel and forgets the text.
            Type(window, "compare");
            var second = window.CommandSearch.Shown[1].Id;
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal(second, window.CommandSearch.Selected?.Id);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitFor(() => window.FocusManager?.GetFocusedElement() is FileListControl, ct);
            Assert.IsType<FileListControl>(window.FocusManager?.GetFocusedElement());
            Assert.Empty(window.CommandSearch.Shown);
            Assert.Equal("", box.Text);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Search_rows_say_why_a_command_is_unavailable_and_F2_changes_its_shortcut()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var listing = vm.ActiveTab!.Listing;
            await WaitFor(() => listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3, ct);
            listing.FocusName("a.txt");
            Assert.True(window.FocusCommandSearch());

            // Remove from set applies only to search results: the row says so, and Enter shows why instead of running it.
            Type(window, "remove from set");
            var entry = window.CommandSearch.Selected;
            Assert.Equal(CommandIds.RemoveFromSet, entry?.Id);
            Assert.False(entry!.Enabled);
            Assert.Contains("unavailable here", CommandSearch.Detail(entry));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(entry.Reason, vm.Notification);
            Assert.DoesNotContain(CommandIds.RemoveFromSet, services.History.RecentCommands);

            // F2 in the search changes the chosen command's shortcut (it does not rename the focused file).
            Assert.True(window.FocusCommandSearch());
            Type(window, "zip");
            Assert.Equal(CommandIds.Pack, window.CommandSearch.Selected?.Id);
            window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
            await WaitFor(() => dialogs.IsOpen, ct);
            Assert.True(dialogs.IsOpen);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<FileListControl>(), l => l.IsRenaming);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitFor(() => !dialogs.IsOpen && window.CommandSearch.Shown.Count > 0, ct);
            // Back in the search where it was.
            var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Search commands");
            Assert.True(box.IsFocused);
            Assert.Equal("zip", box.Text);
            Assert.Equal(CommandIds.Pack, window.CommandSearch.Selected?.Id);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_click_on_a_result_runs_it_and_the_press_leaves_the_keyboard_in_the_box()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Search commands");
            await WaitFor(() => window.FocusManager?.GetFocusedElement() is FileListControl, ct);
            Assert.True(window.FocusCommandSearch());
            Type(window, "settings");
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Commands found");
            await WaitFor(() => list.ContainerFromIndex(0) is ListBoxItem { Bounds.Height: > 0 }, ct);
            var item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            Point Center() => item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            // The popup settles under the box first.
            var point = Center();
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(20, ct);
                if (Center() == point) break;
                point = Center();
            }
            window.MouseDown(point, MouseButton.Left);
            Assert.True(box.IsFocused, "the press leaves the keyboard in the box, so the list stays open");
            Assert.NotEmpty(window.CommandSearch.Shown);
            window.MouseUp(point, MouseButton.Left);
            await WaitFor(() => dialogs.IsOpen, ct);
            Assert.True(dialogs.IsOpen, "the click ran Settings");
            Assert.Equal(CommandIds.Settings, services.History.RecentCommands[0]);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitFor(() => !dialogs.IsOpen, ct);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task The_palette_dialog_is_the_same_search()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var dialogs = (OverlayDialogService)vm.Dialogs;
            _ = vm.ShowPaletteAsync();
            await WaitFor(() => dialogs.IsOpen, ct);
            var filter = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Filter Search commands");
            filter.Text = "undelete";
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Search commands");
            await WaitFor(() => list.ContainerFromIndex(0) is not null, ct);
            var firstTitle = list.ContainerFromIndex(0)!.GetVisualDescendants().OfType<TextBlock>().First().Text;
            Assert.StartsWith("Find deleted files", firstTitle);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitFor(() => !dialogs.IsOpen, ct);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// Types as a keyboard does: a key goes down before its text arrives (the window swallows the text of a key that ran
    /// a command, such as the P of Ctrl+Shift+P, until the next key goes down).
    /// </summary>
    private static void Type(Window window, string text)
    {
        window.KeyPress(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
        window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
        window.KeyTextInput(text);
    }

    private static async Task WaitFor(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, ct);
    }
}
