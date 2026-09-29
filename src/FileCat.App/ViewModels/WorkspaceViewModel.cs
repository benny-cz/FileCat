using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Services;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

/// <summary>
/// Ordered panels, the active panel, and source→target designation (plan §4.2, D-09):
/// with two panels the target is always the other panel; with three or more each panel has an explicit
/// target that is never inferred from focus movement. Stored pairings use stable panel ids.
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    public const int MaxPanels = 6;
    private PanelViewModel? _previousPanel;

    public WorkspaceViewModel(AppServices services)
    {
        Services = services;
        Panels.CollectionChanged += (_, _) => UpdateIndicators();
    }

    public AppServices Services { get; }
    public ObservableCollection<PanelViewModel> Panels { get; } = [];
    public List<TabState> RecentlyClosed { get; } = [];

    [ObservableProperty] private PanelViewModel? _activePanel;
    [ObservableProperty] private string _layout = "Columns";
    [ObservableProperty] private PanelViewModel? _maximizedPanel;

    public event Action? LayoutChanged;

    /// <summary>Where each panel is (ADR-18): splits side by side or stacked, with proportional sizes.</summary>
    public PanelLayoutNode LayoutTree { get; private set; } = PanelLayoutNode.Panel("none");

    /// <summary>
    /// Shows a new arrangement: the panels are numbered in reading order (left to right, top to bottom), and each keeps
    /// its identity, target, and tabs.
    /// </summary>
    private void ApplyLayout(PanelLayoutNode tree)
    {
        LayoutTree = tree;
        var order = PanelLayout.PanelIds(tree);
        for (int i = 0; i < order.Count; i++)
        {
            int at = -1;
            for (int j = 0; j < Panels.Count; j++)
                if (Panels[j].Id == order[i]) at = j;
            if (at >= 0 && at != i) Panels.Move(at, i);
        }
        UpdateIndicators();
        LayoutChanged?.Invoke();
    }

    /// <summary>Moves a panel to a side of another (a drop on that panel's edge).</summary>
    public void DockPanel(PanelViewModel panel, PanelViewModel target, DockSide side)
    {
        if (ReferenceEquals(panel, target)) return;
        MaximizedPanel = null;
        ApplyLayout(PanelLayout.Dock(LayoutTree, panel.Id, target.Id, side));
    }

    /// <summary>Two panels trade places (a drop on a panel's middle); their locations, tabs, and targets go with them.</summary>
    public void SwapPanelPlaces(PanelViewModel a, PanelViewModel b)
    {
        if (ReferenceEquals(a, b)) return;
        MaximizedPanel = null;
        ApplyLayout(PanelLayout.Swap(LayoutTree, a.Id, b.Id));
    }

    /// <summary>Moves a panel one step that way; false when it is already at that edge.</summary>
    public bool MovePanelToward(PanelViewModel panel, DockSide direction)
    {
        var moved = PanelLayout.MoveToward(LayoutTree, panel.Id, direction);
        if (moved.ToString() == LayoutTree.ToString()) return false;
        MaximizedPanel = null;
        ApplyLayout(moved);
        return true;
    }

    /// <summary>The split holding the panel turns between side by side and stacked; false for a lone panel.</summary>
    public bool RotateSplit(PanelViewModel panel)
    {
        if (LayoutTree.IsPanel) return false;
        MaximizedPanel = null;
        ApplyLayout(PanelLayout.Rotate(LayoutTree, panel.Id));
        return true;
    }

    /// <summary>Every split shares its space equally.</summary>
    public void EqualizeSizes() => ApplyLayout(PanelLayout.Equalize(LayoutTree));

    /// <summary>The layout the drop would make, to check that it fits before it is made.</summary>
    public PanelLayoutNode PreviewDock(PanelViewModel panel, PanelViewModel target, DockSide? side) =>
        side is { } s ? PanelLayout.Dock(LayoutTree, panel.Id, target.Id, s) : PanelLayout.Swap(LayoutTree, panel.Id, target.Id);

    public TabViewModel? ActiveTab => ActivePanel?.ActiveTab;

    partial void OnActivePanelChanged(PanelViewModel? oldValue, PanelViewModel? newValue)
    {
        if (oldValue is not null && !ReferenceEquals(oldValue, newValue)) _previousPanel = oldValue;
        UpdateIndicators();
        OnPropertyChanged(nameof(ActiveTab));
    }

    public void OnPanelTabChanged(PanelViewModel panel)
    {
        if (ReferenceEquals(panel, ActivePanel)) OnPropertyChanged(nameof(ActiveTab));
    }

    public void Activate(PanelViewModel panel)
    {
        if (!ReferenceEquals(ActivePanel, panel)) ActivePanel = panel;
    }

    /// <summary>The designated target of <paramref name="source"/>, or null when it must be chosen explicitly.</summary>
    public PanelViewModel? GetTarget(PanelViewModel source)
    {
        if (Panels.Count == 2) return Panels.First(p => !ReferenceEquals(p, source));
        if (Panels.Count < 2) return null;
        var t = Panels.FirstOrDefault(p => p.Id == source.TargetPanelId);
        return t is null || ReferenceEquals(t, source) ? null : t;
    }

    public PanelViewModel? ActiveTarget => ActivePanel is null ? null : GetTarget(ActivePanel);

    /// <summary>Tab moves focus to the designated target; Shift+Tab returns (plan §4.2).</summary>
    public void SwitchToTarget()
    {
        if (ActivePanel is null) return;
        var t = GetTarget(ActivePanel);
        if (t is not null) Activate(t);
        else if (Panels.Count > 1) Activate(Panels[(Panels.IndexOf(ActivePanel) + 1) % Panels.Count]);
    }

    public void SwitchBack()
    {
        if (_previousPanel is not null && Panels.Contains(_previousPanel)) Activate(_previousPanel);
        else if (ActivePanel is not null && Panels.Count > 1)
            Activate(Panels[(Panels.IndexOf(ActivePanel) - 1 + Panels.Count) % Panels.Count]);
    }

    public void SetTarget(PanelViewModel source, PanelViewModel? target)
    {
        source.TargetPanelId = target?.Id;
        UpdateIndicators();
    }

    /// <summary>Adds a panel beside the active one (to its right unless <paramref name="side"/> says otherwise).</summary>
    public PanelViewModel AddPanel(Location? location = null, DockSide side = DockSide.Right)
    {
        var source = ActivePanel;
        var panel = new PanelViewModel(this, Services);
        var beside = source ?? Panels.LastOrDefault();
        var existing = Panels.Select(p => p.Id).ToList();
        if (!PanelLayout.Matches(LayoutTree, existing) && existing.Count > 0) LayoutTree = PanelLayout.Default(existing);
        LayoutTree = beside is null ? PanelLayoutNode.Panel(panel.Id) : PanelLayout.Insert(LayoutTree, panel.Id, beside.Id, side);
        Panels.Add(panel);
        panel.OpenTab(location ?? source?.ActiveTab?.Location ?? DefaultLocation());
        if (Panels.Count == 3)
        {
            // Entering explicit targeting: keep the pairing the user already had (plan §4.2).
            var a = Panels[0];
            var b = Panels[1];
            a.TargetPanelId ??= b.Id;
            b.TargetPanelId ??= a.Id;
        }
        // The new panel gets a visible target assignment before its first transfer.
        panel.TargetPanelId = source?.Id ?? Panels[0].Id;
        MaximizedPanel = null;
        ApplyLayout(LayoutTree);
        return panel;
    }

    public void RemovePanel(PanelViewModel panel)
    {
        if (Panels.Count <= 1) return;
        foreach (var t in panel.Tabs) RememberClosed(t.ToState());
        int idx = Panels.IndexOf(panel);
        LayoutTree = PanelLayout.Remove(LayoutTree, panel.Id);
        Panels.Remove(panel);
        foreach (var p in Panels) if (p.TargetPanelId == panel.Id) p.TargetPanelId = null;
        if (Panels.Count == 2)
        {
            // Back to two panels: both revert to implicit other-panel targeting.
            foreach (var p in Panels) p.TargetPanelId = null;
        }
        if (ReferenceEquals(ActivePanel, panel)) ActivePanel = Panels[Math.Min(idx, Panels.Count - 1)];
        if (ReferenceEquals(MaximizedPanel, panel)) MaximizedPanel = null;
        foreach (var t in panel.Tabs) t.Dispose();
        ApplyLayout(LayoutTree);
    }

    public void ToggleMaximize()
    {
        MaximizedPanel = MaximizedPanel is null ? ActivePanel : null;
        LayoutChanged?.Invoke();
    }

    public void UpdateIndicators()
    {
        for (int i = 0; i < Panels.Count; i++) Panels[i].Number = i + 1;
        var target = ActivePanel is null ? null : GetTarget(ActivePanel);
        foreach (var p in Panels)
        {
            p.IsActive = ReferenceEquals(p, ActivePanel);
            p.IsTarget = ReferenceEquals(p, target);
            var own = GetTarget(p);
            p.RoleLabel = Panels.Count <= 2
                ? p.IsTarget ? "TARGET" : string.Empty
                : (p.IsTarget ? "TARGET · " : string.Empty) + (own is null ? "no target set" : $"→ {own.Number}");
        }
        OnPropertyChanged(nameof(ActiveTarget));
    }

    public void RememberClosed(TabState state)
    {
        if (state.Location is null) return;
        RecentlyClosed.Insert(0, state);
        int max = Math.Max(5, Services.Settings.RecentlyClosedTabs);
        if (RecentlyClosed.Count > max) RecentlyClosed.RemoveRange(max, RecentlyClosed.Count - max);
    }

    public void ReopenClosed(int index = 0)
    {
        if (ActivePanel is null || index >= RecentlyClosed.Count) return;
        var s = RecentlyClosed[index];
        RecentlyClosed.RemoveAt(index);
        var tab = ActivePanel.OpenTab(s.Location!, s.FocusName);
        tab.ApplyState(s);
    }

    public static Location DefaultLocation()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Location.FileSystem(string.IsNullOrEmpty(home) ? Path.GetPathRoot(Environment.CurrentDirectory) ?? "/" : home);
    }

    // ---- Persistence -----------------------------------------------------------------------------------------

    public WorkspaceState ToState(WindowPlacement? window)
    {
        var rects = PanelLayout.Arrange(LayoutTree);
        return new WorkspaceState
        {
            Panels = Panels.Select(p =>
            {
                var state = p.ToState();
                // Sizes as P2 read them, for a version without the tree.
                if (rects.TryGetValue(p.Id, out var r)) state.Size = LayoutTree is { IsPanel: false, Stacked: true } ? r.Height : r.Width;
                return state;
            }).ToList(),
            ActivePanelId = ActivePanel?.Id,
            Layout = LayoutTree is { IsPanel: false, Stacked: true } ? "Rows" : "Columns",
            Tree = LayoutTree.Clone(),
            Window = window,
        };
    }

    /// <summary>Restores panels lazily; an unreadable or empty state yields the default two panels.</summary>
    public void LoadState(WorkspaceState? state)
    {
        foreach (var p in Panels.ToList())
        {
            foreach (var t in p.Tabs) t.Dispose();
        }
        Panels.Clear();
        if (state is not null)
        {
            Layout = state.Layout is "Rows" ? "Rows" : "Columns";
            foreach (var ps in state.Panels.Take(MaxPanels))
            {
                var panel = new PanelViewModel(this, Services, ps.Id) { TargetPanelId = ps.TargetPanelId, Size = ps.Size > 0 ? ps.Size : 1 };
                foreach (var ts in ps.Tabs)
                {
                    if (ts.Location is null) continue;
                    var tab = new TabViewModel(Services, panel);
                    tab.ApplyState(ts);
                    panel.Tabs.Add(tab);
                    tab.Navigate(ts.Location, ts.FocusName, record: false);
                }
                if (panel.Tabs.Count == 0) continue;
                Panels.Add(panel);
                panel.ActiveTab = panel.Tabs[Math.Clamp(ps.ActiveTab, 0, panel.Tabs.Count - 1)];
            }
        }
        while (Panels.Count < 2)
        {
            var panel = new PanelViewModel(this, Services);
            Panels.Add(panel);
            panel.OpenTab(Panels.Count == 1 ? DefaultLocation() : new Location(Schemes.Computer, string.Empty));
        }
        if (Panels.Count == 2) foreach (var p in Panels) p.TargetPanelId = null;
        // The saved tree when it shows exactly these panels; otherwise one split, as P2 laid them out.
        var ids = Panels.Select(p => p.Id).ToList();
        LayoutTree = PanelLayout.Matches(state?.Tree, ids)
            ? PanelLayout.Normalize(state!.Tree!.Clone())
            : PanelLayout.Default(ids, Layout == "Rows", Panels.Select(p => p.Size).ToList());
        ActivePanel = Panels.FirstOrDefault(p => p.Id == state?.ActivePanelId) ?? Panels[0];
        ApplyLayout(LayoutTree);
    }
}
