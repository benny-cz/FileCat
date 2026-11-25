using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.App.Tests;

/// <summary>
/// I663: the panel numbers take a new theme's colors as soon as it is chosen, in every panel: the active panel's number in
/// the accent with its text, the others in the header's muted colors. rc.2 kept the startup theme's colors until the active
/// panel changed or FileCat restarted, High Contrast included.
/// </summary>
[Collection("Shared theme state")]
public sealed class PanelNumberThemeTests
{
    [AvaloniaFact]
    public async Task Panel_numbers_take_a_new_themes_colors_at_once_in_the_active_and_the_other_panel()
    {
        var styles = ThemeAudit.ProductStyles();
        Application.Current!.Styles.Add(styles);
        string before = ThemeManager.RequestedName;
        // The record's startup theme (System, which is Classic on that machine), in place when the panels are built.
        ThemeManager.Apply("Classic");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.Theme = "Classic";
            var panels = vm.Workspace.Panels;
            Assert.Equal(2, panels.Count);
            vm.Workspace.Activate(panels[0]);
            AssertNumbers("startup");

            // View → Theme… → Steampunk: the numbers change while the picker previews it, and change back on Cancel.
            var picking = vm.ChooseThemeAsync();
            var list = await Picker(window);
            list.SelectedIndex = ThemeManager.Names.ToList().IndexOf("Steampunk");
            Assert.Equal("Steampunk", ThemeManager.Current.Name);
            AssertNumbers("previewed in the theme picker");
            await Click(window, "Cancel");
            await picking;
            Assert.Equal("Classic", ThemeManager.Current.Name);
            AssertNumbers("after Cancel");

            // Every theme, High Contrast included, with either panel active.
            foreach (var panel in new[] { panels[1], panels[0] })
            {
                vm.Workspace.Activate(panel);
                Assert.True(panel.IsActive);
                foreach (var palette in ThemePalette.All)
                {
                    ThemeManager.Apply(palette.Name);
                    Assert.Equal(palette.Name, ThemeManager.Current.Name);
                    AssertNumbers("after choosing it");
                }
            }
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
            ThemeManager.Apply(before);
            Application.Current!.Styles.Remove(styles);
        }

        void AssertNumbers(string when)
        {
            var palette = ThemeManager.Current;
            foreach (var panel in vm.Workspace.Panels)
            {
                var view = window.GetVisualDescendants().OfType<PanelView>().Single(v => ReferenceEquals(v.DataContext, panel));
                var badge = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NumberBadge");
                var number = Assert.IsType<TextBlock>(badge.Child);
                Assert.Equal(panel.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), number.Text);
                var (fill, text) = panel.IsActive
                    ? (Color.Parse(palette.ActiveAccent), Color.Parse(ThemeManager.OnColor(palette.ActiveAccent)))
                    : (Color.Parse(palette.Header), Color.Parse(palette.TextMuted));
                string which = $"{palette.Name}, {when}: panel {panel.Number} ({(panel.IsActive ? "active" : "not active")})";
                Assert.True(fill == (badge.Background as ISolidColorBrush)?.Color,
                    $"{which}: the number's fill is {(badge.Background as ISolidColorBrush)?.Color}, not {fill}.");
                Assert.True(text == (number.Foreground as ISolidColorBrush)?.Color,
                    $"{which}: the number's text is {(number.Foreground as ISolidColorBrush)?.Color}, not {text}.");
            }
        }
    }

    private static async Task<ListBox> Picker(Window window)
    {
        for (int i = 0; i < 250; i++)
        {
            if (window.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(l => Avalonia.Automation.AutomationProperties.GetName(l) == "Themes") is { } list)
                return list;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("The theme picker did not open.");
    }

    private static async Task Click(Window window, string text)
    {
        var ct = TestContext.Current.CancellationToken;
        for (int i = 0; i < 250 && !window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == text && b.IsEffectivelyEnabled); i++)
            await Task.Delay(20, ct);
        window.GetVisualDescendants().OfType<Button>().Last(b => b.Content as string == text && b.IsEffectivelyVisible)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
