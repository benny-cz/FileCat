using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class DiskUsageWindowSizeTests
{
    [AvaloniaFact]
    public void Disk_usage_restores_its_last_normal_size_after_closing_and_restarting()
    {
        string root = Directory.CreateTempSubdirectory("filecat-disk-usage-size-").FullName;
        var paths = AppPaths.Resolve(overrideRoot: root);
        try
        {
            using (var services = AppServices.CreateForPaths(paths))
            {
                services.DiskSpace.Open([]);
                var window = Assert.IsType<DiskSpaceWindow>(services.DiskSpace.Window);
                window.Width = 810;
                window.Height = 560;
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()?.Dispose();
                Assert.InRange(window.Bounds.Width, 809, 811);
                Assert.InRange(window.Bounds.Height, 559, 561);
                window.Close();
            }

            using (var services = AppServices.CreateForPaths(paths))
            {
                services.DiskSpace.Open([]);
                var window = Assert.IsType<DiskSpaceWindow>(services.DiskSpace.Window);
                Assert.InRange(window.Width, 809, 811);
                Assert.InRange(window.Height, 559, 561);

                // A backend may publish the larger frame before reporting the maximized state.
                window.Width = 1200;
                window.Height = 760;
                Dispatcher.UIThread.RunJobs();
                window.WindowState = WindowState.Maximized;
                Dispatcher.UIThread.RunJobs();
                window.Close();
            }

            using var restarted = AppServices.CreateForPaths(paths);
            restarted.DiskSpace.Open([]);
            var restored = Assert.IsType<DiskSpaceWindow>(restarted.DiskSpace.Window);
            Assert.InRange(restored.Width, 809, 811);
            Assert.InRange(restored.Height, 559, 561);
            Assert.Null(restarted.Settings.ViewerWidth);
            Assert.Null(restarted.Settings.ViewerHeight);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
