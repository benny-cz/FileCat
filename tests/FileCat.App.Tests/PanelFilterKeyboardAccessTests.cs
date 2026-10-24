using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class PanelFilterKeyboardAccessTests
{
    [AvaloniaFact]
    public void Shortcut_reaches_only_the_active_panels_inline_filter_even_in_a_narrow_window()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420);
        try
        {
            Assert.Equal(CommandIds.FocusPanelFilter,
                services.Keymap.Resolve(new KeyChord("F", KeyMods.Ctrl | KeyMods.Shift), CommandContext.Panel));
            var command = Assert.Single(vm.CommandSearchEntries(), entry => entry.Id == CommandIds.FocusPanelFilter);
            Assert.Equal("Edit active panel filter", command.Title);
            Assert.Equal("Ctrl+Shift+F", command.Gesture);
            var panels = vm.Workspace.Panels;
            var filters = panels.Select(panel => window.GetVisualDescendants().OfType<PanelView>()
                .Single(view => ReferenceEquals(view.DataContext, panel)).GetVisualDescendants()
                .OfType<TextBox>().Single(box => box.Name == "FilterBox")).ToArray();
            for (int i = 0; i < panels.Count; i++)
            {
                vm.Workspace.Activate(panels[i]);
                window.FocusActivePanel();
                window.KeyPress(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F, null);
                Assert.True(filters[i].IsFocused);
                Assert.True(filters[i].IsEffectivelyVisible);
                Assert.Equal(filters[i].Text?.Length, filters[i].SelectionEnd - filters[i].SelectionStart);
                Assert.False(filters[1 - i].IsFocused);

                // The direct route must keep the inline correction flow rather than opening a second filter dialog.
                filters[i].Text = "\"unfinished";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.True(filters[i].IsFocused);
                Assert.Equal("\"unfinished", filters[i].Text);
                Assert.NotNull(panels[i].ActiveTab!.FilterValidationError);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.False(filters[i].IsFocused);
                Assert.Null(panels[i].ActiveTab!.FilterValidationError);
                Assert.Equal(PanelView.AllItemsMask, filters[i].Text);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
