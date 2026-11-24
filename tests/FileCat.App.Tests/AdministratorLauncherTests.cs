using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FileCat.App.Services;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// I644: every administrator window starts through FileCat.AdminLauncher.exe beside FileCat.exe. Windows builds an
/// elevated program's environment from the account's registry environment, and the launcher removes the .NET runtime's
/// settings from it before FileCat starts. The native tests run this build's real launcher without UAC, beside a
/// windowless stand-in for FileCat.exe that records what it received: nothing is elevated and no window opens.
/// </summary>
public sealed class AdministratorLauncherTests
{
    private const string Volume = @"\\?\Volume{f1e6d606-a052-4ae1-bfc7-d948f4f16fb3}";
    private const string JournalRoot = @"C:\Mount Points\Drive & 100%\";
    private const string DeniedFolder = @"D:\Odepřeno ^!\Složka 😀\";
    private static readonly string LinkPlan = @"C:\Users\Someone\AppData\Local\FileCat\elevation\link plan.json", LinkHash = new('a', 64);
    private static readonly string TaskPlan = @"C:\exchange folder\task.json", TaskHash = new('b', 64);
    private static readonly string[] RuntimePrefixes = ["DOTNET_", "COMPLUS_", "CORECLR_", "COR_", "COREHOST_", "MONO_"];

    // Every route into an administrator window, with arguments that need quoting: spaces, trailing backslashes, shell
    // characters and text outside ASCII.
    private static IEnumerable<(string Route, ProcessStartInfo Start)> Routes(string? applicationFolder)
    {
        yield return ("Tools", AdministratorWindow.CreateStartInfo(applicationFolder: applicationFolder));
        yield return ("change journal", AdministratorWindow.CreateStartInfo(new Location(Schemes.Journal, JournalRoot, session: Volume),
            applicationFolder: applicationFolder));
        yield return ("denied folder", AdministratorWindow.CreateStartInfo(folder: (DeniedFolder, Volume), applicationFolder: applicationFolder));
        yield return ("link review", AdministratorWindow.CreateStartInfo(linkReview: (LinkPlan, LinkHash), applicationFolder: applicationFolder));
        yield return ("administrator review", AdministratorWindow.CreateStartInfo(task: (TaskPlan, TaskHash), applicationFolder: applicationFolder));
    }

