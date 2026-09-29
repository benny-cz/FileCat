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
        double all = 0;
        for (int i = 0; i < items; i++) all += Children[i].DesiredSize.Width;
        if (all <= availableSize.Width)
        {
            ShownCount = items;
            return new Size(all, height);
        }
        double room = availableSize.Width - Children[^1].DesiredSize.Width;
        int shown = 0;
        double used = 0;
        while (shown < items && used + Children[shown].DesiredSize.Width <= room) used += Children[shown++].DesiredSize.Width;
        // A gap between groups never ends the row.
        while (shown > 0 && Children[shown - 1].Classes.Contains("placeGap")) used -= Children[--shown].DesiredSize.Width;
        ShownCount = shown;
        return new Size(Math.Min(availableSize.Width, used + Children[^1].DesiredSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int items = Children.Count - 1;
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
