using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>
/// Tooltips in the selected theme's colors, laid out rather than run together (I80; the owner: "improve tooltips for
/// icons, style them better according to the selected theme").
/// </summary>
public sealed class TooltipTests
{
    private static Color ColorOf(string key) => Avalonia.Application.Current!.TryGetResource(key, null, out var v) switch
    {
        true when v is Color c => c,
        true when v is ISolidColorBrush b => b.Color,
        _ => throw new KeyNotFoundException(key),
    };

    [AvaloniaFact]
    public void Tooltips_are_solid_and_their_text_reads_in_every_theme()
    {
        string before = ThemeManager.Current.Name;
        try
        {
            foreach (var name in ThemeManager.Names.Where(n => n != "System"))
            {
                ThemeManager.Apply(name);
                var tip = ColorOf("FcTipBackgroundColor");
                // A see-through card (Psychedelic's) would show what lies under the tooltip through its words.
                Assert.Equal(255, tip.A);
                foreach (var text in new[] { "FcText", "FcTextMuted", "FcTipKey" })
                {
                    double contrast = ThemeManager.Contrast(ThemeManager.Opaque(ColorOf(text), tip), tip);
                    Assert.True(contrast >= 4.5, $"{name}: {text} on the tooltip is {contrast:0.0}:1");
                }
            }
        }
        finally
        {
            ThemeManager.Apply(before);
        }
    }

    [AvaloniaFact]
    public void A_rich_tip_reads_as_one_text_and_shows_each_part_once()
    {
        var tip = new RichTip("Copy…", "F5", "Copies the marked items", ["Middle-click opens it in a new tab", null, ""]);
        Assert.Equal("Copy… (F5)\nCopies the marked items\nMiddle-click opens it in a new tab", tip.Text);
        Assert.Equal(tip.Text, tip.ToString());
        // The title with its key, the description, one hint: the empty hints leave no gap.
        Assert.Equal(3, tip.Children.Count);
        var texts = tip.GetLogicalDescendants().OfType<TextBlock>().ToList();
        Assert.Equal(["tipTitle", "tipKey", "", "tipHint"], texts.Select(t => string.Join(" ", t.Classes)));
    }

    [AvaloniaFact]
    public void A_toolbar_buttons_tooltip_is_drawn_in_the_themes_colors_and_in_the_next_themes()
    {
        string before = ThemeManager.Current.Name;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            // FileCat's own styles, as App.axaml includes them (the tests' application has Fluent's only).
            window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://FileCat.App.Tests/"))
            {
                Source = new Uri("avares://FileCat/Themes/Styles.axaml"),
            });
            ThemeManager.Apply("Classic");
            ToolTip OpenTip()
            {
                // A new theme rebuilds the toolbar to draw its icons in the theme's colors: the button is looked up each time.
                var copy = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("toolbar") && (string?)b.Tag == CommandIds.Copy);
                ToolTip.SetIsOpen(copy, true);
                Dispatcher.UIThread.RunJobs();
                return Assert.IsType<RichTip>(ToolTip.GetTip(copy)).GetVisualAncestors().OfType<ToolTip>().First();
            }
            var toolTip = OpenTip();
            Assert.Equal(ColorOf("FcTipBackgroundColor"), Assert.IsAssignableFrom<ISolidColorBrush>(toolTip.Background).Color);
            Assert.Equal(ColorOf("FcText"), Assert.IsAssignableFrom<ISolidColorBrush>(toolTip.Foreground).Color);
            var classic = ColorOf("FcTipBackgroundColor");
            // Another theme: its tooltips take its colors.
            ThemeManager.Apply("ClassicDark");
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(classic, ColorOf("FcTipBackgroundColor"));
            toolTip = OpenTip();
            Assert.Equal(ColorOf("FcTipBackgroundColor"), Assert.IsAssignableFrom<ISolidColorBrush>(toolTip.Background).Color);
            Assert.Equal(ColorOf("FcText"), Assert.IsAssignableFrom<ISolidColorBrush>(toolTip.Foreground).Color);
        }
        finally
        {
            ThemeManager.Apply(before);
            AccessibilityTests.Close(services, window, root);
        }
    }
}
