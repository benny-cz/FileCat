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
    }

    public string Id { get; }
    public WorkspaceViewModel Workspace { get; }
    public AppServices Services { get; }
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty] private TabViewModel? _activeTab;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isTarget;
    [ObservableProperty] private int _number = 1;
    /// <summary>Explicit target panel id; used only with three or more panels (D-09).</summary>
    [ObservableProperty] private string? _targetPanelId;
    [ObservableProperty] private string _roleLabel = string.Empty;
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
                newValue.Listing.Load(root);
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

    public void CycleTab(int delta)
    {
        if (Tabs.Count < 2 || ActiveTab is null) return;
        int i = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[((i + delta) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    public PanelState ToState() => new()
    {
        Id = Id,
        Tabs = Tabs.Select(t => t.ToState()).Where(t => t.Location is not null).ToList(),
        ActiveTab = ActiveTab is null ? 0 : Math.Max(0, Tabs.IndexOf(ActiveTab)),
        TargetPanelId = TargetPanelId,
        Size = Size,
    };
}
