using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileCat.Core.Diagnostics;

namespace FileCat.App.Services;

/// <summary>Opt-in, bounded menu lifecycle tracing in the existing diagnostics log; no text or paths are recorded.</summary>
internal static class MenuInteractionTrace
{
    public static void Install(Window window, Menu menu)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_MENU_TRACE") != "1") return;
        var attached = new ConditionalWeakTable<MenuItem, object>();
        int remaining = 250;
        void Write(string message)
        {
            if (remaining <= 0) return;
            remaining--;
            AppLog.Info($"MenuTrace pid={Environment.ProcessId} {message}");
            if (remaining == 0) AppLog.Info("MenuTrace record limit reached");
        }
        void Attach(MenuItem item, string id)
        {
            if (attached.TryGetValue(item, out _)) return;
            attached.Add(item, new object());
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property != MenuItem.IsSubMenuOpenProperty) return;
                Write($"item={id} open={item.IsSubMenuOpen} active={window.IsActive}");
                if (!item.IsSubMenuOpen)
                {
                    string stack = new StackTrace(false).ToString();
                    Write("close-stack=" + stack[..Math.Min(stack.Length, 4000)]);
                }
            };
            item.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
                Write($"item={id} press source={e.Source?.GetType().Name} handled={e.Handled} mods={e.KeyModifiers}"),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            item.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
                Write($"item={id} release button={e.InitialPressMouseButton} handled={e.Handled} mods={e.KeyModifiers}"),
                RoutingStrategies.Bubble, handledEventsToo: true);
            int child = 0;
            foreach (var nested in item.Items.OfType<MenuItem>()) Attach(nested, id + "/" + child++);
        }
        void AttachItems()
        {
            Write("menu-items-changed");
            int index = 0;
            foreach (var item in menu.Items.OfType<MenuItem>()) Attach(item, index++.ToString());
        }
        menu.PropertyChanged += (_, e) => { if (e.Property == ItemsControl.ItemsSourceProperty) AttachItems(); };
        window.Activated += (_, _) => Write("window-activated");
        window.Deactivated += (_, _) => Write("window-deactivated");
        window.AddHandler(InputElement.GotFocusEvent, (_, e) => Write("focus-got source=" + e.Source?.GetType().Name),
            RoutingStrategies.Bubble, handledEventsToo: true);
        window.AddHandler(InputElement.LostFocusEvent, (_, e) => Write("focus-lost source=" + e.Source?.GetType().Name),
            RoutingStrategies.Bubble, handledEventsToo: true);
        void Key(string phase, KeyEventArgs e)
        {
            if (e.Key is Avalonia.Input.Key.LeftAlt or Avalonia.Input.Key.RightAlt or Avalonia.Input.Key.Escape or Avalonia.Input.Key.F10)
                Write($"key-{phase} key={e.Key} mods={e.KeyModifiers} handled={e.Handled}");
        }
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => Key("down", e), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyUpEvent, (_, e) => Key("up", e), RoutingStrategies.Tunnel, handledEventsToo: true);
        Action theme = () => Write("theme-changed name=" + ThemeManager.Current.Name);
        ThemeManager.ThemeChanged += theme;
        window.Closed += (_, _) => { Write("window-closed"); ThemeManager.ThemeChanged -= theme; };
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(window, (IntPtr _, uint message, IntPtr wParam, IntPtr lParam, ref bool _) =>
            {
                // Activation/focus/capture/cancel only: no native key or character messages.
                if (message is 0x0006 or 0x0007 or 0x0008 or 0x001C or 0x001F or 0x0215)
                    Write($"native message={message:x4} w={wParam.ToInt64():x} l={lParam.ToInt64():x}");
                return IntPtr.Zero;
            });
        Write($"enabled compat={Program.CompatibleRendering} theme={ThemeManager.Current.Name} module={typeof(MenuInteractionTrace).Module.ModuleVersionId}");
        AttachItems();
    }
}
