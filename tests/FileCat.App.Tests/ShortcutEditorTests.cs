using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>Shortcuts change by pressing them: F1, find the command, F2, press the keys, Enter.</summary>
public sealed class ShortcutEditorTests
{
    [AvaloniaFact]
    public async Task F2_in_the_keyboard_reference_takes_the_pressed_shortcut()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && window.FocusManager?.GetFocusedElement() is not FileCat.App.Controls.FileListControl; i++) await Task.Delay(20, ct);

            async Task ChangeAsync(string search, Key key, RawInputModifiers mods, PhysicalKey physical)
            {
                vm.Execute(CommandIds.Help);
                for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
                var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == "Search command, shortcut, or command ID");
                for (int i = 0; i < 100 && !box.IsFocused; i++) await Task.Delay(20, ct);
                box.Text = search;
                await Task.Delay(50, ct);
                window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
                Border? Capture() => window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => AutomationProperties.GetName(b) == "New shortcut");
                for (int i = 0; i < 250 && Capture() is not { IsFocused: true }; i++) await Task.Delay(20, ct);
                Assert.True(Capture()?.IsFocused);
                window.KeyPress(key, mods, physical, null);
                await Task.Delay(50, ct);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                // The reference opens again on the command, with its new shortcut.
                for (int i = 0; i < 250 && Capture() is not null; i++) await Task.Delay(20, ct);
                for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            }

            // A free chord.
            await ChangeAsync("Duplicate here", Key.J, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.J);
            Assert.Equal(["Ctrl+Shift+J"], services.Settings.KeyBindings[CommandIds.Duplicate]);
            Assert.Equal(CommandIds.Duplicate, services.Keymap.Resolve(new KeyChord("J", KeyMods.Ctrl | KeyMods.Shift), CommandContext.Panel));

            // A chord another command uses: that command gives it up.
            await ChangeAsync("Duplicate here", Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            Assert.Equal(CommandIds.Duplicate, services.Keymap.Resolve(new KeyChord("F5", KeyMods.None), CommandContext.Panel));
            Assert.Empty(services.Keymap.GetChords(CommandIds.Copy));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
