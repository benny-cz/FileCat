using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;


namespace FileCat.App.Views;

/// <summary>
/// About FileCat (Help → About): who made it, what it runs on, and where it keeps its data, dressed in the current
/// theme: Cyberpunk's circuitry, Psychedelic's drifting light, Steampunk's iron and glass behind the name, a text-mode
/// box with a cat for DOS Commander, an accent glow for Classic, and nothing but clear text for High Contrast. The
/// details copy as plain text for a bug report.
/// </summary>
internal static class AboutDialog
{
    public const string Author = "Marek Střihavka";
    public const string AuthorMail = "marek.strihavka@gmail.com";
    private const string Tagline = "MIT-licensed file manager and system-resource navigator";

    public static async Task ShowAsync(MainViewModel vm)
    {
        string version = Version();
        var facts = Facts(vm);
        var body = new StackPanel { Spacing = 14, Width = 600 };
        body.Children.Add(Header(ThemeManager.Current, version));
        var made = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        made.Children.Add(new TextBlock { Text = "Made by " + Author, VerticalAlignment = VerticalAlignment.Center });
        var mail = new HyperlinkButton { Content = AuthorMail, NavigateUri = new Uri("mailto:" + AuthorMail), Padding = new Thickness(2, 0) };
        Avalonia.Automation.AutomationProperties.SetName(mail, "Write to " + Author);
        made.Children.Add(mail);
        body.Children.Add(made);
        body.Children.Add(FactsGrid(facts));
        var answer = await vm.Dialogs.ShowCustomAsync("About FileCat", body,
            [new DialogButton("Copy details", "copy"), new DialogButton("Show data folder", "data"), new DialogButton("Close", "close", IsDefault: true, IsCancel: true)]);
        switch (answer as string)
        {
            case "copy":
                vm.CopyTextToClipboard(DetailsText(version, facts));
                vm.Notify("The details are on the clipboard.");
                break;
            case "data":
                vm.ActiveTab?.Navigate(FileCat.Core.Resources.Location.FileSystem(vm.Services.Paths.SettingsDirectory));
                break;
        }
    }

