using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
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
        Commands = CommandRegistry.CreateDefault();
        Keymap = new Keymap(Commands, Settings.KeyBindings);
        foreach (var c in Keymap.Conflicts) AppLog.Warn("Key binding: " + c);
        Icons = new IconProvider();
        Platform = PlatformFactory.Create();
        Shell = Platform.Shell;
        Providers = new ProviderRegistry();
        Platform.RegisterProviders(Providers);
        Formatters.DateFormat = Settings.DateFormat;
    }

    public static AppServices Current { get; private set; } = null!;

    public AppPaths Paths { get; }
    public AppSettings Settings { get; }
    public StateLoadStatus SettingsStatus { get; }
    public HistoryState History { get; }
    public StateLoadStatus HistoryStatus { get; }
    public DeviceIoScheduler Io { get; }
    public IUiDispatcher Ui { get; }
    public CommandRegistry Commands { get; }
    public Keymap Keymap { get; private set; }
    public IconProvider Icons { get; }
    public IPlatform Platform { get; }
    public IShellServices Shell { get; }
    public ProviderRegistry Providers { get; }

    public bool SettingsReadOnly => SettingsStatus == StateLoadStatus.NewerSchemaReadOnly;

    public static AppServices Initialize(string? profile)
    {
        Current = new AppServices(AppPaths.Resolve(profile));
        return Current;
    }

    public void ReloadKeymap() => Keymap = new Keymap(Commands, Settings.KeyBindings);

    public void SaveSettings()
    {
        if (SettingsReadOnly) return;
        try { JsonFileStore.Save(Paths.SettingsFile, Settings, StateJsonContext.Default.AppSettings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Saving settings failed", ex); }
    }

    public void SaveHistory()
    {
        if (HistoryStatus == StateLoadStatus.NewerSchemaReadOnly) return;
        try { JsonFileStore.Save(Paths.HistoryFile, History, StateJsonContext.Default.HistoryState); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Saving history failed", ex); }
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
