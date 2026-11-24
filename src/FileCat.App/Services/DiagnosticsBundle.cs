using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.App.Services;

/// <summary>
/// Explicitly exported, previewable diagnostics (plan §19.2): environment, redacted logs, and settings without
/// secrets or file contents. Paths in logs are hashed when written unless diagnostic mode was enabled, and again on export
/// while it is off (I643).
/// </summary>
public static class DiagnosticsBundle
{
    public sealed record Summary(string Preview);

    public static Summary Describe(AppServices s) => new(Preview(WindowsTokenAuthority.IsHighIntegrity, AppLog.DiagnosticMode));

    /// <summary>
    /// What the bundle holds, as the export asks before it writes. The log and crash reports hash paths (I643), but no rule
    /// can find a bare file name, a relative path or a server's name in a message, so the preview says those can remain.
    /// </summary>
    internal static string Preview(bool elevated, bool diagnosticMode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The bundle contains:");
        sb.AppendLine("• FileCat version, OS, runtime, and platform adapter");
        if (elevated) sb.AppendLine("• persisted diagnostics logs are omitted while FileCat runs as administrator");
        else sb.AppendLine("• the diagnostics log and crash reports, if any (" + (diagnosticMode
            ? "diagnostic mode is ON, so paths are included)"
            : "paths are hashed, but a message can still name a file, folder, or server)"));
        sb.AppendLine("• settings (tool paths included; no passwords or keys are ever stored there)");
        sb.AppendLine("It never contains file contents, Registry data, or credentials.");
        return sb.ToString();
    }

    public static string Write(AppServices s, string? destinationDirectory = null)
    {
        var desktop = destinationDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
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
        foreach (var file in WindowsTokenAuthority.IsHighIntegrity ? [] : new[] { "filecat.log", "filecat.log.1", "crash.log" })
        {
            var p = Path.Combine(s.Paths.LogDirectory, file);
            if (File.Exists(p))
            {
                using var src = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dst = zip.CreateEntry(file).Open();
                CopyLog(src, dst, AppLog.DiagnosticMode);
            }
        }
        return path;
    }

    /// <summary>
    /// A log or crash report as exported. With diagnostic mode on it is copied as it is. With it off, its paths are hashed
    /// again line by line, so the lines written while diagnostic mode was on, or by a FileCat before I643, leave as the
    /// preview says; a line already hashed comes out the same.
    /// </summary>
    internal static void CopyLog(Stream source, Stream destination, bool diagnosticMode)
    {
        if (diagnosticMode)
        {
            source.CopyTo(destination);
            return;
        }
        using var reader = new StreamReader(source, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        while (reader.ReadLine() is { } line)
        {
            writer.Write(LogRedaction.HashPaths(line));
            writer.Write(Environment.NewLine);
        }
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(text);
    }
}