    /// <summary>The version as built ("0.1.0-preview"), with the commit it was built from when the build recorded one.</summary>
    internal static string Version()
    {
        var assembly = typeof(MainViewModel).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational)) return assembly.GetName().Version?.ToString() ?? "?";
        int plus = informational.IndexOf('+');
        // "0.1.0-preview+0874b79c…" reads as "0.1.0-preview (0874b79)".
        return plus < 0 ? informational : $"{informational[..plus]} ({informational[(plus + 1)..Math.Min(informational.Length, plus + 8)]})";
    }

    /// <summary>What a bug report wants to know, in the order it matters.</summary>
    internal static List<(string Label, string Value)> Facts(MainViewModel vm)
    {
        var services = vm.Services;
        var paths = services.Paths;
        // The platform names the system as people know it ("Windows 11 (build 26220, x64)").
        string system = services.Platform.Name;
        if (RuntimeInformation.ProcessArchitecture != RuntimeInformation.OSArchitecture) system += $" · FileCat runs as {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        if (services.Shell.IsElevated) system += " · running as administrator";
        var facts = new List<(string, string)>
        {
            ("System", system),
            ("Runtime", $"{RuntimeInformation.FrameworkDescription} · Avalonia {typeof(Application).Assembly.GetName().Version?.ToString(3)}"),
            ("Theme", ThemeManager.Current.DisplayName),
            ("Profile", paths.ProfileName + (paths.IsPortable ? " (portable)" : "")),
            ("Settings", paths.SettingsDirectory),
            ("Local data", paths.LocalDirectory),
        };
        return facts;
    }

    internal static string DetailsText(string version, IEnumerable<(string Label, string Value)> facts) =>
        $"FileCat {version}\n{Tagline}\nMade by {Author} ({AuthorMail})\n" + string.Join("\n", facts.Select(f => $"{f.Label}: {f.Value}"));

    private static Control FactsGrid(List<(string Label, string Value)> facts)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 3, ColumnSpacing = 14 };
        for (int i = 0; i < facts.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = facts[i].Label, Classes = { "muted" } };
            // Selectable, so a path can be copied as it is.
            var value = new SelectableTextBlock { Text = facts[i].Value, TextWrapping = TextWrapping.Wrap };
            Avalonia.Automation.AutomationProperties.SetName(value, facts[i].Label);
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        return grid;
    }

    // ---- The header, in the theme's own dress --------------------------------------------------------------------

    private static Control Header(ThemePalette palette, string version)
    {
        var band = new Border { Height = 132, CornerRadius = new CornerRadius(6), ClipToBounds = true, BorderThickness = new Thickness(1), BorderBrush = Brush(palette.Border) };
        var layers = new Grid();
        band.Child = layers;
        if (palette.Name == ThemePalette.DosCommander.Name)
        {
            band.Background = Brush(palette.Panel);
            layers.Children.Add(TextModeBox(palette, version));
            return band;
        }
        switch (palette.Effect)
        {
            case ThemeEffect.None when palette.Name == ThemePalette.HighContrast.Name:
                band.Background = Brush(palette.Card);
                break;
            case ThemeEffect.None:
                // Classic and Classic Dark: the accent's glow fading into the card.
                band.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(WithAlpha(palette.ActiveAccent, (byte)(palette.IsDark ? 0x66 : 0x40)), 0),
                        new GradientStop(WithAlpha(palette.TargetAccent, (byte)(palette.IsDark ? 0x22 : 0x18)), 0.7),
                        new GradientStop(Color.Parse(palette.Card), 1),
                    },
                };
                break;
            default:
                // The window's own backdrop, drawn for the band: circuitry, drifting light, or iron with glass above and below.
                band.Background = Brush(palette.Window);
                layers.Children.Add(new ThemeBackdrop { GlassBands = (26, 106) });
                break;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(20, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Image { Source = Logo(), Width = 84, Height = 84 });
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = "FileCat", FontSize = 30, FontWeight = FontWeight.Bold };
        switch (palette.Effect)
        {
            case ThemeEffect.Psychedelic:
                name.Foreground = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse(palette.ActiveAccent), 0),
                        new GradientStop(Color.Parse(palette.SearchHit), 0.5),
                        new GradientStop(Color.Parse(palette.TargetAccent), 1),
                    },
                };
                break;
            case ThemeEffect.Steampunk:
                name.Foreground = Brush(palette.HeaderText);
                break;
            case ThemeEffect.Cyberpunk:
                name.Foreground = Brush(palette.ActiveAccent);
                break;
        }
        words.Children.Add(name);
        words.Children.Add(new TextBlock
        {
            Text = palette.Effect switch
            {
                ThemeEffect.Cyberpunk => "$ filecat --version  " + version,
                ThemeEffect.Steampunk => "Version " + version + " · Est. MMXXVI",
                _ => "Version " + version,
            },
            Classes = { "muted" },
        });
        words.Children.Add(new TextBlock { Text = Tagline, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
        // Over drifting light or riveted iron the words sit on a dark glass, so they stay readable.
        row.Children.Add(palette.Effect is ThemeEffect.Psychedelic or ThemeEffect.Steampunk
            ? new Border { Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x10, 0x08, 0x18)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6), Child = words }
            : words);
        layers.Children.Add(row);
        return band;
    }

    /// <summary>DOS Commander: a double-lined box in the text screen's colors, with a cat where the logo would be.</summary>
    private static Control TextModeBox(ThemePalette palette, string version)
    {
        string[] cat = [" /\\_/\\ ", "( o.o )", " > ^ < "];
        string[] words = ["FileCat " + version, "File manager and", "system-resource navigator"];
        int inner = Math.Max(40, words.Max(w => w.Length) + cat[0].Length + 6);
        var text = new TextBlock
        {
            FontFamily = new FontFamily(palette.FontFamily ?? "Cascadia Mono, Consolas, monospace"),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Inlines = new InlineCollection(),
        };
        var frame = Brush(palette.Text);
        text.Inlines.Add(new Run("╔" + new string('═', inner) + "╗\n") { Foreground = frame });
        for (int i = 0; i < 3; i++)
        {
            text.Inlines.Add(new Run("║  ") { Foreground = frame });
            text.Inlines.Add(new Run(cat[i]) { Foreground = Brush(palette.TextMarked) });
            text.Inlines.Add(new Run("   ") { Foreground = frame });
            string line = words[i].PadRight(inner - cat[i].Length - 5);
            text.Inlines.Add(new Run(line) { Foreground = i == 0 ? Brush(palette.TextMarked) : Brush(palette.Text), FontWeight = i == 0 ? FontWeight.Bold : FontWeight.Normal });
            text.Inlines.Add(new Run("║" + (i < 2 ? "\n" : "")) { Foreground = frame });
        }
        text.Inlines.Add(new Run("\n╚" + new string('═', inner) + "╝") { Foreground = frame });
        return text;
    }

    private static IImage? Logo()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.png"));
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static Color WithAlpha(string hex, byte alpha)
    {
        var c = Color.Parse(hex);
        return Color.FromArgb(alpha, c.R, c.G, c.B);
    }
}
