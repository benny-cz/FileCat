using System.Diagnostics;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Recovery;

namespace FileCat.App.Services;

internal static class AdministratorWindow
{
    /// <summary>The native launcher beside FileCat.exe that every administrator window starts through (I644).</summary>
    internal const string LauncherName = "FileCat.AdminLauncher.exe";
    internal const string ExecutableName = "FileCat.exe";

    /// <summary>
    /// Starts the administrator window through <see cref="LauncherName"/>, never FileCat.exe itself. Windows builds an
    /// elevated program's environment from the account's registry environment, which the ordinary account can change
    /// without elevation, and the .NET runtime loads the profilers, startup hooks and runtimes named there before
    /// FileCat's own code runs. The launcher removes those settings and then starts the FileCat.exe beside it with
    /// exactly these arguments. <paramref name="applicationFolder"/> (tests) replaces FileCat's own folder.
    /// </summary>
    internal static ProcessStartInfo CreateStartInfo(Location? journal = null,
        (string Folder, string Volume)? folder = null, (string Path, string Hash)? linkReview = null,
        (string Path, string Hash)? task = null, string? applicationFolder = null)
    {
        // FileCat's own folder, also when a development build was started through dotnet.
        var start = new ProcessStartInfo(Path.Combine(applicationFolder ?? AppContext.BaseDirectory, LauncherName))
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

    /// <summary>
    /// Refuses before Windows asks for approval when the launcher or FileCat.exe is missing. There is no fallback to
    /// starting FileCat.exe directly: that would open the administrator window with the account's runtime settings.
    /// </summary>
    internal static void RequireLauncher(ProcessStartInfo info)
    {
        string folder = Path.GetDirectoryName(info.FileName) ?? "";
        if (!Path.GetFileName(info.FileName).Equals(LauncherName, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(info.FileName) || !File.Exists(Path.Combine(folder, ExecutableName)))
            throw new FileNotFoundException($"{LauncherName} or {ExecutableName} is missing from {Formatters.SafeName(folder)}. " +
                "FileCat opens an administrator window only through this launcher, which first removes the account's .NET runtime settings. Reinstall FileCat.",
                info.FileName);
    }

    // The launcher becomes idle only after the FileCat it started has, and exits with it, so the waits below follow
    // the administrator window itself.
    internal static void Start(ProcessStartInfo info, bool waitForStartup)
    {
        RequireLauncher(info);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows did not start FileCat.");
        // The elevated process snapshots the draft before setting up any UI. Keep the request available until then.
        if (waitForStartup) process.WaitForInputIdle(60_000);
    }

    internal static Task StartFileRetry(ProcessStartInfo info, CancellationToken stopping)
    {
        RequireLauncher(info);
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
