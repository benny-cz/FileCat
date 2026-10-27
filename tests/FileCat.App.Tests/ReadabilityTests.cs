using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ReadabilityTests
{
    [Fact]
    public void Content_size_bounds_untrusted_saved_values_and_density_is_explicit()
    {
        Assert.Equal(13, ReadabilityPolicy.ContentSize(double.NaN));
        Assert.Equal(13, ReadabilityPolicy.ContentSize(double.PositiveInfinity));
        Assert.Equal(9, ReadabilityPolicy.ContentSize(-100));
        Assert.Equal(32, ReadabilityPolicy.ContentSize(1000));
        Assert.True(ReadabilityPolicy.Comfortable("Comfortable"));
        Assert.False(ReadabilityPolicy.Comfortable("comfortable"));
        Assert.False(ReadabilityPolicy.Comfortable("unexpected"));
    }

    [AvaloniaFact]
    public async Task Appearance_saves_separate_content_size_and_comfortable_density_without_rewriting_list_size()
    {
        using var appStyles = new FileCatStyles();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            double listSize = services.Settings.FontSize;
            vm.Execute(CommandIds.Settings);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            window.UpdateLayout();
            var density = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(box => box.Items.OfType<string>().SequenceEqual(["Compact", "Comfortable"]));
            Assert.Equal("Compact", density.SelectedItem);
            var sizes = window.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            Assert.True(sizes.Length >= 2);
            Assert.Equal((decimal)listSize, sizes[0].Value);
            density.SelectedIndex = 1;
            sizes[1].Value = 18;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.False(dialogs.IsOpen);
            Assert.Equal(listSize, services.Settings.FontSize);
            Assert.Equal(18, services.Settings.ContentFontSize);
            Assert.Equal("Comfortable", services.Settings.RowDensity);
            Assert.True(vm.ComfortableDensity);
            Assert.Contains("comfortable", window.Classes);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var toolbarButton = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("toolbar"));
            Assert.True(window.TryFindResource("FcToolbarHeight", window.ActualThemeVariant, out var heightToken));
            Assert.Equal(32d, heightToken);
            Assert.True(toolbarButton.MinHeight >= 32, $"Comfortable toolbar MinHeight = {toolbarButton.MinHeight}; window font = {window.FontSize}; classes = {string.Join(',', toolbarButton.Classes)}");
            Assert.Equal(15, window.FontSize);
            var list = window.GetVisualDescendants().OfType<FileListControl>().First();
            Assert.True(list.ComfortableRows);
            int comfortableCapacity = list.VisibleRowCapacity;
            services.Settings.RowDensity = "Compact";
            vm.ApplySettings();
            window.UpdateLayout();
            Assert.Equal(14, window.FontSize);
            Assert.False(vm.ComfortableDensity);
            Assert.DoesNotContain("comfortable", window.Classes);
            var compactList = window.GetVisualDescendants().OfType<FileListControl>().First();
            Assert.False(compactList.ComfortableRows);
            Assert.True(compactList.VisibleRowCapacity >= comfortableCapacity);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Comfortable_density_keeps_primary_menu_and_dialog_actions_inside_minimum_window()
    {
        using var appStyles = new FileCatStyles();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 480);
        try
        {
            services.Settings.RowDensity = "Comfortable";
            vm.ApplySettings();
            window.UpdateLayout();
            var more = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ToolbarMore");
            AssertInside(window, more);
            vm.Execute(CommandIds.Settings);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            window.UpdateLayout();
            AssertInside(window, window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "OK"));
            AssertInside(window, window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Cancel"));
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Viewer_starts_from_persisted_content_size_and_visible_controls_match_shortcuts()
    {
        using var appStyles = new FileCatStyles();
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            using (var first = AppServices.CreateForPaths(paths))
            {
                first.Settings.ContentFontSize = 17;
                first.Settings.RowDensity = "Comfortable";
                first.SaveSettings();
            }
            using var services = AppServices.CreateForPaths(paths);
            var viewer = new ViewerWindow(services,
                new MemoryContentSource("readme.txt", Encoding.UTF8.GetBytes("content")), "readme.txt", hex: false);
            viewer.Show();
            try
            {
                Assert.Contains("comfortable", viewer.Classes);
                Assert.True(viewer.FontSize >= 15, $"Comfortable viewer font = {viewer.FontSize}");
                Assert.Equal(17, Field<TextViewer>(viewer, "_text").FontSize);
                Assert.Equal(17, Field<HexView>(viewer, "_hex").FontSize);
                Assert.Equal(17, Field<TextViewer>(viewer, "_info").FontSize);
                var bigger = Named<Button>(viewer, "Increase text and hex size");
                var smaller = Named<Button>(viewer, "Decrease text and hex size");
                var size = Named<TextBlock>(viewer, "Text and hex size");
                Assert.Equal("17 size", size.Text);
                bigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(18, Field<TextViewer>(viewer, "_text").FontSize);
                Assert.Equal("18 size", size.Text);
                viewer.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, null);
                Assert.Equal(17, Field<HexView>(viewer, "_hex").FontSize);
                Assert.True(smaller.IsEnabled);
                for (int i = 0; i < 40; i++) bigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(32, Field<TextViewer>(viewer, "_text").FontSize);
                Assert.False(bigger.IsEnabled);
                for (int i = 0; i < 40; i++) smaller.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(9, Field<HexView>(viewer, "_hex").FontSize);
                Assert.False(smaller.IsEnabled);
                Assert.Equal(17, services.Settings.ContentFontSize);
            }
            finally { viewer.Close(); }
        }
        finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public async Task Comparison_uses_preferred_size_for_text_and_bytes_with_visible_and_keyboard_adjustment()
    {
        var compare = CompareWindow.Open("a.txt", new MemoryContentSource("a.txt", Encoding.UTF8.GetBytes("abc")),
            "b.txt", new MemoryContentSource("b.txt", Encoding.UTF8.GetBytes("abd")), contentFontSize: 19, comfortable: true);
        try
        {
            for (int i = 0; i < 250 && compare.IsComparing; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Contains("comfortable", compare.Classes);
            var hex = Field<HexCompareView>(compare, "_hex");
            var rows = Field<ListBox>(compare, "_rows");
            Assert.Equal(19, hex.FontSize);
            Assert.Equal(19, rows.FontSize);
            compare.UpdateLayout();
            var sourceCells = compare.GetVisualDescendants().OfType<CompareSourceCell>().ToArray();
            Assert.NotEmpty(sourceCells);
            Assert.All(sourceCells, cell => Assert.Equal(19, cell.FontSize));
            Named<Button>(compare, "Increase comparison content size").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(20, hex.FontSize);
            compare.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, null);
            Assert.Equal(19, hex.FontSize);
            Assert.Equal("19 size", Named<TextBlock>(compare, "Comparison content size").Text);
        }
        finally { compare.Close(); }
    }

    [AvaloniaFact]
    public void Hex_editor_starts_from_preferred_size_and_has_visible_adjustment()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-readability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "sample.bin");
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            services.Settings.ContentFontSize = 21;
            Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
            var editor = Assert.Single(HexEditorWindow.OpenWindows);
            try
            {
                var hex = editor.GetVisualDescendants().OfType<HexView>().Single();
                Assert.Equal(21, hex.FontSize);
                Named<Button>(editor, "Decrease editor content size")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(20, hex.FontSize);
                hex.Focus();
                editor.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Digit0, null);
                Assert.Equal(21, hex.FontSize);
                Assert.Equal("21 size", Named<TextBlock>(editor, "Editor content size").Text);
            }
            finally { editor.Close(); }
        }
        finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }
    }

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsType<T>(owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner));

    [AvaloniaFact]
    public void Viewer_controls_keep_an_opaque_live_palette_surface_during_theme_changes()
    {
        using var styles = new FileCatStyles();
        string before = ThemeManager.RequestedName;
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var viewer = new ViewerWindow(services,
                new MemoryContentSource("owned.txt", Encoding.UTF8.GetBytes("visible\n")),
                "owned.txt", hex: false);
            try
            {
                ThemeManager.Apply("Psychedelic");
                viewer.Show();
                Named<Button>(viewer, "Show or hide viewer Find bar")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var surfaces = viewer.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Child is WrapPanel panel &&
                        (panel.Children.OfType<Button>().Any(button => button.Content as string == "Refresh") ||
                         panel.Children.OfType<TextBox>().Any(box => box.PlaceholderText == "Find (Ctrl+F)")))
                    .ToArray();
                Assert.Equal(2, surfaces.Length);
                foreach (string palette in new[] { "Psychedelic", "HighContrast", "Classic" })
                {
                    ThemeManager.Apply(palette);
                    viewer.CaptureRenderedFrame()?.Dispose();
                    foreach (var surface in surfaces)
                    {
                        var brush = Assert.IsType<SolidColorBrush>(surface.Background);
                        Assert.Equal(255, brush.Color.A);
                        Assert.Equal(Color.Parse(ThemeManager.Current.Window), brush.Color);
                    }
                }
            }
            finally { viewer.Close(); }
        }
        finally
        {
            ThemeManager.Apply(before);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void Report_and_hex_editor_toolbars_follow_the_opaque_palette_surface()
    {
        using var styles = new FileCatStyles();
        string before = ThemeManager.RequestedName;
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "owned.bin");
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
            var editor = Assert.Single(HexEditorWindow.OpenWindows);
            var report = new ReportWindow("Owned report", "owned fixture", _ => Task.FromResult("safe report\n"));
            report.Show();
            try
            {
                var save = Field<Button>(editor, "_save");
                var refresh = Field<Button>(report, "_refresh");
                Border Surface(Window window, Button button) => window.GetVisualDescendants()
                    .OfType<Border>().Single(border => border.Child is WrapPanel panel &&
                        panel.Children.Contains(button));
                var editorToolbar = Surface(editor, save);
                var reportToolbar = Surface(report, refresh);
                foreach (string palette in new[] { "Psychedelic", "HighContrast", "Classic" })
                {
                    ThemeManager.Apply(palette);
                    editor.CaptureRenderedFrame()?.Dispose();
                    report.CaptureRenderedFrame()?.Dispose();
                    foreach (var surface in new[] { editorToolbar, reportToolbar })
                    {
                        var brush = Assert.IsType<SolidColorBrush>(surface.Background);
                        Assert.Equal(255, brush.Color.A);
                        Assert.Equal(Color.Parse(ThemeManager.Current.Window), brush.Color);
                    }
                }
            }
            finally { report.Close(); editor.Close(); }
        }
        finally
        {
            ThemeManager.Apply(before);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static void AssertInside(Window window, Control control)
    {
        var topLeft = control.TranslatePoint(new Point(0, 0), window);
        Assert.NotNull(topLeft);
        Assert.InRange(topLeft.Value.X, 0, window.ClientSize.Width);
        Assert.InRange(topLeft.Value.Y, 0, window.ClientSize.Height);
        Assert.True(topLeft.Value.X + control.Bounds.Width <= window.ClientSize.Width + 1);
        Assert.True(topLeft.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
    }

    private sealed class FileCatStyles : IDisposable
    {
        private readonly StyleInclude _include = new(new Uri("avares://FileCat/"))
        {
            Source = new Uri("avares://FileCat/Themes/Styles.axaml"),
        };

        public FileCatStyles() => Application.Current!.Styles.Add(_include);
        public void Dispose() => Application.Current!.Styles.Remove(_include);
    }
}
