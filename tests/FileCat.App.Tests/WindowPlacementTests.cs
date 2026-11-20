using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using Microsoft.Win32;

namespace FileCat.App.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void Saved_secondary_monitor_rectangle_is_preserved_and_removed_monitor_is_recentred()
    {
        var displays = new[]
        {
            new DisplayWorkArea(0, 0, 1920, 1040, 1),
            new DisplayWorkArea(1920, 0, 1920, 1040, 1),
        };
        var saved = new WindowPlacement { X = 2130, Y = 85, Width = 1100, Height = 740, Maximized = true };
        var both = Assert.IsType<WindowPlacement>(WindowPlacementPolicy.Restore(saved, displays));
        Assert.Equal((2130d, 85d, 1100d, 740d, true),
            (both.X, both.Y, both.Width, both.Height, both.Maximized));

        var removed = Assert.IsType<WindowPlacement>(WindowPlacementPolicy.Restore(saved, displays[..1]));
        Assert.InRange(removed.X, 0, 1920 - removed.Width);
        Assert.InRange(removed.Y, 0, 1040 - removed.Height);
        Assert.Equal(1100, removed.Width);
        Assert.Equal(740, removed.Height);
        Assert.True(removed.Maximized);
    }

    [Fact]
    public void Invalid_or_scaled_geometry_cannot_restore_offscreen_or_oversized()
    {
        var display = new[] { new DisplayWorkArea(-1600, 0, 1600, 900, 1.5) };
        var saved = new WindowPlacement { X = -600, Y = -80, Width = 1280, Height = 800 };
        var result = Assert.IsType<WindowPlacement>(WindowPlacementPolicy.Restore(saved, display));
        Assert.InRange(result.Width, 640, 1600 / 1.5);
        Assert.InRange(result.Height, 420, 900 / 1.5);
        Assert.InRange(result.X, -1600, -1600 + 1600 - result.Width * 1.5);
        Assert.InRange(result.Y, 0, 900 - result.Height * 1.5);

        var offscreen = Assert.IsType<WindowPlacement>(WindowPlacementPolicy.Restore(
            new WindowPlacement { X = 99999, Y = -99999, Width = 800, Height = 600 }, display));
        Assert.InRange(offscreen.X, -1600, -800);
        Assert.InRange(offscreen.Y, 0, 300);
        Assert.Null(WindowPlacementPolicy.Restore(new WindowPlacement { Width = double.NaN }, display));
        Assert.Null(WindowPlacementPolicy.Restore(saved, []));
    }

    [AvaloniaFact]
    public async Task Closing_maximized_saves_the_previous_normal_rectangle_and_restores_it()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 900, height: 620);
        MainWindow? reopened = null;
        try
        {
            var work = window.Screens.Primary?.WorkingArea;
            Assert.NotNull(work);
            var original = new PixelPoint(work.Value.X + 80, work.Value.Y + 70);
            window.Position = original;
            window.Width = 930;
            window.Height = 640;
            window.UpdateLayout();
            await Task.Delay(180, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            window.WindowState = WindowState.Maximized;
            window.Close();
            var saved = Assert.IsType<WindowPlacement>(vm.LastPlacement);
            Assert.True(saved.Maximized);
            Assert.InRange(saved.Width, 925, 935);
            Assert.InRange(saved.Height, 635, 645);
            Assert.InRange(saved.X, original.X - 2, original.X + 2);
            Assert.InRange(saved.Y, original.Y - 2, original.Y + 2);

            reopened = new MainWindow(vm, saved);
            reopened.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowStartupLocation.Manual, reopened.WindowStartupLocation);
            Assert.Equal(WindowState.Maximized, reopened.WindowState);
            reopened.WindowState = WindowState.Normal;
            // A window manager may unmaximize a window that opened maximized to a place of its own (GNOME's mutter
            // put it in the work area's corner); it still comes back to its saved normal bounds.
            reopened.Position = new PixelPoint(work.Value.X, work.Value.Y);
            await Task.Delay(180, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(reopened.Bounds.Width, 925, 935);
            Assert.InRange(reopened.Bounds.Height, 635, 645);
            Assert.InRange(reopened.Position.X, original.X - 2, original.X + 2);
            Assert.InRange(reopened.Position.Y, original.Y - 2, original.Y + 2);
        }
        finally { AccessibilityTests.Close(services, reopened ?? window, root); }
    }

    [AvaloniaFact]
    public void Genuine_high_token_restores_resized_window_without_writing_normal_profile()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity)
            Assert.Skip("Run in a high-integrity Windows test process.");

        string root = Path.Combine(Path.GetTempPath(), "filecat-administrator-window-" + Guid.NewGuid().ToString("N"));
        string keyName = @"SOFTWARE\FileCat.TransferTests." + Guid.NewGuid().ToString("N");
        var registry = new AdministratorTransferRegistry(keyName);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        try
        {
            var paths = AppPaths.Resolve("Work", overrideRoot: root);
            var baseline = new Dictionary<string, string>
            {
                [paths.SettingsFile] = """{"SchemaVersion":1,"Theme":"ClassicDark"}""",
                [paths.SettingsFile + ".bak"] = """{"SchemaVersion":1,"Theme":"Light"}""",
                [paths.WorkspaceFile] = """{"SchemaVersion":1,"Window":{"Width":700,"Height":500}}""",
                [paths.WorkspaceFile + ".bak"] = """{"SchemaVersion":1,"Window":{"Width":740,"Height":540}}""",
            };
            foreach (var (path, contents) in baseline) File.WriteAllText(path, contents);

            var services = AppServices.CreateForPathsWithAdministratorRegistry(paths, registry);
            var vm = new MainViewModel(services);
            var state = vm.LoadSavedWorkspace();
            var placement = vm.LoadStartupWindowPlacement(state, reset: false);
            var window = new MainWindow(vm, placement);
            try
            {
                Assert.True(vm.WorkspaceReadOnly);
                Assert.Null(placement);
                vm.Initialize(state);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.Width = 930;
                window.Height = 640;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }
            finally { AccessibilityTests.Close(services, window, Path.Combine(root, "first-window")); }

            var reopened = AppServices.CreateForPathsWithAdministratorRegistry(paths, registry);
            var reopenedVm = new MainViewModel(reopened);
            var reopenedState = reopenedVm.LoadSavedWorkspace();
            var reopenedWindow = new MainWindow(reopenedVm, reopenedVm.LoadStartupWindowPlacement(reopenedState, reset: false));
            try
            {
                reopenedVm.Initialize(reopenedState);
                reopenedWindow.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowStartupLocation.Manual, reopenedWindow.WindowStartupLocation);
                Assert.InRange(reopenedWindow.Bounds.Width, 925, 935);
                Assert.InRange(reopenedWindow.Bounds.Height, 635, 645);
                Assert.Null(reopenedVm.LoadStartupWindowPlacement(reopenedState, reset: true));
            }
            finally { AccessibilityTests.Close(reopened, reopenedWindow, Path.Combine(root, "second-window")); }

            using var other = AppServices.CreateForPathsWithAdministratorRegistry(AppPaths.Resolve("Other", overrideRoot: root), registry);
            var otherVm = new MainViewModel(other);
            Assert.Null(otherVm.LoadStartupWindowPlacement(otherVm.LoadSavedWorkspace(), reset: false));
            foreach (var (path, contents) in baseline) Assert.Equal(contents, File.ReadAllText(path));
        }
        finally
        {
            machine.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            FileCat.Tests.TestCleanup.DeleteTree(root);
        }
    }
}
