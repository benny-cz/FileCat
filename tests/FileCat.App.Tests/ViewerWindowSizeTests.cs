using System.Text;
using Avalonia;
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
}
