using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerToolbarHierarchyTests
{
    [AvaloniaFact]
    public async Task Viewer_keeps_modes_and_core_actions_visible_with_search_and_secondary_actions_reachable()
    {
        using var fixture = new Fixture();
        var viewer = new ViewerWindow(fixture.Services,
            new MemoryContentSource("note.txt", Encoding.UTF8.GetBytes("alpha beta alpha")), "note.txt", hex: false);
        viewer.Show();
        try
        {
            var findBar = Field<WrapPanel>(viewer, "_findBar");
            var search = Field<TextBox>(viewer, "_search");
            var mode = Named<TextBlock>(viewer, "Current viewer mode");
            var find = Named<Button>(viewer, "Show or hide viewer Find bar");
            var more = Named<Button>(viewer, "More viewer actions");
            Assert.Equal("Mode: Text", mode.Text);
            Assert.False(findBar.IsVisible);
            Assert.True(find.IsVisible);
            Assert.True(more.IsVisible);
            viewer.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Assert.True(findBar.IsVisible);
            Assert.True(search.IsFocused);
            search.Text = "alpha";
            find.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(findBar.IsVisible);
            Assert.Equal("alpha", search.Text);
            find.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(findBar.IsVisible);
            Assert.Equal("alpha", search.Text);
            var menuItems = Assert.IsAssignableFrom<IEnumerable<Control>>(more.ContextMenu!.ItemsSource)
                .OfType<MenuItem>().ToArray();
            var actions = menuItems.Select(item => item.Header as string).ToArray();
            Assert.Contains("Checksum…", actions);
            Assert.Contains("Copy visible source text", actions);
            Assert.Contains("Copy displayed text", actions);
            menuItems.Single(item => item.Header as string == "Checksum…")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(Field<Border>(viewer, "_checksumPanel").IsVisible);

            viewer.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null);
            Assert.Equal("Mode: Hex", mode.Text);
            await viewer.ShowInfoAsync();
            Assert.Equal("Mode: Info", mode.Text);
            foreach (var (width, height) in new[] { (640, 420), (1280, 720) })
            {
                viewer.Width = width;
                viewer.Height = height;
                viewer.UpdateLayout();
                AssertInside(viewer, more);
                AssertInside(viewer, find);
                Assert.True(Field<TextViewer>(viewer, "_info").Bounds.Height > 100);
            }
        }
        finally { viewer.Close(); }
    }

    [AvaloniaFact]
    public void Hex_editor_keeps_save_and_history_visible_and_retains_find_query_behind_more_menu()
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.Root, "edit.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        Assert.Null(HexEditorWindow.OpenOrActivate(fixture.Services, path));
        var editor = Assert.Single(HexEditorWindow.OpenWindows);
        try
        {
            var findBar = Field<WrapPanel>(editor, "_findBar");
            var search = Field<TextBox>(editor, "_search");
            var find = Named<Button>(editor, "Show or hide hex editor Find bar");
            var more = Named<Button>(editor, "More hex editor actions");
            Assert.False(findBar.IsVisible);
            Assert.True(Field<Button>(editor, "_save").IsVisible);
            Assert.True(Field<Button>(editor, "_undo").IsVisible);
            Assert.True(Field<Button>(editor, "_redo").IsVisible);
            editor.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Assert.True(findBar.IsVisible);
            Assert.True(search.IsFocused);
            search.Text = "01 02";
            find.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(findBar.IsVisible);
            find.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("01 02", search.Text);
            var menuItems = Assert.IsAssignableFrom<IEnumerable<Control>>(more.ContextMenu!.ItemsSource)
                .OfType<MenuItem>().ToArray();
            var actions = menuItems.Select(item => item.Header as string).ToArray();
            Assert.Contains("Save As…", actions);
            Assert.Contains("Export patch…", actions);
            Assert.Contains("Apply patch…", actions);
            Assert.Contains("Stop copying", actions);
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(menuItems.Single(item => item.Header as string == "Save As…").IsEnabled);
            Assert.False(menuItems.Single(item => item.Header as string == "Export patch…").IsEnabled);
            AssertInside(editor, more);
        }
        finally { editor.Close(); }
    }

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsType<T>(owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner));

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static void AssertInside(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(point);
        Assert.InRange(point.Value.X, 0, window.ClientSize.Width);
        Assert.True(point.Value.X + control.Bounds.Width <= window.ClientSize.Width + 1);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "filecat-viewer-toolbar-" + Guid.NewGuid().ToString("N"));
        public AppServices Services { get; }

        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Root));
        }

        public void Dispose()
        {
            Services.Dispose();
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
