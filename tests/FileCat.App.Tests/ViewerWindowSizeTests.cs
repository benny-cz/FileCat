using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerWindowSizeTests
{
    [Fact]
    public void Saved_viewer_dimensions_are_validated_and_clamped_to_the_current_work_area()
    {
        Assert.Equal(new Size(980, 700), ViewerWindow.RestoreViewerSize(null, null, new Size(1024, 768)));
        Assert.Equal(new Size(1256, 696), ViewerWindow.RestoreViewerSize(3000, 2000, new Size(1280, 720)));
        Assert.Equal(new Size(980, 696), ViewerWindow.RestoreViewerSize(double.NaN, 600, new Size(1280, 720)));
        Assert.Equal(new Size(980, 696), ViewerWindow.RestoreViewerSize(500, double.PositiveInfinity, new Size(1280, 720)));
        Assert.Equal(new Size(376, 226), ViewerWindow.RestoreViewerSize(600, 400, new Size(400, 250)));
    }

    [AvaloniaFact]
    public void User_sized_text_viewer_reopens_as_markdown_and_hex_after_restart()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            using (var services = AppServices.CreateForPaths(paths))
            {
                var text = new ViewerWindow(services, new MemoryContentSource("note.txt", Encoding.UTF8.GetBytes("note")), "note.txt", hex: false);
                text.Show();
                var earlier = new ViewerWindow(services, new MemoryContentSource("earlier.txt", Encoding.UTF8.GetBytes("old")), "earlier.txt", hex: false);
                earlier.Show();
                text.Width = 850;
                text.Height = 600;
                Dispatcher.UIThread.RunJobs();
                text.CaptureRenderedFrame()?.Dispose();
                Assert.InRange(text.Bounds.Width, 849, 851);
                Assert.InRange(text.Bounds.Height, 599, 601);
                text.Close();
                earlier.Close();
                Assert.InRange(services.Settings.ViewerWidth!.Value, 849, 851);
                Assert.InRange(services.Settings.ViewerHeight!.Value, 599, 601);
            }

            using (var services = AppServices.CreateForPaths(paths))
            {
                var markdown = new ViewerWindow(services, new MemoryContentSource("notes.md", Encoding.UTF8.GetBytes("# Notes")), "notes.md", hex: false);
                Assert.InRange(markdown.Width, 849, 851);
                Assert.InRange(markdown.Height, 599, 601);
                markdown.Show();
                markdown.Close();
                // Merely viewing another file must not replace a newer saved size.
                Assert.InRange(services.Settings.ViewerWidth!.Value, 849, 851);

                var hex = new ViewerWindow(services, new MemoryContentSource("sample.exe", [0x4D, 0x5A]), "sample.exe", hex: true);
                Assert.InRange(hex.Width, 849, 851);
                Assert.InRange(hex.Height, 599, 601);
                hex.Show();
                hex.Close();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void Maximizing_then_restoring_viewer_does_not_save_the_maximized_size()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var seed = new ViewerWindow(services, new MemoryContentSource("seed.txt", Encoding.UTF8.GetBytes("seed")), "seed.txt", hex: false);
            seed.Show();
            seed.Width = 850;
            seed.Height = 600;
            Dispatcher.UIThread.RunJobs();
            seed.Close();
            Assert.InRange(services.Settings.ViewerWidth!.Value, 849, 851);
            var viewer = new ViewerWindow(services, new MemoryContentSource("note.txt", Encoding.UTF8.GetBytes("note")), "note.txt", hex: false);
            viewer.Show();
            viewer.CaptureRenderedFrame()?.Dispose();
            // The backend can publish a maximized-sized frame before its
            // WindowState notification. It must not replace the saved size.
            viewer.Width = 2000;
            viewer.Height = 1200;
            Dispatcher.UIThread.RunJobs();
            viewer.WindowState = WindowState.Maximized;
            Dispatcher.UIThread.RunJobs();
            viewer.CaptureRenderedFrame()?.Dispose();
            viewer.WindowState = WindowState.Normal;
            // Native backends can publish a transient maximized-sized layout
            // after the state has already changed back to Normal.
            viewer.Width = 2000;
            viewer.Height = 1200;
            Dispatcher.UIThread.RunJobs();
            viewer.CaptureRenderedFrame()?.Dispose();
            viewer.Close();
            Assert.InRange(services.Settings.ViewerWidth!.Value, 849, 851);
            Assert.InRange(services.Settings.ViewerHeight!.Value, 599, 601);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void A_scaling_change_with_the_viewer_open_does_not_replace_the_size_the_user_chose()
    {
        // On the Win11 VM a change from 100% to 200% shrank an enlarged viewer to fit, and its close saved that size.
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var seed = new ViewerWindow(services, new MemoryContentSource("seed.txt", Encoding.UTF8.GetBytes("seed")), "seed.txt", hex: false);
            seed.Show();
            seed.Width = 850;
            seed.Height = 600;
            Dispatcher.UIThread.RunJobs();
            seed.Close();
            Assert.InRange(services.Settings.ViewerWidth!.Value, 849, 851);
            var viewer = new ViewerWindow(services, new MemoryContentSource("note.txt", Encoding.UTF8.GetBytes("note")), "note.txt", hex: false);
            viewer.Show();
            Dispatcher.UIThread.RunJobs();
            // The display's scaling changes, then the platform resizes the window for it.
            viewer.PlatformImpl!.ScalingChanged!(2.0);
            viewer.Width = 600;
            viewer.Height = 400;
            Dispatcher.UIThread.RunJobs();
            viewer.Close();
            Assert.InRange(services.Settings.ViewerWidth!.Value, 849, 851);
            Assert.InRange(services.Settings.ViewerHeight!.Value, 599, 601);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
