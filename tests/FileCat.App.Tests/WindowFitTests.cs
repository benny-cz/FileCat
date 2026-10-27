using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Views;

namespace FileCat.App.Tests;

/// <summary>Secondary windows stay inside the working area of their screen, and a fit is not taken for a resize.</summary>
public sealed class WindowFitTests
{
    [AvaloniaFact]
    public void A_window_larger_than_its_screen_is_shrunk_and_moved_inside_the_working_area()
    {
        var window = new Window { Width = 1040, Height = 720, MinWidth = 640, MinHeight = 420 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            Assert.NotNull(screen);
            var area = screen.WorkingArea;
            double scale = window.RenderScaling;
            // Larger than the screen and partly above and left of it, as a scaling change can leave it.
            window.Width = area.Width / scale + 300;
            window.Height = area.Height / scale + 200;
            window.Position = new PixelPoint(area.X - 50, area.Y - 40);
            Dispatcher.UIThread.RunJobs();
            var calls = new List<bool>();
            Assert.True(WindowFit.Fit(window, calls.Add));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([true, false], calls);
            Assert.True(window.Width <= area.Width / scale + 0.5, $"width {window.Width} in {area.Width / scale}");
            Assert.True(window.Height <= area.Height / scale + 0.5, $"height {window.Height} in {area.Height / scale}");
            Assert.True(window.Position.X >= area.X && window.Position.Y >= area.Y, $"position {window.Position} in {area}");
            Assert.True(window.MinWidth <= window.Width && window.MinHeight <= window.Height);
            // Inside already: nothing to do, and no correction is announced.
            calls.Clear();
            Assert.False(WindowFit.Fit(window, calls.Add));
            Assert.Empty(calls);
        }
        finally
        {
            window.Close();
        }
    }
}
