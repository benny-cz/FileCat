using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

/// <summary>Arranging panels from the keyboard (plan §4.1, ADR-18): every drag has a command.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Whether a layout command applies now, or null for other commands.</summary>
    private CommandAvailability? LayoutAvailability(string id)
    {
        int panels = Workspace.Panels.Count;
        return id switch
        {
            CommandIds.PanelMoveLeft or CommandIds.PanelMoveRight or CommandIds.PanelMoveUp or CommandIds.PanelMoveDown
                or CommandIds.RotatePanels or CommandIds.EqualizePanels when panels < 2 =>
                CommandAvailability.No("There is only one panel. Panels → Add panel adds another."),
            CommandIds.PanelSwapPlaces when Workspace.ActiveTarget is null =>
                CommandAvailability.No(panels < 2 ? "There is only one panel." : "This panel has no target panel; Shift+F12 chooses one."),
            CommandIds.AddPanelBelow when panels >= WorkspaceViewModel.MaxPanels =>
                CommandAvailability.No($"At most {WorkspaceViewModel.MaxPanels} panels fit a usable layout."),
            _ => null,
        };
    }

    /// <summary>Runs a layout command; false for other commands.</summary>
    private bool ExecuteLayoutCommand(string id)
    {
        if (Workspace.ActivePanel is not { } panel) return false;
        switch (id)
        {
            case CommandIds.PanelMoveLeft: Move(DockSide.Left, "left"); return true;
            case CommandIds.PanelMoveRight: Move(DockSide.Right, "right"); return true;
            case CommandIds.PanelMoveUp: Move(DockSide.Top, "top"); return true;
            case CommandIds.PanelMoveDown: Move(DockSide.Bottom, "bottom"); return true;
            case CommandIds.PanelSwapPlaces:
                if (Workspace.ActiveTarget is { } target)
                {
                    Workspace.SwapPanelPlaces(panel, target);
                    Notify($"Panel {panel.Number} and panel {target.Number} traded places; each kept its tabs and target.");
                }
                View.FocusActivePanel();
                return true;
            case CommandIds.RotatePanels:
                Workspace.RotateSplit(panel);
                View.FocusActivePanel();
                return true;
            case CommandIds.EqualizePanels:
                Workspace.EqualizeSizes();
                View.FocusActivePanel();
                return true;
            case CommandIds.AddPanelBelow:
                if (!RoomForPanel(panel, DockSide.Bottom)) return true;
                Workspace.Activate(Workspace.AddPanel(side: DockSide.Bottom));
                View.FocusActivePanel();
                return true;
        }
        return false;

        void Move(DockSide side, string edge)
        {
            if (!Workspace.MovePanelToward(panel, side)) Notify($"Panel {panel.Number} is already at the {edge} edge.");
            else Notify($"Panel {panel.Number} moved {(side is DockSide.Top ? "up" : side is DockSide.Bottom ? "down" : edge)}{Beside(panel)}.");
            View.FocusActivePanel();
        }
    }

    /// <summary>Where a panel is now, by up to two neighbors (": right of panel 2, above panel 3").</summary>
    private string Beside(PanelViewModel panel)
    {
        var parts = new List<string>();
        foreach (var (side, word) in new[] { (DockSide.Left, "right of"), (DockSide.Top, "below"), (DockSide.Right, "left of"), (DockSide.Bottom, "above") })
        {
            if (PanelLayout.Neighbor(Workspace.LayoutTree, panel.Id, side) is { } id && Workspace.Panels.FirstOrDefault(p => p.Id == id) is { } other)
                parts.Add($"{word} panel {other.Number}");
            if (parts.Count == 2) break;
        }
        return parts.Count == 0 ? string.Empty : ": " + string.Join(", ", parts);
    }

    /// <summary>
    /// Whether a new panel beside <paramref name="panel"/> leaves every panel its minimum size in this window; says why
    /// not when it does not.
    /// </summary>
    private bool RoomForPanel(PanelViewModel panel, DockSide side)
    {
        if (View.TopLevel is not { } top) return true;
        // The panels get the window less its menu, command line, and key bar.
        var room = (Width: top.Bounds.Width - 8, Height: top.Bounds.Height - 130);
        var tree = PanelLayout.Insert(Workspace.LayoutTree, "new panel", panel.Id, side);
        if (PanelLayout.Fits(tree, room.Width, room.Height)) return true;
        Notify($"There is no room for another panel there: each keeps at least {PanelLayout.PanelMinWidth:0} × {PanelLayout.PanelMinHeight:0} pixels. "
               + "Enlarge the window, add it elsewhere, or close a panel.");
        return false;
    }
}
