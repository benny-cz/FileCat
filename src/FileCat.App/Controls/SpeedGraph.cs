using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileCat.App.Controls;

/// <summary>
/// The last 90 seconds of an operation's pace: time advances to the right and a stall occupies horizontal space at
/// the floor. The height scales to the fastest recent sample; the numeric unit and peak are labelled by the view.
/// </summary>
public sealed class SpeedGraph : Control
{
    /// <summary>X: elapsed seconds. Y: actual bytes or settled items per second.</summary>
    public static readonly StyledProperty<Point[]> ValuesProperty = AvaloniaProperty.Register<SpeedGraph, Point[]>(nameof(Values), []);

    static SpeedGraph() => AffectsRender<SpeedGraph>(ValuesProperty);

    public SpeedGraph() => Avalonia.Automation.AutomationProperties.SetName(this, "Operation speed over the last 90 seconds");

    public Point[] Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        double width = Bounds.Width, height = Bounds.Height;
        if (width < 10 || height < 4) return;
        var accent = this.TryFindResource("FcActiveAccent", ActualThemeVariant, out var found) && found is ISolidColorBrush solid ? solid.Color : Colors.SteelBlue;
        var ink = this.TryFindResource("FcText", ActualThemeVariant, out var textBrush) && textBrush is ISolidColorBrush textSolid ? textSolid.Color : Colors.Black;
        var points = Project(values, width, height);
        if (points.Length == 0) return;
        context.DrawLine(new Pen(new SolidColorBrush(ink, 0.2), 1), new Point(1, height - 1), new Point(width - 1, height - 1));
        if (points.Length == 1)
        {
            context.DrawEllipse(new SolidColorBrush(ink, 0.85), null, points[0], 3, 3);
            return;
        }
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, height - 1), true);
            foreach (var point in points) g.LineTo(point);
            g.LineTo(new Point(points[^1].X, height - 1));
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(accent, 0.11), null, area);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false);
            foreach (var point in points.Skip(1)) g.LineTo(point);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(ink, 0.85), 1.6), line);
    }

    /// <summary>Projects timestamped samples into pixels; kept pure so stalls and window limits can be tested.</summary>
    internal static Point[] Project(Point[] values, double width, double height)
    {
        if (width < 10 || height < 4 || values.Length == 0) return [];
        var valid = values.Where(v => double.IsFinite(v.X) && double.IsFinite(v.Y)).ToArray();
        if (valid.Length == 0) return [];
        double last = valid[^1].X;
        double earliest = Math.Max(0, last - 90);
        double span = Math.Max(1, last - earliest);
        double peak = Math.Max(1, valid.Max(v => v.Y));
        return valid.Where(v => v.X >= earliest).Select(v => new Point(
            1 + Math.Clamp((v.X - earliest) / span, 0, 1) * (width - 2),
            height - 1 - Math.Clamp(v.Y, 0, peak) / peak * (height - 4))).ToArray();
    }
}
