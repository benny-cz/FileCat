using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace FileCat.App.Controls;

/// <summary>
/// Clicks in a list that serves a text box (the command search, folder suggestions): the item under the button when it
/// goes up is chosen, and the press never takes the keyboard from the box. Taking it would close the list while the
/// button is still down, so the click would do nothing.
/// </summary>
internal static class ListClicks
{
    public static void ChooseOnClick<T>(ListBox list, Action<T> choose) where T : class
    {
        T? pressed = null;
        list.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            pressed = null;
            if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed || ItemAt<T>(e.Source) is not { } item) return;
            pressed = item;
            list.SelectedItem = item;
            // Handled before the item sees it: nothing takes focus, and the list stays open until the button goes up.
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            var chosen = pressed;
            pressed = null;
            // By value: a list refilled while the button was down (new suggestions) holds equal, new items.
            if (chosen is null || !EqualityComparer<T>.Default.Equals(chosen, ItemAt<T>(e.Source))) return;
            e.Handled = true;
            choose(chosen);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static T? ItemAt<T>(object? source) where T : class =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as T;
}
