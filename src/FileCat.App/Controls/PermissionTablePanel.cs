using Avalonia;
using Avalonia.Controls;

namespace FileCat.App.Controls;

/// <summary>
/// One row of a permissions table (or its heading row). With room for every column's minimum, the cells sit in weighted
/// columns, so the rows of one table line up under their heading. In a narrow dialog or at a large text size the cells
/// flow onto as many lines as they need instead of being cut to a few letters, and the heading row takes no space.
/// </summary>
internal sealed class PermissionTablePanel : Panel
{
    private const double ColumnGap = 12, LineGap = 2;
    private readonly double[] _weights;
    private readonly double[] _minimumEms;
    private readonly bool _heading;

    /// <param name="weights">Each column's share of the row's width.</param>
    /// <param name="minimumEms">Each column's least useful width, in multiples of the font size.</param>
    /// <param name="heading">A heading row, hidden while the rows flow.</param>
    internal PermissionTablePanel(double[] weights, double[] minimumEms, bool heading = false)
    {
        if (weights.Length != minimumEms.Length || weights.Length == 0 || weights.Any(weight => !(weight > 0)))
            throw new ArgumentException("Every column needs a positive weight and a minimum.", nameof(weights));
        _weights = weights;
        _minimumEms = minimumEms;
        _heading = heading;
    }

    /// <summary>Whether the last layout set the cells in columns (false: they flowed).</summary>
    internal bool InColumns { get; private set; } = true;

    // The inherited text size, the same for a heading row (whose own labels are smaller) and the rows under it.
    private double Em => GetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty);

    private bool Fits(double width) =>
        !double.IsFinite(width) || width >= _minimumEms.Sum() * Em + ColumnGap * (_weights.Length - 1);

    private double[] ColumnWidths(double width)
    {
        double usable = Math.Max(0, width - ColumnGap * (_weights.Length - 1));
        double total = _weights.Sum();
        return _weights.Select(weight => usable * weight / total).ToArray();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        InColumns = Fits(width);
        if (InColumns)
        {
            if (!double.IsFinite(width))
            {
                // Measured without a width limit (a measuring pass): report the cells' natural widths.
                double natural = 0, tallest = 0;
                foreach (var child in Children)
                {
                    child.Measure(Size.Infinity);
                    natural += child.DesiredSize.Width;
                    tallest = Math.Max(tallest, child.DesiredSize.Height);
                }
                return new Size(natural + ColumnGap * Math.Max(0, Children.Count - 1), tallest);
            }
            var columns = ColumnWidths(width);
            double height = 0;
            for (int index = 0; index < Children.Count; index++)
            {
                Children[index].Measure(new Size(index < columns.Length ? columns[index] : 0, double.PositiveInfinity));
                height = Math.Max(height, Children[index].DesiredSize.Height);
            }
            return new Size(width, height);
        }
        if (_heading)
        {
            foreach (var child in Children) child.Measure(default);
            return default;
        }
        return Flow(width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InColumns)
        {
            var columns = ColumnWidths(finalSize.Width);
            double x = 0;
            for (int index = 0; index < Children.Count; index++)
            {
                double column = index < columns.Length ? columns[index] : 0;
                Children[index].Arrange(new Rect(x, 0, column, finalSize.Height));
                x += column + ColumnGap;
            }
            return finalSize;
        }
        if (_heading)
        {
            foreach (var child in Children) child.Arrange(default);
            return finalSize;
        }
        Flow(finalSize.Width, arrange: true);
        return finalSize;
    }

    // Cells left to right at their own widths (none wider than the row), wrapping to a new line when one does not fit.
    private Size Flow(double width, bool arrange)
    {
        double x = 0, y = 0, line = 0;
        foreach (var child in Children)
        {
            if (!arrange) child.Measure(new Size(width, double.PositiveInfinity));
            double cell = Math.Min(child.DesiredSize.Width, width);
            if (!child.IsVisible || cell <= 0)
            {
                // An empty cell (a POSIX mask has no account) takes no place and no gap.
                if (arrange) child.Arrange(default);
                continue;
            }
            if (x > 0 && x + cell > width)
            {
                y += line + LineGap;
                x = 0;
                line = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, cell, child.DesiredSize.Height));
            x += cell + ColumnGap;
            line = Math.Max(line, child.DesiredSize.Height);
        }
        return new Size(width, y + line);
    }
}
