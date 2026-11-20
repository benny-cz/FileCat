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
    /// <summary>Restores and remembers a user-sized normal window without saving automatic display fits.</summary>
    internal static void RememberSize(Window window, Size defaults, Size minimum,
        Func<(double? Width, double? Height)> load, Action<Size> save)
    {
        Size Restore()
        {
            Size area = WorkingArea(window);
            var (width, height) = load();
            var size = RestoreSize(width, height, area, defaults, minimum);
            window.MinWidth = Math.Min(minimum.Width, Math.Max(1, area.Width - 24));
            window.MinHeight = Math.Min(minimum.Height, Math.Max(1, area.Height - 24));
            window.Width = size.Width;
            window.Height = size.Height;
            return size;
        }

        Size initial = Restore();
        Size lastNormal = initial;
        bool opened = false, userSized = false, restoring = false, pending = false;
        var restoreTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        var resizeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = restoreTimer.Interval };
        void Remember(Size size)
        {
            if (!ValidDimension(size.Width) || !ValidDimension(size.Height)) return;
            lastNormal = size;
            if (Math.Abs(size.Width - initial.Width) > 1 || Math.Abs(size.Height - initial.Height) > 1) userSized = true;
        }
        void SuppressResize(bool ongoing)
        {
            resizeTimer.Stop();
            pending = false;
            restoring = true;
            restoreTimer.Stop();
            if (!ongoing) restoreTimer.Start();
        }

        window.Opened += (_, _) =>
        {
            // Screen information can first be available when the native window opens.
            if (Math.Abs(window.Width - initial.Width) < 1 && Math.Abs(window.Height - initial.Height) < 1) Restore();
            lastNormal = initial = new Size(window.Width, window.Height);
            opened = true;
        };
        restoreTimer.Tick += (_, _) => { restoreTimer.Stop(); restoring = false; };
        resizeTimer.Tick += (_, _) =>
        {
            resizeTimer.Stop();
            pending = false;
            if (!restoring && window.WindowState == WindowState.Normal) Remember(window.Bounds.Size);
        };
        Attach(window, SuppressResize);
        window.ScalingChanged += (_, _) => SuppressResize(false);
        window.PropertyChanged += (_, change) =>
        {
            if (!opened || change.Property != Window.WindowStateProperty) return;
            // Some backends publish the maximized frame before the new state.
            SuppressResize(window.WindowState != WindowState.Normal);
        };
        window.SizeChanged += (_, args) =>
        {
            if (!opened || restoring || window.WindowState != WindowState.Normal ||
                !ValidDimension(args.NewSize.Width) || !ValidDimension(args.NewSize.Height)) return;
            pending = true;
            resizeTimer.Stop();
            resizeTimer.Start();
        };
        window.Closed += (_, _) =>
        {
            restoreTimer.Stop();
            resizeTimer.Stop();
            if (pending && !restoring && window.WindowState == WindowState.Normal) Remember(window.Bounds.Size);
            if (userSized) save(lastNormal);
        };
    }

    internal static Size RestoreSize(double? width, double? height, Size usable, Size defaults, Size minimum)
    {
        double availableWidth = double.IsFinite(usable.Width) && usable.Width > 0 ? usable.Width : 3840;
        double availableHeight = double.IsFinite(usable.Height) && usable.Height > 0 ? usable.Height : 2160;
        double maximumWidth = Math.Min(8192, Math.Max(1, availableWidth - 24));
        double maximumHeight = Math.Min(8192, Math.Max(1, availableHeight - 24));
        bool stored = width is { } w && height is { } h && ValidDimension(w) && ValidDimension(h);
        return new Size(Math.Clamp(stored ? width!.Value : defaults.Width, Math.Min(minimum.Width, maximumWidth), maximumWidth),
            Math.Clamp(stored ? height!.Value : defaults.Height, Math.Min(minimum.Height, maximumHeight), maximumHeight));
    }

    private static bool ValidDimension(double value) => double.IsFinite(value) && value is >= 240 and <= 8192;

    private static Size WorkingArea(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return new Size(3840, 2160);
        double scale = double.IsFinite(screen.Scaling) && screen.Scaling > 0 ? screen.Scaling : 1;
        double width = screen.WorkingArea.Width / scale, height = screen.WorkingArea.Height / scale;
        return new Size(double.IsFinite(width) && width > 0 ? width : 3840,
            double.IsFinite(height) && height > 0 ? height : 2160);
    }

    /// <summary>
    /// Fits <paramref name="window"/> once it opens and again when its scaling or the screens change. <paramref name="fitting"/>
    /// runs with true before a correction and false after it, so a window that remembers its size can tell it from a resize.
    /// </summary>
    internal static void Attach(Window window, Action<bool>? fitting = null)
    {
        // GNOME fits a window to its screen itself (Find at 200% on a 1,920 by 1,200 display: 1,786 by 1,062 pixels),
        // and an X11 window can report a placeholder position before the window manager places it, which a fit would
        // pin to the corner.
        if (OperatingSystem.IsLinux()) return;
        bool closed = false; Action? detachScreens = null;
        void Later() => Dispatcher.UIThread.Post(() => { if (!closed) Fit(window, fitting); }, DispatcherPriority.Background);
        void ScreensChanged(object? sender, EventArgs e) => Later();
        window.Opened += (_, _) =>
        {
            if (closed) return;
            Later();
            var screens = window.Screens;
            screens.Changed += ScreensChanged;
            detachScreens = () => screens.Changed -= ScreensChanged;
        };
        window.ScalingChanged += (_, _) => Later();
        window.Closed += (_, _) => { closed = true; detachScreens?.Invoke(); detachScreens = null; };
    }

    /// <summary>Shrinks and moves a normal window whose frame reaches past its screen's working area; returns whether it did.</summary>
    internal static bool Fit(Window window, Action<bool>? fitting = null)
    {
        if (!window.IsVisible || window.WindowState != WindowState.Normal) return false;
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return false;
        // Screen coordinates use the screen's scaling, which need not equal the render backing scale.
        // Avalonia.Native reports macOS points at Scaling=1 even on a Retina window rendering at 2x.
        double scale = screen.Scaling;
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
