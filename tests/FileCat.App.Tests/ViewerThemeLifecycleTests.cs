using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class ViewerThemeLifecycleTests
{
    [AvaloniaFact]
    public void Open_text_and_hex_surfaces_follow_theme_changes_without_losing_position_or_selection()
    {
        string prior = ThemeManager.RequestedName;
        using var reader = new PagedReader(new MemoryContentSource("theme.txt", Encoding.UTF8.GetBytes(
            string.Join('\n', Enumerable.Range(0, 80).Select(i => $"line {i:D2} with enough text to inspect")))));
        var text = new TextViewer { Height = 200 };
        var hex = new HexView { Height = 200 };
        var window = new Window { Width = 900, Height = 500, Content = new StackPanel { Children = { text, hex } } };
        try
        {
            window.Show();
            text.SetReader(reader, new UTF8Encoding(false), 0);
            hex.SetReader(reader);
            text.ScrollToOffset(90);
            text.SetSelection(94, 102);
            hex.GoTo(100, select: true, length: 4);
            var selectedText = text.Selection;
            var selectedHex = hex.Selection;
            long textOffset = text.TopOffset, hexOffset = hex.CursorOffset;

            foreach (string name in new[] { "Classic", "ClassicDark", "HighContrast", "DosCommander", "Classic" })
            {
                ThemeManager.Apply(name);
                window.UpdateLayout();
                Assert.Same(Resource(text, "FcText"), Brush(text, "_text"));
                Assert.Same(Resource(text, "FcPanel"), Brush(text, "_bg"));
                Assert.Same(Resource(hex, "FcText"), Brush(hex, "_text"));
                Assert.Same(Resource(hex, "FcPanel"), Brush(hex, "_bg"));
                Assert.Equal(selectedText, text.Selection);
                Assert.Equal(selectedHex, hex.Selection);
                Assert.Equal(textOffset, text.TopOffset);
                Assert.Equal(hexOffset, hex.CursorOffset);
            }
        }
        finally
        {
            window.Close();
            ThemeManager.Apply(prior);
        }
    }

    [AvaloniaFact]
    public void Theme_event_owns_only_attached_viewers_and_releases_them_on_detach()
    {
        var text = new TextViewer();
        var hex = new HexView();
        var stack = new StackPanel { Children = { text, hex } };
        var window = new Window { Content = stack };
        try
        {
            window.Show();
            Assert.Equal(1, Subscribers(text));
            Assert.Equal(1, Subscribers(hex));
            window.Content = null;
            Assert.Equal(0, Subscribers(text));
            Assert.Equal(0, Subscribers(hex));
            window.Content = stack;
            Assert.Equal(1, Subscribers(text));
            Assert.Equal(1, Subscribers(hex));
        }
        finally { window.Close(); }
        Assert.Equal(0, Subscribers(text));
        Assert.Equal(0, Subscribers(hex));
    }

    private static IBrush Resource(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush : throw new KeyNotFoundException(key);

    private static IBrush Brush(object control, string field) =>
        (IBrush)control.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;

    private static int Subscribers(object target) =>
        (typeof(ThemeManager).GetField("ThemeChanged", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null) as MulticastDelegate)?.GetInvocationList().Count(d => ReferenceEquals(d.Target, target)) ?? 0;
}
