using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

/// <summary>Find (Alt+F7) in a window of its own, as in Salamander (plan §11, SEARCH-002).</summary>
public sealed class FindWindowTests
{
    private static async Task WaitFor(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, ct);
    }

    private static async Task<FindWindow> OpenFindAsync(ViewModels.MainViewModel vm, CancellationToken ct)
    {
        var listing = vm.ActiveTab!.Listing;
        await WaitFor(() => listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount >= 3, ct);
        vm.Execute(CommandIds.FindFiles);
        await WaitFor(() => FindWindow.OpenWindows.Count == 1, ct);
        var find = Assert.Single(FindWindow.OpenWindows);
        find.Activate();
        await WaitFor(() => find.NamesBox.IsFocused, ct);
        return find;
    }

    /// <summary>Types a name mask and searches with Enter (or a refining chord).</summary>
    private static async Task SearchAsync(FindWindow find, string names, CancellationToken ct, Key key = Key.Enter, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        find.NamesBox.Focus();
        find.NamesBox.Text = names;
        find.KeyPress(key, modifiers, key == Key.Enter ? PhysicalKey.Enter : Enum.Parse<PhysicalKey>(key.ToString()), null);
        await Task.Delay(50, ct);
        await WaitFor(() => find.IsIdle, ct);
    }

    private static void CloseAll()
    {
        foreach (var w in FindWindow.OpenWindows.ToList()) w.Close();
    }

    [AvaloniaFact]
    public async Task Alt_F7_opens_a_window_that_finds_items_and_Space_shows_one_in_the_panel()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var find = await OpenFindAsync(vm, ct);
            Assert.Empty(AccessibilityTests.Unnamed(find));
            // A word without wildcards finds the names that contain it.
            await SearchAsync(find, "b", ct);
            Assert.Equal(["b.txt"], find.Found);
            Assert.Contains("Finished", find.Status);
            Assert.Equal(["b"], services.History.SearchNames.Take(1));

            var results = find.ResultsTab!.Listing;
            await WaitFor(() => results.VisibleCount == 1, ct);
            find.List.Focus();
            results.SetFocus(0);
            find.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            var panel = vm.Workspace.ActiveTab!.Listing;
            await WaitFor(() => panel.TryGetFocused(out var f) && f.Name == "b.txt", ct);
            Assert.True(panel.TryGetFocused(out var focused) && focused.Name == "b.txt");

            // Esc with no search running closes the window.
            find.Activate();
            find.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitFor(() => FindWindow.OpenWindows.Count == 0, ct);
            Assert.Empty(FindWindow.OpenWindows);
        }
        finally
        {
            CloseAll();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_new_search_keeps_removes_or_adds_found_items()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            File.WriteAllText(Path.Combine(root, "files", "c.log"), "log");
            var find = await OpenFindAsync(vm, ct);
            await SearchAsync(find, "*.txt", ct);
            Assert.Equal(["a.txt", "b.txt"], find.Found.Order());
            await SearchAsync(find, "a", ct, Key.I, RawInputModifiers.Control); // keep only those found again
            Assert.Equal(["a.txt"], find.Found);
            await SearchAsync(find, "*.log", ct, Key.W, RawInputModifiers.Control); // add the new finds
            Assert.Equal(["a.txt", "c.log"], find.Found.Order());
            await SearchAsync(find, "a.txt", ct, Key.S, RawInputModifiers.Control); // remove those found again
            Assert.Equal(["c.log"], find.Found);
            await SearchAsync(find, "*", ct); // a plain search starts over
            Assert.Equal(["a.txt", "b.txt", "c.log"], find.Found.Order());
        }
        finally
        {
            CloseAll();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Panel_commands_act_on_found_items_and_ask_in_the_Find_window()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var mainDialogs = (OverlayDialogService)vm.Dialogs;
            var find = await OpenFindAsync(vm, ct);
            await SearchAsync(find, "a.txt", ct);
            var results = find.ResultsTab!.Listing;
            await WaitFor(() => results.VisibleCount == 1, ct);
            find.List.Focus();
            results.SetFocus(0);

            // F5 asks where to copy in this window; the panels stay free.
            find.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
            await WaitFor(() => find.Dialogs.IsOpen, ct);
            Assert.True(find.Dialogs.IsOpen);
            Assert.False(mainDialogs.IsOpen);
            find.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitFor(() => !find.Dialogs.IsOpen, ct);
            Assert.Single(FindWindow.OpenWindows); // Esc closed the dialog, not the window

            // Shift+F8 deletes the original file after asking here.
            string original = Path.Combine(root, "files", "a.txt");
            find.List.Focus();
            find.KeyPress(Key.F8, RawInputModifiers.Shift, PhysicalKey.F8, null);
            await WaitFor(() => find.Dialogs.IsOpen, ct);
            Assert.True(find.Dialogs.IsOpen);
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitFor(() => !File.Exists(original), ct);
            Assert.False(File.Exists(original));
            Assert.False(mainDialogs.IsOpen);
        }
        finally
        {
            CloseAll();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task The_log_lists_ignored_folders_and_shows_one_in_the_active_panel()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string skipped = Path.Combine(root, "files", "cache");
            Directory.CreateDirectory(skipped);
            File.WriteAllText(Path.Combine(skipped, "x.txt"), "x");
            services.Settings.SearchIgnoredFolders.Add(new IgnoredFolderEntry { Folder = "cache" });
            var find = await OpenFindAsync(vm, ct);
            await SearchAsync(find, "*.txt", ct);
            Assert.Equal(["a.txt", "b.txt"], find.Found.Order()); // nothing from the ignored folder
            var entry = Assert.Single(find.Session!.Log);
            Assert.Equal(SearchLogKind.Ignored, entry.Kind);
            Assert.Contains("1 not searched", find.Status);

            find.OpenLog();
            await WaitFor(() => find.Dialogs.IsOpen, ct);
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var panel = vm.Workspace.ActiveTab!;
            await WaitFor(() => panel.Location?.Path == skipped, ct);
            Assert.Equal(skipped, panel.Location?.Path);
        }
        finally
        {
            CloseAll();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Hex_criteria_that_cannot_work_say_why_in_the_window()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var find = await OpenFindAsync(vm, ct);
            find.SetContent("4G", hex: true);
            await SearchAsync(find, "*", ct);
            Assert.Contains("not a hex digit", find.Error);
            Assert.Null(find.Session);
            find.SetContent("\"alpha\"", hex: true);
            await SearchAsync(find, "*", ct);
            Assert.Equal(["a.txt"], find.Found); // a.txt holds "alpha"
        }
        finally
        {
            CloseAll();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
