using System.IO.Compression;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ElevatedStateDiagnosticsTests
{
    [AvaloniaFact]
    public void Native_high_token_omits_linked_logs_and_refuses_linked_autosave()
    {
        string? fixture = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_LINK_FIXTURE");
        if (fixture is null) Assert.Skip("Run with the owned medium/high-token linked fixture in the disposable Windows guest.");
        Assert.True(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity);
        var paths = AppPaths.Resolve(overrideRoot: fixture);
        string marker = Path.Combine(fixture, "high-only-marker.txt");
        const string original = "OWNED-HIGH-MARKER-UNTOUCHED";
        Assert.Equal(original, File.ReadAllText(marker));
        Assert.True((File.GetAttributes(paths.WorkspaceFile + ".tmp") & FileAttributes.ReparsePoint) != 0);
        Assert.True((File.GetAttributes(Path.Combine(paths.LogDirectory, "filecat.log.1")) & FileAttributes.ReparsePoint) != 0);

        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(statePaths: paths);
        try
        {
            Assert.Equal(string.Empty, AppLog.CurrentLogFile);
            AppLog.Info("OWNED-HIGH-STARTUP-CONTROL");
            AppLog.AppendCrash(paths.LogDirectory, "OWNED-HIGH-CRASH-CONTROL");
            var notices = new List<string>();
            services.StateSaveFailed += notices.Add;
            vm.SaveWorkspace(null); // same route called by the 60-second autosave timer
            Assert.Contains(notices, n => n.Contains("did not save", StringComparison.OrdinalIgnoreCase) && n.Contains("administrator", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(original, File.ReadAllText(marker));
            Assert.False(File.Exists(paths.WorkspaceFile));
            string safeState = Path.Combine(fixture, "safe-state.json");
            Assert.Throws<IOException>(() => JsonFileStore.Save(safeState, new WorkspaceState(), StateJsonContext.Default.WorkspaceState));
            Assert.False(File.Exists(safeState));

            string preview = DiagnosticsBundle.Describe(services).Preview;
            Assert.Contains("omitted", preview, StringComparison.OrdinalIgnoreCase);
            string export = Path.Combine(fixture, "export");
            Directory.CreateDirectory(export);
            using var zip = ZipFile.OpenRead(DiagnosticsBundle.Write(services, export));
            Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || e.FullName == "crash.log");
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                Assert.DoesNotContain(original, reader.ReadToEnd());
            }
            Assert.Equal(original, File.ReadAllText(marker));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [Fact]
    public void Ordinary_token_can_save_genuine_state_and_export_genuine_log()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity)
            Assert.Skip("The genuine-control run requires a Windows medium token.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-ordinary-state-log", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            JsonFileStore.Save(paths.WorkspaceFile, new WorkspaceState(), StateJsonContext.Default.WorkspaceState);
            Assert.True(File.Exists(paths.WorkspaceFile));
            using var services = AppServices.CreateForPaths(paths);
            AppLog.Info("OWNED-ORDINARY-LOG-CONTROL");
            AppLog.AppendCrash(paths.LogDirectory, "OWNED-ORDINARY-CRASH-CONTROL");
            using var zip = ZipFile.OpenRead(DiagnosticsBundle.Write(services, root));
            var log = Assert.Single(zip.Entries, e => e.FullName == "filecat.log");
            using var reader = new StreamReader(log.Open());
            Assert.Contains("OWNED-ORDINARY-LOG-CONTROL", reader.ReadToEnd());
            var crash = Assert.Single(zip.Entries, e => e.FullName == "crash.log");
            using var crashReader = new StreamReader(crash.Open());
            Assert.Contains("OWNED-ORDINARY-CRASH-CONTROL", crashReader.ReadToEnd());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
