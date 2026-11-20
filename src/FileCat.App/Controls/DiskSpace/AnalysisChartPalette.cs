using System.Runtime.CompilerServices;
using Avalonia.Media;
using FileCat.App.Services;

namespace FileCat.App.Controls.DiskSpace;

/// <summary>Stable category hues; semantic warning/error tokens are never used for data categories.</summary>
internal static class AnalysisChartPalette
{
    internal sealed record Paint(Color Fill, Color Text, Color Border, Color Focus, Color Mark);
    private static readonly ConditionalWeakTable<ThemePalette, Dictionary<string, Paint>> Cache = new();
    internal static Paint For(string key, ThemePalette palette)
    {
        var colors = Cache.GetOrCreateValue(palette);
        if (colors.TryGetValue(key, out var existing)) return existing;
        Color panel = Panel(palette);
        string hue = key switch
        {
            "text" => "#007D9C", "code" => "#3973CF", "image" => "#9361B8", "audio" => "#218B77", "video" => "#6265B2",
            "archive" => "#A17A16", "executable" => "#788330", "document" => "#626F9B",
            "depth-0" => "#8A8F98", "depth-1" => "#708EA4", "depth-3" => "#4B86AF", "depth-6" => "#277BB8", "depth-11" => "#006EC2",
            "age-7" => "#249A99", "age-30" => "#3F8BA8", "age-180" => "#637DB6", "age-365" => "#826FA5", "age-old" => "#91818F",
            "age-future" => "#8A8564", "shared" => "#477F85", "directory-allocation" => "#8C8272",
            "growth-up" => "#377DC4", "growth-down" => "#BD7B24", "growth-added" => "#318A79", "growth-removed" => "#9167A5",
            "growth-attribution" => "#7F879B", "growth-moved" => "#658A99", "growth-stable" => "#7F8981", _ => "#888B90"
        };
        Color accent = Color.Parse(hue);
        Color fill = Color.FromRgb(Blend(panel.R, accent.R), Blend(panel.G, accent.G), Blend(panel.B, accent.B));
        Color black = Colors.Black, white = Colors.White;
        Color border = Math.Min(ThemeManager.Contrast(black, fill), ThemeManager.Contrast(black, panel)) >=
            Math.Min(ThemeManager.Contrast(white, fill), ThemeManager.Contrast(white, panel)) ? black : white;
        var paint = new Paint(fill, On(fill, Color.Parse(palette.Text)), border,
            Boundary(Color.Parse(palette.FocusBorder)), Boundary(Color.Parse(palette.TextMarked)));
        colors.Add(key, paint); return paint;
        Color Boundary(Color preferred) => ThemeManager.Contrast(preferred, fill) >= 3 && ThemeManager.Contrast(preferred, panel) >= 3 ? preferred : border;
        static byte Blend(byte a, byte b) => (byte)Math.Round(a * .72 + b * .28);
    }
    internal static Color Panel(ThemePalette palette) => ThemeManager.Opaque(Color.Parse(palette.Panel), Color.Parse(palette.Window));
    internal static Color On(Color background, Color preferred) => ThemeManager.Contrast(preferred, background) >= 4.5 ? preferred :
        ThemeManager.Contrast(Colors.Black, background) >= ThemeManager.Contrast(Colors.White, background) ? Colors.Black : Colors.White;
}
