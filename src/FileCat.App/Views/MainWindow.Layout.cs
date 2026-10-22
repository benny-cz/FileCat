using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Controls;
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

    private sealed record PanelSplitterBinding(GridSplitter Splitter, Grid Grid, PanelLayoutNode Node, int RightIndex);
    private sealed record PanelResizeSession(PanelSplitterBinding Boundary, PanelLayoutNode Root, double[] OriginalSizes, double[] CurrentSizes);
    private readonly List<PanelSplitterBinding> _panelSplitters = [];
    private PanelResizeSession? _panelResize;

    /// <summary>Until the window has drawn its first frame, panels wait (see the constructor's Opened handler).</summary>
    private bool _panelsDeferred = true;

    public Size? WorkspaceViewport => WorkspaceHost.Bounds.Width > 0 && WorkspaceHost.Bounds.Height > 0
        ? WorkspaceHost.Bounds.Size : null;

    private void RebuildPanels()
    {
        if (_panelsDeferred) return;
        bool retiredResize = _panelResize is not null;
        _panelResize = null; // A replaced tree never receives an obsolete Escape restoration.
        ResizeStrip.IsVisible = false;
        _panelSplitters.Clear();
        var ws = _vm.Workspace;
        foreach (var dead in _panelViews.Keys.Where(k => !ws.Panels.Contains(k)).ToList())
        {
            // Retired views must release tab/source bindings and QuickView readers even when already hidden.
            // Layout-only detaches keep their view and context for reuse below.
            _panelViews[dead].DataContext = null;
            _panelViews.Remove(dead);
        }
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
        if (retiredResize) FocusActivePanel();
        else if (inActivePanel && focused is InputElement again && TopLevel.GetTopLevel(again) is not null) again.Focus();
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
            if (WorkspaceViewport is { } small && !PanelLayout.Fits(PanelLayoutNode.Panel(m.Id), small.Width, small.Height))
                MaximizedText.Text += " · hide chrome or enlarge the window for a usable list";
            MaximizedStrip.IsVisible = true;
            return;
        }
        bool cramped = WorkspaceViewport is { } viewport
            && !PanelLayout.Fits(ws.LayoutTree, viewport.Width, viewport.Height);
        MaximizedText.Text = cramped
            ? ws.Panels.Count > 1
                ? "The window is too small for every panel · F11 shows the active one whole"
                : "The window is too small for a usable list · hide chrome or enlarge it"
            : string.Empty;
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
                var splitter = new PanelSplitter
                {
                    ResizeDirection = node.Stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns,
                    Background = Avalonia.Media.Brushes.Transparent,
                    Focusable = true,
                    IsTabStop = false,
                };
                splitter.Classes.Add("panelResize");
                var boundary = new PanelSplitterBinding(splitter, grid, node, i);
                _panelSplitters.Add(boundary);
                UpdateSplitterName(boundary);
                // Dragged sizes become the tree's, so they are saved with the workspace.
                splitter.DragCompleted += (_, _) =>
                {
                    KeepSizes(grid, node);
                    UpdateSplitterNames(node);
                };
                splitter.LostFocus += (_, _) =>
                {
                    if (ReferenceEquals(_panelResize?.Boundary, boundary)) EndPanelResize(restore: false, returnFocus: false);
                };
                splitter.PointerPressed += (_, _) =>
                {
                    if (ReferenceEquals(_panelResize?.Boundary, boundary)) EndPanelResize(restore: false, returnFocus: false);
                };
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

    public bool BeginPanelResize(PanelViewModel panel)
    {
        if (_vm.Workspace.MaximizedPanel is not null) return false;
        // The closest split containing this panel wins. Tab in resize mode visits every other boundary.
        var boundary = _panelSplitters
            .Where(b => b.Node.Children.Skip(b.RightIndex - 1).Take(2)
                .Any(c => PanelLayout.PanelIds(c).Contains(panel.Id)))
            .OrderBy(b => PanelLayout.PanelIds(b.Node).Count)
            .FirstOrDefault(b => BoundaryExtent(b, b.RightIndex - 1) > 0 && BoundaryExtent(b, b.RightIndex) > 0);
        return boundary is not null && StartPanelResize(boundary);
    }

    private bool StartPanelResize(PanelSplitterBinding boundary)
    {
        if (WorkspaceViewport is not { } viewport || !PanelLayout.Fits(_vm.Workspace.LayoutTree, viewport.Width, viewport.Height))
            return false;
        if (!ReferenceEquals(_vm.Workspace.LayoutTree, _panelResize?.Root))
            _panelResize = null;
        if (_panelResize is not null) EndPanelResize(restore: false, returnFocus: false);
        if (BoundaryExtent(boundary, boundary.RightIndex - 1) <= 0 || BoundaryExtent(boundary, boundary.RightIndex) <= 0)
            return false;
        _panelResize = new PanelResizeSession(boundary, _vm.Workspace.LayoutTree,
            [.. boundary.Node.Children.Select(c => c.Size)],
            [.. Enumerable.Range(0, boundary.Node.Children.Count).Select(i => BoundaryExtent(boundary, i))]);
        boundary.Splitter.Classes.Add("resizeActive");
        if (!boundary.Splitter.Focus())
        {
            boundary.Splitter.Classes.Remove("resizeActive");
            _panelResize = null;
            return false;
        }
        ResizeStrip.IsVisible = true;
        UpdateSplitterName(boundary);
        return true;
    }

    private bool HandlePanelResizeKey(KeyEventArgs e)
    {
        var session = _panelResize;
        var focused = FocusManager?.GetFocusedElement() as GridSplitter;
        if (session is null && focused is not null)
        {
            var found = _panelSplitters.FirstOrDefault(b => ReferenceEquals(b.Splitter, focused));
            if (found is not null && (e.Key is Key.Left or Key.Right or Key.Up or Key.Down))
            {
                if (StartPanelResize(found)) session = _panelResize;
                else
                {
                    _vm.Notify("The workspace is cramped. Enlarge the window or hide chrome before resizing.");
                    e.Handled = true;
                    return true;
                }
            }
        }
        if (session is null) return false;
        if (!ReferenceEquals(_vm.Workspace.LayoutTree, session.Root)
            || !_panelSplitters.Contains(session.Boundary))
        {
            _panelResize = null;
            return false;
        }
        var boundary = session.Boundary;
        if (!ReferenceEquals(focused, boundary.Splitter) && focused is not null) return false;
        if (e.Key == Key.Escape)
        {
            EndPanelResize(restore: true, returnFocus: true);
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Enter)
        {
            EndPanelResize(restore: false, returnFocus: true);
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            int at = _panelSplitters.IndexOf(boundary);
            int next = (at + (e.KeyModifiers == KeyModifiers.Shift ? -1 : 1) + _panelSplitters.Count) % _panelSplitters.Count;
            StartPanelResize(_panelSplitters[next]);
            e.Handled = true;
            return true;
        }
        bool along = boundary.Node.Stacked ? e.Key is Key.Up or Key.Down : e.Key is Key.Left or Key.Right;
        if ((e.KeyModifiers & ~KeyModifiers.Shift) != 0) return false;
        if (!along)
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return false;
            _vm.Notify(boundary.Node.Stacked ? "Use Up or Down for this horizontal boundary." : "Use Left or Right for this vertical boundary.");
            e.Handled = true;
            return true;
        }
        int direction = e.Key is Key.Right or Key.Down ? 1 : -1;
        ResizeBoundary(boundary, direction * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 32 : 8));
        e.Handled = true;
        return true;
    }

    private void ResizeBoundary(PanelSplitterBinding boundary, double delta)
    {
        if (WorkspaceViewport is not { } viewport || !PanelLayout.Fits(_vm.Workspace.LayoutTree, viewport.Width, viewport.Height))
        {
            _vm.Notify("The current workspace is cramped. Enlarge the window, hide chrome, or use F11 before resizing.");
            return;
        }
        int left = boundary.RightIndex - 1, right = boundary.RightIndex;
        var session = _panelResize!;
        double a = session.CurrentSizes[left], b = session.CurrentSizes[right];
        var (aw, ah) = PanelLayout.MinimumSize(boundary.Node.Children[left]);
        var (bw, bh) = PanelLayout.MinimumSize(boundary.Node.Children[right]);
        double minA = boundary.Node.Stacked ? ah : aw, minB = boundary.Node.Stacked ? bh : bw;
        if (a + b < minA + minB)
        {
            _vm.Notify("This boundary is already cramped. Enlarge the window or hide chrome before resizing.");
            return;
        }
        double next = Math.Clamp(a + delta, minA, a + b - minB);
        if (Math.Abs(next - a) < 0.5)
        {
            _vm.Notify("Panel minimum reached at this boundary.");
            return;
        }
        // Convert the measured shares to star weights so the same proportions survive a resize and workspace save.
        for (int i = 0; i < boundary.Node.Children.Count; i++)
        {
            double extent = i == left ? next : i == right ? a + b - next : session.CurrentSizes[i];
            session.CurrentSizes[i] = extent;
            boundary.Node.Children[i].Size = extent;
            if (boundary.Node.Stacked) boundary.Grid.RowDefinitions[2 * i].Height = new GridLength(extent, GridUnitType.Star);
            else boundary.Grid.ColumnDefinitions[2 * i].Width = new GridLength(extent, GridUnitType.Star);
        }
        UpdateSplitterNames(boundary.Node);
    }

    private void EndPanelResize(bool restore, bool returnFocus)
    {
        var session = _panelResize;
        if (session is null) return;
        _panelResize = null;
        session.Boundary.Splitter.Classes.Remove("resizeActive");
        ResizeStrip.IsVisible = false;
        if (ReferenceEquals(_vm.Workspace.LayoutTree, session.Root))
        {
            if (restore)
            {
                for (int i = 0; i < session.OriginalSizes.Length; i++)
                {
                    double size = session.OriginalSizes[i];
                    session.Boundary.Node.Children[i].Size = size;
                    if (session.Boundary.Node.Stacked) session.Boundary.Grid.RowDefinitions[2 * i].Height = new GridLength(size, GridUnitType.Star);
                    else session.Boundary.Grid.ColumnDefinitions[2 * i].Width = new GridLength(size, GridUnitType.Star);
                }
                _vm.Notify("Panel boundary restored.");
            }
            UpdateSplitterNames(session.Boundary.Node);
        }
        if (returnFocus) FocusActivePanel();
    }

    private static double BoundaryExtent(PanelSplitterBinding boundary, int child) => boundary.Node.Stacked
        ? boundary.Grid.RowDefinitions[2 * child].ActualHeight
        : boundary.Grid.ColumnDefinitions[2 * child].ActualWidth;

    private void UpdateSplitterNames(PanelLayoutNode node)
    {
        foreach (var boundary in _panelSplitters.Where(b => ReferenceEquals(b.Node, node))) UpdateSplitterName(boundary);
    }

    private void UpdateSplitterName(PanelSplitterBinding boundary)
    {
        var left = boundary.Node.Children[boundary.RightIndex - 1];
        var right = boundary.Node.Children[boundary.RightIndex];
        string Panels(PanelLayoutNode node) => string.Join(", ", PanelLayout.PanelIds(node)
            .Select(id => _vm.Workspace.Panels.FirstOrDefault(p => p.Id == id)?.Number.ToString() ?? "unknown"));
        double first = Math.Max(0, left.Size), second = Math.Max(0, right.Size);
        int share = first + second > 0 ? (int)Math.Round(100 * first / (first + second)) : 50;
        string orientation = boundary.Node.Stacked ? "Horizontal" : "Vertical";
        // The share is the slider's value, which screen readers speak as it changes.
        AutomationProperties.SetName(boundary.Splitter,
            $"{orientation} panel boundary, first side's share of panels {Panels(left)} and {Panels(right)}");
        AutomationProperties.SetHelpText(boundary.Splitter,
            "Arrow keys resize; Shift+arrow moves farther; Tab chooses another boundary; Enter saves; Escape restores.");
        if (boundary.Splitter is PanelSplitter panelSplitter) panelSplitter.Share = share;
        ToolTip.SetTip(boundary.Splitter, $"{orientation} panel boundary, panels {Panels(left)} and {Panels(right)}, first side {share} percent");
        if (ReferenceEquals(_panelResize?.Boundary, boundary))
            ResizeText.Text = $"{orientation} boundary · panels {Panels(left)} and {Panels(right)} · first side {share}% · arrows 8 px · Shift 32 px · Tab next · Enter save · Esc restore";
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
            drag.Allowed = WorkspaceViewport is { } viewport && PanelLayout.Fits(tree, viewport.Width, viewport.Height);
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
