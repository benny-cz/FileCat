using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FileCat.App.Controls;

/// <summary>
/// macOS: Avalonia reports a modifier change as a press or release of the key in the system's change event, once per
/// changed modifier. A modifier set by synthesized input, such as AppleScript's <c>using command down</c>, comes with
/// no key, which Avalonia reads as the A key: FileCat saw ⌘A or ⌃A (Select all) before every such ⌘ or ⌃ shortcut, so
/// ⌘C copied the whole folder, and an AZERTY layout turns the same event into ⌘Q. An A key event whose modifiers differ
/// from the modifiers last seen is such a change: it is marked handled where its route starts, before any window or
/// control sees it. A modifier pressed or released on a keyboard arrives as its own key and only updates the
/// modifiers seen.
/// </summary>
internal static class SyntheticModifierKeys
{
    private const KeyModifiers Tracked = KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta;

    /// <summary>The modifiers of the last key event; unknown after a window's activation changes.</summary>
    internal sealed class State
    {
        private KeyModifiers? _held;
        private int _pending;

        /// <summary>Whether the event is a modifier change reported as the A key; records the event's modifiers.</summary>
        internal bool IsModifierChange(PhysicalKey key, KeyModifiers modifiers, bool down)
        {
            modifiers &= Tracked;
            var previous = _held;
            _held = modifiers;
            if (key != PhysicalKey.A)
            {
                _pending = 0;
                return false;
            }
            if (previous == modifiers)
            {
                // Avalonia raises one event per modifier that changed, all with the modifiers after the change.
                if (_pending == 0) return false;
                _pending--;
                return true;
            }
            // Unknown modifiers: a release is harmless either way, and a press with no modifier is a typed key.
            if (previous is null && (!down || modifiers == KeyModifiers.None)) return false;
            _pending = BitOperations.PopCount((uint)(modifiers ^ (previous ?? KeyModifiers.None))) - 1;
            return true;
        }

        /// <summary>Modifiers changed while no FileCat window received keys.</summary>
        internal void Forget()
        {
            _held = null;
            _pending = 0;
        }
    }

    private static readonly State s_state = new();
    private static KeyEventArgs? s_seen;

    internal static void Install()
    {
        if (OperatingSystem.IsMacOS()) Attach(s_state);
    }

    /// <summary>Filters key events in every window; disposing removes the filter.</summary>
    internal static IDisposable Attach(State state)
    {
        // A class handler runs before the handlers of the element its route reaches; the first is the route's start.
        var down = InputElement.KeyDownEvent.AddClassHandler<Interactive>((_, e) => Filter(state, e, down: true), RoutingStrategies.Tunnel, handledEventsToo: true);
        var up = InputElement.KeyUpEvent.AddClassHandler<Interactive>((_, e) => Filter(state, e, down: false), RoutingStrategies.Tunnel, handledEventsToo: true);
        var activation = WindowBase.IsActiveProperty.Changed.AddClassHandler<WindowBase>((_, _) => state.Forget());
        return new Detach(down, up, activation);
    }

    private static void Filter(State state, KeyEventArgs e, bool down)
    {
        if (ReferenceEquals(e, s_seen)) return;
        s_seen = e;
        if (state.IsModifierChange(e.PhysicalKey, e.KeyModifiers, down)) e.Handled = true;
    }

    private sealed class Detach(params IDisposable[] subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
