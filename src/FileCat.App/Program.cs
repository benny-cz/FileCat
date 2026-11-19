using Avalonia;
using FileCat.Platform.Windows;

namespace FileCat.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (TryRunStorageCloudQueryWorker(args) is { } workerExit) return workerExit;
        if (args.Length > 0 && Services.ArchiveWorker.IsArgument(args[0])) return Services.ArchiveWorker.Run(args);
        if (args.Length > 0 && Services.ArchiveWorker.IsLinuxArgument(args[0])) return Services.ArchiveWorker.RunLinux(args);
        if (args.Length > 0 && Services.ArchiveWorker.IsMacArgument(args[0])) return Services.ArchiveWorker.RunMac(args);
        // Linux enters a filesystem/network domain here, then replaces this trusted launcher with the worker below.
        if (args.Length > 0 && args[0] == Services.LinuxPictureSandbox.Argument)
            return Services.LinuxPictureSandbox.Run(args);
        // The picture decoder (a process of its own for each picture the viewer shows): no window, no instance check.
        if (args.Length > 0 && args[0] == Services.PictureWorker.Argument) return Services.PictureWorker.Run(args);
        if (args.Length > 0 && args[0] == Services.WindowsContextMenu.HostArgument)
            return Services.WindowsContextMenu.RunHost(args);
        // A narrow read-only Linux process check can run after PolicyKit authorization without starting a GUI.
        if (args.Length > 0 && args[0] == RecoveryProcessCensus.Argument)
            return RecoveryProcessCensus.RunHelper(args);
        // Answered before any window or instance check: packages are smoke-tested this way on machines without a display.
        if (args is ["--version"] or ["-v"])
        {
            var version = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";
            Console.WriteLine("FileCat " + version.Split('+')[0]);
            return 0;
        }
        var options = StartupOptions.Parse(args);
        using var storageCloudQuery = OperatingSystem.IsWindows() ? StartStorageCloudQuery(typeof(Program)) : null;
        bool menuTrace = Environment.GetEnvironmentVariable("FILECAT_MENU_TRACE") == "1" && options.DataRoot is not null;
        if (menuTrace)
        {
            var paths = Core.State.AppPaths.Resolve(options.Profile, dataRoot: options.DataRoot);
            Core.Diagnostics.AppLog.Initialize(paths.LogDirectory);
            Core.Diagnostics.AppLog.Info($"MenuTrace entry pid={Environment.ProcessId} module={typeof(Program).Module.ModuleVersionId} compat={CompatibleRendering}");
        }
        if (SingleInstance.TryForward(options))
            return 0;
        App.StartupOptions = options;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) when (menuTrace)
        {
            Core.Diagnostics.AppLog.Error("MenuTrace startup failed", ex);
            Console.Error.WriteLine(ex);
            throw;
        }
        finally
        {
            SingleInstance.Release();
        }
    }

    internal static int? TryRunStorageCloudQueryWorker(string[] args) =>
        args is [WindowsStorageCloudQuery.WorkerArgument] ? WindowsStorageCloudQuery.RunWorker() : null;

    internal static IDisposable StartStorageCloudQuery(Type entryPoint)
    {
        string executable = Environment.ProcessPath ?? Path.Join(AppContext.BaseDirectory, entryPoint.Assembly.GetName().Name + ".exe");
        string? managedEntry = Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? entryPoint.Assembly.Location : null;
        return WindowsStorageCloudQuery.UseWorker(executable, managedEntry);
    }

    /// <summary>FILECAT_RENDERING=compat restores Avalonia's default composition (troubleshooting).</summary>
    public static bool CompatibleRendering =>
        string.Equals(Environment.GetEnvironmentVariable("FILECAT_RENDERING"), "compat", StringComparison.OrdinalIgnoreCase);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
        // TV-01: a low-latency swap chain keeps held-key scrolling at the display rate (continuous paging p95 frame
        // interval 16.8 ms versus 19-21 ms with the default composition); the other modes remain fallbacks.
        if (!CompatibleRendering)
        {
            builder = builder.With(new Win32PlatformOptions
            {
                CompositionMode =
                [
                    Win32CompositionMode.LowLatencyDxgiSwapChain, Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.DirectComposition, Win32CompositionMode.RedirectionSurface,
                ],
            });
        }
        return builder;
    }
}
