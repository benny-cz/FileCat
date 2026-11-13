using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// NTFS-compressed and EFS-encrypted names in blue and green, as Explorer and Open Salamander draw them, in each theme's
/// own shades: readable on its panels and clearly apart from the colors its rows already use.
/// </summary>
public sealed class AttributeColorTests
{
    [Fact]
    public void Every_theme_draws_compressed_and_encrypted_names_readably_and_apart_from_its_other_name_colors()
    {
        foreach (var p in ThemePalette.All)
        {
            var window = Color.Parse(p.Window);
            var panel = ThemeManager.Opaque(Color.Parse(p.Panel), window);
            var inactive = ThemeManager.Opaque(Color.Parse(p.PanelInactive), window);
            // Hidden items' color, as the theme resources choose it.
            string hidden = new[] { p.TextDim, p.TextMuted, p.Text }
                .First(c => ThemeManager.Contrast(ThemeManager.Opaque(Color.Parse(c), panel), panel) >= 4.5);
            var others = new[] { ("text", p.Text), ("folder", p.TextDirectory), ("archive", p.TextArchive),
                ("program", p.TextExecutable), ("marked", p.TextMarked), ("hidden", hidden) };
            foreach (var (label, hex) in new[] { ("compressed", p.TextCompressed), ("encrypted", p.TextEncrypted) })
            {
                var ink = ThemeManager.Opaque(Color.Parse(hex), panel);
                Assert.True(ThemeManager.Contrast(ink, panel) >= 4.5, $"{p.Name}: {label} {hex} on its panel is {ThemeManager.Contrast(ink, panel):0.00}:1");
                var inkInactive = ThemeManager.Opaque(Color.Parse(hex), inactive);
                Assert.True(ThemeManager.Contrast(inkInactive, inactive) >= 4.5,
                    $"{p.Name}: {label} {hex} on its inactive panel is {ThemeManager.Contrast(inkInactive, inactive):0.00}:1");
                foreach (var (name, other) in others)
                {
                    double distance = DeltaE(ink, ThemeManager.Opaque(Color.Parse(other), panel));
                    Assert.True(distance >= 20, $"{p.Name}: {label} {hex} is too close to the {name} color {other} (ΔE {distance:0.0})");
                }
            }
            double apart = DeltaE(ThemeManager.Opaque(Color.Parse(p.TextCompressed), panel), ThemeManager.Opaque(Color.Parse(p.TextEncrypted), panel));
            Assert.True(apart >= 20, $"{p.Name}: compressed and encrypted are too close (ΔE {apart:0.0})");
        }
    }

    [AvaloniaFact]
    public void Compressed_and_encrypted_names_take_their_colors_before_hidden_and_type_colors_in_every_theme()
    {
        string before = ThemeManager.RequestedName;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var list = window.GetVisualDescendants().OfType<FileListControl>().First();
            foreach (var theme in ThemeManager.Names)
            {
                ThemeManager.Apply(theme);
                Color Resource(string key) => ((ISolidColorBrush)Application.Current!.FindResource(key)!).Color;
                Color Name(EntryKind kind, string name, EntryFlags flags, bool marked = false, bool cursor = false) =>
                    ((ISolidColorBrush)list.NameBrush(new EntryData(name, kind) { Flags = flags }, marked, cursor)).Color;
                Color compressed = Resource("FcTextCompressed"), encrypted = Resource("FcTextEncrypted");

                Assert.Equal(compressed, Name(EntryKind.File, "notes.txt", EntryFlags.Compressed));
                Assert.Equal(compressed, Name(EntryKind.Directory, "Logs", EntryFlags.Compressed));
                Assert.Equal(compressed, Name(EntryKind.File, "backup.zip", EntryFlags.Compressed | EntryFlags.Container));
                Assert.Equal(encrypted, Name(EntryKind.File, "secret.txt", EntryFlags.Encrypted));
                Assert.Equal(encrypted, Name(EntryKind.File, "tool.exe", EntryFlags.Encrypted));
                Assert.Equal(encrypted, Name(EntryKind.Directory, "Private", EntryFlags.Encrypted));
                // Hidden and system items keep the attribute's color (their icons are dimmed instead).
                Assert.Equal(compressed, Name(EntryKind.File, "pagefile.log", EntryFlags.Compressed | EntryFlags.Hidden));
                Assert.Equal(encrypted, Name(EntryKind.File, "keys.dat", EntryFlags.Encrypted | EntryFlags.System));
                // Marks, the cursor, and unavailability keep their own meaning.
                Assert.Equal(Resource("FcTextMarked"), Name(EntryKind.File, "notes.txt", EntryFlags.Compressed, marked: true));
                Assert.Equal(Resource("FcFocusText"), Name(EntryKind.File, "secret.txt", EntryFlags.Encrypted, cursor: true));
                Assert.Equal(Resource("FcActionableDimText"), Name(EntryKind.File, "gone.txt", EntryFlags.Compressed | EntryFlags.Unavailable));
                // Items with neither keep their usual colors.
                Assert.Equal(Resource("FcTextExecutable"), Name(EntryKind.File, "tool.exe", EntryFlags.None));
                Assert.Equal(Resource("FcTextDirectory"), Name(EntryKind.Directory, "Plain", EntryFlags.None));
                Assert.Equal(Resource("FcActionableDimText"), Name(EntryKind.File, "hidden.txt", EntryFlags.Hidden));
            }
        }
        finally
        {
            ThemeManager.Apply(before);
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>CIE76 color difference in CIELAB (D65): about 2 is just noticeable; 20 is plainly another color.</summary>
    private static double DeltaE(Color a, Color b)
    {
        var (l1, a1, b1) = Lab(a);
        var (l2, a2, b2) = Lab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));

        static (double L, double A, double B) Lab(Color c)
        {
            static double Linear(byte v)
            {
                double s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
            double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, y = 0.2126 * r + 0.7152 * g + 0.0722 * b,
                z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
            static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
            return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
        }
    }
}
