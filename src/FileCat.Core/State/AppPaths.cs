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
    }

    public string SettingsDirectory { get; }
    public string LocalDirectory { get; }
    public bool IsPortable { get; }
    public string ProfileName { get; }

    public string JournalDirectory => Path.Combine(LocalDirectory, "journal");
    public string LogDirectory => Path.Combine(LocalDirectory, "diagnostics");
    public string CacheDirectory => Path.Combine(LocalDirectory, "cache");
    /// <summary>Private scratch space (listing spill files, staged previews); never on a browsed volume.</summary>
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
        if (File.Exists(Path.Combine(baseDirectory, PortableMarker)))
        {
            var root = Path.Combine(baseDirectory, "Data", suffix);
            return new AppPaths(root, Path.Combine(root, "local"), true, profile).Ensure();
        }
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        return new AppPaths(Path.Combine(roaming, "FileCat", suffix), Path.Combine(local, "FileCat", suffix), false, profile).Ensure();
    }

    private AppPaths Ensure()
    {
        foreach (var d in new[] { SettingsDirectory, LocalDirectory, JournalDirectory, LogDirectory, CacheDirectory, TempDirectory, WorkspacesDirectory })
            Directory.CreateDirectory(d);
        return this;
    }

    private static string Sanitize(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
}
