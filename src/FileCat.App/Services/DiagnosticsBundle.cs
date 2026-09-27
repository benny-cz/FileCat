using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FileCat.Core.Diagnostics;

namespace FileCat.App.Services;

/// <summary>
/// Explicitly exported, previewable diagnostics (plan §19.2): environment, redacted logs, and settings without
/// secrets or file contents. Paths in logs are already hashed unless diagnostic mode was enabled.
/// </summary>
public static class DiagnosticsBundle
{
    public sealed record Summary(string Preview);

    public static Summary Describe(AppServices s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The bundle contains:");
        sb.AppendLine("• FileCat version, OS, runtime, and platform adapter");
        sb.AppendLine("• the diagnostics log (paths are hashed" + (AppLog.DiagnosticMode ? "; diagnostic mode is ON, so selected paths are included" : "") + ")");
        sb.AppendLine("• settings (tool paths included; no passwords or keys are ever stored there)");
        sb.AppendLine("• crash reports, if any");
        sb.AppendLine("It never contains file contents, Registry data, or credentials.");
        return new Summary(sb.ToString());
    }

    public static string Write(AppServices s)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop)) desktop = s.Paths.LocalDirectory;
        var path = Path.Combine(desktop, $"FileCat-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var env = new StringBuilder();
        env.AppendLine($"FileCat {typeof(DiagnosticsBundle).Assembly.GetName().Version}");
        env.AppendLine($"OS: {Environment.OSVersion} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})");
        env.AppendLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        env.AppendLine($"Platform adapter: {s.Platform.Name}");
        env.AppendLine($"Portable: {s.Paths.IsPortable}; profile: {s.Paths.ProfileName}");
        env.AppendLine($"Culture: {System.Globalization.CultureInfo.CurrentCulture.Name}");
        env.AppendLine($"Processors: {Environment.ProcessorCount}; 64-bit process: {Environment.Is64BitProcess}");
        env.AppendLine(UiStallMonitor.Describe());
        AddText(zip, "environment.txt", env.ToString());
        AddText(zip, "settings.json", JsonSerializer.Serialize(s.Settings, Core.State.StateJsonContext.Default.AppSettings));
        foreach (var file in new[] { "filecat.log", "filecat.log.1", "crash.log" })
        {
            var p = Path.Combine(s.Paths.LogDirectory, file);
            if (File.Exists(p))
            {
                using var src = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dst = zip.CreateEntry(file).Open();
                src.CopyTo(dst);
            }
        }
        return path;
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(text);
    }
}
