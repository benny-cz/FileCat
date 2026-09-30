using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileCat.App.Controls;

/// <summary>
/// An operation's pace along the way (release issue I30), as the Windows copy dialog draws it: across is how far the
/// operation is (the right edge is its end), up is the speed, scaled to the fastest so far. It fills from the left while
/// the operation runs; a stall shows as a drop to the floor, a slower part of the work as a step.
/// </summary>
public sealed class SpeedGraph : Control
{
    /// <summary>X: how far the operation was, 0 to 1. Y: its speed then, in bytes per second.</summary>
    public static readonly StyledProperty<Point[]> ValuesProperty = AvaloniaProperty.Register<SpeedGraph, Point[]>(nameof(Values), []);

    static SpeedGraph() => AffectsRender<SpeedGraph>(ValuesProperty);

    public SpeedGraph() => Avalonia.Automation.AutomationProperties.SetName(this, "Speed along the operation");

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
        context.DrawRectangle(null, new Pen(new SolidColorBrush(accent, 0.35), 1), new Rect(0.5, 0.5, width - 1, height - 1));
        if (values.Length == 0) return;
        double max = Math.Max(values.Max(v => v.Y), 1);
        double X(Point v) => 1 + Math.Clamp(v.X, 0, 1) * (width - 2);
        double Y(Point v) => height - 1 - v.Y / max * (height - 4);
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(X(values[0]), height - 1), true);
            foreach (var v in values) g.LineTo(new Point(X(v), Y(v)));
            g.LineTo(new Point(X(values[^1]), height - 1));
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(accent, 0.25), null, area);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(new Point(X(values[0]), Y(values[0])), false);
            foreach (var v in values.Skip(1)) g.LineTo(new Point(X(v), Y(v)));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 1.4), line);
    }
}
