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
    private AppServices(AppPaths paths)
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
        ResultSets = new Core.Search.ResultSetProvider(Providers, Platform.FileOperations);
        Providers.Register(ResultSets);
        Zip = new Core.Archives.ZipProvider(paths.TempDirectory);
        Providers.Register(Zip);
        if (Providers.Get(Schemes.FileSystem) is LocalFileSystemProvider local) local.ContainerDetector = Zip;
        Formatters.DateFormat = Settings.DateFormat;
        Jobs = new Core.Jobs.JobManager(Platform.FileOperations, Providers, paths.JournalDirectory);
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

    public Core.Archives.ZipProvider Zip { get; private set; } = null!;

    public Core.Jobs.JobManager Jobs { get; }
    /// <summary>Persistent external edits of archive members (plan §14.2).</summary>
    public Core.Edit.EditSessionStore EditSessions { get; }
    public Core.Search.ResultSetProvider ResultSets { get; }

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
    public static AppServices CreateForPaths(AppPaths paths) => new(paths);

    /// <param name="overrideRoot">Isolated state root (the TV-01 benchmark never touches the user's profile).</param>
    public static AppServices Initialize(string? profile, string? overrideRoot = null)
    {
        Current = new AppServices(AppPaths.Resolve(profile, overrideRoot: overrideRoot));
        return Current;
    }

    public void ReloadKeymap() => Keymap = new Keymap(Commands, Settings.KeyBindings);

    public void SaveSettings()
    {
        if (SettingsReadOnly) return;
        try { JsonFileStore.Save(Paths.SettingsFile, Settings, StateJsonContext.Default.AppSettings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Saving settings failed", ex); }
    }

    /// <summary>Saved filters for <c>@name</c> in masks; looked up at parse time, so edits apply to the next mask.</summary>
    private void RegisterSavedFilters() =>
        Core.Selection.Mask.SavedFilters = name => Settings.SavedFilters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Mask;

    public void SaveHistory()
    {
        if (HistoryStatus == StateLoadStatus.NewerSchemaReadOnly) return;
        try { JsonFileStore.Save(Paths.HistoryFile, History, StateJsonContext.Default.HistoryState); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Saving history failed", ex); }
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
        SaveHistory();
    }

    public void RecordFolder(Location location)
    {
        if (location.Scheme is Schemes.ResultSet) return;
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
        Io.Dispose();
        Platform.Dispose();
    }
}
