using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private async Task CheckSetupTypographyAsync(DiskSpaceWindow window)
    {
        var location = Find<TextBox>(window, "Analysis location 1");
        var remove = Find<Button>(window, "Remove analysis location 1");
        var allocation = Find<ComboBox>(window, "Allocation components");
        string? locationText = location.Text;
        int component = allocation.SelectedIndex;
        string themeBefore = ThemeManager.RequestedName;
        double sizeBefore = window.FontSize;
        try
        {
            window.FontSize = 28;
            location.Text = locationText + "/Projets liés/Archive_2026_à_vérifier/Very long folder name for a compact location field";
            allocation.SelectedIndex = 1;
            foreach (string theme in ThemeManager.Names)
            {
                ThemeManager.Apply(theme);
                await RevealLayoutControlAsync(window, location, $"{theme} setup location");
                if (location.Bounds.Width < 160 || BoundsInWindow(window, location).Intersects(BoundsInWindow(window, remove)))
                    throw new InvalidOperationException($"{theme} squeezed the long location editor or overlapped its Remove button.");
                await RevealLayoutControlAsync(window, allocation, $"{theme} allocation selector");
                var label = allocation.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible &&
                    text.Text?.StartsWith("Detailed allocation", StringComparison.Ordinal) == true);
                if (label.TextLayout.Height > label.Bounds.Height + 1 || label.TextLayout.Width > label.Bounds.Width + 1)
                    throw new InvalidOperationException($"{theme} clipped the selected allocation option ({label.Bounds.Size}).");
                CheckVisualCaptions(window);
                RenderNativeFrame($"analysis-reading-layout-setup-{theme}.png");
            }
        }
        finally
        {
            ThemeManager.Apply(themeBefore);
            window.FontSize = sizeBefore;
            allocation.SelectedIndex = component;
            location.Text = locationText;
            window.UpdateLayout();
            foreach (var scroll in location.GetVisualAncestors().OfType<ScrollViewer>()) scroll.Offset = default;
        }
    }

    private async Task<IReadOnlyList<ScratchThemeFrame>> CheckWideTypographyAsync(DiskSpaceWindow window, DiskSpaceViewModel model)
    {
        var original = new Size(window.Width, window.Height);
        double font = window.FontSize;
        var mode = Find<ComboBox>(window, "Chart view");
        int initialMode = mode.SelectedIndex;
        try
        {
            window.Width = 1060;
            window.Height = 720;
            window.FontSize = 14;
            await ReadyAsync(() => window.ClientSize.Width >= 900 && window.ClientSize.Height >= 580);
            mode.SelectedIndex = 0;
            var treemap = await CheckResultThemesAsync(window, model, warning: false, "layout-treemap");
            mode.SelectedIndex = 1;
            var sunburst = await CheckResultThemesAsync(window, model, warning: false, "layout-sunburst");
            return [.. treemap, .. sunburst];
        }
        finally
        {
            window.Width = original.Width;
            window.Height = original.Height;
            window.FontSize = font;
            mode.SelectedIndex = initialMode;
            await ReadyAsync(() => window.ClientSize.Width < 640);
        }
    }

    private async Task<IReadOnlyList<string>> CheckDialogTypographyAsync(DiskSpaceWindow window)
    {
        string themeBefore = ThemeManager.RequestedName;
        double widthBefore = window.Width, sizeBefore = window.FontSize;
        var themes = new List<string>();
        try
        {
            window.FontSize = 28;
            var review = FindAnalysisButton(window, "Review cleanup")!;
            await RevealLayoutControlAsync(window, review, "Review cleanup");
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ReadyAsync(() => FindAnalysisButton(window, "Cancel / Keep files") is { IsFocused: true });
            var keep = FindAnalysisButton(window, "Cancel / Keep files")!;
            var details = window.GetVisualDescendants().OfType<Expander>().Single(expander => expander.IsEffectivelyVisible &&
                Equals(expander.Header, "Measurement and entry details"));
            details.IsExpanded = true;
            foreach (string theme in ThemeManager.Names)
            {
                ThemeManager.Apply(theme);
                window.Width = themes.Count % 2 == 0 ? widthBefore : widthBefore + 70;
                window.UpdateLayout();
                await ReadyAsync(() => FullyVisible(window, keep) && keep.IsFocused, TimeSpan.FromSeconds(5));
                var footer = keep.GetVisualAncestors().OfType<Border>().Single(border => border.Classes.Contains("dialogFooter"));
                var unavailable = footer.GetVisualDescendants().OfType<Button>().Single(button => !ReferenceEquals(button, keep));
                if (!keep.IsDefault || !keep.IsCancel || unavailable.IsEnabled || !details.IsExpanded ||
                    !FullyVisible(window, unavailable) || BoundsInWindow(window, keep).Intersects(BoundsInWindow(window, unavailable)) || !Hits(window, keep))
                    throw new InvalidOperationException($"{theme} clipped or overlapped cleanup actions, lost expanded details or changed the safe default.");
                CheckVisualCaptions(window);
                var close = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Name == "DialogCloseButton");
                if (!FullyVisible(window, close) || !close.GetVisualDescendants().OfType<PathIcon>().Any())
                    throw new InvalidOperationException($"{theme} hid the cleanup dialog's close icon.");
                await RecipeFrameAsync($"analysis-reading-layout-cleanup-{theme}.png", true, _indexedLifetime.Token);
                themes.Add(theme);
            }
            keep.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ReadyAsync(() => FindAnalysisButton(window, "Cancel / Keep files") is null);
            return themes;
        }
        finally
        {
            if (FindAnalysisButton(window, "Cancel / Keep files") is { IsEnabled: true } close)
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ThemeManager.Apply(themeBefore);
            window.Width = widthBefore;
            window.FontSize = sizeBefore;
            await ReadyAsync(() => Math.Abs(window.ClientSize.Width - widthBefore) <= 2);
        }
    }

    private async Task RevealLayoutControlAsync(DiskSpaceWindow window, Control control, string label)
    {
        if (!control.Focus(NavigationMethod.Tab)) throw new InvalidOperationException($"Cannot focus {label}.");
        control.BringIntoView();
        try { await ReadyAsync(() => control.IsFocused && FullyVisible(window, control) && Hits(window, control), TimeSpan.FromSeconds(5)); }
        catch (TimeoutException error)
        {
            RenderNativeFrame("analysis-reading-layout-control-failure.png");
            string scrolls = string.Join("; ", control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll =>
                $"{AutomationProperties.GetName(scroll) ?? scroll.Name} viewport {scroll.Bounds.Size} offset {scroll.Offset}, control at {control.TranslatePoint(default, scroll)}"));
            throw new InvalidOperationException($"{label} is clipped or has an obscured hit area at {BoundsInWindow(window, control)}. " +
                $"Focused {control.IsFocused}, hit {Hits(window, control)}. {scrolls}", error);
        }
    }

    private static T Find<T>(DiskSpaceWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);

    private static void CheckVisualCaptions(DiskSpaceWindow window)
    {
        if (window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
            text.Text?.StartsWith("Avalonia.Controls.", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("A control rendered a type name in place of its content.");
    }

    private static Rect BoundsInWindow(DiskSpaceWindow window, Control control) =>
        new(control.TranslatePoint(default, window) ?? new Point(-1000, -1000), control.Bounds.Size);

    private static bool FullyVisible(DiskSpaceWindow window, Control control) =>
        control.Bounds.Height > 0 && new Rect(window.ClientSize).Inflate(1).Contains(BoundsInWindow(window, control)) &&
        control.GetVisualAncestors().OfType<ScrollViewer>().All(scroll => control.TranslatePoint(default, scroll) is { } top &&
            new Rect(scroll.Bounds.Size).Inflate(1).Contains(new Rect(top, control.Bounds.Size)));

    private static bool Hits(DiskSpaceWindow window, Control control) =>
        window.InputHitTest(BoundsInWindow(window, control).Center) is Visual hit && (ReferenceEquals(hit, control) || control.IsVisualAncestorOf(hit));
}
