using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// The theme picker (View → Theme…): moving through the list puts each theme on FileCat itself at once, with a line
    /// about it; Keep saves the choice and Cancel (Esc) goes back to the theme in use.
    /// </summary>
    public async Task ChooseThemeAsync()
    {
        var s = Services.Settings;
        string original = s.Theme;
        bool originalAnimations = s.ThemeAnimations;
        var names = ThemeManager.Names;
        var list = new ListBox { Classes = { "choices" }, MinWidth = 520, MaxHeight = 420 };
        Avalonia.Automation.AutomationProperties.SetName(list, "Themes");
        foreach (var name in names)
        {
            list.Items.Add(new ListBoxItem
            {
                Tag = name,
                Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = ThemeManager.DisplayName(name), FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = ThemeManager.Description(name), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 500 },
                    },
                },
            });
        }
        list.SelectedIndex = Math.Max(0, names.ToList().IndexOf(original));
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: string chosen }) return;
            ThemeManager.Apply(chosen);
            Services.Icons.ClearCache();
        };
        var animations = new CheckBox { Content = "Animate theme light, edges, and subtle glitches", IsChecked = originalAnimations };
        animations.IsCheckedChanged += (_, _) => ThemeAnimation.SetAllowed(animations.IsChecked == true);
        var body = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(new TextBlock { Text = "Arrow keys try each theme on FileCat itself. Enter keeps it; Esc goes back.", Classes = { "muted" } });
        body.Children.Add(list);
        body.Children.Add(animations);
        if (ThemeAnimation.SystemPrefersReducedMotion())
            body.Children.Add(new TextBlock { Text = "The system asks for reduced motion, so animated themes stand still.", Classes = { "muted" } });

        var answer = await Dialogs.ShowCustomAsync("Theme", body, [new DialogButton("Cancel", false, IsCancel: true), new DialogButton("Keep", true, IsDefault: true)], list);
        if (answer is true && list.SelectedItem is ListBoxItem { Tag: string kept })
        {
            s.Theme = kept;
            s.ThemeAnimations = animations.IsChecked == true;
            Services.SaveSettings();
            Notify($"Theme: {ThemeManager.DisplayName(kept)}.");
            return;
        }
        ThemeManager.Apply(original);
        ThemeAnimation.SetAllowed(originalAnimations);
        Services.Icons.ClearCache();
    }
}
