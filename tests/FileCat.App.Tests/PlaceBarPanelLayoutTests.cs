using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;

namespace FileCat.App.Tests;

/// <summary>
/// The place bar decides which buttons it shows from the width it is laid out in, not only from the width it was measured
/// with (I661): a shown button is never placed past the bar's right edge, where it would be clipped away while not being
/// on the overflow menu either.
/// </summary>
public sealed class PlaceBarPanelLayoutTests
{
    /// <summary>Five 60-pixel places and a 30-pixel overflow button, the last child as in the app.</summary>
    private static (PlaceBarPanel Panel, Control Overflow) Bar()
    {
        var panel = new PlaceBarPanel();
        for (int i = 0; i < 5; i++) panel.Children.Add(new Border { Width = 60, Height = 20 });
        var overflow = new Border { Width = 30, Height = 20 };
        panel.Children.Add(overflow);
        return (panel, overflow);
    }

    [AvaloniaFact]
    public void A_bar_laid_out_narrower_than_it_was_measured_moves_what_no_longer_fits_to_the_overflow_menu()
    {
        var (panel, overflow) = Bar();
        panel.Measure(new Size(330, 30)); // all five fit: 300 pixels
        Assert.Equal(5, panel.ShownCount);
        panel.Arrange(new Rect(0, 0, 290, 30)); // the final width is narrower: four fit beside the overflow button
        var shown = panel.Children.Take(panel.ShownCount).ToList();
        Assert.All(shown, c => Assert.True(c.Bounds.Right <= 290, $"A shown place ends at {c.Bounds.Right}, past the bar's 290."));
        Assert.Equal(4, panel.ShownCount);
        Assert.Single(panel.Hidden);
        Assert.True(overflow.Bounds.X >= 0 && overflow.Bounds.Right <= 290, $"The overflow button is at {overflow.Bounds}.");
    }

    [AvaloniaFact]
    public void A_bar_laid_out_in_the_width_it_was_measured_with_shows_what_measuring_found()
    {
        var (panel, overflow) = Bar();
        panel.Measure(new Size(290, 30));
        panel.Arrange(new Rect(0, 0, 290, 30));
        Assert.Equal(4, panel.ShownCount);
        Assert.All(panel.Children.Take(4), c => Assert.True(c.Bounds.Right <= 290));
        Assert.True(overflow.Bounds.Right <= 290);

        panel.Measure(new Size(330, 30));
        panel.Arrange(new Rect(0, 0, 330, 30));
        Assert.Equal(5, panel.ShownCount);
        Assert.Empty(panel.Hidden);
        Assert.True(overflow.Bounds.X > 330, "With every place shown, the overflow button stays off the bar.");
    }
}
