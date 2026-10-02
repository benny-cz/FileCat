using Avalonia;

namespace FileCat.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The picture decoder (a process of its own for each picture the viewer shows): no window, no instance check.
        if (args.Length > 0 && args[0] == Services.PictureWorker.Argument) return Services.PictureWorker.Run(args);
        if (args.Length > 0 && args[0] == Services.WindowsContextMenu.HostArgument)
            return Services.WindowsContextMenu.RunHost(args);
        // Answered before any window or instance check: packages are smoke-tested this way on machines without a display.
        if (args is ["--version"] or ["-v"])
        {
            var version = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";
            Console.WriteLine("FileCat " + version.Split('+')[0]);
            return 0;
        }
        var options = StartupOptions.Parse(args);
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
