using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.Platform;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class SecurityRouteTests
{
    [AvaloniaFact]
    public async Task Native_high_token_browsing_checksum_sidecar_leaves_verification_unchecked()
    {
        string? staged = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_LINK_FIXTURE");
        if (staged is null) Assert.Skip("Run in the owned medium/high Windows fixture.");
        Assert.True(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity);
        var previousFactory = PlatformFactory.WindowsFactory;
        AppServices? services = null;
        MainViewModel? vm = null;
        MainWindow? window = null;
        string? testRoot = null;
        try
        {
            PlatformFactory.WindowsFactory = () => new WindowsPlatform();
            (services, vm, window, testRoot) = AccessibilityTests.OpenMainWindow();
            var tab = vm.ActiveTab!;
            tab.Navigate(FileCat.Core.Resources.Location.FileSystem(staged));
            for (int i = 0; i < 250 && tab.Listing.State != ListingState.Complete; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(ListingState.Complete, tab.Listing.State);
            Assert.True(tab.HasSidecars);
            // A second listing schedules FolderChanged; the visible row schedules Automatic.
            tab.Refresh();
            for (int i = 0; i < 250 && tab.Listing.State != ListingState.Complete; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(tab.Listing.FocusName("verification-linked.txt"));
            for (int i = 0; i < 50; i++)
            {
                window.CaptureRenderedFrame();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            var listing = tab.Listing;
            int row = Enumerable.Range(0, listing.VisibleCount)
                .Single(index => listing.GetVisible(index).Name == "verification-linked.txt");
            var entry = listing.GetVisible(row);
            int store = listing.GetStoreIndex(row);
            Assert.Null(tab.Verification(entry, store));
            Assert.DoesNotContain("checksums:", tab.StatusLeft, StringComparison.Ordinal);
            Assert.Equal("OWNED-HIGH-MARKER-UNTOUCHED", File.ReadAllText(Path.Combine(staged, "high-only-marker.txt")));
            Assert.Equal("OWNED-CACHE-SENTINEL", File.ReadAllText(Path.Combine(staged, "verification-cache.jsonl")));
            File.WriteAllText(Path.Combine(staged, "ac34-ui-result.txt"),
                $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\nSidecarsListed={tab.HasSidecars}\n" +
                $"RowResult=null\nStatus={tab.StatusLeft}\n" +
                $"Marker={File.ReadAllText(Path.Combine(staged, "high-only-marker.txt"))}\n" +
                $"Cache={File.ReadAllText(Path.Combine(staged, "verification-cache.jsonl"))}\n");
        }
        finally
        {
            if (services is not null && window is not null && testRoot is not null)
                AccessibilityTests.Close(services, window, testRoot);
            PlatformFactory.WindowsFactory = previousFactory;
        }
    }

    [AvaloniaFact]
    public async Task Native_high_token_Pack_panel_refuses_the_medium_staged_source_link()
    {
        string? staged = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_LINK_FIXTURE");
        if (staged is null) Assert.Skip("Run in the owned medium/high Windows fixture.");
        Assert.True(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity);
        var previousFactory = PlatformFactory.WindowsFactory;
        AppServices? services = null;
        MainViewModel? vm = null;
        MainWindow? window = null;
        string? testRoot = null;
        try
        {
            PlatformFactory.WindowsFactory = () => new WindowsPlatform();
            (services, vm, window, testRoot) = AccessibilityTests.OpenMainWindow();
            var tab = vm.ActiveTab!;
            tab.Navigate(FileCat.Core.Resources.Location.FileSystem(staged!));
            for (int i = 0; i < 250 && tab.Listing.State != ListingState.Complete; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(ListingState.Complete, tab.Listing.State);
            Assert.True(tab.Listing.FocusName("zip-source-link.txt"));
            string archivePath = Path.Combine(testRoot, "owned-ac35-ui.zip");
            var pending = vm.ExecuteAsync(CommandIds.Pack);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(dialogs.IsOpen);
            window.UpdateLayout();
            var pathBox = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Archive path");
            pathBox.Text = archivePath;
            window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Pack"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (int i = 0; i < 500 && (services.Jobs.Jobs.Count == 0 || !services.Jobs.Jobs[0].State.IsFinished()); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobState.Failed, job.State);
            Assert.Contains(job.Issues, issue => issue.Message.Contains("link", StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(archivePath));
            string marker = Path.Combine(staged!, "zip-high-only-marker.txt");
            Assert.Equal("OWNED-ZIP-HIGH-ONLY-MARKER", File.ReadAllText(marker));

            string existingZip = Path.Combine(testRoot, "owned-ac35-f5.zip");
            using (var prior = ZipFile.Open(existingZip, ZipArchiveMode.Create))
            using (var kept = new StreamWriter(prior.CreateEntry("kept.txt").Open()))
                kept.Write("OWNED-PRIOR-ZIP-BYTES");
            byte[] existingBytes = File.ReadAllBytes(existingZip);
            var targetTab = vm.Workspace.Panels.Single(panel => !ReferenceEquals(panel, tab.Panel)).ActiveTab!;
            targetTab.Navigate(ZipProvider.ForFile(existingZip));
            for (int i = 0; i < 250 && targetTab.Listing.State != ListingState.Complete; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(ListingState.Complete, targetTab.Listing.State);
            vm.Workspace.Activate(tab.Panel);
            Assert.True(tab.Listing.FocusName("zip-source-link.txt"));
            var pendingAdd = vm.ExecuteAsync(CommandIds.Copy);
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(dialogs.IsOpen);
            window.UpdateLayout();
            var destination = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Destination");
            Assert.Contains("owned-ac35-f5.zip", destination.Text);
            var copyButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Copy") &&
                    button.GetVisualAncestors().OfType<Border>().Any(border => border.Classes.Contains("backdrop")));
            for (int i = 0; i < 250 && !copyButton.IsEnabled; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(copyButton.IsEnabled, $"Destination did not validate: {destination.Text}");
            copyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pendingAdd.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (int i = 0; i < 500 && (services.Jobs.Jobs.Count < 2 || !services.Jobs.Jobs[1].State.IsFinished()); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(2, services.Jobs.Jobs.Count);
            var addJob = services.Jobs.Jobs[1];
            Assert.Equal(JobState.Failed, addJob.State);
            Assert.Contains(addJob.Issues, issue => issue.Message.Contains("link", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(existingBytes, File.ReadAllBytes(existingZip));
            Assert.Equal("OWNED-ZIP-HIGH-ONLY-MARKER", File.ReadAllText(marker));
            File.WriteAllText(Path.Combine(staged!, "ac35-ui-result.txt"),
                $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\nPackState={job.State}\n" +
                $"PackArchiveExists={File.Exists(archivePath)}\nF5AddState={addJob.State}\n" +
                $"ExistingZipUnchanged=True\nMarker={File.ReadAllText(marker)}\n" +
                $"PackIssues={string.Join(" | ", job.Issues.Select(issue => issue.Message))}\n" +
                $"F5Issues={string.Join(" | ", addJob.Issues.Select(issue => issue.Message))}\n");
        }
        finally
        {
            if (vm is not null) ((OverlayDialogService)vm.Dialogs).CancelAll();
            if (services is not null && window is not null && testRoot is not null)
                AccessibilityTests.Close(services, window, testRoot);
            PlatformFactory.WindowsFactory = previousFactory;
        }
    }

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
    public void An_elevated_process_launches_no_tool_from_user_settings_on_any_platform()
    {
        // Review finding R-M12: the gates applied on Windows only, so root on Linux or macOS ran tools its user configured.
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            typeof(AppServices).GetField("<Shell>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(services, new ElevatedShell());
            var admit = typeof(MainViewModel).GetMethod("AdmitConfiguredExternalTool", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.False((bool)admit.Invoke(vm, [new ToolDefinition { Name = "Configured", Executable = "/bin/true" }])!);
            Assert.Contains("will not launch the configured external tool", vm.Notification, StringComparison.Ordinal);
            Assert.Contains(OperatingSystem.IsWindows() ? "as administrator" : "as root", vm.Notification, StringComparison.Ordinal);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class ElevatedShell : PortableShellServices
    {
        public override bool IsElevated => true;
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
