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
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class BackgroundContextMenuTests
{
    private static readonly FieldInfo OpenMenu = typeof(PanelView).GetField("_openItemMenu",
        BindingFlags.Static | BindingFlags.NonPublic)!;

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
        public void CloseWhenIdle() => inner.CloseWhenIdle();
        public void OpenMenuBar() => inner.OpenMenuBar();
        public void ShowContextMenu() => inner.ShowContextMenu();
        public void ShowNotification(string message, bool isError = false) => inner.ShowNotification(message, isError);
        public void ReloadChrome() => inner.ReloadChrome();
    }
}
