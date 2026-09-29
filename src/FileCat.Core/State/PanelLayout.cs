namespace FileCat.Core.State;

/// <summary>Where a panel goes beside another, or which way it moves.</summary>
public enum DockSide
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>
/// A node of the workspace layout (ADR-18): a panel, or a split whose children sit side by side or stack top to
/// bottom, each taking a share of the split proportional to its size. The workspace saves the same shape.
/// </summary>
public sealed class PanelLayoutNode
{
    /// <summary>The panel shown here; null for a split.</summary>
    public string? PanelId { get; set; }

    /// <summary>A split's children stack top to bottom; otherwise they sit side by side.</summary>
    public bool Stacked { get; set; }

    /// <summary>The node's share of its split, relative to its siblings' sizes.</summary>
    public double Size { get; set; } = 1;

    public List<PanelLayoutNode> Children { get; set; } = [];

    public bool IsPanel => PanelId is not null;

    public static PanelLayoutNode Panel(string id, double size = 1) => new() { PanelId = id, Size = size };

    public static PanelLayoutNode Split(bool stacked, IEnumerable<PanelLayoutNode> children, double size = 1) =>
        new() { Stacked = stacked, Children = [.. children], Size = size };

    public PanelLayoutNode Clone() => new() { PanelId = PanelId, Stacked = Stacked, Size = Size, Children = [.. Children.Select(c => c.Clone())] };

    public override string ToString() =>
        IsPanel ? PanelId! : (Stacked ? "rows(" : "cols(") + string.Join(",", Children.Select(c => c.ToString())) + ")";
}

/// <summary>A rectangle in the unit square the layout fills (0 to 1 both ways).</summary>
public readonly record struct UnitRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// The layout operations (ADR-18). Each returns a new, normalized tree: no split with fewer than two children and no
/// split directly inside a split that runs the same way. Panel identity, never position, names panels, so moving one
/// never retargets a job or a panel's designated target.
/// </summary>
public static class PanelLayout
{
    public const double PanelMinWidth = 260;
    public const double PanelMinHeight = 140;
    public const double Splitter = 5;

    /// <summary>The panels in one split, side by side or stacked, with their sizes (P2's flat layouts).</summary>
    public static PanelLayoutNode Default(IReadOnlyList<string> ids, bool stacked = false, IReadOnlyList<double>? sizes = null)
    {
        if (ids.Count == 0) throw new ArgumentException("A layout needs a panel.", nameof(ids));
        var leaves = ids.Select((id, i) => PanelLayoutNode.Panel(id, sizes is not null && i < sizes.Count && sizes[i] > 0 ? sizes[i] : 1));
        return Normalize(PanelLayoutNode.Split(stacked, leaves));
    }

    /// <summary>The panels in reading order: left to right, then top to bottom within each part.</summary>
    public static List<string> PanelIds(PanelLayoutNode root)
    {
        var ids = new List<string>();
        void Walk(PanelLayoutNode n)
        {
            if (n.IsPanel) ids.Add(n.PanelId!);
            else foreach (var c in n.Children) Walk(c);
        }
        Walk(root);
        return ids;
    }

    /// <summary>Whether the tree shows exactly these panels, each once.</summary>
    public static bool Matches(PanelLayoutNode? root, IReadOnlyCollection<string> ids)
    {
        if (root is null) return false;
        var shown = PanelIds(root);
        return shown.Count == ids.Count && shown.Distinct().Count() == shown.Count && shown.All(ids.Contains);
    }

    /// <summary>Collapses one-child splits, drops empty ones, and merges a split into its parent when both run the same way.</summary>
    public static PanelLayoutNode Normalize(PanelLayoutNode node)
    {
        if (node.IsPanel) return node;
        var children = new List<PanelLayoutNode>();
        foreach (var child in node.Children.Select(Normalize))
        {
            if (!child.IsPanel && child.Children.Count == 0) continue;
            if (!child.IsPanel && child.Stacked == node.Stacked)
            {
                // Its children join this split, sharing out the child's size as they shared the child.
                double total = child.Children.Sum(g => Math.Max(g.Size, 1e-6));
                foreach (var g in child.Children)
                {
                    g.Size = child.Size * Math.Max(g.Size, 1e-6) / total;
                    children.Add(g);
                }
            }
            else children.Add(child);
        }
        node.Children = children;
        if (children.Count == 1)
        {
            var only = children[0];
            only.Size = node.Size;
            return only;
        }
        return node;
    }

    /// <summary>The tree without the panel; its space goes to its siblings.</summary>
    public static PanelLayoutNode Remove(PanelLayoutNode root, string id)
    {
        var copy = root.Clone();
        if (copy.IsPanel) return copy.PanelId == id ? throw new InvalidOperationException("The last panel stays.") : copy;
        void Walk(PanelLayoutNode n)
        {
            n.Children.RemoveAll(c => c.PanelId == id);
            foreach (var c in n.Children) if (!c.IsPanel) Walk(c);
        }
        Walk(copy);
        return Normalize(copy);
    }

