using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FileCat.App.Controls;

namespace FileCat.Rendering.Tests;

public sealed class OwnerColumnWidthTests
{
    [AvaloniaFact]
    public void Windows_owner_default_width_fits_the_required_well_known_names()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Windows Owner default width has its own native font check.");
        var choice = Assert.Single(ColumnProfileSet.Choices, c => c.Key == "meta:owner");
        var face = new Typeface(FontFamily.Default);
        const double size = 13;
        var glyphs = SimpleGlyphs.TryCreate(face, size);
        foreach (string name in new[] { @"BUILTIN\Administrators", @"NT AUTHORITY\SYSTEM" })
        {
            using var text = glyphs?.TryLayout(name, Brushes.Black, choice.DefaultWidth - 12)
                ?? CellText.Formatted(name, Brushes.Black, face, size, choice.DefaultWidth - 12);
            Assert.False(text.IsTruncated, $"Owner width {choice.DefaultWidth} clips the required {name} sample.");
        }
    }
}
