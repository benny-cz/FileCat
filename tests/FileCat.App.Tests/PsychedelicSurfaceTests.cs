using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using SkiaSharp;

namespace FileCat.App.Tests;

public sealed class PsychedelicSurfaceTests
{
    [AvaloniaFact]
    public void Functional_surfaces_stay_opaque_and_menu_text_keeps_contrast_across_effect_phases()
    {
        var palette = ThemePalette.Psychedelic;
        foreach (string token in new[] { palette.Panel, palette.PanelInactive, palette.Card,
                     palette.Header, palette.KeyBarBackground, palette.StatusBackground, palette.MenuBackground })
            Assert.Equal(255, Color.Parse(token).A);
        Assert.True(ThemeManager.Contrast(Color.Parse(palette.MenuBackground), Color.Parse(palette.Text)) >= 4.5);

        // Sample the moving backdrop across its color and position cycle. It remains distinctive,
        // while an opaque functional surface is independent of every sampled background pixel.
        var frames = Enumerable.Range(0, 13).Select(i => DrawBackdrop(i * 5.0)).ToArray();
        Assert.Contains(frames.Skip(1), frame => !frame.SequenceEqual(frames[0]));
        Assert.All(frames, frame => Assert.All(frame, pixel => Assert.Equal(255, pixel.Alpha)));
    }

    [AvaloniaFact]
    public void Decorations_remain_below_content_and_disabled_animation_freezes_its_visual_time()
    {
        var content = new Border();
        var card = new Border();
        var layers = Assert.IsType<Grid>(ThemeLayers.Over(content, card));
        Assert.Equal(2, layers.Children.Count);
        Assert.IsType<ThemeBackdrop>(layers.Children[0]);
        Assert.Same(content, layers.Children[1]);

        string prior = ThemeManager.RequestedName;
        try
        {
            ThemeManager.Apply("Psychedelic");
            ThemeAnimation.SetAllowed(false);
            double frozen = ThemeAnimation.Seconds;
            var before = DrawBackdrop(frozen);
            Thread.Sleep(160);
            Assert.Equal(frozen, ThemeAnimation.Seconds);
            Assert.Equal(before, DrawBackdrop(ThemeAnimation.Seconds));
        }
        finally
        {
            ThemeManager.Apply(prior);
            ThemeAnimation.SetAllowed(true);
        }
    }

    [AvaloniaFact]
    public void Returning_to_psychedelic_restores_the_opaque_menu_resource_after_other_themes()
    {
        string prior = ThemeManager.RequestedName;
        try
        {
            foreach (string name in new[] { "Classic", "Psychedelic", "HighContrast", "DosCommander", "Psychedelic" })
            {
                ThemeManager.Apply(name);
                Assert.Equal(name, ThemeManager.Current.Name);
                Assert.True(Application.Current!.TryGetResource("FcMenuBackground", null, out var value));
                var brush = Assert.IsAssignableFrom<ISolidColorBrush>(value);
                Assert.Equal(Color.Parse(ThemeManager.Current.MenuBackground), brush.Color);
                if (name == "Psychedelic") Assert.Equal(255, brush.Color.A);
            }
        }
        finally { ThemeManager.Apply(prior); }
    }

    private static SKColor[] DrawBackdrop(double seconds)
    {
        using var surface = SKSurface.Create(new SKImageInfo(640, 400));
        EffectArt.Psychedelic(surface.Canvas, new SKRect(0, 0, 640, 400), seconds);
        using var snapshot = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(snapshot);
        var pixels = new List<SKColor>();
        for (int y = 20; y < 400; y += 47)
            for (int x = 20; x < 640; x += 59)
                pixels.Add(bitmap.GetPixel(x, y));
        return pixels.ToArray();
    }
}
