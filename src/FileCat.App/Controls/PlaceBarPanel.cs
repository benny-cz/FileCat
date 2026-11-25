using Avalonia;
using Avalonia.Controls;

namespace FileCat.App.Controls;

/// <summary>
/// One row of place buttons (D-53). What does not fit goes onto the overflow button, the last child, as on a browser's
/// bookmarks bar: the row never wraps, so the path below it stays put and two panels' lists start level.
/// </summary>
public sealed class PlaceBarPanel : Panel
{
    public PlaceBarPanel()
    {
        // Children that do not fit are arranged past the right edge, where this clips them away.
        ClipToBounds = true;
    }

    /// <summary>How many children, from the first, are shown; the rest (but the overflow button) are on its menu.</summary>
    public int ShownCount { get; private set; }

    /// <summary>The children shown on the overflow button's menu, in order (gaps between groups left out).</summary>
    public IEnumerable<Control> Hidden => Children.Take(Math.Max(0, Children.Count - 1)).Skip(ShownCount);

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        int items = Children.Count - 1;
        if (items < 0)
        {
            ShownCount = 0;
            return default;
        }
        ShownCount = Fit(availableSize.Width, items, out double used);
        return ShownCount == items
            ? new Size(used, height)
            : new Size(Math.Min(availableSize.Width, used + Children[^1].DesiredSize.Width), height);
    }

    /// <summary>
    /// How many children, from the first, fit in <paramref name="width"/>, leaving room for the overflow button when not
    /// all do; <paramref name="used"/> is their width. A gap between groups never ends the row.
    /// </summary>
    private int Fit(double width, int items, out double used)
    {
        double all = 0;
        for (int i = 0; i < items; i++) all += Children[i].DesiredSize.Width;
        if (all <= width)
        {
            used = all;
            return items;
        }
        double room = width - Children[^1].DesiredSize.Width;
        int shown = 0;
        used = 0;
        while (shown < items && used + Children[shown].DesiredSize.Width <= room) used += Children[shown++].DesiredSize.Width;
        while (shown > 0 && Children[shown - 1].Classes.Contains("placeGap")) used -= Children[--shown].DesiredSize.Width;
        return shown;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int items = Children.Count - 1;
        // The final width decides again (I661): it can be narrower than the width measured with (layout rounding, or a
        // parent that offers more than it gives), and a button placed past it would be clipped away while not being on
        // the overflow menu either.
        if (items >= 0) ShownCount = Fit(finalSize.Width, items, out _);
        double x = 0;
        for (int i = 0; i < items; i++)
        {
            var child = Children[i];
            if (i < ShownCount)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width;
            }
            else child.Arrange(new Rect(finalSize.Width + 1000, 0, child.DesiredSize.Width, finalSize.Height));
        }
        if (items >= 0)
        {
            var overflow = Children[^1];
            overflow.Arrange(ShownCount < items
                ? new Rect(x, 0, overflow.DesiredSize.Width, finalSize.Height)
                : new Rect(finalSize.Width + 1000, 0, overflow.DesiredSize.Width, finalSize.Height));
        }
        return finalSize;
    }
}