    /// <summary>
    /// Adds a panel beside another: into the same split when it runs that way (the neighbor gives it half its space),
    /// otherwise in a new split that takes the neighbor's place.
    /// </summary>
    public static PanelLayoutNode Insert(PanelLayoutNode root, string id, string besideId, DockSide side)
    {
        var copy = root.Clone();
        bool stacked = side is DockSide.Top or DockSide.Bottom;
        bool after = side is DockSide.Right or DockSide.Bottom;
        var (parent, index) = FindParent(copy, besideId);
        var beside = parent is null ? copy : parent.Children[index];
        if (beside.PanelId != besideId) throw new ArgumentException($"No panel {besideId} in the layout.", nameof(besideId));
        if (parent is not null && parent.Stacked == stacked)
        {
            beside.Size /= 2;
            parent.Children.Insert(after ? index + 1 : index, PanelLayoutNode.Panel(id, beside.Size));
            return Normalize(copy);
        }
        var added = PanelLayoutNode.Panel(id);
        var split = PanelLayoutNode.Split(stacked, after ? [beside, added] : [added, beside], beside.Size);
        beside.Size = 1;
        if (parent is null) return Normalize(split);
        parent.Children[index] = split;
        return Normalize(copy);
    }

    /// <summary>Moves a panel to a side of another (a drop on that panel's edge); on itself nothing changes.</summary>
    public static PanelLayoutNode Dock(PanelLayoutNode root, string id, string targetId, DockSide side) =>
        id == targetId ? root.Clone() : Insert(Remove(root, id), id, targetId, side);

    /// <summary>The two panels trade places (a drop on a panel's middle).</summary>
    public static PanelLayoutNode Swap(PanelLayoutNode root, string a, string b)
    {
        var copy = root.Clone();
        void Walk(PanelLayoutNode n)
        {
            if (n.PanelId == a) n.PanelId = b;
            else if (n.PanelId == b) n.PanelId = a;
            foreach (var c in n.Children) Walk(c);
        }
        Walk(copy);
        return copy;
    }

    /// <summary>
    /// Moves a panel one step that way, as tiling window managers do: past its neighbor within its split, or else out
    /// of its split beside the part that holds it, or else to that edge of the whole workspace. Returns the same shape
    /// when it is already there.
    /// </summary>
    public static PanelLayoutNode MoveToward(PanelLayoutNode root, string id, DockSide direction)
    {
        var copy = root.Clone();
        bool stacked = direction is DockSide.Top or DockSide.Bottom;
        bool forward = direction is DockSide.Right or DockSide.Bottom;
        var path = PathTo(copy, id) ?? throw new ArgumentException($"No panel {id} in the layout.", nameof(id));
        // path[i] is (split, index of the child on the way to the panel), from the root down to the panel's split.
        for (int level = path.Count - 1; level >= 0; level--)
        {
            var (split, index) = path[level];
            if (split.Stacked != stacked) continue;
            int next = forward ? index + 1 : index - 1;
            bool direct = level == path.Count - 1;
            if (direct)
            {
                if (next < 0 || next >= split.Children.Count) continue; // at this split's edge: out of it
                (split.Children[index], split.Children[next]) = (split.Children[next], split.Children[index]);
                return Normalize(copy);
            }
            // Out of the part that holds it, beside that part in this split.
            var part = split.Children[index];
            var panel = Detach(copy, id);
            panel.Size = split.Children.Average(c => c.Size);
            split.Children.Insert(forward ? index + 1 : index, panel);
            return Normalize(copy);
        }
        // No split runs that way with room. When the outermost one does, the panel is at its edge already; otherwise
        // the panel takes that edge of the whole workspace.
        if (copy.IsPanel || copy.Stacked == stacked) return Normalize(copy);
        var rest = Remove(copy, id);
        rest.Size = 1;
        var added = PanelLayoutNode.Panel(id);
        return Normalize(PanelLayoutNode.Split(stacked, forward ? [rest, added] : [added, rest]));
    }

    /// <summary>The split holding the panel turns: side by side becomes stacked, and back.</summary>
    public static PanelLayoutNode Rotate(PanelLayoutNode root, string id)
    {
        var copy = root.Clone();
        var (parent, _) = FindParent(copy, id);
        if (parent is null) return copy;
        parent.Stacked = !parent.Stacked;
        return Normalize(copy);
    }

    /// <summary>Every split shares its space equally.</summary>
    public static PanelLayoutNode Equalize(PanelLayoutNode root)
    {
        var copy = root.Clone();
        void Walk(PanelLayoutNode n)
        {
            foreach (var c in n.Children)
            {
                c.Size = 1;
                Walk(c);
            }
        }
        Walk(copy);
        return copy;
    }

