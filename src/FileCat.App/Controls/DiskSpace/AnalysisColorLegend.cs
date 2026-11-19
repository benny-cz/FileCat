using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Controls.DiskSpace;

/// <summary>A bounded, keyboard-expandable legend shared by memory and indexed views.</summary>
public sealed class AnalysisColorLegend : Expander
{
    protected override Type StyleKeyOverride => typeof(Expander);
    private AnalysisColorDimension _dimension;
    private DateTimeOffset _observed;
    private bool _initialized;
    public AnalysisColorLegend()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AttachedToVisualTree += (_, _) => { ThemeManager.ThemeChanged += Rebuild; Rebuild(); };
        DetachedFromVisualTree += (_, _) => ThemeManager.ThemeChanged -= Rebuild;
    }
    public static ComboBox CreateSelector()
    {
        var selector = new ComboBox { ItemsSource = new[] { "Color: file type", "Color: depth", "Color: modification age" }, SelectedIndex = 0, MinWidth = 190 };
        AutomationProperties.SetName(selector, "Chart color dimension");
        ToolTip.SetTip(selector, new RichTip("Chart colors", detail: "Color changes the legend and tile appearance only. Area, totals, focus and marks keep their recorded meaning. Modification age uses the capture time, including timestamp uncertainty. Folder colors use the folder's own timestamp; its contents can differ. Growth colors require a comparison view."));
        return selector;
    }
    public void SetView(AnalysisColorDimension dimension, DateTimeOffset observed)
    {
        if (_initialized && _dimension == dimension && _observed == observed) return;
        _dimension = dimension; _observed = observed; _initialized = true; Rebuild();
    }
    private void Rebuild()
    {
        if (!_initialized) return;
        var p = ThemeManager.Current; Color panel = AnalysisChartPalette.Panel(p), text = AnalysisChartPalette.On(panel, Color.Parse(p.Text));
        Background = new SolidColorBrush(panel); Foreground = new SolidColorBrush(text);
        string title = "Colors: " + AnalysisChartColors.Title(_dimension) + (_dimension == AnalysisColorDimension.ModifiedAge ?
            " · captured " + _observed.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) : "");
        Header = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(this, title + ". Expand or collapse the category legend.");
        var entries = new WrapPanel { ItemSpacing = 14, LineSpacing = 6, Margin = new Thickness(4) };
        foreach (var category in AnalysisChartColors.Legend(_dimension))
        {
            var paint = AnalysisChartPalette.For(category.Key, p);
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            entry.Children.Add(new Border { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(paint.Fill), BorderBrush = new SolidColorBrush(paint.Border), BorderThickness = new Thickness(1) });
            entry.Children.Add(new TextBlock { Text = category.Label, Foreground = new SolidColorBrush(text), VerticalAlignment = VerticalAlignment.Center });
            AutomationProperties.SetName(entry, category.Label); entries.Children.Add(entry);
        }
        var content = new StackPanel { Spacing = 4 };
        if (_dimension == AnalysisColorDimension.ModifiedAge)
            content.Children.Add(new TextBlock { Text = "Entry timestamps; folder contents can have different ages. Day = 24 hours.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(entries);
        Content = new ScrollViewer { Content = content, MaxHeight = 90, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
