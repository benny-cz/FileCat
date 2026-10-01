using Avalonia.Headless.XUnit;
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
    public void A_failed_save_is_told_once_and_tried_again()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-v11-save", Guid.NewGuid().ToString("N"));
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            var told = new List<string>();
            services.StateSaveFailed += told.Add;

            // A folder where the save writes its temporary copy: the write fails as on a medium that takes none.
            string blocker = services.Paths.SettingsFile + ".tmp";
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