    [Fact]
    public void Every_route_starts_the_launcher_beside_FileCat_and_never_FileCat_itself()
    {
        string folder = Path.Combine(Path.GetTempPath(), "filecat-folder-without-files");
        var options = new Dictionary<string, StartupOptions>();
        foreach (var (route, start) in Routes(folder))
        {
            Assert.Equal(Path.Combine(folder, AdministratorWindow.LauncherName), start.FileName);
            Assert.True(start.UseShellExecute, route);
            Assert.Equal("runas", start.Verb);
            Assert.Equal(Environment.SystemDirectory, start.WorkingDirectory);
            Assert.Equal("--new-instance", start.ArgumentList[0]);
            Assert.Empty(start.Arguments);
            options[route] = StartupOptions.Parse(start.ArgumentList.ToArray());
        }
        Assert.All(options.Values, parsed => Assert.True(parsed.NewInstance));
        Assert.Equal((JournalRoot, Volume), options["change journal"].StartupJournal);
        Assert.Equal((DeniedFolder, Volume), options["denied folder"].AdministratorFolder);
        Assert.Equal((LinkPlan, LinkHash), options["link review"].AdministratorLinkReview);
        Assert.Equal((TaskPlan, TaskHash), options["administrator review"].AdministratorRequest);
        Assert.Equal(new[] { "--new-instance" }, Routes(folder).First().Start.ArgumentList);
        // FileCat's own folder by default, also when a development build runs through dotnet.
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, AdministratorWindow.LauncherName), AdministratorWindow.CreateStartInfo().FileName);
    }

    [Fact]
    public void A_missing_launcher_or_FileCat_refuses_before_Windows_asks_and_never_starts_FileCat_directly()
    {
        string folder = Directory.CreateTempSubdirectory("filecat-admin-launcher-").FullName;
        try
        {
            var start = AdministratorWindow.CreateStartInfo(task: (TaskPlan, TaskHash), applicationFolder: folder);
            string executable = Path.Combine(folder, AdministratorWindow.ExecutableName);
            var missing = Assert.Throws<FileNotFoundException>(() => AdministratorWindow.Start(start, waitForStartup: true));
            Assert.Contains(AdministratorWindow.LauncherName, missing.Message);
            Assert.Contains("Reinstall FileCat", missing.Message);
            Assert.Equal(start.FileName, missing.FileName);

            File.WriteAllBytes(executable, []);
            Assert.Throws<FileNotFoundException>(() => AdministratorWindow.Start(start, waitForStartup: false));
            // Synchronously, before Windows is asked: the retry's caller keeps its staged request only while this runs.
            Assert.Throws<FileNotFoundException>(() => { _ = AdministratorWindow.StartFileRetry(start, CancellationToken.None); });
            File.WriteAllBytes(start.FileName, []);
            File.Delete(executable);
            Assert.Throws<FileNotFoundException>(() => AdministratorWindow.RequireLauncher(start));
            File.WriteAllBytes(executable, []);
            AdministratorWindow.RequireLauncher(start);
            // Only the launcher starts an administrator window, even with FileCat.exe present.
            Assert.Throws<FileNotFoundException>(() => AdministratorWindow.RequireLauncher(new ProcessStartInfo(executable)));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void This_build_carries_its_own_launcher_beside_FileCat()
    {
        RequireWindows();
        string launcher = Path.Combine(AppContext.BaseDirectory, AdministratorWindow.LauncherName);
        Assert.True(File.Exists(launcher), $"The build puts {AdministratorWindow.LauncherName} beside FileCat.exe.");
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, AdministratorWindow.ExecutableName)));
        byte[] bytes = File.ReadAllBytes(launcher);
        int header = BitConverter.ToInt32(bytes, 0x3C);
        Assert.Equal("PE\0\0", Encoding.ASCII.GetString(bytes, header, 4));
        ushort expected = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? (ushort)0xAA64 : (ushort)0x8664;
        Assert.Equal(expected, BitConverter.ToUInt16(bytes, header + 4));
        var version = FileVersionInfo.GetVersionInfo(launcher);
        // Windows' elevation prompt names the program by its description.
        Assert.Equal("FileCat administrator window", version.FileDescription);
        Assert.Equal("FileCat", version.ProductName);
        Assert.Equal(AdministratorWindow.LauncherName, version.OriginalFilename);
        // FileCat's own native program, not a .NET application host: neither half of the apphost's placeholder.
        string text = Encoding.ASCII.GetString(bytes);
        Assert.DoesNotContain("c3ab8ff13720e8ad9047dd39466b3c89", text);
        Assert.DoesNotContain("74e592c2fa383d4a3960714caef0c4f2", text);
    }

    [Fact]
    public void The_launcher_removes_the_runtime_settings_and_passes_its_arguments_exactly()
    {
        RequireWindows();
        using var fixture = new LauncherFixture(exitCode: 37);
        // The text after the launcher's own name, exactly as FileCat.exe must receive it, and the arguments the C runtime
        // (FileCat.exe's .NET host) reads from it: escaped quotes, a trailing backslash before a closing quote, an empty
        // argument, backslashes not before a quote, three backslashes before a quote, a tab, repeated spaces, percent
        // signs and text outside ASCII.
        const string arguments = "--new-instance   --review-symbolic-links \"C:\\Some Folder\\plan \\\"quoted\\\".json\" " +
            "\"trailing backslash\\\\\" \"\" plain\\path\\ a\\\\\\\"b \"tab\tinside\" %PATH% ünï 😀";
        string[] expected = ["--new-instance", "--review-symbolic-links", "C:\\Some Folder\\plan \"quoted\".json", "trailing backslash\\",
            "", "plain\\path\\", "a\\\"b", "tab\tinside", "%PATH%", "ünï", "😀"];
        var start = new ProcessStartInfo(fixture.Launcher) { UseShellExecute = false, Arguments = arguments };
        // I644's probe variables, with other runtime, host and diagnostics settings, some in other letter cases, which
        // Windows treats as the same names.
        var runtime = new Dictionary<string, string>
        {
            ["CORECLR_ENABLE_PROFILING"] = "1",
            ["CORECLR_PROFILER"] = "{8A3E1F52-6C1B-4E07-9D35-0F2B7A3C9E41}",
            ["CORECLR_PROFILER_PATH"] = Path.Combine(fixture.Folder, "probe.dll"),
            ["coreclr_profiler_path_64"] = Path.Combine(fixture.Folder, "probe.dll"),
            ["COR_ENABLE_PROFILING"] = "1",
            ["Cor_Profiler"] = "{8A3E1F52-6C1B-4E07-9D35-0F2B7A3C9E41}",
            ["DOTNET_STARTUP_HOOKS"] = Path.Combine(fixture.Folder, "hook.dll"),
            ["DOTNET_ADDITIONAL_DEPS"] = Path.Combine(fixture.Folder, "deps"),
            ["Dotnet_Root"] = Path.Combine(fixture.Folder, "runtime"),
            ["DOTNET_ROOT(x86)"] = Path.Combine(fixture.Folder, "runtime"),
            ["DOTNET_EnableDiagnostics"] = "1",
            ["DOTNET_DbgEnableMiniDump"] = "1",
            ["complus_ZapDisable"] = "1",
            ["COREHOST_TRACE"] = "1",
            ["CoreHost_TraceFile"] = Path.Combine(fixture.Folder, "trace.txt"),
            ["MONO_ENV_OPTIONS"] = "--debug",
        };
        var kept = new Dictionary<string, string>
        {
            ["XDOTNET_KEEP"] = "kept",
            ["DOTNETX"] = "kept",
            ["CORECLR"] = "kept",
            ["COR"] = "kept",
            ["FILECAT_LAUNCHER_TEST_VALUE"] = "a=b; \"c\" \\d %PATH%",
        };
        foreach (var (name, value) in runtime.Concat(kept)) start.Environment[name] = value;

        var (exitCode, launcherId) = fixture.Run(start);
        var record = fixture.Record();
        Assert.Equal(37, exitCode);
        Assert.NotEqual(launcherId, record.ProcessId);
        Assert.Equal($"\"{fixture.FileCat}\" {arguments}", record.CommandLine);
        Assert.Equal(expected, record.Arguments);
        Assert.Equal(new[] { "DOTNET_EnableDiagnostics=0" }, record.Environment.Where(IsRuntimeSetting));
        foreach (var (name, value) in kept) Assert.Equal(value, Value(record, name));
        Assert.Equal(start.Environment["PATH"], Value(record, "PATH"));
        Assert.Equal(Environment.SystemDirectory.TrimEnd('\\'), record.CurrentDirectory.TrimEnd('\\'), ignoreCase: true);
    }

    [Fact]
    public async Task Every_route_reaches_FileCat_through_the_launcher_with_exactly_its_arguments()
    {
        RequireWindows();
        foreach (var (route, _) in Routes(null))
        {
            // FileCat becomes idle shortly after it starts and stays until its window closes.
            using var fixture = new LauncherFixture(idleDelayMs: 200, stayMs: 800);
            var start = Routes(fixture.Folder).Single(candidate => candidate.Route == route).Start;
            // Without UAC: the same program and arguments through the shell, without the runas verb, by the product's
            // own start routines.
            start.Verb = "";
            if (route == "administrator review")
            {
                await AdministratorWindow.StartFileRetry(start, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60),
                    TestContext.Current.CancellationToken);
                Assert.NotNull(fixture.Record().ExitingAt);
            }
            else
            {
                AdministratorWindow.Start(start, waitForStartup: true);
                var returned = DateTime.UtcNow;
                // The window's request stays staged until FileCat is idle: it was by the time Start returned.
                Assert.True(fixture.Record().IdleAt <= returned, route);
            }
            var record = fixture.Record();
            Assert.Equal(start.ArgumentList, record.Arguments);
            Assert.Equal(new[] { "DOTNET_EnableDiagnostics=0" }, record.Environment.Where(IsRuntimeSetting));
            Assert.Equal(Environment.SystemDirectory.TrimEnd('\\'), record.CurrentDirectory.TrimEnd('\\'), ignoreCase: true);
        }
    }

    [Fact]
    public void The_launcher_turns_idle_only_after_FileCat_does_and_exits_with_FileCat()
    {
        RequireWindows();
        using var fixture = new LauncherFixture(idleDelayMs: 1500, stayMs: 5000, exitCode: 23);
        using var launcher = fixture.Start(new ProcessStartInfo(fixture.Launcher) { UseShellExecute = false });
        // What AdministratorWindow.Start waits for before it removes a staged request.
        Assert.True(launcher.WaitForInputIdle(30_000));
        var idle = DateTime.UtcNow;
        Assert.True(launcher.WaitForExit(30_000));
        var exited = DateTime.UtcNow;
        var record = fixture.Record();
        Assert.Equal(23, launcher.ExitCode);
        Assert.InRange(idle, record.IdleAt!.Value, record.ExitingAt!.Value);
        Assert.True(exited >= record.ExitingAt, "The launcher stays until FileCat exits.");
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator launcher is a Windows program.");
    }

    private static bool IsRuntimeSetting(string entry) =>
        !entry.StartsWith('=') && RuntimePrefixes.Any(prefix => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string? Value(StandInRecord record, string name) =>
        record.Environment.Where(entry => entry.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry[(name.Length + 1)..]).SingleOrDefault();

    private sealed record StandInRecord(int ProcessId, string CommandLine, string[] Arguments, string[] Environment,
        string CurrentDirectory, DateTime? IdleAt, DateTime? ExitingAt);

    /// <summary>A fresh folder with this build's launcher and the stand-in as FileCat.exe beside it.</summary>
    private sealed class LauncherFixture : IDisposable
    {
        private readonly List<Process> _started = [];

        public LauncherFixture(int idleDelayMs = 0, int stayMs = 3000, int exitCode = 0)
        {
            string launcher = Path.Combine(AppContext.BaseDirectory, AdministratorWindow.LauncherName);
            string standIn = Path.Combine(AppContext.BaseDirectory, "AdministratorLauncher", "AdministratorLauncherStandIn.exe");
            Assert.True(File.Exists(launcher), $"The build puts {AdministratorWindow.LauncherName} beside FileCat.exe.");
            Assert.True(File.Exists(standIn), "The App tests' build compiles the launcher's stand-in.");
            // Spaces, as in Program Files, and text outside ASCII: the launcher's own quoted name must not leak into the
            // arguments it passes on.
            Folder = Directory.CreateTempSubdirectory("filecat admin launcher ü ").FullName;
            File.Copy(launcher, Launcher);
            File.Copy(standIn, FileCat);
            File.WriteAllText(Path.Combine(Folder, "stand-in-settings.txt"), $"idle-delay-ms={idleDelayMs}\nstay-ms={stayMs}\nexit-code={exitCode}\n");
        }

        public string Folder { get; }
        public string Launcher => Path.Combine(Folder, AdministratorWindow.LauncherName);
        public string FileCat => Path.Combine(Folder, AdministratorWindow.ExecutableName);

        public Process Start(ProcessStartInfo start)
        {
            var process = Process.Start(start) ?? throw new InvalidOperationException("The launcher did not start.");
            _started.Add(process);
            return process;
        }

        public (int ExitCode, int ProcessId) Run(ProcessStartInfo start)
        {
            var launcher = Start(start);
            Assert.True(launcher.WaitForExit(60_000), "The launcher did not finish.");
            return (launcher.ExitCode, launcher.Id);
        }

        public StandInRecord Record()
        {
            string path = Path.Combine(Folder, "stand-in-record.json");
            Assert.True(File.Exists(path), "FileCat.exe, the stand-in, did not run.");
            // A stand-in that still runs may replace its record meanwhile: never block that.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            DateTime? Time(string name) => root.TryGetProperty(name, out var value) ? DateTime.FromFileTimeUtc(value.GetInt64()) : null;
            string[] Strings(string name) => root.GetProperty(name).EnumerateArray().Select(value => value.GetString()!).ToArray();
            return new(root.GetProperty("pid").GetInt32(), root.GetProperty("commandLine").GetString()!, Strings("arguments"),
                Strings("environment"), root.GetProperty("currentDirectory").GetString()!, Time("idleAt"), Time("exitingAt"));
        }

        public void Dispose()
        {
            foreach (var process in _started)
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
                process.Dispose();
            }
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(Folder, recursive: true);
                    return;
                }
                catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && attempt < 150)
                {
                    // A launcher the shell started exits with its stand-in, shortly after the test ends. Stop either only
                    // when it still runs from this fixture's own folder after a while.
                    if (attempt == 100) StopFixtureProcesses();
                    Thread.Sleep(100);
                }
            }
        }

        private void StopFixtureProcesses()
        {
            foreach (string name in new[] { "FileCat.AdminLauncher", "FileCat" })
                foreach (var process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        try
                        {
                            if (process.MainModule?.FileName is { } image && image.StartsWith(Folder + "\\", StringComparison.OrdinalIgnoreCase))
                                process.Kill();
                        }
                        catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException) { }
                    }
        }
    }
}
