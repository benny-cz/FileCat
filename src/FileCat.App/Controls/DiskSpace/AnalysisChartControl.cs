using System.Collections.Immutable;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Controls.DiskSpace;

/// <summary>Both chart modes consume the same immutable accounting tree and stable item keys.</summary>
public abstract partial class AnalysisChartControl : Control
{
    protected sealed record Part(string Key, Rect Bounds, double Start = 0, double Sweep = 0, int Depth = 0, bool Branch = false);
    private sealed record View(IAnalysisChartData Tree, string Scope, string? Focus, ImmutableHashSet<ulong> Marks, AnalysisColorDimension Color);
    private enum Outline { Normal, Marked, Ancestor, Focused }
    private sealed record DrawingStyle(IBrush Fill, IPen Border, string TextColor);
    private static readonly ImmutableDashStyle UnverifiedBorder = new([3, 2], 0);
    private View? _view, _pending;
    private ImmutableArray<Part> _parts = [];
    private readonly Dictionary<string, AnalysisTreeItem> _displayItems = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Category, Outline Outline, bool Dashed), DrawingStyle> _drawingStyles = new();
    private ThemePalette? _drawingPalette;
    private Size _layoutSize;
    private string? _layoutFontFamily;
    private double _layoutFontSize, _labelLineHeight;
    private double _centerLineHeight;
    protected double SunburstHoleRatio { get; private set; } = .27;
    private bool _pressed;
    private string? _downKey, _hover;
    private int _clicks;
    public event Action<string, bool, bool>? Activated;
    public event Action<bool>? InteractionChanged;
    protected abstract string ViewName { get; }
    protected IAnalysisChartData? Tree => _view?.Tree;
    protected string Scope => _view?.Scope ?? AnalysisTree.RootKey;
    protected abstract ImmutableArray<Part> Layout(ImmutableArray<AnalysisTreeItem> rows, Rect bounds, UInt128 total);
    protected abstract bool Contains(Part part, Point point);
    protected abstract void DrawPart(DrawingContext context, Part part, IBrush fill, IPen border);
    private double ComparisonHeaderHeight => Tree is IAnalysisComparisonChartData ? _labelLineHeight + 6 : 0;
    protected virtual Rect PlotBounds => new(8, 8 + ComparisonHeaderHeight, Math.Max(0, Bounds.Width - 16), Math.Max(0, Bounds.Height - 16 - ComparisonHeaderHeight));
    private static string MetricLabel(AnalysisMetric metric) => metric switch
    {
        AnalysisMetric.FileSize => "File size",
        AnalysisMetric.UniqueFileSize => "Unique file size",
        AnalysisMetric.OnDisk => "On disk — reported",
        _ => "Files"
    };
    private static string ComparisonHeading(IAnalysisComparisonChartData chart) => (chart.Previous ? "Previous" : "Current") + " · " + MetricLabel(chart.Metric);
    protected double TileLabelHeight => 2 * _labelLineHeight + 12;
    static AnalysisChartControl() => AffectsRender<AnalysisChartControl>(Avalonia.Controls.Documents.TextElement.FontSizeProperty);
    public AnalysisChartControl()
    {
        Focusable = true; MinHeight = 120;
        AttachedToVisualTree += (_, _) => ThemeManager.ThemeChanged += ThemeChanged;
        DetachedFromVisualTree += (_, _) => ThemeManager.ThemeChanged -= ThemeChanged;
        GotFocus += (_, _) => ShowTip(_view?.Focus);
    }
    private void ThemeChanged() => InvalidateVisual();
    public void SetView(IAnalysisChartData tree, string scope, string? focus, ImmutableHashSet<ulong> marks, AnalysisColorDimension color = AnalysisColorDimension.FileType)
    {
        _ = AnalysisChartColors.Title(color);
        var next = new View(tree, scope, focus, marks, color);
        if (_pressed) { _pending = next; return; }
        if (_view?.Tree != tree || _view.Scope != scope) { _layoutSize = default; _parts = []; _displayItems.Clear(); }
        _view = next;
        string metric = tree is IAnalysisComparisonChartData comparison ? ComparisonHeading(comparison) : MetricLabel(tree.Metric);
        AutomationProperties.SetName(this, $"{ViewName}: {AnalysisNames.Display(tree.NameOf(scope))}. {metric}: {ScopeAmount(tree, scope, exact: true)}. Colors: {AnalysisChartColors.Title(color)}. Use the table for every entry and its measurement and identity states.");
        if (IsFocused) ShowTip(focus); else if (_hover is not null) ShowTip(_hover);
        InvalidateVisual();
    }
    public void Clear() { _view = _pending = null; _parts = []; _displayItems.Clear(); _hover = null; ToolTip.SetTip(this, null); InvalidateVisual(); }
    protected ImmutableArray<AnalysisTreeItem> DisplayRows(string parent, UInt128 total, double minimumRatio, int limit)
    {
        if (Tree is null || total == 0 || limit < 2) return [];
        var all = Tree.ChildrenOf(parent);
        int known = 0;
        while (known < all.Length && all[known].Value > 0) known++;
        int take = 0;
        while (take < known && take < limit - 1 && (take == 0 || (double)all[take].Value / (double)total >= minimumRatio)) take++;
        if (take == known) return all.Take(take).ToImmutableArray();
        var result = all.Take(take).ToImmutableArray();
        UInt128 sum = 0, logical = 0, unverified = 0; ulong files = 0; bool unknown = false, logicalUnknown = false;
        foreach (var item in all.Skip(take)) { sum = checked(sum + item.Value); unverified = checked(unverified + item.IdentityUnverified); logical = checked(logical + item.LogicalSize); files = checked(files + item.Files); unknown |= item.Unknown; logicalUnknown |= item.LogicalUnknown; }
        if (sum == 0) return result;
        string key = AnalysisTree.OtherKey(parent, take);
        var changes = all.Skip(take).Select(item => item.ComparisonChange).Distinct().Take(2).ToArray();
        var other = new AnalysisTreeItem(key, parent, "Other small items", null, AnalysisKind.Special, true, true, sum, logical, files, unknown, false, "other", [], [])
            { IdentityUnverified = unverified, LogicalUnknown = logicalUnknown, ComparisonChange = changes.Length == 1 ? changes[0] : AnalysisChange.Unknown };
        _displayItems[key] = other; return result.Add(other);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var p = ThemeManager.Current;
        if (!ReferenceEquals(_drawingPalette, p))
        {
            _drawingPalette = p;
            _drawingStyles.Clear();
        }
        context.FillRectangle(new SolidColorBrush(AnalysisChartPalette.Panel(p)), new Rect(Bounds.Size));
        if (_view is null) { Text(context, "Start an analysis to explore its measured storage.", new(16, 20), p.TextMuted); return; }
        var tree = _view.Tree;
        string family = p.FontFamily ?? FontFamily.Default.Name;
        double fontSize = GetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty);
        if (!_pressed && (_layoutSize != Bounds.Size || _layoutFontFamily != family || _layoutFontSize != fontSize))
        {
            _layoutFontFamily = family; _layoutFontSize = fontSize;
            _labelLineHeight = Format("Ag", p.Text).Height;
            if (this is SunburstControl)
            {
                var title = Format(AnalysisNames.Display(tree.NameOf(Scope), 24), p.Text);
                var amount = Format(CenterAmount(tree), p.TextMuted);
                _centerLineHeight = Math.Max(title.Height, amount.Height);
                double radius = Math.Min(PlotBounds.Width, PlotBounds.Height) / 2;
                double halfWidth = Math.Max(title.Width, amount.Width) / 2 + 6;
                double halfHeight = _centerLineHeight + 2;
                double needed = Math.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight) + 6;
                SunburstHoleRatio = radius <= 0 ? .65 : Math.Clamp(needed / radius, .27, .65);
            }
            _displayItems.Clear();
            UInt128 total = tree.ScopeValue(Scope);
            double pixels = this is SunburstControl ? Math.PI * Math.Min(PlotBounds.Width, PlotBounds.Height) : PlotBounds.Width * PlotBounds.Height;
            var rows = DisplayRows(Scope, total, (this is SunburstControl ? 3 : 24) / Math.Max(1, pixels), 2048);
            _parts = Layout(rows.Where(r => r.Value > 0).ToImmutableArray(), PlotBounds, total);
            _layoutSize = Bounds.Size;
        }
        if (tree is IAnalysisComparisonChartData comparisonChart)
            Text(context, ComparisonHeading(comparisonChart), new(8, 6), p.Text, Bounds.Width - 16);
        var focus = _view.Focus;
        var ancestors = FocusAncestors(tree, focus);
        foreach (var part in _parts)
        {
            var item = Item(part.Key);
            if (item is null) continue;
            bool focused = focus == part.Key, marked = item.NodeId is { } id && _view.Marks.Contains(id);
            bool ancestor = ancestors?.Contains(part.Key) == true;
            var outline = focused ? Outline.Focused : ancestor ? Outline.Ancestor : marked ? Outline.Marked : Outline.Normal;
            var style = Style(AnalysisChartColors.Category(tree, item, _view.Color).Key, outline,
                item.Synthetic || item.IdentityUnverified > 0, p);
            DrawPart(context, part, style.Fill, style.Border);
            if (this is TreemapControl && part.Bounds.Width > 72 && part.Bounds.Height >= _labelLineHeight + 10)
            {
                var label = new Rect(part.Bounds.Position, new Size(part.Bounds.Width, Math.Min(part.Bounds.Height, TileLabelHeight)));
                using (context.PushClip(label.Deflate(4)))
                {
                    Text(context, (marked ? "✓ " : "") + AnalysisNames.Display(item.Name, 90), part.Bounds.TopLeft + new Vector(7, 5), style.TextColor, part.Bounds.Width - 14);
                    if (part.Bounds.Height >= TileLabelHeight)
                        Text(context, FileCat.App.ViewModels.DiskSpaceViewModel.ValueText(item, tree.Metric), part.Bounds.TopLeft + new Vector(7, 6 + _labelLineHeight), style.TextColor, part.Bounds.Width - 14);
                }
            }
        }
        if (_parts.Length == 0) Text(context, "Zero, unknown and shared entries remain available in the table.", new(16, PlotBounds.Top + 14), p.TextMuted);
        if (this is SunburstControl)
        {
            string title = AnalysisNames.Display(tree.NameOf(Scope), 24);
            var center = PlotBounds.Center;
            double radius = Math.Min(PlotBounds.Width, PlotBounds.Height) / 2 * SunburstHoleRatio;
            double halfHeight = _centerLineHeight + 2;
            double width = 2 * Math.Sqrt(Math.Max(0, radius * radius - halfHeight * halfHeight)) - 12;
            if (width >= 20)
            {
                var label = new Rect(center.X - width / 2, center.Y - halfHeight, width, 2 * halfHeight);
                using (context.PushClip(label))
                {
                    Text(context, title, label.TopLeft, p.Text, width);
                    var amount = Format(CenterAmount(tree), p.TextMuted);
                    if (amount.Width <= width) context.DrawText(amount, new(label.X, center.Y + 2));
                }
            }
        }
    }
    private DrawingStyle Style(string category, Outline outline, bool dashed, ThemePalette palette)
    {
        var key = (category, outline, dashed);
        if (_drawingStyles.TryGetValue(key, out var style)) return style;
        var paint = AnalysisChartPalette.For(category, palette);
        var (color, width) = outline switch
        {
            Outline.Focused => (paint.Focus, 3),
            Outline.Ancestor => (paint.Focus, 2),
            Outline.Marked => (paint.Mark, 2),
            _ => (paint.Border, 1)
        };
        style = new(new ImmutableSolidColorBrush(paint.Fill),
            new ImmutablePen(new ImmutableSolidColorBrush(color), width, dashed ? UnverifiedBorder : null), paint.Text.ToString());
        _drawingStyles.Add(key, style);
        return style;
    }
    private string CenterAmount(IAnalysisChartData tree) => ScopeAmount(tree, Scope, exact: false);
    internal static string ScopeAmount(IAnalysisChartData tree, string scope, bool exact)
    {
        UInt128 value = tree.ScopeValue(scope);
        bool partial = tree.Items.GetValueOrDefault(scope)?.Unknown == true;
        if (partial && value == 0 && tree.Metric != AnalysisMetric.Files) return "Unknown";
        string amount = tree.Metric == AnalysisMetric.Files ? value.ToString("N0", CultureInfo.CurrentCulture) + (value == 1 ? " file path" : " file paths")
            : exact ? value.ToString("N0", CultureInfo.CurrentCulture) + (value == 1 ? " byte" : " bytes") : DiskSpaceViewModel.Bytes(value);
        return partial && exact ? amount + (tree.Metric == AnalysisMetric.Files ? " observed" : " known") : amount;
    }
    private AnalysisTreeItem? Item(string key)
    {
        if (_view is null) return null;
        if (_view.Tree.Items.TryGetValue(key, out var item)) return item;
        return _displayItems.GetValueOrDefault(key);
    }
    private static HashSet<string>? FocusAncestors(IAnalysisChartData tree, string? focus)
    {
        if (focus is null || focus == AnalysisTree.RootKey) return null;
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        for (int depth = 0; depth < 8 && focus != AnalysisTree.RootKey; depth++)
        {
            focus = tree.ParentOf(focus);
            ancestors.Add(focus);
        }
        return ancestors;
    }
    private FormattedText Format(string text, string color)
    {
        var family = new FontFamily(_pressed && _layoutFontFamily is not null ? _layoutFontFamily : ThemeManager.Current.FontFamily ?? FontFamily.Default.Name);
        double size = _pressed && _layoutFontFamily is not null ? _layoutFontSize : GetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty);
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(family), size, new SolidColorBrush(Color.Parse(color)));
    }
    protected void Text(DrawingContext context, string text, Point point, string color, double? width = null)
    {
        var formatted = Format(text, color);
        if (width is { } maximum) { formatted.MaxTextWidth = Math.Max(1, maximum); formatted.MaxLineCount = 1; formatted.Trimming = TextTrimming.CharacterEllipsis; }
        context.DrawText(formatted, point);
    }
    private string? Hit(Point point) => _parts.Reverse().FirstOrDefault(p => Contains(p, point))?.Key;
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); _pressed = true; _downKey = Hit(e.GetPosition(this)); _clicks = e.ClickCount;
        e.Pointer.Capture(this); InteractionChanged?.Invoke(true); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;
        if (_downKey is { } key && Hit(e.GetPosition(this)) == key)
            Activated?.Invoke(key, (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0, _clicks > 1);
        EndInteraction(); e.Pointer.Capture(null); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); EndInteraction(); }
    private void EndInteraction()
    {
        if (!_pressed) return; _pressed = false; _downKey = null;
        if (_pending is { } pending) { _pending = null; SetView(pending.Tree, pending.Scope, pending.Focus, pending.Marks, pending.Color); }
        InvalidateVisual();
        InteractionChanged?.Invoke(false);
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); string? key = Hit(e.GetPosition(this)); if (_hover != key) { _hover = key; ShowTip(key); } }
    private void ShowTip(string? key)
    {
        var item = key is null ? null : Item(key);
        if (item is null) { ToolTip.SetTip(this, null); return; }
        string metric = Tree?.Metric.ToString() ?? "Measure";
        var names = new Stack<string>(); string current = item.Key;
        for (int depth = 0; Tree is { } tree && depth < AnalysisLimits.Depth; depth++)
        { names.Push(tree.NameOf(current)); if (current == AnalysisTree.RootKey) break; current = tree.ParentOf(current); }
        UInt128 denominator = Tree?.ScopeValue(Scope) ?? 0;
        string percent = item.IsNonFileLeaf || !item.IncludedByPolicy ? "Not included in file measures" : denominator == 0 ? "Unknown percentage" : ((double)item.Value / (double)denominator).ToString("P1", CultureInfo.CurrentCulture) + " of current scope's known measure";
        var tip = new RichTip(AnalysisNames.Display(string.Join(" / ", names), 240), detail: $"{metric}: {FileCat.App.ViewModels.DiskSpaceViewModel.ValueText(item, Tree?.Metric ?? AnalysisMetric.OnDisk, true)}. {percent}. File size: {FileCat.App.ViewModels.DiskSpaceViewModel.LogicalText(item, true)}." +
            (item.NameEvidence is { } evidence ? "\n" + evidence : "") +
            (item.IdentityUnverified > 0 ? Tree?.Metric == AnalysisMetric.UniqueFileSize
                ? $" {item.IdentityUnverified} identity-unverified path bytes are excluded from unique chart area."
                : $" Includes {item.IdentityUnverified} identity-unverified path bytes; aliases may repeat." : "") +
            (Tree is { } colorTree && _view is { } view ? $" Color — {AnalysisChartColors.Title(view.Color)}: {AnalysisChartColors.Category(colorTree, item, view.Color).Label}." : ""),
            hints: [FileCat.App.ViewModels.DiskSpaceViewModel.EntryExplanation(item) ?? item.ChargeLocation ?? (item.SharedElsewhere ? "Shared allocation is attributed at the common parent." : item.Unknown ? "Measurement or namespace coverage is incomplete." : "Observed metadata; allocation can share physical blocks."), "More in inspector."]) { MaxWidth = 420 };
        ToolTip.SetTip(this, tip); ToolTip.SetShowDelay(this, 450);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && ToolTip.GetIsOpen(this)) { ToolTip.SetIsOpen(this, false); e.Handled = true; return; }
        if (_view is null || e.Handled) return;
        var rows = _view.Tree.ChildrenOf(Scope);
        var keys = _view.Tree is AnalysisIndexedChart or AnalysisIndexedComparisonChartProjection or AnalysisGroupedChart { GeometryProjection: true } ? _parts.Where(part => part.Depth == 0).Select(part => part.Key).Distinct().ToArray() : rows.Select(row => row.Key).ToArray();
        if (e.Key == Key.Enter)
        {
            string? key = _view.Focus ?? keys.FirstOrDefault();
            if (key is not null) Activated?.Invoke(key, false, true); e.Handled = true; return;
        }
        if (keys.Length == 0) return;
        int at = Math.Max(0, Array.IndexOf(keys, _view.Focus));
        int next = e.Key switch { Key.Left or Key.Up => Math.Max(0, at - 1), Key.Right or Key.Down => Math.Min(keys.Length - 1, at + 1), Key.Home => 0, Key.End => keys.Length - 1, _ => -1 };
        if (next >= 0) { Activated?.Invoke(keys[next], false, false); e.Handled = true; }
    }
}

