using Avalonia;
using Avalonia.Data.Converters;

namespace FileCat.App.ViewModels;

/// <remarks>
/// Colors are not converted here: a converter reads a theme once, when its binding changes, so a brush picked by one keeps
/// the old theme's color after a theme change (I663). Theme colors come from styles with dynamic resources instead.
/// </remarks>
public static class Converters
{
    /// <summary>The active panel has a thicker frame: a non-color cue for focus (PI-04).</summary>
    public static readonly IValueConverter ActiveBorder =
        new FuncValueConverter<bool, Thickness>(active => active ? new Thickness(2) : new Thickness(1));
}
