using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;

namespace FileCat.App.Services;

/// <summary>
/// The single composition root (plan §6.1): explicit construction, no service locator inside core code.
/// View models receive this object; Core types receive only what they need.
/// </summary>
public sealed class AppServices : IDisposable
{
    private AppServices(AppPaths paths, Remote.Sftp.ISftpConnector? sftpConnector = null)
    {
        Paths = paths;
        AppLog.Initialize(paths.LogDirectory);
        Settings = JsonFileStore.Load(paths.SettingsFile, StateJsonContext.Default.AppSettings, AppSettings.CurrentSchema,
            () => new AppSettings(), out var settingsStatus);
        SettingsStatus = settingsStatus;
        History = JsonFileStore.Load(paths.HistoryFile, StateJsonContext.Default.HistoryState, HistoryState.CurrentSchema,
            () => new HistoryState(), out var historyStatus);
        HistoryStatus = historyStatus;
        AppLog.DiagnosticMode = Settings.DiagnosticMode;

        Io = new DeviceIoScheduler();
        Io.HealthChanged += (device, health) => FileCatEventSource.Log.DeviceHealth(device, health.ToString());
        Ui = AvaloniaUiDispatcher.Instance;
        Commands = CommandRegistry.CreateDefault(CommandTranslations.Load(Path.Combine(AppContext.BaseDirectory, "lang"), System.Globalization.CultureInfo.CurrentUICulture));
        Keymap = new Keymap(Commands, Settings.KeyBindings);
        foreach (var c in Keymap.Conflicts) AppLog.Warn("Key binding: " + c);
        Icons = new IconProvider();
        Platform = PlatformFactory.Create();
        Shell = Platform.Shell;
        Providers = new ProviderRegistry();
        Platform.RegisterProviders(Providers);
        if (Providers.IsRegistered(Schemes.Network))
        {
            var networkRoot = new Location(Schemes.Network, string.Empty);
            if (OperatingSystem.IsWindows() && Providers.For(networkRoot) is FileCat.Platform.Windows.NetworkShareProvider network) network.KnownServers = KnownNetworkServers;
            else if (Providers.For(networkRoot) is Core.Network.UnixNetworkProvider unixNetwork) unixNetwork.KnownServers = KnownNetworkServers;
        }
        ResultSets = new Core.Search.ResultSetProvider(Providers, Platform.FileOperations);
        Providers.Register(ResultSets);
        WorkingSets = new Core.Search.WorkingSets(paths.WorkingSetsFile, ResultSets);
        Zip = new Core.Archives.ZipProvider(paths.TempDirectory);
        Providers.Register(Zip);
        // Read-only TAR, 7z, RAR, compressed files, and disc images (P8); archives of either kind nest in the other.
        Archives = new FileCat.Archives.ArchiveProvider(paths.TempDirectory, Providers);
        Providers.Register(Archives);
        // Files staged for dragging out of archives are taken by other programs long before a day passes.
        Task.Run(() => Core.Operations.DragStaging.Sweep(paths.TempDirectory, TimeSpan.FromDays(1)));
        // Deleted items of disk images, read-only (P10).
        Recovery = new FileCat.Recovery.RecoveryProvider();
        if (OperatingSystem.IsWindows())
        {
            // Drives are read through the installed administrator helper, which only reads (ADR-08); a FileCat that
            // runs as administrator already has the rights the helper would ask for, and reads them itself.
            string exchange = Path.Combine(paths.JournalDirectory, "elevation");
            Recovery.OpenDevice = (device, name, ct) => Environment.IsPrivilegedProcess
                ? FileCat.Platform.Windows.Recovery.DirectDeviceSource.Open(device, name)
                : FileCat.Platform.Windows.Recovery.BrokeredDeviceSource.Open(device, name, paths.IsPortable, exchange, ct);
            Recovery.SharesDisk = FileCat.Platform.Windows.Recovery.DeviceTopology.SharesDisk;
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Drives are read directly when this user may, otherwise through the system's own authorization, which hands
            // over a descriptor that only reads: UDisks2 and polkit on Linux, authopen on macOS (D-47).
            Recovery.OpenDevice = (device, name, ct) => FileCat.Recovery.Unix.UnixDeviceSource.Open(device, name, ct);
            Recovery.SharesDisk = FileCat.Recovery.Unix.UnixDisks.SharesDisk;
        }
        Providers.Register(Recovery);
        // Drives coming and going: This PC lists them anew, and panels leave a drive that is gone (plan §8.2).
        Drives = new DriveWatcher(() => Providers.TryGet(Schemes.Computer, out var p) && p is ComputerProvider computer ? computer.CurrentRoots() : []);
        Zip.OtherArchives = Archives;
        Zip.SpoolForeignMember = Archives.Spool;
        if (Providers.Get(Schemes.FileSystem) is LocalFileSystemProvider local) local.ContainerDetector = new ContainerDetectors(Zip, Archives);
        Formatters.DateFormat = Settings.DateFormat;
        Jobs = new Core.Jobs.JobManager(Platform.FileOperations, Providers, paths.JournalDirectory);
        // SFTP (P6): FileCat's own known_hosts beside its state, seeded read-only by OpenSSH's.
        // FTP and FTPS (P8) share the remote stack; FTPS certificates the OS does not accept are pinned only on request.
        Sftp = new Remote.Sftp.SftpConnections(FindRemoteProfile,
            new Remote.Sftp.ProtocolConnector(sftpConnector ?? new Remote.Sftp.SshNetConnector(), new Remote.Ftp.FluentFtpConnector()),
            new Remote.Sftp.HostKeyTrust(Path.Combine(paths.LocalDirectory, "known_hosts")), Platform.Secrets, new RefusingInteraction(),
            new Remote.Ftp.CertificateTrust(Path.Combine(paths.LocalDirectory, "trusted_certificates")));
        Sftp.ProfileChanged += p =>
        {
            if (!p.Temporary) SaveSettings();
        };
        SftpProvider = new Remote.Sftp.SftpProvider(Sftp, () => Settings.RemoteProfiles, p =>
        {
            lock (_temporaryProfiles)
            {
                var same = _temporaryProfiles.FirstOrDefault(t => string.Equals(t.Host, p.Host, StringComparison.OrdinalIgnoreCase) && t.Port == p.Port && t.User == p.User);
                if (same is not null) return same;
                _temporaryProfiles.Add(p);
                return p;
            }
        });
        Providers.Register(SftpProvider);
        Remote.Sftp.SftpJobs.Register();
        // Shell handlers run only in the restricted helper beside FileCat (plan §8.2, TV-16), started on first use.
        if (FileCat.Platform.Windows.Shell.ShellHostClient.FindExecutable() is { } shellHelper)
            ShellPictures = new FileCat.Platform.Windows.Shell.ShellPreviews(new FileCat.Platform.Windows.Shell.ShellHostClient(shellHelper), () => Settings.ShellPicturesOnNetworkAndRemovable);
        EditSessions = new Core.Edit.EditSessionStore(Path.Combine(paths.LocalDirectory, "edit-sessions"), Platform.FileOperations);
        Metadata = new Core.Metadata.MetadataService(Io);
        Columns = new Controls.ColumnProfileSet(Settings.ColumnProfiles);
        // Widths chosen by dragging and edited profiles persist immediately.
        Columns.Changed += () =>
        {
            Settings.ColumnProfiles = Columns.ToSettings();
            SaveSettings();
        };
        RegisterSavedFilters();
    }

