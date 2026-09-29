using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;

namespace FileCat.App.Tests;

/// <summary>Typing a path in a panel's location box suggests the folders that complete it.</summary>
public sealed class PathSuggestionTests
{
    [Fact]
    public void Folders_starting_with_the_typed_name_are_suggested()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-suggest", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            foreach (var name in new[] { "alpha", "Alpine", "beta" }) Directory.CreateDirectory(Path.Combine(root, name));
            var hidden = Directory.CreateDirectory(Path.Combine(root, ".hidden"));
            if (OperatingSystem.IsWindows()) hidden.Attributes |= FileAttributes.Hidden;
            File.WriteAllText(Path.Combine(root, "alps.txt"), "a file, not a folder");
            var ct = TestContext.Current.CancellationToken;
            string sep = Path.DirectorySeparatorChar.ToString();

            Assert.Equal([Path.Join(root, "alpha"), Path.Join(root, "Alpine")], PathSuggestions.For(root + sep + "al", includeHidden: false, ct));
            Assert.Equal([Path.Join(root, "alpha"), Path.Join(root, "Alpine"), Path.Join(root, "beta")], PathSuggestions.For(root + sep, includeHidden: false, ct));
            Assert.Contains(Path.Join(root, ".hidden"), PathSuggestions.For(root + sep, includeHidden: true, ct));
            Assert.Empty(PathSuggestions.For(root + sep + "zz", includeHidden: false, ct));
            Assert.Empty(PathSuggestions.For(Path.Join(root, "missing", "al"), includeHidden: false, ct));
            Assert.Empty(PathSuggestions.For("relative" + sep + "al", includeHidden: false, ct));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Typing_a_path_suggests_its_folders_Tab_completes_and_Enter_goes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string files = Path.Combine(root, "files");
            Directory.CreateDirectory(Path.Combine(files, "alpha", "inner"));
            Directory.CreateDirectory(Path.Combine(files, "alpine"));
            var panel = vm.Workspace.Panels[0];
            vm.Workspace.Activate(panel);
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            var box = view.GetVisualDescendants().OfType<TextBox>().First(t => AutomationProperties.GetName(t) == "Location");
            // The window puts the keyboard in the active list when it opens; then the location box takes it.
            for (int i = 0; i < 250 && window.FocusManager?.GetFocusedElement() is not FileCat.App.Controls.FileListControl; i++) await Task.Delay(20, ct);
            await Task.Delay(100, ct);
            view.FocusPathBox();
            for (int i = 0; i < 100 && !box.IsFocused; i++) await Task.Delay(20, ct);
            Assert.True(box.IsFocused);

            box.Text = Path.Combine(files, "al");
            for (int i = 0; i < 250 && view.PathSuggestionsShown.Count != 2; i++) await Task.Delay(20, ct);
            Assert.True(view.PathSuggestionsShown.Count == 2, $"focused={box.IsFocused} text={box.Text} focus={window.FocusManager?.GetFocusedElement()?.GetType().Name}");
            Assert.Equal([Path.Combine(files, "alpha"), Path.Combine(files, "alpine")], view.PathSuggestionsShown);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); // the first: alpha
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Equal(Path.Combine(files, "alpha") + Path.DirectorySeparatorChar, box.Text);
            Assert.True(box.IsFocused); // Tab completed instead of leaving the box
            for (int i = 0; i < 250 && view.PathSuggestionsShown.Count != 1; i++) await Task.Delay(20, ct);
            Assert.Equal([Path.Combine(files, "alpha", "inner")], view.PathSuggestionsShown);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && panel.ActiveTab?.Location?.Path != Path.Combine(files, "alpha", "inner"); i++) await Task.Delay(20, ct);
            Assert.Equal(Path.Combine(files, "alpha", "inner"), panel.ActiveTab?.Location?.Path);
            Assert.Empty(view.PathSuggestionsShown);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
