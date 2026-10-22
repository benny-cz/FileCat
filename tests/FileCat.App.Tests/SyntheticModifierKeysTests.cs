using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>
/// macOS reports a modifier set by synthesized input (AppleScript's <c>using command down</c>) with no key, which
/// Avalonia reads as the A key: ⌘C first selected everything, so it copied the whole folder.
/// </summary>
public sealed class SyntheticModifierKeysTests
{
    [Fact]
    public void A_synthesized_command_change_around_a_shortcut_is_dropped_and_the_shortcut_kept()
    {
        var state = new SyntheticModifierKeys.State();
        Assert.False(state.IsModifierChange(PhysicalKey.ArrowDown, KeyModifiers.None, down: true));
        // ⌘C: the Command change as A, the C key, then the release as A.
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.C, KeyModifiers.Meta, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.C, KeyModifiers.Meta, down: false));
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.None, down: false));
        // ⌘A: the change, then the A key itself with the modifiers already seen.
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: false));
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.None, down: false));
        // Typing a after that is a key press.
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.None, down: true));
    }

    [Fact]
    public void Keyboard_modifiers_arrive_as_their_own_keys_and_never_drop_the_A_key()
    {
        var state = new SyntheticModifierKeys.State();
        Assert.False(state.IsModifierChange(PhysicalKey.MetaLeft, KeyModifiers.Meta, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.ShiftLeft, KeyModifiers.Meta | KeyModifiers.Shift, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta | KeyModifiers.Shift, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta | KeyModifiers.Shift, down: false));
        Assert.False(state.IsModifierChange(PhysicalKey.ShiftLeft, KeyModifiers.Meta, down: false));
        Assert.False(state.IsModifierChange(PhysicalKey.MetaLeft, KeyModifiers.None, down: false));
    }

    [Fact]
    public void Modifiers_changed_together_are_one_change_per_modifier()
    {
        var state = new SyntheticModifierKeys.State();
        Assert.False(state.IsModifierChange(PhysicalKey.Escape, KeyModifiers.None, down: true));
        // ⌘⇧A in one change: one event for Command and one for Shift, then the A key.
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta | KeyModifiers.Shift, down: true));
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta | KeyModifiers.Shift, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta | KeyModifiers.Shift, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta | KeyModifiers.Shift, down: false));
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.None, down: false));
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.None, down: false));
        // Shift released while Command is pressed: a release and a press, both with the modifiers after the change.
        Assert.False(state.IsModifierChange(PhysicalKey.ShiftLeft, KeyModifiers.Shift, down: true));
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: false));
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Meta, down: true));
    }

    [Fact]
    public void After_an_activation_change_only_a_press_with_a_modifier_is_taken_for_one()
    {
        var state = new SyntheticModifierKeys.State();
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.None, down: true));
        state.Forget();
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.None, down: false));
        state.Forget();
        Assert.True(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Control, down: true));
        Assert.False(state.IsModifierChange(PhysicalKey.A, KeyModifiers.Control, down: true));
    }

    [AvaloniaFact]
    public async Task A_synthesized_control_change_never_selects_every_file_in_the_main_window()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var listing = vm.ActiveTab!.Listing;
            await FileListFixture.Wait(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            window.ActiveList!.Focus();
            Dispatcher.UIThread.RunJobs();
            // Attached only while this synchronous part runs: other tests press Ctrl+A without a Control key first.
            using (SyntheticModifierKeys.Attach(new SyntheticModifierKeys.State()))
            {
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                window.KeyRelease(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, listing.MarkedCount);

                window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, listing.MarkedCount);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