public sealed class TreemapControl : AnalysisChartControl
{
    protected override string ViewName => "Treemap";
    protected override ImmutableArray<Part> Layout(ImmutableArray<AnalysisTreeItem> rows, Rect bounds, UInt128 total)
    {
        var parts = ImmutableArray.CreateBuilder<Part>();
        void Level(ImmutableArray<AnalysisTreeItem> siblings, Rect container, UInt128 sum, int depth)
        {
            int firstPart = parts.Count;
            void Split(int start, int count, Rect area, UInt128 subtotal)
            {
                if (count == 0 || subtotal == 0) return;
                if (count == 1) { parts.Add(new(siblings[start].Key, area, Depth: depth)); return; }
                int firstCount = 1; UInt128 first = siblings[start].Value;
                while (firstCount < count - 1 && first < subtotal / 2) first = checked(first + siblings[start + firstCount++].Value);
                double ratio = (double)first / (double)subtotal;
                if (area.Width >= area.Height)
                {
                    double w = area.Width * ratio;
                    Split(start, firstCount, new(area.X, area.Y, w, area.Height), first);
                    Split(start + firstCount, count - firstCount, new(area.X + w, area.Y, area.Width - w, area.Height), subtotal - first);
                }
                else
                {
                    double h = area.Height * ratio;
                    Split(start, firstCount, new(area.X, area.Y, area.Width, h), first);
                    Split(start + firstCount, count - firstCount, new(area.X, area.Y + h, area.Width, area.Height - h), subtotal - first);
                }
            }
            Split(0, siblings.Length, container, sum);
            int end = parts.Count;
            for (int i = firstPart; i < end && depth < 3; i++)
            {
                var part = parts[i]; var row = siblings[i - firstPart];
                if (part.Bounds.Width < 100 || part.Bounds.Height < TileLabelHeight + 40 || row.Members.Length > 0 || row.Synthetic) continue;
                // Keep the folder's measured name/value header visible and hit-testable.
                // Only the inner child surface is subdivided; sibling ratios and charges stay unchanged.
                var childBounds = new Rect(part.Bounds.X + 3, part.Bounds.Y + TileLabelHeight, part.Bounds.Width - 6, part.Bounds.Height - TileLabelHeight - 3);
                var children = DisplayRows(row.Key, row.Value, 24 / Math.Max(1, childBounds.Width * childBounds.Height), Math.Min(512, AnalysisLimits.ChartLeaves - parts.Count));
                if (children.Length == 0) continue;
                parts[i] = part with { Branch = true };
                Level(children, childBounds, row.Value, depth + 1);
            }
        }
        Level(rows, bounds, total, 0); return parts.ToImmutable();
    }
    protected override bool Contains(Part part, Point point) => part.Bounds.Contains(point);
    protected override void DrawPart(DrawingContext context, Part part, IBrush fill, IPen border) => context.DrawRectangle(fill, border, part.Bounds.Deflate(.5));
}

