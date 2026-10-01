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
        bool benchmark = StartupOptions.BenchmarkCount > 0;
        // The TV-01 benchmark runs on isolated, temporary state with synthetic listings.
        string? benchmarkRoot = benchmark ? Path.Combine(Path.GetTempPath(), "FileCat-benchmark-" + Guid.NewGuid().ToString("N")) : null;
        var services = AppServices.Initialize(StartupOptions.Profile, benchmarkRoot, StartupOptions.DataRoot);
        string syntheticRoot = Path.Combine(benchmarkRoot ?? services.Paths.TempDirectory, "synthetic");
        if (benchmark)
        {
            for (int i = 0; i < 4; i++) Directory.CreateDirectory(Path.Combine(syntheticRoot, "synthetic-" + i));
            services.Providers.Register(new SyntheticListingProvider(services.Providers.Get(Core.Resources.Schemes.FileSystem), syntheticRoot, StartupOptions.BenchmarkCount));
        }
        AppLog.Info($"FileCat starting on {services.Platform.Name}, profile {services.Paths.ProfileName}{(services.Paths.IsPortable ? " (portable)" : "")}");
        ThemeManager.Apply(services.Settings.Theme);
        ThemeAnimation.SetAllowed(services.Settings.ThemeAnimations);
        if (PlatformSettings is { } ps)
        {
            ps.ColorValuesChanged += (_, _) =>
            {
                if (services.Settings.Theme == "System") Dispatcher.UIThread.Post(() => ThemeManager.Apply("System"));
            };
        }
        services.Icons.Native = NativeIconSource.TryCreate(services.Shell, () => services.AllowedShellPictures) ?? MacIconSource.TryCreate() ?? FreedesktopIconSource.TryCreate();

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
        UiStallMonitor.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(vm, state?.Window);
            // Periodic autosave keeps the layout recoverable even after a hard crash (plan §19.2).
            var autosave = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background, (_, _) => vm.SaveWorkspace(null));
            autosave.Start();
            vm.Initialize(state);
            vm.OpenArguments(StartupOptions, initial: true);
            if (!benchmark) _ = vm.CheckForUpdatesOnStartupAsync();
            desktop.MainWindow = window;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            // Signing out or shutting down while operations run: Windows shows the reason set by the view model and
            // lets the user decide; interrupted jobs are reviewed at the next start.
            desktop.ShutdownRequested += (_, e) =>
            {
                if (vm.ShouldBlockSessionEnd) e.Cancel = true;
            };
            if (!benchmark && services.Shell.IsElevated)
                vm.Notify("FileCat is running as administrator: every operation has full rights, and Windows blocks drag and drop from other programs. Start it normally for everyday work.", true);
            if (services.Paths.PortableUnavailableReason is { } portable) vm.Notify(portable, true);
            desktop.Exit += (_, _) =>
            {
                // Sign-out or a forced exit: running jobs stop at their next safe boundary and journal the rest.
                vm.StopJobsForExit(TimeSpan.FromSeconds(3));
                services.Dispose();
                if (benchmarkRoot is not null)
                {
                    try { Directory.Delete(benchmarkRoot, recursive: true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            };
            if (benchmark)
            {
                window.Opened += async (_, _) => await NativeBenchmark.RunAsync(window, vm, services, StartupOptions, syntheticRoot);
            }
            else
            {
                SingleInstance.ArgumentsReceived += args => Dispatcher.UIThread.Post(() =>
                {
                    vm.OpenArguments(StartupOptions.Parse(args));
                    if (window.WindowState == Avalonia.Controls.WindowState.Minimized) window.WindowState = Avalonia.Controls.WindowState.Normal;
                    window.Activate();
                });
                SingleInstance.StartServer(StartupOptions.Profile, StartupOptions.DataRoot);
            }
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
