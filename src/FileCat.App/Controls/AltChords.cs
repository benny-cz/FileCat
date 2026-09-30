using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace FileCat.App.Controls;

/// <summary>
/// A window's own shortcut with Alt (Alt+F1, Alt+Enter, Alt+Left), marked handled so that releasing Alt does not then
/// open the window's menu bar.
/// </summary>
/// <remarks>
/// A window's tunneling key handlers run last-added first, so a window's own handler runs before Avalonia's access-key
/// handler, which the window's constructor added first. A key marked handled there never reaches it: it takes the Alt
/// for a lone press, and releasing Alt opens the menu bar, which takes the keyboard. After Alt+F1, a drive letter went
/// to the menu instead of the location menu. The key is marked handled one level down instead, on the window's
/// content: after the access-key handler has seen it, before any control does.
/// </remarks>
internal static class AltChords
{
    private sealed class Pending(Control content)
    {
        public Control Content { get; } = content;
        public KeyEventArgs? Args;
    }

    private static readonly ConditionalWeakTable<Window, Pending> s_windows = new();

    /// <summary>
    /// Readies <paramref name="window"/>, once its content is set: an event's route is fixed when it is raised, so the
    /// content's handler must be there before the first key.
    /// </summary>
    public static void Attach(Window window)
    {
        if (window.Content is not Control content) return;
        var pending = new Pending(content);
        content.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (ReferenceEquals(args, pending.Args)) args.Handled = true;
            pending.Args = null;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        s_windows.AddOrUpdate(window, pending);
    }

    /// <summary>Marks <paramref name="e"/> handled: at once, or, with Alt held, after the access-key handler saw it.</summary>
    public static void Handle(Window window, KeyEventArgs e)
    {
        // Only where the key passes through the window's content on its way (not the window itself, or its layers).
        if ((e.KeyModifiers & KeyModifiers.Alt) == 0 || !s_windows.TryGetValue(window, out var pending)
            || !ReferenceEquals(window.Content, pending.Content) || e.Source is not Visual source || !pending.Content.IsVisualAncestorOf(source))
        {
            e.Handled = true;
            return;
        }
        pending.Args = e;
    }
}
