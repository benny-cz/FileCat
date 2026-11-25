using System.IO.Compression;
using System.Text;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>I643: Export diagnostics says what its log keeps, and the log it exports keeps no more.</summary>
public sealed class DiagnosticsExportTests
{
    private static readonly string[] LogFiles = ["filecat.log", "filecat.log.1", "crash.log"];

    [Fact]
    public void The_preview_says_paths_are_hashed_and_what_can_remain()
    {
        string off = DiagnosticsBundle.Preview(elevated: false, diagnosticMode: false, windows: OperatingSystem.IsWindows());
        Assert.Contains("• the diagnostics log and crash reports, if any (paths are hashed, but a message can still name a file, folder, or server)",
            off, StringComparison.Ordinal);
        string on = DiagnosticsBundle.Preview(elevated: false, diagnosticMode: true, windows: OperatingSystem.IsWindows());
        Assert.Contains("• the diagnostics log and crash reports, if any (diagnostic mode is ON, so paths are included)", on, StringComparison.Ordinal);
        Assert.DoesNotContain("hashed", on, StringComparison.Ordinal);
        string elevated = DiagnosticsBundle.Preview(elevated: true, diagnosticMode: false, windows: true);
        Assert.Contains("persisted diagnostics logs are omitted while FileCat runs as administrator", elevated, StringComparison.Ordinal);
        Assert.DoesNotContain("crash reports", elevated, StringComparison.Ordinal);
        // I655: root on Linux and macOS keeps no log either, and the preview says so in its terms.
        string root = DiagnosticsBundle.Preview(elevated: true, diagnosticMode: false, windows: false);
        Assert.Contains("persisted diagnostics logs are omitted while FileCat runs as root", root, StringComparison.Ordinal);
        Assert.DoesNotContain("administrator", root, StringComparison.Ordinal);
        Assert.DoesNotContain("crash reports", root, StringComparison.Ordinal);
        foreach (string preview in new[] { off, on, elevated, root })
        {
            Assert.StartsWith("The bundle contains:", preview, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.NewLine + Environment.NewLine, preview, StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public void An_export_hashes_the_paths_left_in_earlier_log_lines()
    {
        if (ProcessAuthority.IsElevatedOrRoot) Assert.Skip("An elevated or root FileCat exports no logs.");
        string root = Directory.CreateTempSubdirectory("filecat-i643-export-").FullName;
        bool priorMode = AppLog.DiagnosticMode;
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: root);
            using var services = AppServices.CreateForPaths(paths);
            // Lines as a FileCat before I643 wrote them, or one in diagnostic mode.
            string secret = OperatingSystem.IsWindows() ? @"C:\Users\jan\Secret plan.docx" : "/home/jan/Secret plan.docx";
            string frame = "   at FileCat.A.B() in /home/runner/work/FileCat/src/A.cs:line 3";
            string record = $"2026-11-20T10:00:00.0000000Z Error   Command x failed | FileNotFoundException: Could not find file '{secret}'.";
            File.WriteAllText(Path.Combine(paths.LogDirectory, "filecat.log.1"), record + Environment.NewLine);
            File.AppendAllText(Path.Combine(paths.LogDirectory, "filecat.log"), record + Environment.NewLine);
            File.WriteAllText(Path.Combine(paths.LogDirectory, "crash.log"),
                $"2026-11-20T10:00:00.0000000Z{Environment.NewLine}System.IO.FileNotFoundException: Could not find file '{secret}'.{Environment.NewLine}{frame}{Environment.NewLine}");

            AppLog.DiagnosticMode = false;
            var off = Export(services, root);
            foreach (string name in LogFiles)
            {
                Assert.DoesNotContain(secret, off[name], StringComparison.Ordinal);
                Assert.Contains($"Could not find file '{AppLog.Hash(secret)}'.", off[name], StringComparison.Ordinal);
            }
            Assert.Contains(frame, off["crash.log"], StringComparison.Ordinal);

            // Diagnostic mode exports the files as they are.
            AppLog.DiagnosticMode = true;
            var on = Export(services, root);
            foreach (string name in LogFiles) Assert.Contains(secret, on[name], StringComparison.Ordinal);
        }
        finally
        {
            AppLog.DiagnosticMode = priorMode;
            Directory.Delete(root, recursive: true);
        }
    }

    private static Dictionary<string, string> Export(AppServices services, string folder)
    {
        string bundle = DiagnosticsBundle.Write(services, folder);
        try
        {
            using var zip = ZipFile.OpenRead(bundle);
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                entries[entry.FullName] = reader.ReadToEnd();
            }
            return entries;
        }
        finally { File.Delete(bundle); }
    }
}