    /// <summary>Mask prompt suggestions: saved filters first (as <c>@name</c>), then recent masks.</summary>
    public IReadOnlyList<string> MaskSuggestions() =>
        Settings.SavedFilters.Select(f => "@" + f.Name).Concat(History.Masks).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Column profiles shared by all tabs (Alt+0–9).</summary>
    public Controls.ColumnProfileSet Columns { get; }

    public Core.Metadata.MetadataService Metadata { get; }

    public FileCat.Recovery.RecoveryProvider Recovery { get; }

    /// <summary>Tells when drives or mount points come and go (checked while the main window listens).</summary>
    public DriveWatcher Drives { get; }

    public Core.Archives.ZipProvider Zip { get; private set; } = null!;

    /// <summary>Read-only archive formats other than ZIP.</summary>
    public FileCat.Archives.ArchiveProvider Archives { get; } = null!;

    public Core.Jobs.JobManager Jobs { get; }

    /// <summary>SFTP connections (leases, host keys, secrets); the main window supplies the prompts.</summary>
    public Remote.Sftp.SftpConnections Sftp { get; }
    public Remote.Sftp.SftpProvider SftpProvider { get; }
    private readonly List<RemoteProfile> _temporaryProfiles = [];

    /// <summary>A saved connection, or one typed as an sftp:// address this session.</summary>
    public RemoteProfile? FindRemoteProfile(string id)
    {
        var saved = Settings.RemoteProfiles.FirstOrDefault(p => p.Id == id);
        if (saved is not null) return saved;
        lock (_temporaryProfiles) return _temporaryProfiles.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Until a window can ask, connections that need the user are refused rather than left waiting.</summary>
    private sealed class RefusingInteraction : Remote.Sftp.IRemoteInteraction
    {
        public Remote.Sftp.HostKeyDecision DecideHostKey(RemoteProfile profile, Remote.Sftp.HostKeyCheck check) => Remote.Sftp.HostKeyDecision.Reject;
        public Remote.Sftp.SecretAnswer? AskSecret(RemoteProfile profile, Remote.Sftp.SecretRequest request) => null;
        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) => null;
    }
    /// <summary>Persistent external edits of archive members (plan §14.2).</summary>
    public Core.Edit.EditSessionStore EditSessions { get; }
    public Core.Search.ResultSetProvider ResultSets { get; }
    /// <summary>Shell thumbnails and per-file icons from the restricted helper; null where this build has none.</summary>
    public FileCat.Platform.Windows.Shell.ShellPreviews? ShellPictures { get; }

