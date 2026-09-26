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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(vm, state?.Window);
            vm.Initialize(state);
            vm.OpenArguments(StartupOptions);
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
}
