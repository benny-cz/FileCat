namespace FileCat.Core.State;

/// <summary>
/// State roots (plan §19.1). Installed builds use the per-user application-data folders; a portable
/// build is selected by a <c>FileCat.portable</c> marker next to the executable and keeps everything in
/// a <c>Data</c> folder beside it. Recovery-critical records, caches, and logs live in separate folders.
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarker = "FileCat.portable";

    private AppPaths(string settingsDir, string localDir, bool portable, string profile)
    {
        SettingsDirectory = settingsDir;
        LocalDirectory = localDir;
        IsPortable = portable;
        ProfileName = profile;
        var privateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(privateRoot)) privateRoot = Path.GetTempPath();
        ListingScratchDirectory = Path.Combine(privateRoot, "FileCat", "scratch", profile);
        HexRecoveryDirectory = Path.Combine(privateRoot, "FileCat", "hex-recovery", profile);
    }

    public string SettingsDirectory { get; }
    public string LocalDirectory { get; }
    public bool IsPortable { get; }
    /// <summary>Set when a portable marker was found but its data folder is not writable (per-user state is used).</summary>
    public string? PortableUnavailableReason { get; private init; }
    public string ProfileName { get; }
    /// <summary>User-local ephemeral listing data, including in portable mode.</summary>
    public string ListingScratchDirectory { get; }
    /// <summary>Original bytes for interrupted hex saves stay in user-local storage, including portable mode.</summary>
    public string HexRecoveryDirectory { get; }

    public string JournalDirectory => Path.Combine(LocalDirectory, "journal");
    public string LogDirectory => Path.Combine(LocalDirectory, "diagnostics");
    public string CacheDirectory => Path.Combine(LocalDirectory, "cache");
    /// <summary>Portable/local scratch for staged previews and tool argument files.</summary>
    public string TempDirectory => Path.Combine(LocalDirectory, "temp");
    public string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");
    public string WorkspaceFile => Path.Combine(SettingsDirectory, "workspace.json");
    public string HistoryFile => Path.Combine(SettingsDirectory, "history.json");
    public string WorkspacesDirectory => Path.Combine(SettingsDirectory, "workspaces");

    public static AppPaths Resolve(string? profile = null, string? baseDirectory = null, string? overrideRoot = null)
    {
        profile = string.IsNullOrWhiteSpace(profile) ? "default" : Sanitize(profile);
        var suffix = profile == "default" ? string.Empty : Path.Combine("profiles", profile);
        if (!string.IsNullOrEmpty(overrideRoot))
        {
            var root = Path.Combine(overrideRoot, suffix);
            return new AppPaths(root, Path.Combine(root, "local"), true, profile).Ensure();
        }
        baseDirectory ??= AppContext.BaseDirectory;
        string? portableProblem = null;
        if (File.Exists(Path.Combine(baseDirectory, PortableMarker)))
        {
            var root = Path.Combine(baseDirectory, "Data", suffix);
            // A marker in a folder the user cannot write to (for example under Program Files) must not stop startup.
            if (IsWritable(root)) return new AppPaths(root, Path.Combine(root, "local"), true, profile).Ensure();
            portableProblem = $"The portable data folder \"{root}\" is not writable, so FileCat keeps its settings in your user profile instead.";
        }
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        return new AppPaths(Path.Combine(roaming, "FileCat", suffix), Path.Combine(local, "FileCat", suffix), false, profile) { PortableUnavailableReason = portableProblem }.Ensure();
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private AppPaths Ensure()
    {
        foreach (var d in new[] { SettingsDirectory, LocalDirectory, JournalDirectory, LogDirectory, CacheDirectory, TempDirectory, WorkspacesDirectory, ListingScratchDirectory, HexRecoveryDirectory })
            Directory.CreateDirectory(d);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(ListingScratchDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return this;
    }

    private static string Sanitize(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
}