    /// <summary>Each panel's place in the unit square.</summary>
    public static Dictionary<string, UnitRect> Arrange(PanelLayoutNode root)
    {
        var rects = new Dictionary<string, UnitRect>(StringComparer.Ordinal);
        void Walk(PanelLayoutNode n, UnitRect r)
        {
            if (n.IsPanel)
            {
                rects[n.PanelId!] = r;
                return;
            }
            double total = n.Children.Sum(c => Math.Max(c.Size, 1e-6));
            double at = n.Stacked ? r.Y : r.X;
            foreach (var c in n.Children)
            {
                double share = Math.Max(c.Size, 1e-6) / total * (n.Stacked ? r.Height : r.Width);
                Walk(c, n.Stacked ? r with { Y = at, Height = share } : r with { X = at, Width = share });
                at += share;
            }
        }
        Walk(root, new UnitRect(0, 0, 1, 1));
        return rects;
    }

    /// <summary>The panel next to this one that way (the one overlapping it most), or null at the workspace's edge.</summary>
    public static string? Neighbor(PanelLayoutNode root, string id, DockSide direction)
    {
        var rects = Arrange(root);
        if (!rects.TryGetValue(id, out var r)) return null;
        const double eps = 1e-6;
        string? best = null;
        double bestGap = double.MaxValue, bestOverlap = 0;
        foreach (var (other, q) in rects)
        {
            if (other == id) continue;
            double gap, overlap;
            switch (direction)
            {
                case DockSide.Left when q.Right <= r.X + eps: gap = r.X - q.Right; overlap = Overlap(q.Y, q.Bottom, r.Y, r.Bottom); break;
                case DockSide.Right when q.X >= r.Right - eps: gap = q.X - r.Right; overlap = Overlap(q.Y, q.Bottom, r.Y, r.Bottom); break;
                case DockSide.Top when q.Bottom <= r.Y + eps: gap = r.Y - q.Bottom; overlap = Overlap(q.X, q.Right, r.X, r.Right); break;
                case DockSide.Bottom when q.Y >= r.Bottom - eps: gap = q.Y - r.Bottom; overlap = Overlap(q.X, q.Right, r.X, r.Right); break;
                default: continue;
            }
            if (overlap <= eps) continue;
            if (gap < bestGap - eps || Math.Abs(gap - bestGap) <= eps && overlap > bestOverlap)
            {
                best = other;
                bestGap = gap;
                bestOverlap = overlap;
            }
        }
        return best;
    }

    private static double Overlap(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

    /// <summary>The least room the layout needs so that every panel keeps its minimum size.</summary>
    public static (double Width, double Height) MinimumSize(PanelLayoutNode node)
    {
        if (node.IsPanel) return (PanelMinWidth, PanelMinHeight);
        var sizes = node.Children.Select(MinimumSize).ToList();
        double gaps = Splitter * (sizes.Count - 1);
        return node.Stacked
            ? (sizes.Max(s => s.Width), sizes.Sum(s => s.Height) + gaps)
            : (sizes.Sum(s => s.Width) + gaps, sizes.Max(s => s.Height));
    }

    /// <summary>Whether the layout fits the room, every panel at least its minimum size.</summary>
    public static bool Fits(PanelLayoutNode root, double width, double height)
    {
        var (w, h) = MinimumSize(root);
        return w <= width + 0.5 && h <= height + 0.5;
    }

    // ---- Tree helpers ---------------------------------------------------------------------------------------------

    private static (PanelLayoutNode? Parent, int Index) FindParent(PanelLayoutNode root, string id)
    {
        if (root.PanelId == id) return (null, -1);
        foreach (var (child, i) in root.Children.Select((c, i) => (c, i)))
        {
            if (child.PanelId == id) return (root, i);
            if (!child.IsPanel && FindParent(child, id) is { Parent: not null } found) return found;
        }
        return (null, -1);
    }

    private static List<(PanelLayoutNode Split, int Index)>? PathTo(PanelLayoutNode root, string id)
    {
        if (root.PanelId == id) return [];
        for (int i = 0; i < root.Children.Count; i++)
        {
            var child = root.Children[i];
            if (child.PanelId == id) return [(root, i)];
            if (!child.IsPanel && PathTo(child, id) is { } below)
            {
                below.Insert(0, (root, i));
                return below;
            }
        }
        return null;
    }

    /// <summary>Takes the panel's node out of the tree (in place; the tree may need normalizing).</summary>
    private static PanelLayoutNode Detach(PanelLayoutNode root, string id)
    {
        var (parent, index) = FindParent(root, id);
        if (parent is null) throw new InvalidOperationException("The last panel stays.");
        var node = parent.Children[index];
        parent.Children.RemoveAt(index);
        return node;
    }
}
