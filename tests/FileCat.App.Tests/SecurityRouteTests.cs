using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Diagnostics;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class SecurityRouteTests
{
    [AvaloniaFact]
    public async Task Native_Windows_persisted_medium_written_tools_cannot_gain_a_high_token()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_PERSISTED") != "1")
            Assert.Skip("This persisted-settings fixture runs only in the disposable Windows VM.");

        string? stateRoot = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_STATE_ROOT");
        var paths = stateRoot is null ? AppPaths.Resolve(profile: "security-acceptance-20261028") : AppPaths.Resolve(overrideRoot: stateRoot);
        Assert.True(File.Exists(paths.SettingsFile), "Stage owned settings from a medium-token process first.");
        var priorFactory = PlatformFactory.WindowsFactory;
        AppServices? services = null;
        MainViewModel? vm = null;
        Avalonia.Controls.Window? window = null;
        string? root = null;
        try
        {
            PlatformFactory.WindowsFactory = () => new WindowsPlatform();
            (services, vm, var opened, root) = AccessibilityTests.OpenMainWindow(statePaths: paths);
            window = opened;
            Assert.Equal(StateLoadStatus.Loaded, services.SettingsStatus);
            Assert.NotNull(services.Settings.Editor);
            Assert.Single(services.Settings.Associations);
            Assert.Single(services.Settings.UserCommands);
            string file = Path.Combine(root, "files", "a.txt");
            string editMarker = file + ".persisted-launched";
            string commandMarker = Path.Combine(root, "files") + ".persisted-launched";
            bool high = services.Shell.IsElevated;

            var editor = typeof(MainViewModel).GetMethod("LaunchEditor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            editor.Invoke(vm, [file]);
            await Task.Delay(3000);
            Assert.True(File.Exists(editMarker) == !high, $"Editor: elevated={high}, marker={File.Exists(editMarker)}, notice={vm.Notification}");
            if (File.Exists(editMarker)) File.Delete(editMarker);

            Assert.True(vm.TryLaunchAssociation("open", file));
            await Task.Delay(3000);
            Assert.True(File.Exists(editMarker) == !high, $"Association: elevated={high}, marker={File.Exists(editMarker)}, notice={vm.Notification}");
            if (File.Exists(editMarker)) File.Delete(editMarker);

            var userMenu = typeof(MainViewModel).GetMethod("ShowUserMenuAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pending = (Task)userMenu.Invoke(vm, [null])!;
            var dialogs = (FileCat.App.Views.OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 100 && !dialogs.IsOpen; i++) await Task.Delay(20);
            Assert.True(dialogs.IsOpen);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(3000);
            Assert.True(File.Exists(commandMarker) == !high, $"User command: elevated={high}, marker={File.Exists(commandMarker)}, notice={vm.Notification}");
            if (File.Exists(commandMarker)) File.Delete(commandMarker);
            if (high) Assert.Contains("will not launch the configured external tool", vm.Notification, StringComparison.Ordinal);
        }
        finally
        {
            if (services is not null && window is not null && root is not null)
                AccessibilityTests.Close(services, window, root);
            PlatformFactory.WindowsFactory = priorFactory;
        }
    }

    [AvaloniaFact]
    public async Task Native_Windows_editor_gate_refuses_implicit_and_configured_high_token_launches()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_EDITOR") != "1")
            Assert.Skip("This owned executable fixture runs only in the disposable Windows VM.");

        string candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Microsoft VS Code", "Code.exe");
        Assert.True(File.Exists(candidate), "The disposable VM must stage its owned editor probe first.");
        var originalFactory = PlatformFactory.WindowsFactory;
        AppServices? services = null;
        MainViewModel? vm = null;
        Avalonia.Controls.Window? window = null;
        string? root = null;
        var ownedProcesses = new List<Process>();
        HashSet<int> before = Process.GetProcessesByName("Code").Select(p => p.Id).ToHashSet();
        try
        {
            Assert.Equal(candidate, FileCat.Core.Tools.ToolLauncher.DetectEditor().Executable);
            PlatformFactory.WindowsFactory = () => new WindowsPlatform();
            (services, vm, var opened, root) = AccessibilityTests.OpenMainWindow();
            window = opened;
            Assert.Equal(OperatingSystem.IsWindows(), services.Shell is FileCat.Platform.Windows.WindowsShellServices);
            services.Settings.Editor = null;
            services.Settings.Associations.Clear();
            string path = Path.Combine(root, "files", "a.txt");
            string marker = path + ".editor-launched";
            var launch = typeof(MainViewModel).GetMethod("LaunchEditor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            launch.Invoke(vm, [path]);
            await Task.Delay(1000);
            var after = Process.GetProcessesByName("Code").Where(p => !before.Contains(p.Id)).ToList();
            ownedProcesses.AddRange(after);
            if (services.Shell.IsElevated)
            {
                Assert.Empty(after);
                Assert.False(File.Exists(marker));
                Assert.Contains("will not choose an external editor automatically", vm.Notification, StringComparison.Ordinal);
                services.Settings.Editor = new ToolDefinition { Name = "Explicit owned editor", Executable = candidate, Arguments = ["{file}"] };
                launch.Invoke(vm, [path]);
                await Task.Delay(500);
                var explicitChildren = Process.GetProcessesByName("Code").Where(p => !before.Contains(p.Id)).ToList();
                ownedProcesses.AddRange(explicitChildren);
                Assert.Empty(explicitChildren);
                Assert.False(File.Exists(marker));
                Assert.Contains("will not launch the configured external tool", vm.Notification, StringComparison.Ordinal);
            }
            else
            {
                for (int i = 0; i < 30 && !File.Exists(marker); i++)
                    await Task.Delay(100);
                Assert.True(File.Exists(marker), "The ordinary-token implicit editor did not start.");
                Assert.DoesNotContain("will not choose an external editor automatically", vm.Notification ?? "", StringComparison.Ordinal);
                File.Delete(marker);
                services.Settings.Editor = new ToolDefinition { Name = "Configured owned editor", Executable = candidate, Arguments = ["{file}"] };
                launch.Invoke(vm, [path]);
                for (int i = 0; i < 30 && !File.Exists(marker); i++) await Task.Delay(100);
                Assert.True(File.Exists(marker), "The ordinary-token configured editor did not start.");
            }
        }
        finally
        {
            ownedProcesses.AddRange(Process.GetProcessesByName("Code").Where(p => !before.Contains(p.Id)));
            foreach (var process in ownedProcesses.DistinctBy(p => p.Id))
            {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                process.Dispose();
            }
            if (services is not null && window is not null && root is not null)
                AccessibilityTests.Close(services, window, root);
            PlatformFactory.WindowsFactory = originalFactory;
        }
    }

    [AvaloniaFact]
    public async Task Failed_sidecar_scan_keeps_the_default_log_and_export_free_of_the_folder_path()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        string? bundle = null;
        bool priorMode = AppLog.DiagnosticMode;
        try
        {
            string folder = Path.Combine(root, "files");
            string source = Path.Combine(folder, "a.txt");
            Directory.Move(folder, folder + ".unavailable");
            var method = typeof(MainViewModel).GetMethod("PlanVerificationAsync", BindingFlags.Static | BindingFlags.NonPublic)!;

            async Task<object> Plan()
            {
                var pending = (Task)method.Invoke(null, new object?[] { new[] { ItemRef.ForFileSystemPath(source, EntryKind.File) }, true })!;
                await pending;
                return pending.GetType().GetProperty("Result")!.GetValue(pending)!;
            }

            AppLog.DiagnosticMode = false;
            object plan = await Plan();
            var planType = plan.GetType();
            Assert.Empty((System.Collections.ICollection)planType.GetProperty("Beside")!.GetValue(plan)!);
            Assert.Empty((System.Collections.ICollection)planType.GetProperty("Lists")!.GetValue(plan)!);
            string unread = (string)planType.GetProperty("Unread")!.GetValue(plan)!;
            Assert.DoesNotContain(folder, unread, StringComparison.Ordinal);
            Assert.Contains("folder", unread, StringComparison.Ordinal);
            string log = File.ReadAllText(AppLog.CurrentLogFile);
            Assert.Contains("could not be read", log, StringComparison.Ordinal);
            Assert.DoesNotContain(folder, log, StringComparison.Ordinal);
            bundle = DiagnosticsBundle.Write(services, services.Paths.LocalDirectory);
            using (var zip = ZipFile.OpenRead(bundle))
            using (var reader = new StreamReader(zip.GetEntry("filecat.log")!.Open()))
            {
                string exportedLog = await reader.ReadToEndAsync();
                Assert.Contains("could not be read", exportedLog, StringComparison.Ordinal);
                Assert.DoesNotContain(folder, exportedLog, StringComparison.Ordinal);
            }

            AppLog.DiagnosticMode = true;
            await Plan();
            Assert.Contains(folder, File.ReadAllText(AppLog.CurrentLogFile), StringComparison.Ordinal);
        }
        finally
        {
            AppLog.DiagnosticMode = priorMode;
            if (bundle is not null) File.Delete(bundle);
            AccessibilityTests.Close(services, window, root);
        }
    }
}
