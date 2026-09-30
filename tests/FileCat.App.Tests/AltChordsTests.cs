using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileCat.App.Controls;

namespace FileCat.App.Tests;

/// <summary>A window's own Alt shortcut leaves the menu bar closed when Alt is released, and no control sees the key.</summary>
public sealed class AltChordsTests
{
    [AvaloniaFact]
    public void A_window_shortcut_with_Alt_does_not_open_the_menu_bar_when_Alt_is_released()
    {
        var box = new TextBox();
        var menu = new Menu { ItemsSource = new[] { new MenuItem { Header = "_File" } } };
        var window = new Window { Content = new DockPanel { Children = { menu, box } }, Width = 400, Height = 200 };
        DockPanel.SetDock(menu, Dock.Top);
        int ran = 0, boxSaw = 0;
        // The window's own shortcut, as FileCat's windows add theirs: tunneling, after the window's constructor.
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.Alt)
            {
                ran++;
                AltChords.Handle(window, e);
            }
        }, RoutingStrategies.Tunnel);
        AltChords.Attach(window);
        box.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.F1) boxSaw++; }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        window.Show();
        box.Focus();
        try
        {
            window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            window.KeyPress(Key.F1, RawInputModifiers.Alt, PhysicalKey.F1, null);
            window.KeyRelease(Key.F1, RawInputModifiers.Alt, PhysicalKey.F1, null);
            window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            Assert.Equal(1, ran);
            Assert.Equal(0, boxSaw);
            Assert.False(menu.IsOpen);
            Assert.Same(box, window.FocusManager?.GetFocusedElement());
            // Alt alone still opens it.
            window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            Assert.True(menu.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }
}
