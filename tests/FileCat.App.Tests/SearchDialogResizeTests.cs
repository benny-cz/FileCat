using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.App.ViewModels;
using FileCat.Core.Search;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class SearchDialogResizeTests
{
    [AvaloniaFact]
    public async Task Resize_grip_supports_keyboard_bounds_window_changes_and_reopen()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 720);
        try
        {
            ProductStyles(window);
            for (int open = 0; open < 2; open++)
            {
                var pending = vm.Dialogs.ShowCustomAsync("Resizable search", new TextBox { Text = "search" },
                    [new DialogButton("Close", "close", IsCancel: true)], resizable: true);
                Layout(window);
                var card = Card(window);
                var grip = Assert.Single(card.GetVisualDescendants().OfType<Thumb>(), t => t.Name == "DialogResizeGrip");
                Assert.Equal("Resize dialog", AutomationProperties.GetName(grip));
                double beforeDragWidth = card.Bounds.Width, beforeDragHeight = card.Bounds.Height;
                grip.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent, Vector = new Vector(32, 24) });
                Layout(window);
                Assert.InRange(card.Bounds.Width - beforeDragWidth, 31, 33);
                Assert.InRange(card.Bounds.Height - beforeDragHeight, 23, 25);
                double width = card.Bounds.Width, height = card.Bounds.Height;
                Assert.True(grip.Focus());
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
                Layout(window);
                Assert.InRange(card.Bounds.Width - width, 15, 17);
                Assert.InRange(card.Bounds.Height - height, 47, 49);
                double expandedWidth = card.Bounds.Width, expandedHeight = card.Bounds.Height;
                window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
                window.KeyPress(Key.Up, RawInputModifiers.Shift, PhysicalKey.ArrowUp, null);
                Layout(window);
                Assert.InRange(expandedWidth - card.Bounds.Width, 47, 49);
                Assert.InRange(expandedHeight - card.Bounds.Height, 47, 49);
                window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
                window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
                Layout(window);

                window.Width = 480;
                window.Height = 360;
                Layout(window);
                var host = window.FindControl<Panel>("OverlayHost")!;
                Assert.InRange(card.Bounds.Width, 1, host.Bounds.Width - 48 + 1);
                Assert.InRange(card.Bounds.Height, 1, host.Bounds.Height - 48 + 1);
                Assert.True(card.TranslatePoint(default, window) is { } p && p.X >= 0 && p.Y >= 0);
                window.Width = 1000;
                window.Height = 720;
                Layout(window);
                Assert.InRange(card.Bounds.Width - width, 15, 17);
                Assert.InRange(card.Bounds.Height - height, 47, 49);
                ((OverlayDialogService)vm.Dialogs).CancelAll();
                Assert.Equal("close", await pending);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Registry_results_and_ignored_folders_gain_room_when_the_dialog_expands()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1200, height: 900);
        try
        {
            ProductStyles(window);
            if (OperatingSystem.IsWindows())
            {
                services.Providers.Register(new WindowsRegistryProvider());
                var registry = RegistrySearchDialog.ShowAsync(vm, new Location(Schemes.Registry, "HKCU"));
                Layout(window);
                var card = Card(window);
                var results = Assert.Single(card.GetVisualDescendants().OfType<ListBox>(),
                    b => AutomationProperties.GetName(b) == "Registry search results");
                double before = results.Bounds.Height;
                var grip = Assert.Single(card.GetVisualDescendants().OfType<Thumb>(), t => t.Name == "DialogResizeGrip");
                Assert.True(grip.Focus());
                for (int i = 0; i < 5; i++)
                    window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
                Layout(window);
                Assert.True(results.Bounds.Height >= before + 40,
                    $"Registry result height {results.Bounds.Height} should grow from {before}.");
                window.Width = 640;
                window.Height = 420;
                Layout(window);
                var scroll = Assert.Single(card.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "DialogBodyScroll");
                var term = Assert.Single(card.GetVisualDescendants().OfType<TextBox>(), t => t.PlaceholderText == "Name or stored data");
                Assert.InRange(card.Bounds.Width, 1, window.FindControl<Panel>("OverlayHost")!.Bounds.Width - 48 + 1);
                Assert.InRange(scroll.Viewport.Height, 24, 420);
                Assert.InRange(term.Bounds.Width, 1, scroll.Viewport.Width + 1);
                Assert.InRange(results.Bounds.Width, 1, scroll.Viewport.Width + 1);
                ((OverlayDialogService)vm.Dialogs).CancelAll();
                Assert.Equal(SearchDialogOutcome.Closed, (await registry).Outcome);
                window.Width = 1200;
                window.Height = 900;
                Layout(window);
            }

            var ignored = FindDialogs.IgnoredFoldersAsync(vm.Dialogs,
                [new IgnoredFolderEntry { Folder = "node_modules", Enabled = true }]);
            Layout(window);
            var foldersCard = Card(window);
            var list = Assert.Single(foldersCard.GetVisualDescendants().OfType<ListBox>(),
                b => AutomationProperties.GetName(b) == "Ignored folders");
            double listBefore = list.Bounds.Height;
            var folderGrip = Assert.Single(foldersCard.GetVisualDescendants().OfType<Thumb>(), t => t.Name == "DialogResizeGrip");
            Assert.True(folderGrip.Focus());
            for (int i = 0; i < 4; i++)
                window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
            Layout(window);
            Assert.True(list.Bounds.Height >= listBefore + 40,
                $"Ignored-folders list height {list.Bounds.Height} should grow from {listBefore}.");
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            Assert.Null(await ignored);

            var advanced = FindDialogs.AdvancedAsync(vm.Dialogs, new AdvancedSearchCriteria());
            Layout(window);
            Assert.Single(Card(window).GetVisualDescendants().OfType<Thumb>(), t => t.Name == "DialogResizeGrip");
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            Assert.Null(await advanced);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static DialogCard Card(Control root) => Assert.Single(root.GetVisualDescendants().OfType<DialogCard>());
    private static void ProductStyles(Window window) => window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/"))
        { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
