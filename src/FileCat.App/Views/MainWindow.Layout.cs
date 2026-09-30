using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.Core.State;

namespace FileCat.App.Views;

/// <summary>
/// The panels' arrangement (plan §4.1, ADR-18): a split tree built as nested grids with splitters, and docking by
/// dragging a panel's number (or its tab strip) onto another panel: an edge puts it beside, above, or below that panel;
/// the middle swaps the two. A preview shows the result before the drop, a drop that leaves a panel too small is
/// refused with the reason, and Esc cancels.
/// </summary>
public partial class MainWindow
{
    /// <summary>A panel being dragged to a new place.</summary>
    private sealed class PanelDrag
    {
        public required PanelViewModel Panel;
        public required Point Start;
        public required IPointer Pointer;
        public required Control Handle;
        public bool Moving;
        public PanelViewModel? Target;
        public DockSide? Side; // null: the middle, a swap
        public bool Allowed;
    }

    private PanelDrag? _panelDrag;

    /// <summary>Until the window has drawn its first frame, panels wait (see the constructor's Opened handler).</summary>
    private bool _panelsDeferred = true;

    private void RebuildPanels()
    {
        if (_panelsDeferred) return;
        var ws = _vm.Workspace;
        foreach (var dead in _panelViews.Keys.Where(k => !ws.Panels.Contains(k)).ToList()) _panelViews.Remove(dead);
        // Views are reused: each leaves the grid that held it before the new ones are built. The keyboard stays in the
        // active panel (leaving the tree would drop it, and a panel that took it would become the active one).
        var focused = FocusManager?.GetFocusedElement() as Visual;
        bool inPanel = focused is not null && _panelViews.Values.Any(v => ReferenceEquals(v, focused) || v.IsVisualAncestorOf(focused));
        bool inActivePanel = inPanel && ws.ActivePanel is { } active && _panelViews.TryGetValue(active, out var activeView)
            && (ReferenceEquals(activeView, focused) || activeView.IsVisualAncestorOf(focused!));
        foreach (var view in _panelViews.Values) (view.Parent as Panel)?.Children.Remove(view);
        WorkspaceHost.Children.Clear();
        WorkspaceHost.ColumnDefinitions.Clear();
        WorkspaceHost.RowDefinitions.Clear();
        var panels = ws.Panels.ToDictionary(p => p.Id);
        if (ws.MaximizedPanel is { } max && ws.Panels.Contains(max)) WorkspaceHost.Children.Add(GetView(max));
        else if (panels.Count > 0)
        {
            // While panels are being added or removed the tree may lag a step: show them in one row until it catches up.
            var tree = PanelLayout.Matches(ws.LayoutTree, panels.Keys) ? ws.LayoutTree : PanelLayout.Default([.. ws.Panels.Select(p => p.Id)]);
            WorkspaceHost.Children.Add(BuildNode(tree, panels));
        }
        if (inActivePanel && focused is InputElement again && TopLevel.GetTopLevel(again) is not null) again.Focus();
        else if (inPanel) FocusActivePanel();
        UpdateLayoutStrip();
        UpdateTitle();
    }

    /// <summary>
    /// The strip over the panels: which panel is maximized, or, when the window is too small for every panel to keep
    /// its minimum size, that F11 shows the active one whole.
    /// </summary>
    private void UpdateLayoutStrip()
    {
        var ws = _vm.Workspace;
        if (ws.MaximizedPanel is { } m)
        {
            var target = ws.GetTarget(m);
            MaximizedText.Text = $"Panel {m.Number} maximized · F11 restores" + (target is null ? " · no target panel" : $" · target: panel {target.Number} ({target.ActiveTab?.DisplayPath})");
            MaximizedStrip.IsVisible = true;
            return;
        }
        bool cramped = WorkspaceHost.Bounds.Width > 0 && ws.Panels.Count > 1
            && !PanelLayout.Fits(ws.LayoutTree, WorkspaceHost.Bounds.Width, WorkspaceHost.Bounds.Height);
        MaximizedText.Text = cramped ? "The window is too small for every panel · F11 shows the active one whole" : string.Empty;
        MaximizedStrip.IsVisible = cramped;
    }

