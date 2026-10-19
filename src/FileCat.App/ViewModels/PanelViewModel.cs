using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Services;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

/// <summary>A viewport and focus context holding ordered tabs; it never owns jobs (plan §4.1).</summary>
public sealed partial class PanelViewModel : ObservableObject
{
    public PanelViewModel(WorkspaceViewModel workspace, AppServices services, string? id = null)
    {
        Workspace = workspace;
        Services = services;
        Id = id ?? Guid.NewGuid().ToString("N");
        Tabs.CollectionChanged += (_, _) => RefreshTabLabels();
    }

    public string Id { get; }
    public WorkspaceViewModel Workspace { get; }
    public AppServices Services { get; }
    public ObservableCollection<TabViewModel> Tabs { get; } = [];
    private readonly HashSet<TabViewModel> _observedTabs = [];

    private void RefreshTabLabels()
    {
        foreach (var old in _observedTabs.Where(tab => !Tabs.Contains(tab)).ToArray())
        {
            old.PropertyChanged -= OnTabIdentityChanged;
            _observedTabs.Remove(old);
        }
        foreach (var tab in Tabs.Where(tab => !_observedTabs.Contains(tab)))
        {
            tab.PropertyChanged += OnTabIdentityChanged;
            _observedTabs.Add(tab);
        }

        foreach (var group in Tabs.GroupBy(tab => Formatters.SafeName(tab.Title), StringComparer.OrdinalIgnoreCase))
        {
            var peers = group.ToArray();
            if (peers.Length == 1)
            {
                peers[0].TabQualifier = string.Empty;
                continue;
            }
            var cues = peers.ToDictionary(tab => tab, LocationCues);
            int maxDepth = cues.Values.Max(parts => parts.Length);
            foreach (var tab in peers)
            {
                string qualifier = string.Empty;
                for (int depth = 1; depth <= maxDepth; depth++)
                {
                    string candidate = Cue(cues[tab], depth);
                    if (peers.Count(other => string.Equals(Cue(cues[other], depth), candidate,
                            StringComparison.OrdinalIgnoreCase)) != 1) continue;
                    qualifier = candidate;
                    break;
                }
                if (qualifier.Length == 0)
                {
                    string candidate = Cue(cues[tab], maxDepth);
                    int instance = peers.TakeWhile(other => !ReferenceEquals(other, tab))
                        .Count(other => string.Equals(Cue(cues[other], maxDepth), candidate,
                            StringComparison.OrdinalIgnoreCase)) + 1;
                    qualifier = candidate + " #" + instance;
                }
                tab.TabQualifier = qualifier;
            }
        }
    }

