using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace FileCat.App.Views;

/// <summary>
/// Keeps a secondary window inside the working area of the screen it is on. Windows does not: a window opened larger
/// than the screen (Find's 1,040 by 720 units on a 200% desktop 962 units wide), or left too large by a scaling change,
/// kept its edges and the controls along them out of reach.
/// </summary>
internal static class WindowFit
{
    /// <summary>
    /// Fits <paramref name="window"/> once it opens and again when its scaling or the screens change. <paramref name="fitting"/>
    /// runs with true before a correction and false after it, so a window that remembers its size can tell it from a resize.
    /// </summary>
    internal static void Attach(Window window, Action<bool>? fitting = null)
    {
        void Later() => Dispatcher.UIThread.Post(() => Fit(window, fitting), DispatcherPriority.Background);
        void ScreensChanged(object? sender, EventArgs e) => Later();
        window.Opened += (_, _) =>
        {
            Later();
            window.Screens.Changed += ScreensChanged;
        };
        window.ScalingChanged += (_, _) => Later();
        window.Closed += (_, _) => window.Screens.Changed -= ScreensChanged;
    }

    /// <summary>Shrinks and moves a normal window whose frame reaches past its screen's working area; returns whether it did.</summary>
    internal static bool Fit(Window window, Action<bool>? fitting = null)
    {
        if (!window.IsVisible || window.WindowState != WindowState.Normal) return false;
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return false;
        double scale = window.RenderScaling;
        var area = screen.WorkingArea;
        var client = window.ClientSize;
        // The title bar and borders around the client area, in units.
        double frameWidth = Math.Max(0, (window.FrameSize?.Width ?? client.Width) - client.Width);
        double frameHeight = Math.Max(0, (window.FrameSize?.Height ?? client.Height) - client.Height);
        double maxWidth = area.Width / scale - frameWidth, maxHeight = area.Height / scale - frameHeight;
        if (maxWidth < 1 || maxHeight < 1) return false;
        double width = Math.Min(client.Width, maxWidth), height = Math.Min(client.Height, maxHeight);
        bool resize = width < client.Width - 0.5 || height < client.Height - 0.5;
        int outerWidth = (int)Math.Ceiling((width + frameWidth) * scale), outerHeight = (int)Math.Ceiling((height + frameHeight) * scale);
        // The title bar first: the window's left and top edges stay on the screen.
        int x = Math.Max(area.X, Math.Min(window.Position.X, area.Right - outerWidth));
        int y = Math.Max(area.Y, Math.Min(window.Position.Y, area.Bottom - outerHeight));
        bool move = x != window.Position.X || y != window.Position.Y;
        if (!resize && !move) return false;
        fitting?.Invoke(true);
        try
        {
            if (resize)
            {
                window.MinWidth = Math.Min(window.MinWidth, width);
                window.MinHeight = Math.Min(window.MinHeight, height);
                window.Width = width;
                window.Height = height;
            }
            if (move) window.Position = new PixelPoint(x, y);
        }
        finally
        {
            fitting?.Invoke(false);
        }
        return true;
    }
}