    /// <summary>The helper, when the user allows Shell pictures.</summary>
    public FileCat.Platform.Windows.Shell.ShellPreviews? AllowedShellPictures => Settings.ShellPictures ? ShellPictures : null;

    /// <summary>Named, persistent reference sets (P7).</summary>
    public Core.Search.WorkingSets WorkingSets { get; }

    public static AppServices Current { get; private set; } = null!;

    public AppPaths Paths { get; }
    public AppSettings Settings { get; }
    public StateLoadStatus SettingsStatus { get; }
    public HistoryState History { get; }
    public StateLoadStatus HistoryStatus { get; }
    public IndexMemoryBudget ListingIndexes { get; } = new();
    public DeviceIoScheduler Io { get; }
    public IUiDispatcher Ui { get; }
    public CommandRegistry Commands { get; }
    public Keymap Keymap { get; private set; }
    public IconProvider Icons { get; }
    public IPlatform Platform { get; }
    public IShellServices Shell { get; }
    public ProviderRegistry Providers { get; }

    public bool SettingsReadOnly => SettingsStatus == StateLoadStatus.NewerSchemaReadOnly;

    /// <summary>Constructs an isolated composition root at explicit paths (including headless UI tests).</summary>
    /// <param name="sftpConnector">Tests connect to an in-memory server instead of SSH.NET.</param>
    public static AppServices CreateForPaths(AppPaths paths, Remote.Sftp.ISftpConnector? sftpConnector = null) => new(paths, sftpConnector);

    /// <param name="overrideRoot">Isolated state root (the TV-01 benchmark never touches the user's profile).</param>
    public static AppServices Initialize(string? profile, string? overrideRoot = null)
    {
        Current = new AppServices(AppPaths.Resolve(profile, overrideRoot: overrideRoot));
        return Current;
    }

    public void ReloadKeymap() => Keymap = new Keymap(Commands, Settings.KeyBindings);

