using System.IO.Compression;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Diagnostics;
using FileCat.Core.Jobs;
using FileCat.Core.Metadata;
using FileCat.Core.Operations;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Tools;
using FileCat.Core.Verification;
using FileCat.Platform.Windows;
using FileCat.Tests;
using static FileCat.Tests.ProcessAuthorityScopes;

namespace FileCat.App.Tests;

/// <summary>
/// I655: FileCat running as root keeps out of the saved state of the profile it finds, as an administrator window does,
/// and says so in its own terms on Linux and macOS. Each case runs with the decision assumed (every platform) and, in a test
/// host started as root or elevated, with the process's own decision; the controls take the process to be an ordinary one.
/// </summary>
public sealed class RootProfileStateUiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Settings_history_and_the_log_are_the_defaults_and_nothing_is_saved(bool assumed)
    {
        string root = Directory.CreateTempSubdirectory("filecat-i655-state-").FullName;
        try
        {
            // Resolved as root would resolve them: on Linux and macOS its scratch then stays out of the user's profile.
            AppPaths paths;
            using (ElevatedOrRoot(assumed)) paths = AppPaths.Resolve(overrideRoot: root);
            using (Ordinary())
            using (var ordinary = AppServices.CreateForPaths(paths))
            {
                ordinary.Settings.DateFormat = "yyyy-MM-dd";
                ordinary.SaveSettings();
                ordinary.History.Masks.Add("*.i655");
                ordinary.SaveHistory();
                AppLog.Info("I655 ordinary log line");
            }
            byte[] settings = File.ReadAllBytes(paths.SettingsFile), history = File.ReadAllBytes(paths.HistoryFile);
            string log = Path.Combine(paths.LogDirectory, "filecat.log");
            byte[] logged = File.ReadAllBytes(log);
            string export = Directory.CreateDirectory(Path.Combine(root, "export")).FullName;
            using (ElevatedOrRoot(assumed))
            using (var services = AppServices.CreateForPaths(paths))
            {
                Assert.Equal(StateLoadStatus.UnavailableReadOnly, services.SettingsStatus);
                Assert.True(services.SettingsReadOnly);
                Assert.Equal(new AppSettings().DateFormat, services.Settings.DateFormat);
                Assert.Equal(StateLoadStatus.UnavailableReadOnly, services.HistoryStatus);
                Assert.DoesNotContain("*.i655", services.History.Masks);
                Assert.Equal(string.Empty, AppLog.CurrentLogFile);
                AppLog.Info("I655 root log line");
                var notices = new List<string>();
                services.StateSaveFailed += notices.Add;
                services.Settings.DateFormat = "dd.MM.yyyy";
                services.SaveSettings();
                services.History.Masks.Add("*.root");
                services.SaveHistory();
                services.ReportSaveFailure("workspace", new IOException("refused"));
                Assert.Equal([AppServices.ElevatedSaveNotice("workspace", OperatingSystem.IsWindows())], notices);
                Assert.Equal($"FileCat did not save its workspace while running {RunningAs}. {Advice} to save changes.", notices[0]);
                if (OperatingSystem.IsWindows())
                    Assert.Equal("Open FileCat normally to access saved appearance settings.",
                        Assert.Throws<IOException>(() => services.NormalAppearanceSettings).Message);
                // Diagnostics: the preview says the logs are left out, and the bundle leaves them out.
                Assert.Contains($"persisted diagnostics logs are omitted while FileCat runs {RunningAs}",
                    DiagnosticsBundle.Describe(services).Preview, StringComparison.Ordinal);
                using (var zip = ZipFile.OpenRead(DiagnosticsBundle.Write(services, export)))
                    Assert.DoesNotContain(zip.Entries, entry => entry.FullName is "filecat.log" or "filecat.log.1" or "crash.log");
                Assert.EndsWith($"FileCat kept running; persisted diagnostics are omitted while it runs {RunningAs}.",
                    App.CrashNotice("boom"), StringComparison.Ordinal);
            }
            Assert.Equal(settings, File.ReadAllBytes(paths.SettingsFile));
            Assert.Equal(history, File.ReadAllBytes(paths.HistoryFile));
            Assert.Equal(logged, File.ReadAllBytes(log));
            Assert.False(File.Exists(Path.Combine(paths.LogDirectory, "crash.log")));
            // The ordinary FileCat's next start reads its settings and saves a change (the record's finite check).
            using (Ordinary())
            using (var again = AppServices.CreateForPaths(paths))
            {
                Assert.Equal("yyyy-MM-dd", again.Settings.DateFormat);
                Assert.Contains("*.i655", again.History.Masks);
                again.Settings.DateFormat = "yyyy";
                again.SaveSettings();
                using var reread = AppServices.CreateForPaths(paths);
                Assert.Equal("yyyy", reread.Settings.DateFormat);
                Assert.EndsWith("FileCat kept running; details are in the diagnostics log.", App.CrashNotice("boom"), StringComparison.Ordinal);
            }
        }
        finally
        {
            // The log is the process's: leave it pointing nowhere rather than into a removed folder.
            using (ElevatedOrRoot(assumed: true)) AppLog.Initialize(root);
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Notices_on_Linux_and_macOS_say_root_and_how_to_start_FileCat_as_the_usual_user()
    {
        // Windows' wording stays as it was.
        Assert.Equal("FileCat did not save its settings while running as administrator. Reopen it without elevation to save changes.",
            AppServices.ElevatedSaveNotice("settings", windows: true));
        Assert.Equal("FileCat did not save its settings while running as root. Start it as your usual user to save changes.",
            AppServices.ElevatedSaveNotice("settings", windows: false));

        string administrator = App.AdministratorNotice(windows: true, transfers: false);
        Assert.StartsWith("FileCat is running as administrator.", administrator, StringComparison.Ordinal);
        Assert.Contains("the normal profile is read-only", administrator, StringComparison.Ordinal);
        string root = App.AdministratorNotice(windows: false, transfers: false);
        Assert.StartsWith("FileCat is running as root.", root, StringComparison.Ordinal);
        Assert.Contains("started with the default settings and saves nothing", root, StringComparison.Ordinal);
        Assert.EndsWith("Start FileCat as your usual user to keep them.", root, StringComparison.Ordinal);
        Assert.DoesNotContain("administrator", root, StringComparison.Ordinal);

        Assert.Equal("Administrator Settings are read-only. You can copy appearance and open folders from a normal window.",
            SettingsDialog.ElevatedReadOnlyReason(windows: true));
        Assert.StartsWith("Settings are read-only while FileCat runs as root", SettingsDialog.ElevatedReadOnlyReason(windows: false), StringComparison.Ordinal);
        Assert.EndsWith("Start FileCat as your usual user to change them.", SettingsDialog.ElevatedReadOnlyReason(windows: false), StringComparison.Ordinal);

        Assert.Equal("not checked (elevated)", TabViewModel.ElevatedOn(windows: true).Text);
        Assert.Equal("not checked (root)", TabViewModel.ElevatedOn(windows: false).Text);
        Assert.Equal("FileCat runs as root and checks no file by itself.", TabViewModel.ElevatedOn(windows: false).Details[0]);

        Assert.Contains("FileCat running as root", MainViewModel.SshTerminalRefusal(windows: false), StringComparison.Ordinal);
        Assert.Contains("elevated sessions", MainViewModel.SshTerminalRefusal(windows: true), StringComparison.Ordinal);
        Assert.StartsWith("Drag export is refused while FileCat runs as root", DragStaging.ElevatedRefusal(windows: false), StringComparison.Ordinal);
        Assert.Contains("Start FileCat as your usual user", DragStaging.ElevatedRefusal(windows: false), StringComparison.Ordinal);
        Assert.StartsWith("Drag export from an elevated FileCat session is refused", DragStaging.ElevatedRefusal(windows: true), StringComparison.Ordinal);
        Assert.StartsWith("Directory Undo is refused while FileCat runs as root", UndoRunner.ElevatedDirectoryUndoRefusal(windows: false), StringComparison.Ordinal);
        Assert.StartsWith("Elevated directory Undo is refused", UndoRunner.ElevatedDirectoryUndoRefusal(windows: true), StringComparison.Ordinal);
        foreach (string text in new[] { AppServices.ElevatedSaveNotice("history", false), root, SettingsDialog.ElevatedReadOnlyReason(false),
                     MainViewModel.SshTerminalRefusal(false), DragStaging.ElevatedRefusal(false), UndoRunner.ElevatedDirectoryUndoRefusal(false) })
        {
            Assert.DoesNotContain("administrator", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("elevation", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_startup_notice_and_the_tool_gates_name_the_process_and_the_usual_user(bool assumed)
    {
        AppServices? services = null;
        Window? window = null;
        string? root = null;
        try
        {
            MainViewModel vm;
            MainWindow opened;
            using (Ordinary()) (services, vm, opened, root) = AccessibilityTests.OpenMainWindow();
            window = opened;
            using (ElevatedOrRoot(assumed))
            {
                App.NotifyAdministratorNotice(vm);
                Assert.Equal(App.AdministratorNotice(OperatingSystem.IsWindows(), transfers: false), vm.Notification);
                var admit = typeof(MainViewModel).GetMethod("AdmitConfiguredExternalTool", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.False((bool)admit.Invoke(vm, [new ToolDefinition { Name = "Configured", Executable = "/bin/true" }])!);
                Assert.EndsWith($"{(OperatingSystem.IsWindows() ? "Reopen FileCat without elevation" : "Start FileCat as your usual user")} to use it.",
                    vm.Notification, StringComparison.Ordinal);
                var terminal = typeof(MainViewModel).GetMethod("OpenSshTerminal", BindingFlags.Instance | BindingFlags.NonPublic)!;
                terminal.Invoke(vm, [new FileCat.Core.Resources.Location(Schemes.Sftp, "/")]);
                Assert.Equal(MainViewModel.SshTerminalRefusal(OperatingSystem.IsWindows()), vm.Notification);
            }
        }
        finally
        {
            if (services is not null && window is not null && root is not null) AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_file_beside_a_checksum_file_says_it_was_not_checked(bool assumed)
    {
        AppServices? services = null;
        Window? window = null;
        string? root = null;
        try
        {
            MainViewModel vm;
            MainWindow opened;
            using (Ordinary())
                (services, vm, opened, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
                    File.WriteAllText(Path.Combine(folder, "checks.sfv"), "a.txt 00000000\n"));
            window = opened;
            var tab = vm.ActiveTab!;
            var expected = TabViewModel.ElevatedOn(OperatingSystem.IsWindows());
            VerificationResult? result = null;
            string cell = string.Empty;
            for (int attempt = 0; attempt < 250 && result is null; attempt++)
            {
                opened.CaptureRenderedFrame();
                var listing = tab.Listing;
                for (int row = 0; row < listing.VisibleCount && tab.HasSidecars; row++)
                {
                    if (listing.GetVisible(row) is not { Name: "a.txt" } entry) continue;
                    using (ElevatedOrRoot(assumed))
                    {
                        result = tab.Verification(entry, listing.GetStoreIndex(row));
                        cell = tab.GetMetadataText(entry, listing.GetStoreIndex(row), BuiltInFields.Verified.Id, out _);
                    }
                    break;
                }
                if (result is null) await Task.Delay(20, Ct);
            }
            Assert.True(tab.HasSidecars);
            Assert.Same(expected, result);
            Assert.Equal(expected.Text, cell);
        }
        finally
        {
            if (services is not null && window is not null && root is not null) AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Settings_open_read_only_and_say_why(bool assumed)
    {
        AppServices? services = null;
        Window? window = null;
        string? root = null;
        try
        {
            using (ElevatedOrRoot(assumed))
            {
                (services, var vm, var opened, root) = AccessibilityTests.OpenMainWindow();
                window = opened;
                Assert.True(services.SettingsReadOnly);
                var showing = SettingsDialog.ShowAsync(vm);
                Dispatcher.UIThread.RunJobs();
                var warnings = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("warning")).Select(t => t.Text);
                Assert.Contains(SettingsDialog.ElevatedReadOnlyReason(OperatingSystem.IsWindows()), warnings);
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Close")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await showing.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            }
        }
        finally
        {
            if (services is not null && window is not null && root is not null) AccessibilityTests.Close(services, window, root);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hex_save_journals_are_neither_listed_nor_read(bool assumed)
    {
        string directory = Directory.CreateTempSubdirectory("filecat-i655-hex-").FullName;
        try
        {
            string journal = Path.Combine(directory, "hex-20261124000000-0123456789abcdef.fcj");
            File.WriteAllText(journal, "planted: names a file and the bytes to write into it");
            using (Ordinary()) Assert.Equal(journal, Assert.Single(HexSaveJournal.Pending(directory)));
            using (ElevatedOrRoot(assumed))
            {
                Assert.Empty(HexSaveJournal.Pending(directory));
                var error = Assert.Throws<IOException>(() => HexSaveJournal.Read(journal));
                Assert.Equal($"FileCat does not save or apply user-profile hex recovery while running {RunningAs}. {Advice}.", error.Message);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Git_status_is_not_read(bool assumed)
    {
        if (GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) is not { } git) { Assert.Skip("Git is not installed."); return; }
        string repository = Directory.CreateTempSubdirectory("filecat-i655-git-").FullName;
        try
        {
            void Git(params string[] arguments)
            {
                var start = new System.Diagnostics.ProcessStartInfo(git)
                {
                    WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                foreach (string argument in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com" }.Concat(arguments))
                    start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!;
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
            }
            Git("init", "-q");
            File.WriteAllText(Path.Combine(repository, "a.txt"), "one");
            Git("add", "a.txt");
            Git("commit", "-q", "-m", "first");
            File.WriteAllText(Path.Combine(repository, "a.txt"), "two");
            using (Ordinary())
                Assert.Equal(GitStatusKind.Modified, Assert.IsType<GitStatusSnapshot>(await GitStatusReader.ReadAsync(repository, Ct, git)).ForName("a.txt"));
            using (ElevatedOrRoot(assumed))
                Assert.Null(await GitStatusReader.ReadAsync(repository, Ct, git));
        }
        finally
        {
            // Git keeps its objects read-only.
            foreach (string file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            TestCleanup.DeleteTree(repository);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Windows_shell_associations_and_terminals_stay_refused(bool assumed)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows' shell services.");
        var shell = new WindowsShellServices();
        string missing = Path.Combine(Path.GetTempPath(), "filecat-i655-" + Guid.NewGuid().ToString("N"), "missing.txt");
        using (ElevatedOrRoot(assumed))
        {
            Assert.True(shell.IsElevated);
            Assert.Equal("System file associations are unavailable while FileCat runs as administrator. Reopen it without elevation.",
                Assert.Throws<IOException>(() => shell.Open(missing)).Message);
            Assert.Equal("Terminals are unavailable while FileCat runs as administrator. Reopen it without elevation.",
                Assert.Throws<IOException>(() => shell.OpenTerminal(Path.GetTempPath(), "cmd")).Message);
            Assert.Equal("Terminals are unavailable while FileCat runs as administrator. Reopen it without elevation.",
                Assert.Throws<IOException>(() => shell.RunInTerminal(Path.GetTempPath(), "echo", "cmd")).Message);
        }
        using (Ordinary()) Assert.False(shell.IsElevated);
    }
}
