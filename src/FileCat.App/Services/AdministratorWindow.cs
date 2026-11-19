using System.Diagnostics;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Recovery;

namespace FileCat.App.Services;

internal static class AdministratorWindow
{
    internal static ProcessStartInfo CreateStartInfo(Location? journal = null,
        (string Folder, string Volume)? folder = null, (string Path, string Hash)? linkReview = null,
        (string Path, string Hash)? task = null)
    {
        // Use the FileCat apphost even when a development build was started through dotnet.
        string executable = Environment.ProcessPath is { } process && !Path.GetFileName(process).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? process
            : Path.Combine(AppContext.BaseDirectory, "FileCat.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("The FileCat executable could not be found.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.SystemDirectory,
        };
        start.ArgumentList.Add("--new-instance");
        // Re-enter through normal administrator startup without forwarding the ordinary window's options or data paths.
        if (journal?.Session is { } volume)
        {
            start.ArgumentList.Add(StartupOptions.OpenJournalArgument);
            start.ArgumentList.Add(journal.Path);
            start.ArgumentList.Add(volume);
        }
        if (folder is { } denied)
        {
            start.ArgumentList.Add(StartupOptions.ReopenFolderArgument);
            start.ArgumentList.Add(denied.Folder);
            start.ArgumentList.Add(denied.Volume);
        }
        if (linkReview is { } review)
        {
            start.ArgumentList.Add(StartupOptions.ReviewLinksArgument);
            start.ArgumentList.Add(review.Path);
            start.ArgumentList.Add(review.Hash);
        }
        if (task is { } request)
        {
            start.ArgumentList.Add(StartupOptions.AdministratorTaskArgument);
            start.ArgumentList.Add(request.Path);
            start.ArgumentList.Add(request.Hash);
        }
        return start;
    }

    internal static Dictionary<string, string> CaptureVolumes(IEnumerable<string> paths)
    {
        var volumes = new Dictionary<string, string>();
        foreach (string path in paths)
        {
            string root = (Path.GetPathRoot(path) ?? "").ToUpperInvariant();
            if (!volumes.ContainsKey(root))
                volumes.Add(root, LocalVolume(path) ?? throw new NotSupportedException("Administrator review requires local Windows drives."));
        }
        return volumes;
    }

    internal static void VerifyVolumes(IEnumerable<string> paths, Dictionary<string, string> volumes)
    {
        var actual = CaptureVolumes(paths);
        if (actual.Any(pair => !volumes.TryGetValue(pair.Key, out var expected) ||
                               !string.Equals(pair.Value, expected, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Windows could not find the original local drives for this request.");
    }

    internal static void Start(ProcessStartInfo info, bool waitForStartup)
    {
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows did not start FileCat.");
        // The elevated process snapshots the draft before setting up any UI. Keep the request available until then.
        if (waitForStartup) process.WaitForInputIdle(60_000);
    }

    internal static Task StartFileRetry(ProcessStartInfo info, CancellationToken stopping)
    {
        var process = Process.Start(info) ?? throw new InvalidOperationException("Windows did not start FileCat.");
        return WaitAsync(process, stopping);

        static async Task WaitAsync(Process process, CancellationToken stopping)
        {
            using (process)
            {
                try { await process.WaitForExitAsync(stopping).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            }
        }
    }

    // A real drive letter survives the account switch only when it still names the captured volume.
    // Reject UNC paths, mapped drives, SUBST aliases and Windows device namespaces.
    internal static string? LocalVolume(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
            ? DeviceTopology.VolumeDevice(path[..3]) : null;
    }
}