public sealed class SunburstControl : AnalysisChartControl
{
    protected override string ViewName => "Sunburst";
    protected override ImmutableArray<Part> Layout(ImmutableArray<AnalysisTreeItem> rows, Rect bounds, UInt128 total)
    {
        var parts = ImmutableArray.CreateBuilder<Part>();
        void Level(ImmutableArray<AnalysisTreeItem> siblings, UInt128 sum, double start, double arc, int depth)
        {
            int first = parts.Count; double angle = start;
            foreach (var row in siblings)
            {
                double sweep = sum == 0 ? 0 : arc * (double)row.Value / (double)sum;
                parts.Add(new(row.Key, bounds, angle, sweep, depth)); angle += sweep;
            }
            int end = parts.Count;
            for (int i = first; i < end && depth < 3; i++)
            {
                var part = parts[i]; var row = siblings[i - first];
                if (row.Members.Length > 0 || row.Synthetic) continue;
                double length = part.Sweep * Math.Min(bounds.Width, bounds.Height) / 2;
                var children = DisplayRows(row.Key, row.Value, 3 / Math.Max(1, length), Math.Min(512, AnalysisLimits.ChartLeaves - parts.Count));
                if (children.Length > 0) Level(children, row.Value, part.Start, part.Sweep, depth + 1);
            }
        }
        Level(rows, total, -Math.PI / 2, 2 * Math.PI, 0);
        return parts.ToImmutable();
    }
    protected override bool Contains(Part part, Point point)
    {
        var d = point - part.Bounds.Center; double r = Math.Min(part.Bounds.Width, part.Bounds.Height) / 2;
        double distance = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        double ring = (1 - SunburstHoleRatio) / 4;
        if (distance < r * (SunburstHoleRatio + part.Depth * ring) || distance > r * (SunburstHoleRatio + (part.Depth + 1) * ring)) return false;
        double angle = Math.Atan2(d.Y, d.X); while (angle < part.Start) angle += 2 * Math.PI;
        return angle <= part.Start + part.Sweep;
    }
    protected override void DrawPart(DrawingContext context, Part part, IBrush fill, IPen border)
    {
        double radius = Math.Min(part.Bounds.Width, part.Bounds.Height) / 2;
        double ring = (1 - SunburstHoleRatio) / 4;
        double r = radius * (SunburstHoleRatio + (part.Depth + 1) * ring), inner = radius * (SunburstHoleRatio + part.Depth * ring);
        var center = part.Bounds.Center; var geometry = new StreamGeometry();
        Point At(double a, double radius) => center + new Vector(Math.Cos(a) * radius, Math.Sin(a) * radius);
        double sweep = Math.Min(part.Sweep, 2 * Math.PI - .000001), end = part.Start + sweep;
        using (var path = geometry.Open())
        {
            path.BeginFigure(At(part.Start, r), true);
            path.ArcTo(At(end, r), new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise);
            path.LineTo(At(end, inner));
            path.ArcTo(At(part.Start, inner), new Size(inner, inner), 0, sweep > Math.PI, SweepDirection.CounterClockwise);
            path.EndFigure(true);
        }
        context.DrawGeometry(fill, border, geometry);
    }
}
