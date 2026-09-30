using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileCat.App.ViewModels;

namespace FileCat.App.Views;

/// <summary>
/// Dragging a tab: along its panel's tab strip to another place, or onto another panel's strip, where it moves to. A
/// line marks where it goes; Esc, or letting go off the strips, leaves it where it was.
/// </summary>
public partial class MainWindow
{
    private sealed class TabDrag
    {
        public required PanelViewModel Panel;
        public required TabViewModel Tab;
        public required Point Start;
        public required IPointer Pointer;
        public required Control Handle;
        public bool Moving;
        /// <summary>The panel whose tab strip is under the pointer, and where among its tabs the tab goes.</summary>
        public PanelViewModel? Target;
        public int Index;
    }

    private TabDrag? _tabDrag;

    private void AttachTabDrag()
    {
        AddHandler(PointerMovedEvent, OnTabDragMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnTabDragReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, e) =>
        {
            if (_tabDrag is { } drag && ReferenceEquals(e.Pointer, drag.Pointer)) EndTabDrag();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>A left press on a tab may start dragging it (after a few pixels of movement; a click stays a click).</summary>
    private void BeginTabDrag(PanelViewModel panel, TabViewModel tab, PointerPressedEventArgs e, Control handle)
    {
        if (_dialogs.IsOpen) return;
        _tabDrag = new TabDrag { Panel = panel, Tab = tab, Start = e.GetPosition(this), Pointer = e.Pointer, Handle = handle };
    }

    private void OnTabDragMoved(object? sender, PointerEventArgs e)
    {
        if (_tabDrag is not { } drag || !ReferenceEquals(e.Pointer, drag.Pointer)) return;
        var p = e.GetPosition(this);
        if (!drag.Moving)
        {
            if (Math.Abs(p.X - drag.Start.X) < 8 && Math.Abs(p.Y - drag.Start.Y) < 8) return;
            drag.Moving = true;
            drag.Handle.Opacity = 0.5;
            Cursor = new Cursor(StandardCursorType.DragMove);
        }
        e.Handled = true;
        ShowTabDrop(drag, e.GetPosition(DockLayer));
    }

    private void OnTabDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_tabDrag is not { } drag || !ReferenceEquals(e.Pointer, drag.Pointer)) return;
        EndTabDrag();
        if (!drag.Moving || drag.Target is not { } target) return;
        e.Handled = true;
        _vm.Workspace.MoveTabToPanel(drag.Tab, target, drag.Index);
        FocusActivePanel();
    }

    /// <summary>Ends a tab drag without moving anything (Esc, or the pointer went elsewhere).</summary>
    private void EndTabDrag()
    {
        if (_tabDrag is not { } drag) return;
        _tabDrag = null;
        drag.Handle.Opacity = 1;
        Cursor = null;
        TabDropMarker.IsVisible = false;
        DockPreview.IsVisible = true; // the layer's preview, for a panel's drag
        DockLayer.IsVisible = false;
    }

    /// <summary>The tab strip under the pointer, if any, and the gap the tab would drop into, marked.</summary>
    private void ShowTabDrop(TabDrag drag, Point p)
    {
        drag.Target = null;
        foreach (var (panel, view) in _panelViews)
        {
            if (!view.IsEffectivelyVisible || view.TabDropAt(p, DockLayer, drag.Tab) is not { } drop) continue;
            drag.Target = panel;
            drag.Index = drop.Index;
            DockPreview.IsVisible = false;
            Canvas.SetLeft(TabDropMarker, drop.Gap.X);
            Canvas.SetTop(TabDropMarker, drop.Gap.Y);
            TabDropMarker.Width = drop.Gap.Width;
            TabDropMarker.Height = drop.Gap.Height;
            TabDropMarker.IsVisible = true;
            DockLayer.IsVisible = true;
            return;
        }
        TabDropMarker.IsVisible = false;
        DockLayer.IsVisible = false;
    }

    /// <summary>For tests: where a tab dragged over <paramref name="p"/> (in the drag layer's coordinates) would go.</summary>
    internal (PanelViewModel? Target, int Index) TabDropAt(TabViewModel tab, Point p)
    {
        var drag = new TabDrag { Panel = tab.Panel, Tab = tab, Start = p, Pointer = null!, Handle = this, Moving = true };
        ShowTabDrop(drag, p);
        TabDropMarker.IsVisible = false;
        DockPreview.IsVisible = true;
        DockLayer.IsVisible = false;
        return (drag.Target, drag.Index);
    }
}
