using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Diagnostics;
using FileCat.Core.Platform;
using FileCat.Core.State;

namespace FileCat.App;

public partial class App : Application
{
    public static StartupOptions StartupOptions { get; set; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        PlatformFactory.WindowsFactory = () => new Platform.Windows.WindowsPlatform();
        var services = AppServices.Initialize(StartupOptions.Profile);
        AppLog.Info($"FileCat starting on {services.Platform.Name}, profile {services.Paths.ProfileName}{(services.Paths.IsPortable ? " (portable)" : "")}");
        ThemeManager.Apply(services.Settings.Theme);
        if (PlatformSettings is { } ps)
        {
            ps.ColorValuesChanged += (_, _) =>
            {
                if (services.Settings.Theme == "System") Dispatcher.UIThread.Post(() => ThemeManager.Apply("System"));
            };
        }
        services.Icons.Native = NativeIconSource.TryCreate(services.Shell);

        var vm = new MainViewModel(services);
        WorkspaceState? state = null;
        if (!StartupOptions.ResetLayout)
        {
            state = JsonFileStore.Load(services.Paths.WorkspaceFile, StateJsonContext.Default.WorkspaceState,
                WorkspaceState.CurrentSchema, () => new WorkspaceState(), out var wsStatus);
            if (wsStatus is StateLoadStatus.CorruptUsingDefaults) AppLog.Warn("Workspace was corrupt; using the default layout.");
            if (wsStatus is StateLoadStatus.NewerSchemaReadOnly) state = null;
        }

        InstallCrashGuard(services, vm);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(vm, state?.Window);
            // Periodic autosave keeps the layout recoverable even after a hard crash (plan §19.2).
            var autosave = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background, (_, _) => vm.SaveWorkspace(null));
            autosave.Start();
            vm.Initialize(state);
            vm.OpenArguments(StartupOptions, initial: true);
            desktop.MainWindow = window;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => services.Dispose();
            SingleInstance.ArgumentsReceived += args => Dispatcher.UIThread.Post(() =>
            {
                vm.OpenArguments(StartupOptions.Parse(args));
                if (window.WindowState == Avalonia.Controls.WindowState.Minimized) window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Activate();
            });
            SingleInstance.StartServer(StartupOptions.Profile);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// A UI exception is logged with its stack and reported instead of terminating FileCat; jobs keep their
    /// journals either way. A tight loop of failures (e.g. a failing render) still ends the process.
    /// </summary>
    private static void InstallCrashGuard(AppServices services, MainViewModel vm)
    {
        var recent = new Queue<DateTime>();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            var now = DateTime.UtcNow;
            recent.Enqueue(now);
            while (recent.Count > 0 && now - recent.Peek() > TimeSpan.FromSeconds(3)) recent.Dequeue();
            AppLog.Error("Unhandled UI exception", e.Exception);
            try
            {
                File.AppendAllText(Path.Combine(services.Paths.LogDirectory, "crash.log"), $"{now:O}\n{e.Exception}\n\n");
            }
            catch (IOException) { }
            if (recent.Count > 5) return; // repeated failures: let the process end rather than loop
            e.Handled = true;
            vm.Notify("Something went wrong: " + e.Exception.Message + " FileCat kept running; details are in the diagnostics log.", true);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(services.Paths.LogDirectory, "crash.log"), $"{DateTime.UtcNow:O} (fatal)\n{e.ExceptionObject}\n\n");
                vm.SaveWorkspace(null);
            }
            catch (Exception) { }
        };
    }
}
