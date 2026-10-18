using System.Collections;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class SelectedGlyphContrastTests
{
    private static Color Token(string key) =>
        ((ISolidColorBrush)Avalonia.Application.Current!.FindResource(key)!).Color;

    private static Color PrivateBrush(object target, string field) =>
        ((ISolidColorBrush)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!).Color;

    private static TextLayout DecoratedLayout(TextViewer view, string property)
    {
        var cache = (IEnumerable)typeof(TextViewer).GetField("_segmentLayouts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        return cache.Cast<object>()
            .Select(entry => entry.GetType().GetProperty("Value")!.GetValue(entry)!)
            .Select(entry => entry.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry))
            .OfType<TextLayout>().Single();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task Dos_text_selection_and_search_shape_glyphs_with_their_paired_foregrounds()
    {
        string before = ThemeManager.RequestedName;
        ThemeManager.Apply("DosCommander");
        using var reader = new PagedReader(new MemoryContentSource("text", Encoding.UTF8.GetBytes("MMMMMMMM")));
        var view = new TextViewer { FontSize = 32, Wrap = false };
        var window = new Window { Content = view, Width = 480, Height = 160 };
        try
        {
            view.SetReader(reader, Encoding.UTF8, 0);
            window.Show();
            await Until(() => view.SourceGlyphBounds(0) is not null);
            view.SetSelection(0, 1);
            view.SetHighlight("MMMM", matchCase: true);
            Assert.Equal(new Color(255, 0, 0, 0), Token("FcFocusText"));
            Assert.NotEqual(Token("FcText"), Token("FcFocusText"));
            foreach (var (layout, token) in new[]
            {
                (DecoratedLayout(view, "SelectionLayout"), "FcFocusText"),
                (DecoratedLayout(view, "HighlightLayout"), "FcOnSearchHit"),
            })
            {
                var runs = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
                Assert.NotEmpty(runs);
                Assert.All(runs, run => Assert.Equal(Token(token), ((ISolidColorBrush)run.Properties.ForegroundBrush!).Color));
            }
        }
        finally { view.SetReader(null, Encoding.UTF8, 0); window.Close(); ThemeManager.Apply(before); }
    }

    [AvaloniaFact]
    public async Task Dos_hex_views_resolve_selected_glyph_ink_independently_of_normal_text()
    {
        string before = ThemeManager.RequestedName;
        ThemeManager.Apply("DosCommander");
        var window = CompareWindow.Open("left.bin", new MemoryContentSource("left.bin", [0xFF, 0x01]),
            "right.bin", new MemoryContentSource("right.bin", [0xFF, 0x02]));
        var hex = new HexView();
        var hexWindow = new Window { Content = hex, Width = 420, Height = 160 };
        try
        {
            await Until(() => !window.IsComparing);
            var compare = window.GetVisualDescendants().OfType<HexCompareView>().Single();
            Assert.True(compare.TrySelectRange(0, 0, 1));
            var comparisonInk = (ISolidColorBrush)typeof(HexCompareView)
                .GetField("_selectionText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compare)!;
            Assert.Equal(Token("FcFocusText"), comparisonInk.Color);
            var normal = (IBrush[])typeof(HexCompareView)
                .GetField("_diffText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compare)!;
            var current = (IBrush[])typeof(HexCompareView)
                .GetField("_diffCurrentText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compare)!;
            foreach (var (index, name) in new[] { (1, "Changed"), (2, "Left"), (3, "Right"), (4, "Unaligned") })
            {
                Assert.Equal(Token("FcOnDiff" + name), ((ISolidColorBrush)normal[index]).Color);
                Assert.Equal(Token("FcOnDiff" + name + "Current"), ((ISolidColorBrush)current[index]).Color);
            }

            hexWindow.Show();
            hex.ResolveBrushes();
            Assert.Equal(Token("FcFocusText"), PrivateBrush(hex, "_selectionText"));
            Assert.Equal(Token("FcOnSearchHit"), PrivateBrush(hex, "_hitText"));
            Assert.Equal(Token("FcOnChangedByte"), PrivateBrush(hex, "_modifiedText"));
            Assert.NotEqual(Token("FcText"), comparisonInk.Color);
        }
        finally { hexWindow.Close(); window.Close(); ThemeManager.Apply(before); }
    }

    [AvaloniaFact]
    public void Actionable_dim_names_keep_a_distinct_readable_brush_after_theme_switches()
    {
        string before = ThemeManager.RequestedName;
        var list = new FileListControl();
        var window = new Window { Content = list, Width = 480, Height = 160 };
        try
        {
            window.Show();
            foreach (string name in ThemeManager.Names.Where(name => name != "System"))
            {
                ThemeManager.Apply(name);
                var ink = PrivateBrush(list, "_dim");
                var panel = ThemeManager.Opaque(Token("FcPanel"), Token("FcWindow"));
                Assert.Equal(Token("FcActionableDimText"), ink);
                Assert.True(ThemeManager.Contrast(ink, panel) >= 4.5, name);
                Assert.NotEqual(Token("FcText"), ink);
            }
        }
        finally { window.Close(); ThemeManager.Apply(before); }
    }
}
