using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

public sealed partial class KeyBarItem : ObservableObject
{
    public KeyBarItem(int number) => Number = number;

    public int Number { get; }
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string? _commandId;
    [ObservableProperty] private bool _isEnabled = true;
}

/// <summary>Root view model: workspace, key bar, command line, and command dispatch (plan §4.4).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<string, CancellationTokenSource> _sizing = new(StringComparer.Ordinal);

    public MainViewModel(AppServices services)
    {
        Services = services;
        Workspace = new WorkspaceViewModel(services);
        for (int i = 1; i <= 12; i++) KeyBar.Add(new KeyBarItem(i));
        UpdateKeyBar(KeyMods.None);
        Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WorkspaceViewModel.ActivePanel) or nameof(WorkspaceViewModel.ActiveTab))
            {
                OnPropertyChanged(nameof(CommandLinePrompt));
                UpdateKeyBar(_mods);
            }
        };
    }

    public AppServices Services { get; }
    public WorkspaceViewModel Workspace { get; }
    public ObservableCollection<KeyBarItem> KeyBar { get; } = [];
    public IDialogService Dialogs { get; set; } = null!;
    public IViewActions View { get; set; } = null!;

    [ObservableProperty] private string _commandLineText = string.Empty;
    [ObservableProperty] private string? _notification;
    [ObservableProperty] private bool _notificationIsError;
    [ObservableProperty] private bool _showKeyBar = true;
    [ObservableProperty] private bool _showCommandLine = true;

    private KeyMods _mods;

    public string CommandLinePrompt => (Workspace.ActiveTab?.DisplayPath ?? string.Empty) + ">";

    public TabViewModel? ActiveTab => Workspace.ActiveTab;

    public void Initialize(WorkspaceState? state)
    {
        ShowKeyBar = Services.Settings.ShowFunctionKeyBar;
        ShowCommandLine = Services.Settings.ShowCommandLine;
        Workspace.LoadState(state);
        if (Services.SettingsStatus == StateLoadStatus.NewerSchemaReadOnly)
            Notify("Settings were written by a newer FileCat and are opened read-only; changes will not be saved.", true);
        else if (Services.SettingsStatus is StateLoadStatus.RecoveredFromBackup or StateLoadStatus.CorruptUsingDefaults)
            Notify("Settings were damaged; FileCat restored a usable copy and kept the damaged file for inspection.", true);
    }

    public void UpdateKeyBar(KeyMods mods)
    {
        _mods = mods;
        var bar = Services.Keymap.GetFunctionKeyBar(mods, CommandContext.Panel);
        for (int i = 0; i < 12; i++)
        {
            KeyBar[i].Label = bar[i].Label;
            KeyBar[i].CommandId = bar[i].CommandId;
            KeyBar[i].IsEnabled = bar[i].CommandId is not null && GetAvailability(bar[i].CommandId!).Enabled;
        }
    }

    public void Notify(string message, bool isError = false)
    {
        Notification = message;
        NotificationIsError = isError;
        if (isError) AppLog.Warn("User notification: " + message);
    }

    public void ClearNotification() => Notification = null;

    internal void CancelSizing(string key)
    {
        if (_sizing.Remove(key, out var cts)) cts.Cancel();
    }

    internal void CancelAllSizing()
    {
        foreach (var c in _sizing.Values) c.Cancel();
        _sizing.Clear();
    }

    internal CancellationToken BeginSizing(string key)
    {
        CancelSizing(key);
        var cts = new CancellationTokenSource();
        _sizing[key] = cts;
        return cts.Token;
    }

    internal void EndSizing(string key, CancellationToken token)
    {
        if (_sizing.TryGetValue(key, out var cts) && cts.Token == token) _sizing.Remove(key);
    }

    /// <summary>Last known window placement, reused by autosave.</summary>
    public WindowPlacement? LastPlacement { get; set; }

    public void SaveWorkspace(WindowPlacement? placement)
    {
        placement ??= LastPlacement;
        LastPlacement = placement;
        try
        {
            JsonFileStore.Save(Services.Paths.WorkspaceFile, Workspace.ToState(placement), StateJsonContext.Default.WorkspaceState);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Saving workspace failed", ex);
        }
        Services.SaveHistory();
        Services.SaveSettings();
    }

    /// <summary>Opens locations forwarded by a second launch or passed on the command line.</summary>
    public void OpenArguments(StartupOptions options, bool initial = false)
    {
        var panel = Workspace.ActivePanel;
        if (panel is null) return;
        void Open(string text, PanelViewModel? into)
        {
            if (!Services.Providers.TryParse(text, null, out var loc) || loc is null)
            {
                Notify($"Could not open \"{text}\".", true);
                return;
            }
            // A file argument opens its folder with the file focused.
            string? focus = null;
            if (loc.IsFileSystem && File.Exists(loc.Path))
            {
                focus = Path.GetFileName(loc.Path);
                loc = Location.FileSystem(Path.GetDirectoryName(loc.Path)!);
            }
            var target = into ?? panel;
            // At startup the requested location replaces the default tab instead of adding one next to it.
            if (initial && target.Tabs.Count == 1 && !target.Tabs[0].IsLocked) target.ActiveTab?.Navigate(loc, focus, record: false);
            else target.OpenTab(loc, focus);
        }
        if (options.LeftLocation is { } l && Workspace.Panels.Count > 0) Open(l, Workspace.Panels[0]);
        if (options.RightLocation is { } r && Workspace.Panels.Count > 1) Open(r, Workspace.Panels[1]);
        foreach (var loc in options.Locations) Open(loc, null);
    }
}
