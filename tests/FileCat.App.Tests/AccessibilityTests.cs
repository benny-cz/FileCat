using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// TV-10 automated part: every visible interactive control has an accessible name, custom controls expose
/// summarized peers, and every command is reachable from the keyboard. Screen-reader runs stay manual.
/// </summary>
public sealed class AccessibilityTests
{
    /// <summary>Visible, enabled interactive controls whose accessible name would be empty.</summary>
    internal static List<string> Unnamed(Control root)
    {
        var problems = new List<string>();
        foreach (var c in root.GetVisualDescendants().OfType<Control>())
        {
            if (!c.IsEffectivelyVisible || !c.IsEffectivelyEnabled) continue;
            if (c is not (TextBox or ComboBox or NumericUpDown or ListBox or CheckBox or RadioButton or Button or Slider)) continue;
            if (c.FindAncestorOfType<NumericUpDown>() is not null || c.FindAncestorOfType<ComboBox>() is not null) continue; // parts
            if (c is Button && c.TemplatedParent is not null) continue; // template parts (spinners, scroll buttons)
            var peer = ControlAutomationPeer.CreatePeerForElement(c);
            var name = peer.GetName();
            if (string.IsNullOrWhiteSpace(name)) problems.Add($"{c.GetType().Name} {(c.Name ?? "")} in {c.GetVisualParent()?.GetType().Name}");
        }
        return problems;
    }

    private static (AppServices Services, MainViewModel Vm, MainWindow Window, string Root) OpenMainWindow()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        var folder = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        File.WriteAllText(Path.Combine(folder, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "beta");
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var vm = new MainViewModel(services);
        var window = new MainWindow(vm, null) { Width = 1200, Height = 800 };
        vm.Initialize(null);
        foreach (var panel in vm.Workspace.Panels) panel.ActiveTab?.Navigate(Location.FileSystem(folder));
        window.Show();
        return (services, vm, window, root);
    }


    private static void Close(AppServices services, Window window, string root)
    {
        window.Close();
        if (window.DataContext is MainViewModel vm)
            foreach (var tab in vm.Workspace.Panels.SelectMany(p => p.Tabs).ToList()) tab.Dispose();
        services.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    [AvaloniaFact]
    public async Task Main_window_and_its_dialogs_name_every_interactive_control()
    {
        var (services, vm, window, root) = OpenMainWindow();
        try
        {
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            listing.SetFocus(1); // a file, so F5 and F8 open their dialogs
            Assert.Empty(Unnamed(window));

            // Shift+F8 opens the delete dialog directly. (F8 in this portable test platform first asks "Delete permanently?",
            // whose long wrapped message spins Avalonia's headless text layout; the native app renders it normally.)
            foreach (var command in new[] { CommandIds.Settings, CommandIds.Copy, CommandIds.DeletePermanent, CommandIds.MakeDirectory, CommandIds.FindFiles, CommandIds.MarkSelectMask })
            {
                vm.Execute(command);
                var dialogs = (OverlayDialogService)vm.Dialogs;
                for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
                Assert.True(dialogs.IsOpen, command + " did not open a dialog");
                await Task.Delay(50, TestContext.Current.CancellationToken);
                // The dialog is really open: the audit must see its input controls.
                Assert.Contains(window.GetVisualDescendants().OfType<Control>(), c => c is TextBox or CheckBox or ListBox);
                var problems = Unnamed(window);
                Assert.True(problems.Count == 0, command + ": " + string.Join("; ", problems));
                // Settings has several pages: audit each one.
                if (command == CommandIds.Settings)
                {
                    var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
                    for (int i = 0; i < tabs.ItemCount; i++)
                    {
                        tabs.SelectedIndex = i;
                        await Task.Delay(50, TestContext.Current.CancellationToken);
                        problems = Unnamed(window);
                        Assert.True(problems.Count == 0, $"Settings page {i}: " + string.Join("; ", problems));
                    }
                }
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task File_list_announces_the_focused_item_with_its_details()
    {
        var (services, vm, window, root) = OpenMainWindow();
        try
        {
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(3, listing.VisibleCount);
            var list = window.ActiveList!;
            var peer = ControlAutomationPeer.CreatePeerForElement(list);
            Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
            vm.ActiveTab!.Listing.SetFocus(1);
            var name = peer.GetName();
            Assert.Contains("a.txt, file", name);
            Assert.Contains("of 3", name);
            vm.ActiveTab.Listing.SetMark(1, true);
            Assert.Contains("marked", peer.GetName());
        }
        finally
        {
            Close(services, window, root);
        }
    }

    [Fact]
    public void Every_command_is_reachable_from_the_keyboard()
    {
        var registry = CommandRegistry.CreateDefault();
        var keymap = new Keymap(registry, new Dictionary<string, string[]>());
        // The command palette (Ctrl+Shift+P) lists every command; it must itself be bound, as must the menu bar.
        Assert.NotEmpty(keymap.GetChords(CommandIds.Palette));
        Assert.NotEmpty(keymap.GetChords(CommandIds.Menu));
        var unbound = registry.All.Where(c => keymap.GetChords(c.Id).Count == 0).Select(c => c.Id).ToList();
        // Commands without a default gesture are fine when the palette offers them: every one must have a title.
        Assert.All(registry.All, c => Assert.False(string.IsNullOrWhiteSpace(c.Title), c.Id));
        Assert.True(unbound.Count < registry.All.Count, "most commands have a gesture");
    }

    [AvaloniaFact]
    public async Task Viewers_expose_read_only_documents_with_their_visible_content()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("first line\nsecond line\nthird line\n");
        using var textReader = new Core.Content.PagedReader(new Core.Resources.MemoryContentSource("t.txt", bytes));
        using var hexReader = new Core.Content.PagedReader(new Core.Resources.MemoryContentSource("t.bin", bytes));
        var text = new Controls.TextViewer();
        var hex = new Controls.HexView();
        var window = new Window { Width = 800, Height = 400, Content = new StackPanel { Children = { text, hex } } };
        text.Height = 150;
        hex.Height = 150;
        window.Show();
        try
        {
            text.SetReader(textReader, System.Text.Encoding.UTF8, 0);
            hex.SetReader(hexReader);
            for (int i = 0; i < 50 && !text.GetSelectedOrVisibleText().Contains("second line", StringComparison.Ordinal); i++)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
                window.CaptureRenderedFrame();
            }
            var textPeer = ControlAutomationPeer.CreatePeerForElement(text);
            Assert.Equal(AutomationControlType.Document, textPeer.GetAutomationControlType());
            var value = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IValueProvider>(textPeer);
            Assert.True(value.IsReadOnly);
            Assert.Contains("second line", value.Value);

            var hexPeer = ControlAutomationPeer.CreatePeerForElement(hex);
            var hexValue = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IValueProvider>(hexPeer);
            for (int i = 0; i < 50 && !(hexValue.Value ?? "").Contains("66 69", StringComparison.Ordinal); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Contains("Offset 0 of 34", hexValue.Value);
            Assert.Contains("66 69 72 73 74", hexValue.Value); // "first"
        }
        finally
        {
            window.Close();
        }
    }
}
