using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace FileCat.App.Services;

/// <summary>What a theme draws besides its colors (behind the panels, and for glitches over them).</summary>
public enum ThemeEffect
{
    None,
    /// <summary>Digital rain behind the lists.</summary>
    Matrix,
    /// <summary>Lava-lamp light, slowly drifting colors, and rare glitches.</summary>
    Psychedelic,
    /// <summary>Riveted, rusty iron with gears, and stained glass above and below.</summary>
    Steampunk,
}

/// <summary>
/// Semantic color tokens (plan §18.2). Themes change colors, never control order or status meaning;
/// every state also has a non-color cue drawn by the controls (mark glyph, focus outline, target label).
/// Panel colors may be translucent where a theme's effect shows through them.
/// </summary>
public sealed record ThemePalette(
    string Name, bool IsDark,
    string Window, string Panel, string PanelInactive, string Header, string Text, string TextMuted, string TextDim,
    string TextDirectory, string TextArchive, string TextExecutable, string TextLink,
    string TextMarked, string MarkedBackground, string FocusBackground, string FocusText, string FocusBorder, string FocusInactiveBorder,
    string ActiveAccent, string TargetAccent, string GridLine, string HeaderText,
    string KeyBarBackground, string KeyBarKey, string KeyBarLabel, string StatusBackground,
    string Warning, string Error, string Success, string Progress,
    string Card, string Border, string Backdrop,
    string FolderIcon, string FileIcon, string ArchiveIcon, string DriveIcon, string ExecIcon, string ImageIcon, string CodeIcon,
    string ChangedByte, string SearchHit)
{
    /// <summary>The name people see (the stored name stays <see cref="Name"/>).</summary>
    public string DisplayName { get; init; } = Name;

    /// <summary>One line for the theme picker.</summary>
    public string Description { get; init; } = "";

    public ThemeEffect Effect { get; init; }

    /// <summary>Behind the menu bar (transparent where the window or a theme's glass should show).</summary>
    public string MenuBackground { get; init; } = "#00000000";

    /// <summary>Icons drawn in one color by their brightness (a phosphor screen), or null for their own colors.</summary>
    public string? IconTint { get; init; }

    /// <summary>Font families for the whole window (a terminal look), or null for the platform's own.</summary>
    public string? FontFamily { get; init; }

    public static readonly ThemePalette Classic = new("Classic", false,
        "#F3F3F3", "#FFFFFF", "#FBFBFB", "#F0F0F0", "#1B1B1B", "#5E5E5E", "#8A8A8A",
        "#1B1B1B", "#7A4A00", "#0B5E0B", "#0B4F9E",
        "#C00000", "#FFEDED", "#CCE4F7", "#000000", "#0063B1", "#9A9A9A",
        "#0063B1", "#B8730A", "#EBEBEB", "#444444",
        "#E9E9E9", "#0063B1", "#1B1B1B", "#EDEDED",
        "#9D5D00", "#C42B1C", "#0F7B0F", "#0063B1",
        "#FFFFFF", "#CFCFCF", "#66000000",
        "#E3A21A", "#8A8A8A", "#C8742B", "#5A6B7C", "#2E8B57", "#3C8DDE", "#7D56C2",
        "#FFD7A8", "#FFF3A0")
    {
        DisplayName = "Classic", Description = "Light, calm, and familiar.",
    };

    public static readonly ThemePalette ClassicDark = new("ClassicDark", true,
        "#1C1C1C", "#232323", "#202020", "#2B2B2B", "#E8E8E8", "#A6A6A6", "#7C7C7C",
        "#E8E8E8", "#E3B26B", "#8FD18F", "#7FB8FF",
        "#FF6B6B", "#3A2426", "#0B4A7A", "#FFFFFF", "#3A96FF", "#6B6B6B",
        "#3A96FF", "#E0A030", "#303030", "#C8C8C8",
        "#1A1A1A", "#5BA8FF", "#E0E0E0", "#1E1E1E",
        "#F0B34C", "#FF6A5C", "#6CCB6C", "#3A96FF",
        "#2A2A2A", "#444444", "#88000000",
        "#E8B040", "#A0A0A0", "#E09050", "#8FA3B8", "#5CC08A", "#6AB0F0", "#A98BE8",
        "#6B4A20", "#5A5320")
    {
        DisplayName = "Classic Dark", Description = "Dark and quiet for long sessions.",
    };

    public static readonly ThemePalette Cyberpunk = new("Cyberpunk", true,
        "#000000", "#C7010A03", "#D0000602", "#DC011606", "#4DFF7A", "#23B04A", "#157A2E",
        "#B8FFCA", "#E0FF4D", "#00FFB3", "#7DF9FF",
        "#F2FFF4", "#105A20", "#0B4A1A", "#F2FFF4", "#4DFF7A", "#1F6B32",
        "#4DFF7A", "#B8FFCA", "#0C2A12", "#7DFF9E",
        "#CC000A02", "#4DFF7A", "#B8FFCA", "#D9000A02",
        "#E0FF4D", "#FF5F5F", "#4DFF7A", "#4DFF7A",
        "#F2021004", "#1F6B32", "#CC000000",
        "#4DFF7A", "#23B04A", "#E0FF4D", "#7DF9FF", "#00FFB3", "#B8FFCA", "#4DFF7A",
        "#1F6B32", "#2E6B00")
    {
        DisplayName = "Cyberpunk (Matrix)",
        Description = "Green phosphor on black in a terminal font, with digital rain falling behind the lists.",
        Effect = ThemeEffect.Matrix,
        FontFamily = "Cascadia Mono, Consolas, DejaVu Sans Mono, Menlo, Liberation Mono, monospace",
        IconTint = "#4DFF7A",
        MenuBackground = "#E6000000", // the rain stays behind the lists, not across the menu's words
    };

    public static readonly ThemePalette Psychedelic = new("Psychedelic", true,
        "#170D29", "#A61F1235", "#B31B1030", "#C22A1A46", "#F7EEFF", "#C7B2E2", "#9580B3",
        "#FFD166", "#FF9F1C", "#06D6A0", "#4CC9F0",
        "#FF5DA2", "#3D1747", "#40297A", "#FFFFFF", "#FFD166", "#6A4F92",
        "#F72585", "#06D6A0", "#2E1E4A", "#E2CFFF",
        "#CC130A22", "#FFD166", "#F7EEFF", "#CC130A22",
        "#FF9F1C", "#FF5C8A", "#06D6A0", "#F72585",
        "#F028183F", "#4A3470", "#99000000",
        "#FFD166", "#C7B2E2", "#FF9F1C", "#4CC9F0", "#06D6A0", "#F72585", "#B388FF",
        "#6A2358", "#4A4A1A")
    {
        DisplayName = "Psychedelic",
        Description = "Neon colors that slowly drift over lava-lamp light, and every now and then a glitch.",
        Effect = ThemeEffect.Psychedelic,
    };

    public static readonly ThemePalette Steampunk = new("Steampunk", true,
        "#1A120B", "#CF241910", "#D9201610", "#E03A2817", "#F2E6CC", "#C8B48C", "#8C7A5A",
        "#E8B25A", "#D98C4A", "#6FC3A5", "#9CC6E8",
        "#FF7A45", "#4A2412", "#5A3A1A", "#FFF4DC", "#E8B25A", "#6B5335",
        "#E8B25A", "#43B3AE", "#3B2A18", "#E9D2A8",
        "#E0201610", "#E8B25A", "#F2E6CC", "#D91E150E",
        "#E8A33D", "#E0573A", "#8FBF6A", "#E8B25A",
        "#F22E2016", "#7A5C38", "#AA0A0604",
        "#E8B25A", "#C8B48C", "#D98C4A", "#9CC6E8", "#6FC3A5", "#43B3AE", "#C79BFF",
        "#6B4A20", "#5A4A12")
    {
        DisplayName = "Steampunk",
        Description = "Brass, copper, and walnut over riveted, rusting iron, lit through stained glass.",
        Effect = ThemeEffect.Steampunk,
        MenuBackground = "#B8140E08",
    };

    public static readonly ThemePalette HighContrast = new("HighContrast", true,
        "#000000", "#000000", "#000000", "#000000", "#FFFFFF", "#FFFFFF", "#D0D0D0",
        "#FFFFFF", "#FFFFFF", "#FFFFFF", "#00FFFF",
        "#00FF00", "#002A00", "#000000", "#FFFF00", "#FFFF00", "#FFFFFF",
        "#FFFF00", "#00FFFF", "#FFFFFF", "#FFFFFF",
        "#000000", "#FFFF00", "#FFFFFF", "#000000",
        "#FFFF00", "#FF4040", "#00FF00", "#00FFFF",
        "#000000", "#FFFFFF", "#CC000000",
        "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF",
        "#800080", "#004080")
    {
        DisplayName = "High Contrast",
        Description = "Maximum contrast. Always used when the system asks for high contrast.",
    };

    /// <summary>In the order the theme picker shows them.</summary>
    public static IReadOnlyList<ThemePalette> All { get; } = [Classic, ClassicDark, HighContrast, Cyberpunk, Psychedelic, Steampunk];

    public static ThemePalette? Find(string name) => All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public static class ThemeManager
{
    private static ResourceDictionary? _current;
    // The accents a drifting theme recolors in place (same brush objects, new colors).
    private static readonly List<(SolidColorBrush Brush, Color Base)> Drifting = [];

    public static ThemePalette Current { get; private set; } = ThemePalette.Classic;

    public static string RequestedName { get; private set; } = "System";

    public static event Action? ThemeChanged;

    /// <summary>Raised when a drifting theme recolored its accents (custom-drawn controls repaint).</summary>
    public static event Action? PaletteTick;

    /// <summary>"System" and every theme's name, in picker order.</summary>
    public static IReadOnlyList<string> Names { get; } = ["System", .. ThemePalette.All.Select(p => p.Name)];

    public static string DisplayName(string name) => name == "System" ? "System (light or dark)" : ThemePalette.Find(name)?.DisplayName ?? name;

    public static string Description(string name) =>
        name == "System" ? "Follows the system: light or dark, and high contrast." : ThemePalette.Find(name)?.Description ?? "";

    /// <summary>Applies a theme by name; "System" follows the OS light/dark and high-contrast settings.</summary>
    public static void Apply(string name)
    {
        var app = Application.Current;
        if (app is null) return;
        RequestedName = name;
        var palette = Resolve(name, app.PlatformSettings);
        var dict = Build(palette);
        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;
        Current = palette;
        app.RequestedThemeVariant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeChanged?.Invoke();
    }

    public static string NextThemeName(string current)
    {
        int i = Names.ToList().FindIndex(n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
        return Names[(i + 1) % Names.Count];
    }

    /// <summary>
    /// Lets a drifting theme's accents move around the color wheel: one turn every two minutes, the same for every
    /// accent, so their relations (and the non-color cues) stay as designed.
    /// </summary>
    public static void Drift(double seconds)
    {
        if (Current.Effect != ThemeEffect.Psychedelic || Drifting.Count == 0) return;
        double turn = seconds / 120.0 * 360.0;
        foreach (var (brush, color) in Drifting) brush.Color = RotateHue(color, turn);
        PaletteTick?.Invoke();
    }

    private static Color RotateHue(Color color, double degrees)
    {
        var hsl = color.ToHsl();
        return HslColor.ToRgb((hsl.H + degrees) % 360.0, hsl.S, hsl.L, hsl.A);
    }

    private static ThemePalette Resolve(string name, IPlatformSettings? settings)
    {
        var colors = settings?.GetColorValues();
        // The OS contrast preference always wins over decorative themes (plan §18.2).
        if (colors?.ContrastPreference == ColorContrastPreference.High) return ThemePalette.HighContrast;
        var explicitPalette = ThemePalette.Find(name);
        if (explicitPalette is not null) return explicitPalette;
        return colors?.ThemeVariant == PlatformThemeVariant.Dark ? ThemePalette.ClassicDark : ThemePalette.Classic;
    }

    private static ResourceDictionary Build(ThemePalette p)
    {
        var d = new ResourceDictionary();
        Drifting.Clear();
        void Add(string key, string hex, bool drift = false)
        {
            var c = Color.Parse(hex);
            var brush = new SolidColorBrush(c);
            d["Fc" + key + "Color"] = c;
            d["Fc" + key] = brush;
            if (drift) Drifting.Add((brush, c));
        }
        Add("Window", p.Window);
        Add("Panel", p.Panel);
        Add("PanelInactive", p.PanelInactive);
        Add("Header", p.Header);
        Add("Text", p.Text);
        Add("TextMuted", p.TextMuted);
        Add("TextDim", p.TextDim);
        Add("TextDirectory", p.TextDirectory, drift: true);
        Add("TextArchive", p.TextArchive, drift: true);
        Add("TextExecutable", p.TextExecutable, drift: true);
        Add("TextLink", p.TextLink, drift: true);
        Add("TextMarked", p.TextMarked, drift: true);
        Add("MarkedBackground", p.MarkedBackground, drift: true);
        Add("FocusBackground", p.FocusBackground, drift: true);
        Add("FocusText", p.FocusText);
        Add("FocusBorder", p.FocusBorder, drift: true);
        Add("FocusInactiveBorder", p.FocusInactiveBorder);
        Add("ActiveAccent", p.ActiveAccent, drift: true);
        Add("TargetAccent", p.TargetAccent, drift: true);
        Add("GridLine", p.GridLine);
        Add("HeaderText", p.HeaderText);
        Add("KeyBarBackground", p.KeyBarBackground);
        Add("KeyBarKey", p.KeyBarKey, drift: true);
        Add("KeyBarLabel", p.KeyBarLabel);
        Add("StatusBackground", p.StatusBackground);
        Add("Warning", p.Warning);
        Add("Error", p.Error);
        Add("Success", p.Success);
        Add("Progress", p.Progress, drift: true);
        Add("Card", p.Card);
        Add("Border", p.Border);
        Add("Backdrop", p.Backdrop);
        Add("FolderIcon", p.FolderIcon);
        Add("FileIcon", p.FileIcon);
        Add("ArchiveIcon", p.ArchiveIcon);
        Add("DriveIcon", p.DriveIcon);
        Add("ExecIcon", p.ExecIcon);
        Add("ImageIcon", p.ImageIcon);
        Add("CodeIcon", p.CodeIcon);
        Add("ChangedByte", p.ChangedByte);
        Add("SearchHit", p.SearchHit);
        Add("MenuBackground", p.MenuBackground);
        // Text on an accent (primary buttons, the target badge): white, unless black reads clearly better (a yellow or a
        // bright green accent).
        Add("OnAccent", OnColor(p.ActiveAccent));
        Add("OnTarget", OnColor(p.TargetAccent));
        d["FcThemeName"] = p.Name;
        d["FcFontFamily"] = p.FontFamily is { } font ? new FontFamily(font) : FontFamily.Default;
        return d;
    }

    /// <summary>White or black text on a color, by WCAG contrast; white unless black is clearly better.</summary>
    internal static string OnColor(string background)
    {
        var c = Color.Parse(background);
        static double Linear(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        double luminance = 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        double onWhite = 1.05 / (luminance + 0.05), onBlack = (luminance + 0.05) / 0.05;
        return onBlack > 1.5 * onWhite ? "#FF000000" : "#FFFFFFFF";
    }
}
