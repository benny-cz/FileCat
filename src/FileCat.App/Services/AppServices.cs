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
    private AppServices(AppPaths paths, Remote.Sftp.ISftpConnector? sftpConnector = null, Core.Archives.ArchiveJobBackends? archiveBackends = null,
        Func<AppPaths,IFileSystemOperations,Core.Edit.EditSessionStore>? editSessionFactory = null, IOwnership? ownershipOverride = null,
        int metadataCacheSize = 50_000, Func<long>? metadataClock = null, IStorageTransforms? storageTransforms = null)
    {
        Paths = paths;
        AppLog.Initialize(paths.LogDirectory);
        StateLoadStatus settingsStatus;
        if (OperatingSystem.IsWindows() && !WindowsTokenAuthority.IsHighIntegrity)
        {
            _windowsSettings = new WindowsSettingsFile(paths.SettingsFile);
            Settings = _windowsSettings.Load(out settingsStatus);
        }
        else
            Settings = JsonFileStore.Load(paths.SettingsFile, StateJsonContext.Default.AppSettings, AppSettings.CurrentSchema,
                () => new AppSettings(), out settingsStatus);
        SettingsStatus = settingsStatus;
        Settings.Terminal.Upgrade();
        History = JsonFileStore.Load(paths.HistoryFile, StateJsonContext.Default.HistoryState, HistoryState.CurrentSchema,
            () => new HistoryState(), out var historyStatus);
        HistoryStatus = historyStatus;
        AppLog.DiagnosticMode = Settings.DiagnosticMode;
        Core.Content.PageCacheBudget.Shared.LimitBytes = Math.Clamp(Settings.ContentCacheMiB, 4, 4096) * 1024L * 1024;

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
        Core.Metadata.BuiltInFields.HiddenDataSource = Platform.HiddenData;
        if (Providers.IsRegistered(Schemes.Network))
        {
            var networkRoot = new Location(Schemes.Network, string.Empty);
            if (OperatingSystem.IsWindows() && Providers.For(networkRoot) is FileCat.Platform.Windows.NetworkShareProvider network) network.KnownServers = KnownNetworkServers;
            else if (Providers.For(networkRoot) is Core.Network.UnixNetworkProvider unixNetwork) unixNetwork.KnownServers = KnownNetworkServers;
        }
        ResultSets = new Core.Search.ResultSetProvider(Providers, Platform.FileOperations);
        Providers.Register(ResultSets);
        WorkingSets = new Core.Search.WorkingSets(paths.WorkingSetsFile, ResultSets);
        WorkingSets.SaveFailed += ex => ReportSaveFailure("working sets", ex);
        // Provider caches identify the actual local source, including a final symlink target.
        string? ArchiveIdentity(string path) => Platform.FileOperations.GetFileIdentity(Platform.FileOperations.GetFinalPath(path) ?? path);
        static string? OpenedArchiveIdentity(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
        {
            try { return FileCat.Platform.Windows.ProtectedHexFile.Identity(handle).ToString(); }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { return null; }
        }
        Zip = new Core.Archives.ZipProvider(paths.TempDirectory) { GetSourceIdentity = ArchiveIdentity, GetOpenedSourceIdentity = OpenedArchiveIdentity };
        Providers.Register(Zip);
        // Read-only TAR, 7z, RAR, compressed files, and disc images (P8); archives of either kind nest in the other.
        Archives = new FileCat.Archives.ArchiveProvider(paths.TempDirectory, Providers) { GetSourceIdentity = ArchiveIdentity, GetOpenedSourceIdentity = OpenedArchiveIdentity };
        Providers.Register(Archives);
        // Files staged for dragging out of archives are taken by other programs long before a day passes; members spooled
        // for viewing that a crash left behind (Linux, macOS) go after a day as well.
        Task.Run(() =>
        {
            Core.Operations.DragStaging.Sweep(paths.TempDirectory, TimeSpan.FromDays(1));
            ClipboardVirtualStaging.Sweep(paths.TempDirectory, TimeSpan.FromDays(1));
            paths.SweepTemporaryLeftovers(TimeSpan.FromDays(1));
        });
        // Deleted items of disk images, read-only (P10).
        Recovery = new FileCat.Recovery.RecoveryProvider { GetSourceIdentity = ArchiveIdentity, GetOpenedSourceIdentity = OpenedArchiveIdentity };
        if (OperatingSystem.IsWindows())
        {
            // Drives are read through the installed administrator helper, which only reads (ADR-08); a FileCat that
            // runs as administrator already has the rights the helper would ask for, and reads them itself.
            string exchange = paths.ElevationExchangeDirectory;
            Recovery.OpenDevice = (device, name, ct) => FileCat.Platform.Windows.Recovery.SelectedDiskInterfaceSource.Bind(
                device, Environment.IsPrivilegedProcess
                    ? FileCat.Platform.Windows.Recovery.DirectDeviceSource.Open(device, name)
                    : FileCat.Platform.Windows.Recovery.BrokeredDeviceSource.Open(device, name, paths.IsPortable, exchange, ct));
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
        Jobs = new Core.Jobs.JobManager(Platform.FileOperations, Providers, paths.JournalDirectory,
            archiveBackends ?? Core.Archives.ArchiveJobBackends.Empty, storageTransforms ?? Platform.StorageTransforms,
            FileCat.Platform.Windows.WindowsStorageReviewEvidence.IsSupportedHost ? FileCat.Platform.Windows.WindowsStorageReviewEvidence.CreateJournal : null,
            FileCat.Platform.Windows.WindowsStorageReviewEvidence.IsSupportedHost ? FileCat.Platform.Windows.WindowsStorageReviewEvidence.Backend : null);
        Archives.MutationAvailability = plan => Jobs.CanReviewArchive(plan) ? Jobs.ArchiveAvailability(plan)
            : Core.Archives.ArchiveJobAvailability.Unqualified;
        // A ZIP route composed here moves ZIP changes to the worker profile, available or refused with its reason; without
        // one, ZIP keeps its existing route.
        Zip.RouteComposed = Jobs.CoversArchive;
        Zip.MutationAvailability = Archives.MutationAvailability;
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
        EditSessions = editSessionFactory is null ? ProductionEditSessions(paths, Platform.FileOperations)
            : editSessionFactory(paths,Platform.FileOperations);
        var ownership = ownershipOverride ?? Platform.Ownership;
        PrincipalNames = new PrincipalNames(ownership);
        Metadata = new Core.Metadata.MetadataService(Io, metadataClock ?? (() => Environment.TickCount64),
            maxCache: metadataCacheSize, ownership: ownership, names: PrincipalNames, fileRecords: Platform.FileRecords);
        // Checksums and signatures beside files (D-57): results kept in the cache folder; trusted minisign keys in the keys
        // folder (made now, so the advice that names it points to a folder that is there).
        try { Directory.CreateDirectory(KeyDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var verification = new Core.Verification.VerificationService(
            new Core.Verification.VerificationCache(Path.Combine(paths.CacheDirectory, "verification.jsonl")),
            () => (long)Math.Max(0, Settings.VerifyAutomaticallyUpToMiB) << 20,
            () => [KeyDirectory]);
        // A changed checksum file or signature makes the shown results stale: they are asked for again.
        verification.SidecarsChanged += _ => Metadata.Invalidate();
        Core.Verification.VerificationService.Current = verification;
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
    public PrincipalNames PrincipalNames { get; }

    public FileCat.Recovery.RecoveryProvider Recovery { get; }

    /// <summary>Tells when drives or mount points come and go (checked while the main window listens).</summary>
    public DriveWatcher Drives { get; }

    public Core.Archives.ZipProvider Zip { get; private set; } = null!;

    /// <summary>Read-only archive formats other than ZIP.</summary>
    public FileCat.Archives.ArchiveProvider Archives { get; } = null!;

    public Core.Jobs.JobManager Jobs { get; }

    // Native authority is retained by the adapter through the complete approval/action lifetime.
    internal Func<string, Guid, CancellationToken, Core.Archives.IArchiveContentCleanupRecoveryLease>? ArchiveCleanupLease { get; set; }
        = OperatingSystem.IsWindows() ? FileCat.Platform.Windows.Archives.WindowsArchiveCleanupRecovery.OpenCurrentLease : null;
    internal Func<string, Guid, CancellationToken, Core.Archives.IArchiveCreationCleanupLease>? ArchiveCreationCleanupLease { get; set; }
        = OperatingSystem.IsWindows() ? FileCat.Platform.Windows.Archives.WindowsArchiveCreationCleanupRecovery.OpenCurrentLease
        : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? Core.Archives.Unix.UnixArchiveCreationCleanupRecovery.OpenCurrentLease : null;
    // A change that never reached publication: a fresh exclusive journal and folder scope; objects bound by recorded identity.
    internal Func<string, Guid, CancellationToken, Core.Archives.IArchiveDiscardLease>? ArchiveDiscardLease { get; set; }
        = OperatingSystem.IsWindows() ? FileCat.Platform.Windows.Archives.WindowsArchiveDiscard.OpenRecovery
        : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? Core.Archives.Unix.UnixArchiveDiscard.OpenRecovery : null;
    // Unix exchange updates: a fresh exclusive journal/folder scope; inspection and cleanup re-observe every object.
    internal Func<string, Guid, CancellationToken, Core.Archives.Unix.UnixArchiveExchangeRecovery>? ArchiveExchangeRecovery { get; set; }
        = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? (path, nonce, ct) => Core.Archives.Unix.UnixArchiveExchangeRecovery.Open(path, nonce, ct) : null;

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

    /// <summary>Public keys FileCat trusts for signatures beside files (minisign's .pub files), in the profile.</summary>
    public string KeyDirectory => Paths.KeysDirectory;
    public StateLoadStatus SettingsStatus { get; }
    public HistoryState History { get; }
    public StateLoadStatus HistoryStatus { get; }
    public IndexMemoryBudget ListingIndexes { get; internal set; } = IndexMemoryBudget.Shared;
    internal int MetadataSnapshotMaxRows { get; set; } = 1_000_000;
    internal int MetadataSnapshotMaxCharacters { get; set; } = 16_000_000;
    public DeviceIoScheduler Io { get; }
    public IUiDispatcher Ui { get; }
    public CommandRegistry Commands { get; }
    public Keymap Keymap { get; private set; }
    public IconProvider Icons { get; }
    public IPlatform Platform { get; }
    public IShellServices Shell { get; }
    public ProviderRegistry Providers { get; }

    public bool SettingsReadOnly => SettingsStatus is StateLoadStatus.NewerSchemaReadOnly or StateLoadStatus.OversizedReadOnly or StateLoadStatus.UnavailableReadOnly;

    /// <summary>Constructs an isolated composition root at explicit paths (including headless UI tests).</summary>
    /// <param name="sftpConnector">Tests connect to an in-memory server instead of SSH.NET.</param>
    public static AppServices CreateForPaths(AppPaths paths, Remote.Sftp.ISftpConnector? sftpConnector = null) => new(paths, sftpConnector);

    internal static AppServices CreateForPathsWithStorageTransforms(AppPaths paths, IStorageTransforms storageTransforms) =>
        new(paths, storageTransforms: storageTransforms);

    internal static AppServices CreateForPathsWithOwnership(AppPaths paths, IOwnership ownership, int cacheSize = 50_000,
        Func<long>? clock = null) => new(paths, ownershipOverride: ownership, metadataCacheSize: cacheSize, metadataClock: clock);

    // Explicit native-store test composition. Ordinary startup keeps the legacy store until qualification.
    internal static AppServices CreateForPathsWithEditSessions(AppPaths paths,
        Func<AppPaths,IFileSystemOperations,Core.Edit.EditSessionStore> factory,
        Remote.Sftp.ISftpConnector? sftpConnector = null,Core.Archives.ArchiveJobBackends? archiveBackends = null)
    {ArgumentNullException.ThrowIfNull(factory);return new(paths,sftpConnector,archiveBackends,factory);}

    // Explicit isolated composition for qualifying the real workflows; ordinary startup never adds these backends.
    internal static AppServices CreateForPathsWithArchiveBackends(AppPaths paths, Core.Archives.ArchiveJobBackends archiveBackends,
        Remote.Sftp.ISftpConnector? sftpConnector = null) => new(paths, sftpConnector, archiveBackends);

    /// <param name="overrideRoot">Isolated state root (the TV-01 benchmark never touches the user's profile).</param>
    /// <param name="dataRoot">--data: everything FileCat writes goes below this folder.</param>
    public static AppServices Initialize(string? profile, string? overrideRoot = null, string? dataRoot = null)
    {
        Current = new AppServices(AppPaths.Resolve(profile, overrideRoot: overrideRoot, dataRoot: dataRoot), archiveBackends: ProductionArchiveBackends());
        return Current;
    }

    /// <summary>
    /// Every native writing format for the qualification host, through each platform's production boundary: AppContainer
    /// workers on Windows, confined workers on Linux and sandboxed workers on macOS, all from the published application.
    /// </summary>
    internal static Core.Archives.ArchiveJobBackends QualificationBackends(string workerExecutable)
    {
        ArgumentException.ThrowIfNullOrEmpty(workerExecutable);
        Core.Archives.ArchiveWriteFormat[] formats = [Core.Archives.ArchiveWriteFormat.Tar, Core.Archives.ArchiveWriteFormat.TarGzip,
            Core.Archives.ArchiveWriteFormat.TarBzip2, Core.Archives.ArchiveWriteFormat.TarZstd, Core.Archives.ArchiveWriteFormat.Rar5,
            Core.Archives.ArchiveWriteFormat.SevenZip, Core.Archives.ArchiveWriteFormat.Gzip, Core.Archives.ArchiveWriteFormat.Bzip2,
            Core.Archives.ArchiveWriteFormat.Zstd];
        return new(formats.Select(format => PlatformArchiveBackend(format, workerExecutable)));
    }

    /// <summary>The platform's confined worker backend for one format, with workers from <paramref name="executable"/>.</summary>
    internal static Core.Archives.IArchiveJobBackend PlatformArchiveBackend(Core.Archives.ArchiveWriteFormat format, string executable) =>
        OperatingSystem.IsWindows() ? FileCat.Platform.Windows.Archives.WindowsArchiveJobBackend.Create(format, executable)
        : OperatingSystem.IsLinux() ? Core.Archives.Linux.LinuxArchiveJobBackend.Create(format, executable)
        : OperatingSystem.IsMacOS() ? Core.Archives.MacOS.MacArchiveJobBackend.Create(format, executable)
        : throw new PlatformNotSupportedException("No archive worker launcher exists for this platform.");

    /// <summary>The qualification host's composition: explicit native backends on an isolated data root (never ordinary startup).</summary>
    internal static AppServices InitializeForQualification(AppPaths paths, Core.Archives.ArchiveJobBackends archiveBackends)
    {
        ArgumentNullException.ThrowIfNull(paths); ArgumentNullException.ThrowIfNull(archiveBackends);
        Current = new AppServices(paths, archiveBackends: archiveBackends);
        return Current;
    }

    /// <summary>
    /// Plan §16.1: only qualified format/runtime/filesystem routes are registered (see <see cref="Core.Archives.ArchiveWriteRoutes"/>);
    /// with none qualified this is null and native archive writing stays unavailable, with its reason, while reading continues.
    /// </summary>
    internal static Core.Archives.ArchiveJobBackends? ProductionArchiveBackends() =>
        Core.Archives.ArchiveWriteRoutes.Compose(Core.Archives.ArchiveWriteRoutes.Qualified, RouteRuntime(),
            format => PlatformArchiveBackend(format, Environment.ProcessPath!), FileSystemOf, PlatformTargetProblem);

    /// <summary>
    /// The runtime the routes are listed for: this process's own, unless it runs emulated (an x64 build on an Arm64
    /// machine), where no route was qualified, so nothing is composed.
    /// </summary>
    internal static string RouteRuntime() =>
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            ? System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier
            : System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier + "-emulated";

    /// <summary>Why this platform's writer cannot write an archive path at all, or null (see the backends' TargetProblem).</summary>
    internal static string? PlatformTargetProblem(string archivePath) =>
        OperatingSystem.IsWindows() ? FileCat.Platform.Windows.Archives.WindowsArchiveJobBackend.TargetProblem(archivePath)
        : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? Core.Archives.Unix.UnixArchiveJobBackend.TargetProblem(archivePath)
        : null;

    /// <summary>
    /// Ledger rows 4 and 8: the edit store through its qualification gate (<see cref="Core.Edit.NativeEditStorage"/>). Native
    /// private storage, in its own root beside the legacy folder, only for a qualified runtime and filesystem; with none
    /// qualified this is the legacy store and the native-only edit actions keep saying why they are unavailable.
    /// </summary>
    internal static Core.Edit.EditSessionStore ProductionEditSessions(AppPaths paths, IFileSystemOperations fileOperations) =>
        ComposeEditSessions(Core.Edit.NativeEditStorage.Qualified, paths, fileOperations);

    internal static Core.Edit.EditSessionStore ComposeEditSessions(IReadOnlyList<Core.Edit.NativeEditStorage.Route> routes, AppPaths paths,
        IFileSystemOperations fileOperations)
    {
        Func<string, bool, CancellationToken, Core.Edit.EditDirectoryLease>? open = null;
        Func<string, CancellationToken, Core.Edit.EditSaveCopyDestination>? copyOpen = null;
        if (OperatingSystem.IsWindows())
        {
            open = (path, requirePrivate, ct) => FileCat.Platform.Windows.Archives.WindowsEditDirectoryLease.OpenRoot(path, requirePrivate, ct);
            copyOpen = FileCat.Platform.Windows.Archives.WindowsEditSaveCopyDestination.Open;
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            open = (path, requirePrivate, ct) => Core.Edit.UnixEditDirectoryLease.OpenRoot(path, requirePrivate, ct);
            copyOpen = Core.Archives.Unix.UnixEditSaveCopyDestination.Open;
        }
        return Core.Edit.NativeEditStorage.Compose(routes, RouteRuntime(),
            paths.EditSessionsDirectory, paths.NativeEditSessionsDirectory, fileOperations, FileSystemOf, open, copyOpen);
    }

    /// <summary>The filesystem of the mount that holds <paramref name="folder"/>, or null when it cannot be established.</summary>
    internal static string? FileSystemOf(string folder)
    {
        try
        {
            string full = Path.GetFullPath(folder);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            // A mount point contains the folder only at a separator boundary: /home does not contain /home2.
            var drive = DriveInfo.GetDrives().Where(candidate =>
            {
                string mount = Path.TrimEndingDirectorySeparator(candidate.Name);
                return string.Equals(full, mount, comparison) || full.StartsWith(mount + Path.DirectorySeparatorChar, comparison) ||
                    Path.EndsInDirectorySeparator(candidate.Name) && full.StartsWith(candidate.Name, comparison);
            }).MaxBy(candidate => candidate.Name.Length);
            // A Windows network drive is excluded before anything is asked of it: an unreachable server would hold up the UI.
            if (drive is null || OperatingSystem.IsWindows() && drive.DriveType == DriveType.Network) return null;
            return drive.IsReady ? LocalFileSystem(drive.DriveType, drive.DriveFormat) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>
    /// A mapped network drive reports its server's file system ("NTFS" for a Windows share), but no route or store is
    /// qualified on a share: it has none here. A ZIP there keeps the old route, as a UNC path does.
    /// </summary>
    internal static string? LocalFileSystem(DriveType type, string format) => type == DriveType.Network ? null : format;

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

    /// <summary>
    /// A state file could not be saved: what the user should be told, once per file per session (the layout is saved
    /// every minute, and a full disk or a folder made read-only would otherwise say so every minute). Raised on the
    /// thread that tried; FileCat keeps trying at the next change.
    /// </summary>
    public event Action<string>? StateSaveFailed;

    private readonly HashSet<string> _saveFailuresReported = [];

    /// <summary>Logs a failed save of <paramref name="what"/> and tells the user the first time it happens.</summary>
    public void ReportSaveFailure(string what, Exception ex)
    {
        AppLog.Error($"Saving {what} failed", ex);
        lock (_saveFailuresReported)
            if (!_saveFailuresReported.Add(what)) return;
        StateSaveFailed?.Invoke(OperatingSystem.IsWindows() && Core.State.WindowsTokenAuthority.IsHighIntegrity
            ? $"FileCat did not save its {what} while running as administrator. Reopen it without elevation to save changes."
            : $"FileCat could not save its {what}: {ex.Message} It keeps trying; until it can, changes to them will not survive a restart.");
    }

    private readonly WindowsSettingsFile? _windowsSettings;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal WindowsSettingsFile NormalAppearanceSettings => _windowsSettings
        ?? throw new IOException("Open FileCat normally to access saved appearance settings.");

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal string TryReplaceNormalAppearance(WindowsSettingsFile.Review review)
    {
        if (SettingsReadOnly) throw new IOException("The normal settings are read-only.");
        return NormalAppearanceSettings.ReplaceAppearance(review, Settings);
    }

    public void SaveSettings()
    {
        if (!SettingsReadOnly)
        {
            try
            {
                if (OperatingSystem.IsWindows() && _windowsSettings is not null) _windowsSettings.Save(Settings);
                else JsonFileStore.Save(Paths.SettingsFile, Settings, StateJsonContext.Default.AppSettings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ReportSaveFailure("settings", ex); }
        }
        Saved?.Invoke();
    }

    /// <summary>Saved filters for <c>@name</c> in masks; looked up at parse time, so edits apply to the next mask.</summary>
    private void RegisterSavedFilters() =>
        Core.Selection.Mask.SavedFilters = name => Settings.SavedFilters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Mask;

    public void SaveHistory()
    {
        if (HistoryStatus is not (StateLoadStatus.NewerSchemaReadOnly or StateLoadStatus.OversizedReadOnly or StateLoadStatus.UnavailableReadOnly))
        {
            try { JsonFileStore.Save(Paths.HistoryFile, History, StateJsonContext.Default.HistoryState); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ReportSaveFailure("history", ex); }
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
        // A drive's deleted items open only from the drive, after its checks (release plan V09): not from the history.
        if (location.Scheme is Schemes.Recovery && FileCat.Recovery.RecoveryProvider.IsDevice(location)) return;
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
        Metadata.Dispose();
        PrincipalNames.Dispose();
        Io.Dispose();
        Platform.Dispose();
    }
}
