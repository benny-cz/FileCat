using Avalonia.Headless.XUnit;
using System.Security.Cryptography;
using System.Text;
using FileCat.App.Services;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan V11 ("failed state writes"): when FileCat cannot save a state file — a full disk, a folder made
/// read-only — the user is told once, not every minute, and FileCat keeps trying. Before, every such failure went to
/// the log only, so changes silently did not survive a restart.
/// </summary>
public sealed class StateSaveFailureTests
{
    [AvaloniaFact]
    public void Oversized_settings_and_history_are_loaded_read_only_and_never_saved_over()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-state-budget", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: Path.Combine(root, "data"));
            Directory.CreateDirectory(paths.SettingsDirectory);
            foreach (string path in new[] { paths.SettingsFile, paths.HistoryFile })
            {
                using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
                writer.Write("{\"SchemaVersion\":1,\"padding\":\"");
                char[] padding = new char[64 * 1024];
                Array.Fill(padding, 'A');
                for (int i = 0; i < 129; i++) writer.Write(padding);
                writer.Write("\"}");
            }
            string settingsBefore;
            string historyBefore;
            using (var stream = File.OpenRead(paths.SettingsFile)) settingsBefore = Convert.ToHexString(SHA256.HashData(stream));
            using (var stream = File.OpenRead(paths.HistoryFile)) historyBefore = Convert.ToHexString(SHA256.HashData(stream));

            using (var services = AppServices.CreateForPaths(paths))
            {
                Assert.Equal(StateLoadStatus.OversizedReadOnly, services.SettingsStatus);
                Assert.Equal(StateLoadStatus.OversizedReadOnly, services.HistoryStatus);
                Assert.True(services.SettingsReadOnly);
                services.Settings.ShowHidden = !services.Settings.ShowHidden;
                services.SaveSettings();
                services.SaveHistory();
            }
            using (var stream = File.OpenRead(paths.SettingsFile)) Assert.Equal(settingsBefore, Convert.ToHexString(SHA256.HashData(stream)));
            using (var stream = File.OpenRead(paths.HistoryFile)) Assert.Equal(historyBefore, Convert.ToHexString(SHA256.HashData(stream)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void A_failed_save_is_told_once_and_tried_again()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-v11-save", Guid.NewGuid().ToString("N"));
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            var told = new List<string>();
            services.StateSaveFailed += told.Add;

            // Block the destination on Unix, where saves deliberately use a fresh temporary sibling;
            // retain the fixed temporary-path control on Windows.
            string blocker = OperatingSystem.IsWindows()
                ? services.Paths.SettingsFile + ".tmp"
                : services.Paths.SettingsFile;
            Directory.CreateDirectory(blocker);
            services.Settings.ShowHidden = !services.Settings.ShowHidden;
            services.SaveSettings();
            services.SaveSettings();
            services.SaveSettings();
            Assert.Single(told);
            Assert.Contains("settings", told[0], StringComparison.Ordinal);
            Assert.Contains("will not survive a restart", told[0], StringComparison.Ordinal);

            // Once the cause is gone, the next save goes through, and nothing more is said.
            Directory.Delete(blocker);
            services.SaveSettings();
            Assert.Single(told);
            Assert.Contains($"\"ShowHidden\": {services.Settings.ShowHidden.ToString().ToLowerInvariant()}", File.ReadAllText(services.Paths.SettingsFile), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