    private void OnTabIdentityChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabViewModel.Title) or nameof(TabViewModel.DisplayPath) or
            nameof(TabViewModel.IsLocked) or nameof(TabViewModel.ReturnToRoot) or nameof(TabViewModel.LockedRoot))
            RefreshTabLabels();
    }

    private static string[] LocationCues(TabViewModel tab)
    {
        string path = tab.DisplayPath;
        // Backslash is a path separator for local Windows paths, but a legal name character on remote Unix paths.
        bool windowsPath = OperatingSystem.IsWindows() && tab.Location?.IsFileSystem == true ||
            path.Length > 1 && char.IsLetter(path[0]) && path[1] == ':';
        if (windowsPath) path = path.Replace('\\', '/');
        string[] parts = path.TrimEnd('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && string.Equals(parts[^1], tab.Title, StringComparison.OrdinalIgnoreCase))
            parts = parts[..^1];
        string scheme = tab.Location?.Scheme ?? "tab";
        return [scheme, .. parts];
    }

    private static string Cue(string[] parts, int depth) =>
        string.Join(" / ", parts.TakeLast(Math.Min(depth, parts.Length)));

    [ObservableProperty] private TabViewModel? _activeTab;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isTarget;
    [ObservableProperty] private int _number = 1;
    /// <summary>Explicit target panel id; used only with three or more panels (D-09).</summary>
    [ObservableProperty] private string? _targetPanelId;
    /// <summary>The role chip's text: TARGET, "Set as target" (three or more panels), or the active panel's own target.</summary>
    [ObservableProperty] private string _roleLabel = string.Empty;
    /// <summary>What the role chip means, and what clicking it does.</summary>
    [ObservableProperty] private string _roleTip = string.Empty;
    /// <summary>Clicking the chip makes this panel the active panel's target (three or more panels).</summary>
    [ObservableProperty] private bool _offersTarget;
    [ObservableProperty] private double _size = 1;
    /// <summary>When set, this panel shows a quick view of that panel's focused item (Ctrl+Q).</summary>
    [ObservableProperty] private PanelViewModel? _quickViewSource;

    partial void OnActiveTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsActiveTab = false;
        if (newValue is not null)
        {
            newValue.IsActiveTab = true;
            // Locked "return to root" tabs go back to their saved location when revisited.
            if (newValue.IsLocked && newValue.ReturnToRoot && newValue.LockedRoot is { } root && newValue.Location != root)
                newValue.Navigate(root, record: false);
        }
        Workspace.OnPanelTabChanged(this);
    }

    public TabViewModel OpenTab(Location location, string? focusName = null, bool activate = true, int? index = null)
    {
        var tab = new TabViewModel(Services, this);
        if (ActiveTab is not null)
        {
            tab.ColumnProfile = ActiveTab.ColumnProfile;
            tab.Listing.Sort = ActiveTab.Listing.Sort;
        }
        int at = index ?? (ActiveTab is null ? Tabs.Count : Tabs.IndexOf(ActiveTab) + 1);
        Tabs.Insert(Math.Clamp(at, 0, Tabs.Count), tab);
        tab.Navigate(location, focusName, record: false);
        if (activate || ActiveTab is null) ActiveTab = tab;
        return tab;
    }

    public void AttachTab(TabViewModel tab, int? index = null)
    {
        tab.Panel = this;
        Tabs.Insert(Math.Clamp(index ?? Tabs.Count, 0, Tabs.Count), tab);
        ActiveTab = tab;
    }

    /// <summary>Closes a tab; the last tab of a panel is replaced by a fresh one at the same location.</summary>
    public void CloseTab(TabViewModel tab)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        Workspace.RememberClosed(tab.ToState());
        if (Tabs.Count == 1)
        {
            // Keep the panel usable: a panel always has one tab.
            return;
        }
        Tabs.RemoveAt(idx);
        if (ReferenceEquals(ActiveTab, tab)) ActiveTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
        tab.Dispose();
    }

    public TabViewModel DetachTab(TabViewModel tab)
    {
        int idx = Tabs.IndexOf(tab);
        Tabs.RemoveAt(idx);
        if (ReferenceEquals(ActiveTab, tab) && Tabs.Count > 0) ActiveTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
        return tab;
    }

    /// <summary>Moves a tab to another place among this panel's tabs (clamped to them); it becomes the active tab.</summary>
    public void MoveTab(TabViewModel tab, int to)
    {
        int from = Tabs.IndexOf(tab);
        if (from < 0) return;
        to = Math.Clamp(to, 0, Tabs.Count - 1);
        if (to != from) Tabs.Move(from, to);
        ActiveTab = tab;
    }

    public void CycleTab(int delta)
    {
        if (Tabs.Count < 2 || ActiveTab is null) return;
        int i = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[((i + delta) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    public PanelState ToState()
    {
        var saved = Tabs.Select(t => t.ToState()).ToList();
        int active = ActiveTab is null ? 0 : Math.Max(0, Tabs.IndexOf(ActiveTab));
        return new PanelState
        {
            Id = Id,
            Tabs = saved.Where(t => t.Location is not null).ToList(),
            ActiveTab = saved.Take(active).Count(t => t.Location is not null),
            TargetPanelId = TargetPanelId,
            Size = Size,
        };
    }
}
