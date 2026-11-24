using System.Security.Cryptography;
using System.Text;
using FileCat.Core.FileSystem;

namespace FileCat.Core.State;

/// <summary>
/// State roots (plan §19.1). Installed builds use the per-user application-data folders; a portable
/// build is selected by a <c>FileCat.portable</c> marker next to the executable and keeps everything in
/// a <c>Data</c> folder beside it. Recovery-critical records, caches, and logs live in separate folders.
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarker = "FileCat.portable";

    /// <param name="dataRoot">A folder that holds everything FileCat writes (--data), the listing scratch and hex originals included.</param>
    /// <param name="privateRoot">Where the listing scratch and hex originals go when it is not <paramref name="dataRoot"/>
    /// or the user's local application data (root's session folder, I655).</param>
    private AppPaths(string settingsDir, string localDir, bool portable, string profile, IReadOnlyList<string> ownRoots, string? dataRoot = null,
        string? privateRoot = null)
    {
        SettingsDirectory = settingsDir;
        LocalDirectory = localDir;
        IsPortable = portable;
        ProfileName = profile;
        DataRoot = dataRoot;
        if (privateRoot is null && dataRoot is not null) privateRoot = dataRoot;
        else if (privateRoot is null)
        {
            privateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(privateRoot)) privateRoot = Path.GetTempPath();
            privateRoot = Path.Combine(privateRoot, "FileCat");
        }
        ListingScratchDirectory = Path.Combine(privateRoot, "scratch", profile);
        AnalysisScratchDirectory = DefaultAnalysisScratchDirectory;
        HexRecoveryDirectory = Path.Combine(privateRoot, "hex-recovery", profile);
        OwnRoots = [.. ownRoots, privateRoot];
    }

    /// <summary>The folder given with --data, which holds everything FileCat writes; null when FileCat keeps its usual places.</summary>
    public string? DataRoot { get; }

    /// <summary>The folders FileCat owns whole: all its state lives below them.</summary>
    private IReadOnlyList<string> OwnRoots { get; }

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

    /// <summary>Owner-private, disposable analysis sessions. Created by the analysis native store, not Ensure.</summary>
    public string AnalysisScratchDirectory { get; private set; }
    internal string DefaultAnalysisScratchDirectory => Path.Combine(ListingScratchDirectory, "analysis");
    internal string? AnalysisScratchSelectionError { get; private set; }
    /// <summary>Owner-private named inventories, independent of disposable caches.</summary>
    public string AnalysisSnapshotsDirectory => Path.Combine(LocalDirectory, "analysis-snapshots");
    /// <summary>Isolated owner-private rolling inventories; retention never opens the named-store root.</summary>
    public string AnalysisHistoryDirectory => Path.Combine(LocalDirectory, "analysis-history");
    /// <summary>Private versioned named/rolling inventory index; never generic application history.</summary>
    public string AnalysisCatalogFile => Path.Combine(AnalysisSnapshotsDirectory, "catalog.json");
    /// <summary>Private roots, queries, recipes and write-locator history; excluded from normal preferences.</summary>
    public string AnalysisPrivateStateFile => Path.Combine(AnalysisSnapshotsDirectory, "private-state.json");

    public string JournalDirectory => Path.Combine(LocalDirectory, "journal");
    public string LogDirectory => Path.Combine(LocalDirectory, "diagnostics");
    public string CacheDirectory => Path.Combine(LocalDirectory, "cache");
    /// <summary>Independent instance lifetime files, inside the same guarded local state root.</summary>
    public string InstancesDirectory => Path.Combine(LocalDirectory, "instances");
    /// <summary>.reg backups taken before Registry subtrees are deleted (plan §12.2: recoverable original data).</summary>
    public string RegistryBackupDirectory => Path.Combine(LocalDirectory, "registry-backups");
    /// <summary>Portable/local scratch for staged previews and tool argument files.</summary>
    public string TempDirectory => Path.Combine(LocalDirectory, "temp");
    public string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");
    public string WorkspaceFile => Path.Combine(SettingsDirectory, "workspace.json");
    public string HistoryFile => Path.Combine(SettingsDirectory, "history.json");
    public string WorkingSetsFile => Path.Combine(SettingsDirectory, "working-sets.json");
    public string WorkspacesDirectory => Path.Combine(SettingsDirectory, "workspaces");
    /// <summary>Trusted minisign keys (D-57).</summary>
    public string KeysDirectory => Path.Combine(SettingsDirectory, "keys");
    /// <summary>Files being edited in other programs (P5).</summary>
    public string EditSessionsDirectory => Path.Combine(LocalDirectory, "edit-sessions");
    /// <summary>Private, identity-bound edit storage, used only where it is qualified (<c>NativeEditStorage</c>); created on first use.</summary>
    public string NativeEditSessionsDirectory => Path.Combine(LocalDirectory, "edit-sessions-native");
    /// <summary>Plans and reports exchanged with the administrator helper (Windows).</summary>
    public string ElevationExchangeDirectory => Path.Combine(JournalDirectory, "elevation");
    /// <summary>The page views' own data (WebView2, WebKit's content rules).</summary>
    public string PageViewDataDirectory => Path.Combine(CacheDirectory, "webview");

    /// <summary>
    /// Every folder FileCat itself writes in, with what it keeps there: the inventory a recovery's source is checked against
    /// (release plan V09, I09). Folders inside others are listed as well, since any of them can be a link to elsewhere.
    /// </summary>
    public IReadOnlyList<(string What, string Folder)> WriteFolders =>
    [
        ("settings and history", SettingsDirectory),
        ("settings and history", WorkspacesDirectory),
        ("settings and history", KeysDirectory),
        ("logs, journals, caches and temporary files", LocalDirectory),
        ("logs, journals, caches and temporary files", InstancesDirectory),
        ("logs, journals, caches and temporary files", JournalDirectory),
        ("logs, journals, caches and temporary files", ElevationExchangeDirectory),
        ("logs, journals, caches and temporary files", LogDirectory),
        ("logs, journals, caches and temporary files", CacheDirectory),
        ("logs, journals, caches and temporary files", PageViewDataDirectory),
        ("logs, journals, caches and temporary files", TempDirectory),
        ("logs, journals, caches and temporary files", RegistryBackupDirectory),
        ("logs, journals, caches and temporary files", EditSessionsDirectory),
        ("logs, journals, caches and temporary files", NativeEditSessionsDirectory),
        ("the scratch of large listings", ListingScratchDirectory),
        ("private disk-space analysis scratch", AnalysisScratchDirectory),
        ("private saved disk-space analyses and catalog", AnalysisSnapshotsDirectory),
        ("private rolling disk-space analysis history", AnalysisHistoryDirectory),
        ("originals kept while hex edits are saved", HexRecoveryDirectory),
    ];

    /// <param name="dataRoot">--data: everything FileCat writes goes below this folder (recovering from the disk that holds FileCat's usual places).</param>
    /// <param name="analysisScratchParent">Explicit existing parent for this launch's private analysis scratch directory.</param>
    public static AppPaths Resolve(string? profile = null, string? baseDirectory = null, string? overrideRoot = null, string? dataRoot = null,
        string? analysisScratchParent = null)
    {
        profile = ProfileFolderName(profile);
        var suffix = profile == "default" ? string.Empty : Path.Combine("profiles", profile);
        if (!string.IsNullOrEmpty(overrideRoot))
        {
            var root = Path.Combine(overrideRoot, suffix);
            // Root's listing scratch and hex originals stay out of the user's local application data (I655).
            string? scratch = UsesRootSession ? RootSession.Folder() : null;
            return new AppPaths(root, Path.Combine(root, "local"), true, profile, [overrideRoot], privateRoot: scratch)
                .ConfigureAnalysisScratch(analysisScratchParent, overrideRoot).Ensure();
        }
        baseDirectory ??= AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(dataRoot))
        {
            string data = Path.GetFullPath(dataRoot);
            var root = Path.Combine(data, suffix);
            // A portable copy or an installed one alike: which administrator helper it may use does not change.
            return new AppPaths(root, Path.Combine(root, "local"), File.Exists(Path.Combine(baseDirectory, PortableMarker)), profile, [data], data)
                .ConfigureAnalysisScratch(analysisScratchParent, data).Ensure();
        }
        if (UsesRootSession)
        {
            // Root on Linux and macOS (I655): everything it writes while it runs goes to a folder of its own, as if it had been
            // started with --data there, never to a profile or a portable copy's data folder (see RootSession).
            string session = RootSession.Folder();
            var root = Path.Combine(session, suffix);
            return new AppPaths(root, Path.Combine(root, "local"), File.Exists(Path.Combine(baseDirectory, PortableMarker)), profile, [session], session)
                .ConfigureAnalysisScratch(analysisScratchParent, session).Ensure();
        }
        string? portableProblem = null;
        if (File.Exists(Path.Combine(baseDirectory, PortableMarker)))
        {
            var root = Path.Combine(baseDirectory, "Data", suffix);
            // A marker in a folder the user cannot write to (for example under Program Files) must not stop startup.
            if (IsWritable(root)) return new AppPaths(root, Path.Combine(root, "local"), true, profile, [Path.Combine(baseDirectory, "Data")])
                .ConfigureAnalysisScratch(analysisScratchParent).Ensure();
            portableProblem = $"The portable data folder \"{root}\" is not writable, so FileCat keeps its settings in your user profile instead.";
        }
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        return new AppPaths(Path.Combine(roaming, "FileCat", suffix), Path.Combine(local, "FileCat", suffix), false, profile,
            [Path.Combine(roaming, "FileCat"), Path.Combine(local, "FileCat")]) { PortableUnavailableReason = portableProblem }
            .ConfigureAnalysisScratch(analysisScratchParent).Ensure();
    }

    /// <summary>
    /// Whether this process keeps what it writes in <see cref="RootSession"/>: root on Linux and macOS (I655). Whatever it
    /// made in a profile would belong to root, so the ordinary FileCat could not read it or save beside it, and a profile
    /// keeps state, which root takes none of. An administrator window keeps its profile's folders, where Windows gives the
    /// account rights to whatever it makes, and keeps out of their state.
    /// </summary>
    internal static bool UsesRootSession => !OperatingSystem.IsWindows() && ProcessAuthority.IsElevatedOrRoot;

    private AppPaths ConfigureAnalysisScratch(string? parent, string? containmentRoot = null)
    {
        if (parent is null) return this;
        if (!Path.IsPathFullyQualified(parent) || PathUtil.HasAmbiguousUnixName(parent) ||
            parent.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(part => part is "." or ".."))
            AnalysisScratchSelectionError = "Choose an absolute analysis scratch folder without relative path components.";
        else
        {
            try
            {
                string full = Path.GetFullPath(parent);
                // Exact spelling keeps this write boundary valid in case-sensitive Windows directories.
                string? root = containmentRoot is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(containmentRoot));
                if (root is not null && !full.Equals(root, StringComparison.Ordinal) &&
                    !full.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    AnalysisScratchSelectionError = "The analysis scratch folder must be inside this launch's data folder.";
                else
                {
                    string profile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SettingsDirectory)))[..24].ToLowerInvariant();
                    AnalysisScratchDirectory = Path.Combine(full, "filecat-analysis-" + profile);
                }
            }
            catch (ArgumentException) { AnalysisScratchSelectionError = "The selected analysis scratch folder path is invalid."; }
        }
        return this;
    }

    /// <summary>
    /// Where a FileCat started without --data keeps its files, worked out without making or writing anything: what the
    /// usual FileCat goes on writing to while one started with --data recovers (release plan V09, I09).
    /// </summary>
    public static AppPaths Usual(string? profile = null, string? baseDirectory = null)
    {
        profile = ProfileFolderName(profile);
        var suffix = profile == "default" ? string.Empty : Path.Combine("profiles", profile);
        baseDirectory ??= AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDirectory, PortableMarker)))
        {
            var root = Path.Combine(baseDirectory, "Data", suffix);
            return new AppPaths(root, Path.Combine(root, "local"), true, profile, [Path.Combine(baseDirectory, "Data")]);
        }
        return UsualUser(profile);
    }

    /// <summary>
    /// Possible state locations of windows started without --data. A portable recovery must also find installed
    /// windows and portable windows that fell back to per-user storage. This inventory creates and writes nothing.
    /// </summary>
    public static IReadOnlyList<AppPaths> UsualCandidates(string? profile = null, string? baseDirectory = null)
    {
        var selected = Usual(profile, baseDirectory);
        return selected.IsPortable ? [selected, UsualUser(ProfileFolderName(profile))] : [selected];
    }

    private static AppPaths UsualUser(string profile)
    {
        var suffix = profile == "default" ? string.Empty : Path.Combine("profiles", profile);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return new AppPaths(Path.Combine(roaming, "FileCat", suffix), Path.Combine(local, "FileCat", suffix), false, profile,
            [Path.Combine(roaming, "FileCat"), Path.Combine(local, "FileCat")]);
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
        {
            // Linux and macOS: FileCat's folders are its user's alone (release plan P16). They hold file names (history,
            // journals, diagnostics) and file contents (previews of archive members and remote files, hex originals), and
            // a folder made under the usual umask (0755) is readable by every local account wherever the home folder does
            // not stop them (Debian's homes are 0755). Folders made before are tightened at the next start; where the mode
            // cannot be set (a FAT stick holding a portable copy), nothing is lost but the setting.
            TightenOwnFolders(OwnRoots, ListingScratchDirectory, Environment.IsPrivilegedProcess);
        }
        return this;
    }

    /// <summary>
    /// Linux and macOS: makes FileCat's folders their user's alone (0700); the listing scratch must be, the others are
    /// where their volume allows. Root changes no folder that is a link (I645): chmod follows a link, and a root FileCat
    /// whose HOME is the user's (sudo keeps it on macOS, and with -E on Linux) would otherwise change the mode of whatever
    /// the user's programs linked there.
    /// </summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    internal static void TightenOwnFolders(IEnumerable<string> roots, string listingScratch, bool privileged)
    {
        const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        foreach (var root in roots)
        {
            if (privileged && UnixFiles.Stat(root) is not { IsLink: false }) continue;
            try { File.SetUnixFileMode(root, Private); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
        }
        if (privileged && UnixFiles.Stat(listingScratch) is not { IsLink: false }) return;
        File.SetUnixFileMode(listingScratch, Private);
    }

    /// <summary>
    /// Files a crashed session left in <see cref="TempDirectory"/>: archive members and nested archives spooled for viewing
    /// ("member-*", "nested-*"). They are deleted on close, by Windows even after a crash, but on Linux and macOS only by
    /// FileCat itself. Those older than <paramref name="age"/> go; one still open stays usable (Windows refuses the
    /// deletion; Linux and macOS keep its data until it is closed). Returns how many went.
    /// <para>
    /// Never through a link (I645): a temporary folder that is itself a link is not swept, and a link named as a leftover
    /// is removed itself, never what it leads to. Not for an elevated FileCat, whose temporary folder is the ordinary
    /// account's, nor for root, whose session folder goes when it closes (<c>AppServices.SweepTemporaryFolders</c>).
    /// </para>
    /// </summary>
    public int SweepTemporaryLeftovers(TimeSpan age)
    {
        if (!TemporarySweep.IsPlainFolder(TempDirectory)) return 0;
        int removed = 0;
        foreach (var entry in TemporarySweep.Entries(TempDirectory))
        {
            try
            {
                if (!entry.Name.StartsWith("member-", StringComparison.Ordinal) && !entry.Name.StartsWith("nested-", StringComparison.Ordinal)) continue;
                // FileCat spools files here: a folder of such a name is not its own (a link to one is a link, and goes itself).
                if ((entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory) continue;
                if (DateTime.UtcNow - entry.LastWriteTimeUtc < age) continue;
                if (TemporarySweep.RemoveItself(entry.FullName)) removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }

    /// <summary>
    /// A profile's name as its folders and its instance use it: letters, digits, '-' and '_', at most 40; "default" when
    /// nothing of it is left. Two names that differ only in other characters are one profile, so one instance.
    /// </summary>
    public static string ProfileFolderName(string? profile) =>
        string.IsNullOrWhiteSpace(profile) || Sanitize(profile) is not { Length: > 0 } safe ? "default" : safe;

    private static string Sanitize(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
}
