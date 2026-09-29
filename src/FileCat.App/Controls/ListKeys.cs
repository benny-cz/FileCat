using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FileCat.App.Controls;

/// <summary>
/// Keys and focus for lists: a focused <see cref="ListBoxItem"/> takes Enter and Space for itself before handlers on
/// its list see them, and a <see cref="ListBox"/> itself cannot take focus. These make a list's own keys and its initial
/// focus work anyway.
/// </summary>
internal static class ListKeys
{
    /// <summary>
    /// Runs <paramref name="action"/> when <paramref name="key"/> is pressed anywhere in <paramref name="list"/>, before its
    /// items see the key; the key counts as handled when the action returns true.
    /// </summary>
    public static void OnKey(Control list, Key key, Func<KeyEventArgs, bool> action) =>
        list.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == key && !e.Handled && action(e)) e.Handled = true;
        }, RoutingStrategies.Tunnel);

    /// <summary>
    /// Focuses the selected item of a list (its first when none is selected), as a click would; other controls are
    /// focused as they are. False when nothing took focus.
    /// </summary>
    public static bool Focus(Control target, NavigationMethod method = NavigationMethod.Tab)
    {
        if (target is SelectingItemsControl { Focusable: false, ItemCount: > 0 } list)
        {
            int index = Math.Clamp(list.SelectedIndex, 0, list.ItemCount - 1);
            list.ScrollIntoView(index);
            list.UpdateLayout();
            if (list.ContainerFromIndex(index) is { } item && item.Focus(method)) return true;
        }
        return target.Focus(method);
    }
}