    /// <summary>A panel's view, or a grid of its split's children with splitters between them.</summary>
    private Control BuildNode(PanelLayoutNode node, IReadOnlyDictionary<string, PanelViewModel> panels)
    {
        if (node.IsPanel) return GetView(panels[node.PanelId!]);
        var grid = new Grid { Tag = node };
        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (i > 0)
            {
                var splitter = new GridSplitter
                {
                    ResizeDirection = node.Stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns,
                    Background = Avalonia.Media.Brushes.Transparent,
                    Focusable = false,
                };
                // Dragged sizes become the tree's, so they are saved with the workspace.
                splitter.DragCompleted += (_, _) => KeepSizes(grid, node);
                if (node.Stacked)
                {
                    grid.RowDefinitions.Add(new RowDefinition(PanelLayout.Splitter, GridUnitType.Pixel));
                    Grid.SetRow(splitter, grid.RowDefinitions.Count - 1);
                }
                else
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition(PanelLayout.Splitter, GridUnitType.Pixel));
                    Grid.SetColumn(splitter, grid.ColumnDefinitions.Count - 1);
                }
                grid.Children.Add(splitter);
            }
            var (minWidth, minHeight) = PanelLayout.MinimumSize(child);
            double weight = Math.Max(0.05, child.Size);
            var content = BuildNode(child, panels);
            if (node.Stacked)
            {
                grid.RowDefinitions.Add(new RowDefinition(weight, GridUnitType.Star) { MinHeight = minHeight });
                Grid.SetRow(content, grid.RowDefinitions.Count - 1);
            }
            else
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(weight, GridUnitType.Star) { MinWidth = minWidth });
                Grid.SetColumn(content, grid.ColumnDefinitions.Count - 1);
            }
            grid.Children.Add(content);
        }
        return grid;
    }

    /// <summary>The sizes a splitter drag left, as the split's shares.</summary>
    private static void KeepSizes(Grid grid, PanelLayoutNode node)
    {
        for (int i = 0; i < node.Children.Count; i++)
        {
            double actual = node.Stacked ? grid.RowDefinitions[2 * i].ActualHeight : grid.ColumnDefinitions[2 * i].ActualWidth;
            if (actual > 0) node.Children[i].Size = actual;
        }
    }

    // ---- Docking by dragging -------------------------------------------------------------------------------------

    /// <summary>A press on a panel's number or tab strip may start moving the panel (after a few pixels of movement).</summary>
    private void BeginPanelDrag(PanelViewModel panel, PointerPressedEventArgs e, Control handle)
    {
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || _vm.Workspace.Panels.Count < 2 || _dialogs.IsOpen) return;
        _panelDrag = new PanelDrag { Panel = panel, Start = e.GetPosition(WorkspaceHost), Pointer = e.Pointer, Handle = handle };
        e.Pointer.Capture(handle);
    }

    private void OnPanelDragMoved(object? sender, PointerEventArgs e)
    {
        if (_panelDrag is not { } drag || !ReferenceEquals(e.Pointer, drag.Pointer)) return;
        var p = e.GetPosition(WorkspaceHost);
        if (!drag.Moving && Math.Abs(p.X - drag.Start.X) + Math.Abs(p.Y - drag.Start.Y) < 8) return;
        drag.Moving = true;
        e.Handled = true;
        ShowDropTarget(drag, p);
    }

    private void OnPanelDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panelDrag is not { } drag || !ReferenceEquals(e.Pointer, drag.Pointer)) return;
        EndPanelDrag();
        if (!drag.Moving) return;
        e.Handled = true;
        if (drag.Target is not { } target || !drag.Allowed) return;
        if (drag.Side is { } side) _vm.Workspace.DockPanel(drag.Panel, target, side);
        else _vm.Workspace.SwapPanelPlaces(drag.Panel, target);
        _vm.Workspace.Activate(drag.Panel);
        FocusActivePanel();
        // Said with the numbers the panels have now (they follow the places).
        _vm.Notify(drag.Side is { } docked
            ? $"Panel {drag.Panel.Number} is now {Where(docked)} panel {target.Number}."
            : $"Panels {drag.Panel.Number} and {target.Number} traded places.");
    }

    /// <summary>Ends a drag without moving anything (Esc, or the pointer went elsewhere).</summary>
    private void EndPanelDrag()
    {
        if (_panelDrag is not { } drag) return;
        _panelDrag = null;
        DockLayer.IsVisible = false;
        if (ReferenceEquals(drag.Pointer.Captured, drag.Handle)) drag.Pointer.Capture(null);
    }

    /// <summary>
    /// The panel under the pointer and the part of it: within the outer quarter of an edge the panel goes beside it,
    /// in the middle the two swap. The preview says what the drop does, or why it cannot.
    /// </summary>
    private void ShowDropTarget(PanelDrag drag, Point p)
    {
        drag.Target = null;
        foreach (var (panel, view) in _panelViews)
        {
            if (!view.IsEffectivelyVisible || view.TranslatePoint(default, WorkspaceHost) is not { } origin) continue;
            var rect = new Rect(origin, view.Bounds.Size);
            if (!rect.Contains(p)) continue;
            if (ReferenceEquals(panel, drag.Panel)) break;
            double fx = (p.X - rect.X) / rect.Width, fy = (p.Y - rect.Y) / rect.Height;
            var edges = new[] { (DockSide.Left, fx), (DockSide.Right, 1 - fx), (DockSide.Top, fy), (DockSide.Bottom, 1 - fy) };
            var (side, distance) = edges.MinBy(x => x.Item2);
            drag.Target = panel;
            drag.Side = distance <= 0.25 ? side : null;
            var tree = _vm.Workspace.PreviewDock(drag.Panel, panel, drag.Side);
            drag.Allowed = PanelLayout.Fits(tree, WorkspaceHost.Bounds.Width, WorkspaceHost.Bounds.Height);
            var zone = drag.Side switch
            {
                DockSide.Left => rect.WithWidth(rect.Width / 2),
                DockSide.Right => new Rect(rect.X + rect.Width / 2, rect.Y, rect.Width / 2, rect.Height),
                DockSide.Top => rect.WithHeight(rect.Height / 2),
                DockSide.Bottom => new Rect(rect.X, rect.Y + rect.Height / 2, rect.Width, rect.Height / 2),
                _ => rect,
            };
            DockPreviewText.Text = !drag.Allowed
                ? $"No room: every panel keeps at least {PanelLayout.PanelMinWidth:0} × {PanelLayout.PanelMinHeight:0} pixels"
                : drag.Side is { } s ? $"Panel {drag.Panel.Number} {Where(s)} panel {panel.Number}" : $"Swap places with panel {panel.Number}";
            DockPreview.Classes.Set("refused", !drag.Allowed);
            Canvas.SetLeft(DockPreview, zone.X);
            Canvas.SetTop(DockPreview, zone.Y);
            DockPreview.Width = zone.Width;
            DockPreview.Height = zone.Height;
            DockLayer.IsVisible = true;
            return;
        }
        DockLayer.IsVisible = false;
    }

    private static string Where(DockSide side) => side switch
    {
        DockSide.Left => "left of",
        DockSide.Right => "right of",
        DockSide.Top => "above",
        _ => "below",
    };

    /// <summary>For tests: where a drag over <paramref name="p"/> (in the workspace's coordinates) would put the panel.</summary>
    internal (PanelViewModel? Target, DockSide? Side, bool Allowed, string Preview) DropAt(PanelViewModel panel, Point p)
    {
        var drag = new PanelDrag { Panel = panel, Start = p, Pointer = null!, Handle = this, Moving = true };
        ShowDropTarget(drag, p);
        DockLayer.IsVisible = false;
        return (drag.Target, drag.Side, drag.Allowed, DockPreviewText.Text ?? string.Empty);
    }
}
