using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace FileCat.App.Services;

/// <summary>
/// Semantic color tokens (plan §18.2). Themes change colors, never control order or status meaning;
/// every state also has a non-color cue drawn by the controls (mark glyph, focus outline, target label).
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
    public static readonly ThemePalette Classic = new("Classic", false,
        "#F3F3F3", "#FFFFFF", "#FBFBFB", "#F0F0F0", "#1B1B1B", "#5E5E5E", "#8A8A8A",
        "#1B1B1B", "#7A4A00", "#0B5E0B", "#0B4F9E",
        "#C00000", "#FFEDED", "#CCE4F7", "#000000", "#0063B1", "#9A9A9A",
        "#0063B1", "#B8730A", "#EBEBEB", "#444444",
        "#E9E9E9", "#0063B1", "#1B1B1B", "#EDEDED",
        "#9D5D00", "#C42B1C", "#0F7B0F", "#0063B1",
        "#FFFFFF", "#CFCFCF", "#66000000",
        "#E3A21A", "#8A8A8A", "#C8742B", "#5A6B7C", "#2E8B57", "#3C8DDE", "#7D56C2",
        "#FFD7A8", "#FFF3A0");

    public static readonly ThemePalette ClassicDark = new("ClassicDark", true,
        "#1C1C1C", "#232323", "#202020", "#2B2B2B", "#E8E8E8", "#A6A6A6", "#7C7C7C",
        "#E8E8E8", "#E3B26B", "#8FD18F", "#7FB8FF",
        "#FF6B6B", "#3A2426", "#0B4A7A", "#FFFFFF", "#3A96FF", "#6B6B6B",
        "#3A96FF", "#E0A030", "#303030", "#C8C8C8",
        "#1A1A1A", "#5BA8FF", "#E0E0E0", "#1E1E1E",
        "#F0B34C", "#FF6A5C", "#6CCB6C", "#3A96FF",
        "#2A2A2A", "#444444", "#88000000",
        "#E8B040", "#A0A0A0", "#E09050", "#8FA3B8", "#5CC08A", "#6AB0F0", "#A98BE8",
        "#6B4A20", "#5A5320");

    public static readonly ThemePalette Cyberpunk = new("Cyberpunk", true,
        "#0A0E18", "#0E1322", "#0C101C", "#131A2D", "#D8F3FF", "#8A9ABA", "#5A6782",
        "#5CF2FF", "#FFB547", "#7CFF6B", "#B38CFF",
        "#FF2E97", "#2A0F24", "#0E3A4F", "#FFFFFF", "#00E5FF", "#3A4668",
        "#00E5FF", "#FF2E97", "#18203A", "#9FB4D9",
        "#080B14", "#FF2E97", "#D8F3FF", "#080B14",
        "#FFB547", "#FF4D6D", "#7CFF6B", "#00E5FF",
        "#111A2E", "#2A3556", "#99000000",
        "#5CF2FF", "#8A9ABA", "#FFB547", "#B38CFF", "#7CFF6B", "#FF2E97", "#B38CFF",
        "#5A1E48", "#2E4A1E");

    public static readonly ThemePalette Psychedelic = new("Psychedelic", true,
        "#170D29", "#1F1235", "#1B1030", "#2A1A46", "#F7EEFF", "#C7B2E2", "#9580B3",
        "#FFD166", "#FF9F1C", "#06D6A0", "#4CC9F0",
        "#FF5DA2", "#3D1747", "#40297A", "#FFFFFF", "#FFD166", "#6A4F92",
        "#F72585", "#06D6A0", "#2E1E4A", "#E2CFFF",
        "#130A22", "#FFD166", "#F7EEFF", "#130A22",
        "#FF9F1C", "#FF5C8A", "#06D6A0", "#F72585",
        "#28183F", "#4A3470", "#99000000",
        "#FFD166", "#C7B2E2", "#FF9F1C", "#4CC9F0", "#06D6A0", "#F72585", "#B388FF",
        "#6A2358", "#4A4A1A");

    public static readonly ThemePalette HighContrast = new("HighContrast", true,
        "#000000", "#000000", "#000000", "#000000", "#FFFFFF", "#FFFFFF", "#D0D0D0",
        "#FFFFFF", "#FFFFFF", "#FFFFFF", "#00FFFF",
        "#00FF00", "#002A00", "#000000", "#FFFF00", "#FFFF00", "#FFFFFF",
        "#FFFF00", "#00FFFF", "#FFFFFF", "#FFFFFF",
        "#000000", "#FFFF00", "#FFFFFF", "#000000",
        "#FFFF00", "#FF4040", "#00FF00", "#00FFFF",
        "#000000", "#FFFFFF", "#CC000000",
        "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF",
        "#800080", "#004080");

    public static IReadOnlyList<ThemePalette> All { get; } = [Classic, ClassicDark, Cyberpunk, Psychedelic, HighContrast];

    public static ThemePalette? Find(string name) => All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public static class ThemeManager
{
    private static ResourceDictionary? _current;

    public static ThemePalette Current { get; private set; } = ThemePalette.Classic;

    public static string RequestedName { get; private set; } = "System";

    public static event Action? ThemeChanged;

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
        var names = new[] { "System", "Classic", "ClassicDark", "Cyberpunk", "Psychedelic", "HighContrast" };
        int i = Array.FindIndex(names, n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
        return names[(i + 1) % names.Length];
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
        void Add(string key, string hex)
        {
            var c = Color.Parse(hex);
            d["Fc" + key + "Color"] = c;
            d["Fc" + key] = new SolidColorBrush(c);
        }
        Add("Window", p.Window);
        Add("Panel", p.Panel);
        Add("PanelInactive", p.PanelInactive);
        Add("Header", p.Header);
        Add("Text", p.Text);
        Add("TextMuted", p.TextMuted);
        Add("TextDim", p.TextDim);
        Add("TextDirectory", p.TextDirectory);
        Add("TextArchive", p.TextArchive);
        Add("TextExecutable", p.TextExecutable);
        Add("TextLink", p.TextLink);
        Add("TextMarked", p.TextMarked);
        Add("MarkedBackground", p.MarkedBackground);
        Add("FocusBackground", p.FocusBackground);
        Add("FocusText", p.FocusText);
        Add("FocusBorder", p.FocusBorder);
        Add("FocusInactiveBorder", p.FocusInactiveBorder);
        Add("ActiveAccent", p.ActiveAccent);
        Add("TargetAccent", p.TargetAccent);
        Add("GridLine", p.GridLine);
        Add("HeaderText", p.HeaderText);
        Add("KeyBarBackground", p.KeyBarBackground);
        Add("KeyBarKey", p.KeyBarKey);
        Add("KeyBarLabel", p.KeyBarLabel);
        Add("StatusBackground", p.StatusBackground);
        Add("Warning", p.Warning);
        Add("Error", p.Error);
        Add("Success", p.Success);
        Add("Progress", p.Progress);
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
        d["FcThemeName"] = p.Name;
        return d;
    }
}