    /// <summary>
    /// The servers already reached (D-54): those of mapped network drives, then of the folder history and bookmarks,
    /// most recent first.
    /// </summary>
    internal IEnumerable<string> KnownNetworkServers()
    {
        var servers = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Network && FileCat.Platform.Windows.WindowsNetwork.GetRemoteName(drive.Name.TrimEnd('\\')) is { } remote &&
                        PathUtil.GetUncServer(remote) is { } server) servers.Add(server.TrimStart('\\'));
                }
                catch (IOException) { }
            }
        }
        // Read off the UI thread while navigating adds to the history: copies, not enumerations of the live lists.
        foreach (var location in History.Folders.ToArray().Select(h => h.Location).Concat(History.Bookmarks.ToArray().Select(b => b.Location)))
        {
            if (location?.Root is not { } root) continue;
            string path = root.Path;
            if (root.Scheme == Schemes.Network && path.Length > 0)
            {
                // \\server on Windows, smb://server/share elsewhere.
                if (Core.Network.UnixNetworkProvider.Parse(path).Server is { } smb) servers.Add(smb);
                else servers.Add(path.TrimStart('\\'));
            }
            else if (root.IsFileSystem && PathUtil.GetUncServer(path) is { } server) servers.Add(server.TrimStart('\\'));
            // A share gvfs mounted: …/gvfs/smb-share:server=NAME,share=SHARE/…
            else if (root.IsFileSystem && path.Split('/').Select(Core.Network.SmbTools.ParseGvfsName).FirstOrDefault(g => g is not null) is { } gvfs)
                servers.Add(gvfs.Server);
        }
        return servers.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Settings or history were saved (or would have been): bookmarks, saved servers, and other places may have changed.</summary>
    public event Action? Saved;

    public void SaveSettings()
    {
        if (!SettingsReadOnly)
        {
            try { JsonFileStore.Save(Paths.SettingsFile, Settings, StateJsonContext.Default.AppSettings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Saving settings failed", ex); }
        }
        Saved?.Invoke();
    }

    /// <summary>Saved filters for <c>@name</c> in masks; looked up at parse time, so edits apply to the next mask.</summary>
    private void RegisterSavedFilters() =>
        Core.Selection.Mask.SavedFilters = name => Settings.SavedFilters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Mask;

    public void SaveHistory()
    {
        if (HistoryStatus != StateLoadStatus.NewerSchemaReadOnly)
        {
            try { JsonFileStore.Save(Paths.HistoryFile, History, StateJsonContext.Default.HistoryState); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Saving history failed", ex); }
        }
        Saved?.Invoke();
    }

    /// <summary>Forgets recent locations and typed text (privacy). Bookmarks stay; pinned entries stay unless included.</summary>
    public void ClearHistory(bool includePinned)
    {
        History.Folders.RemoveAll(h => includePinned || !h.Pinned);
        History.Files.RemoveAll(h => includePinned || !h.Pinned);
        History.CommandLine.Clear();
        History.ApplyCommands.Clear();
        History.CopyDestinations.Clear();
        History.Masks.Clear();
        History.SearchNames.Clear();
        History.SearchTexts.Clear();
        History.SearchFolders.Clear();
        History.RecentCommands.Clear();
        SaveHistory();
    }

    public void RecordFolder(Location location)
    {
        if (location.Scheme is Schemes.ResultSet && !Core.Search.ResultSetProvider.IsPersistent(location)) return;
        var list = History.Folders;
        list.RemoveAll(h => h.Location == location && !h.Pinned);
        var existing = list.FirstOrDefault(h => h.Location == location);
        if (existing is not null) existing.LastUsedUtc = DateTime.UtcNow;
        else list.Insert(0, new HistoryEntry { Location = location, LastUsedUtc = DateTime.UtcNow });
        Trim(list);
    }

    public void RecordFile(Location parent, string name)
    {
        var list = History.Files;
        list.RemoveAll(h => h.Location == parent && h.Name == name && !h.Pinned);
        if (!list.Any(h => h.Location == parent && h.Name == name))
            list.Insert(0, new HistoryEntry { Location = parent, Name = name, LastUsedUtc = DateTime.UtcNow });
        Trim(list);
    }

    private void Trim(List<HistoryEntry> list)
    {
        int max = Math.Max(20, Settings.HistorySize);
        while (list.Count > max)
        {
            int idx = list.FindLastIndex(h => !h.Pinned);
            if (idx < 0) break;
            list.RemoveAt(idx);
        }
    }

    public static void RememberText(List<string> list, string value, int max = 50)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        list.RemoveAll(v => v == value);
        list.Insert(0, value);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
    }

    public void Dispose()
    {
        Drives.Dispose();
        Recovery.CloseAll(); // drives' helper sessions end with FileCat
        WorkingSets.Dispose();
        ShellPictures?.Dispose();
        Sftp.Dispose();
        Io.Dispose();
        Platform.Dispose();
    }
}
