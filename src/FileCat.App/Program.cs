using Avalonia;

namespace FileCat.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = StartupOptions.Parse(args);
        if (!options.NewInstance && SingleInstance.TryForward(options))
            return 0;
        App.StartupOptions = options;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Release();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
