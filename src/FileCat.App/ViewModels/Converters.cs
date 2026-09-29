using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FileCat.App.ViewModels;

public static class Converters
{
    private static IBrush Resource(string key, IBrush fallback) =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) == true && v is IBrush b ? b : fallback;

    /// <summary>The active panel has a thicker frame: a non-color cue for focus (PI-04).</summary>
    public static readonly IValueConverter ActiveBorder =
        new FuncValueConverter<bool, Thickness>(active => active ? new Thickness(2) : new Thickness(1));

    /// <summary>The panel number's background: the active color, else the header's (its muted number stays readable in every theme).</summary>
    public static readonly IValueConverter ActiveAccent =
        new FuncValueConverter<bool, IBrush>(active => active ? Resource("FcActiveAccent", Brushes.SteelBlue) : Resource("FcHeader", Brushes.LightGray));

    public static readonly IValueConverter ActiveAccentText =
        new FuncValueConverter<bool, IBrush>(active => active ? Resource("FcPanel", Brushes.White) : Resource("FcTextMuted", Brushes.Gray));
}
