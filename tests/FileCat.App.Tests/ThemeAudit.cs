using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FileCat.App.Services;

namespace FileCat.App.Tests;

/// <summary>
/// What a dialog would draw in the active theme, read from its controls' effective brushes (the App tests draw nothing):
/// text against the surfaces painted under it (WCAG AA: 4.5:1, 3:1 for large text), selection and input boundaries
/// (3:1), and whether each color comes from the theme. Theme colors are FileCat's palette tokens and Fluent's resources
/// for the active light or dark variant; a literal, or the operating system's accent that Fluent would otherwise carry into
/// every palette, is reported. Custom-drawn surfaces (the theme backdrops, the file list) are outside what this reads.
/// </summary>
internal static class ThemeAudit
{
    internal static StyleInclude ProductStyles() => new(new Uri("avares://FileCat/"))
    {
        Source = new Uri("avares://FileCat/Themes/Styles.axaml"),
    };

    /// <summary>The colors the active theme supplies, by RGB, and the system accent's shades among Fluent's.</summary>
    internal sealed record ThemeColors(HashSet<uint> Tokens, HashSet<uint> Fluent, HashSet<uint> Accent)
    {
        internal bool FromTheme(Color color) => Tokens.Contains(Rgb(color)) || Fluent.Contains(Rgb(color)) && !Accent.Contains(Rgb(color));
    }

    internal static uint Rgb(Color color) => color.ToUInt32() & 0xFFFFFF;

    internal static ThemeColors Colors()
    {
        var app = Application.Current!;
        var variant = app.ActualThemeVariant;
        var tokens = new HashSet<uint>();
        var fluent = new HashSet<uint>();
        var accent = new HashSet<uint>();
        // FileCat's palette: the dictionary ThemeManager merged into the application (it names its theme).
        foreach (var dictionary in app.Resources.MergedDictionaries.OfType<IResourceDictionary>().Where(d => d.ContainsKey("FcThemeName")))
            foreach (var key in dictionary.Keys)
                if (dictionary.TryGetResource(key, variant, out var value) && ColorOf(value) is { } color) tokens.Add(Rgb(color));
        // Fluent's resources as the application resolves them now (FileCat's overrides included).
        var keys = new HashSet<object>();
        foreach (var provider in app.Styles)
        {
            if (provider is Styles styles) Keys(styles.Resources, keys);
            else if (provider is StyleBase style) Keys(style.Resources, keys);
        }
        foreach (var key in keys)
            if (app.TryGetResource(key, variant, out var value) && ColorOf(value) is { } color) fluent.Add(Rgb(color));
        foreach (string key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                     "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
            if (app.TryGetResource(key, variant, out var value) && ColorOf(value) is { } color && !tokens.Contains(Rgb(color))) accent.Add(Rgb(color));
        return new ThemeColors(tokens, fluent, accent);

        static void Keys(IResourceDictionary dictionary, HashSet<object> into)
        {
            foreach (var key in dictionary.Keys) into.Add(key);
            foreach (var merged in dictionary.MergedDictionaries.OfType<IResourceDictionary>()) Keys(merged, into);
            foreach (var (_, themed) in dictionary.ThemeDictionaries)
                if (themed is IResourceDictionary inner) Keys(inner, into);
        }
    }

    private static Color? ColorOf(object? value) => value switch
    {
        Color color => color,
        ISolidColorBrush brush => brush.Color,
        _ => null,
    };

    internal static Color Token(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) && ColorOf(value) is { } color
            ? color : throw new KeyNotFoundException(key);

    /// <summary>Everything wrong with how <paramref name="root"/> (a dialog card) and its open popups read in the active theme.</summary>
    internal static List<string> Problems(Visual root, string label)
    {
        var colors = Colors();
        var problems = new List<string>();
        var roots = new List<Visual> { root };
        // A combo box's list and other popups render in their own top level; they belong to the dialog.
        roots.AddRange(root.GetVisualDescendants().OfType<Popup>().Where(popup => popup.IsOpen && popup.Child is not null)
            .Select(popup => (Visual)popup.Child!));
        foreach (var scope in roots)
        {
            foreach (var visual in scope.GetVisualDescendants().Prepend(scope))
            {
                if (visual is not Control control || !control.IsEffectivelyVisible || Opacity(control) <= 0.01 ||
                    control.Bounds.Width <= 0 || control.Bounds.Height <= 0) continue;
                switch (control)
                {
                    case TextBlock text when control.IsEffectivelyEnabled:
                        CheckText(text, colors, label, problems);
                        break;
                    case TextPresenter presenter when control.IsEffectivelyEnabled:
                        CheckPresenter(presenter, colors, label, problems);
                        break;
                }
                CheckFill(control, colors, label, problems);
                switch (control)
                {
                    case ListBoxItem { IsSelected: true } item when control.IsEffectivelyEnabled:
                        CheckSelection(item, label, problems);
                        break;
                    case ComboBoxItem { IsSelected: true } item when control.IsEffectivelyEnabled:
                        CheckSelection(item, label, problems);
                        break;
                    case TextBox or ComboBox when control.IsEffectivelyEnabled && control.TemplatedParent is null:
                        CheckBoundary(control, label, problems);
                        break;
                }
            }
        }
        return problems;
    }

    /// <summary>The visible color(s) behind <paramref name="target"/>'s area: each layer painted under it, outermost first.</summary>
    internal static List<Color> Behind(Visual target, bool includeSelf = false)
    {
        var area = new Rect(target.Bounds.Size);
        var under = new List<Color> { Token("FcWindow") };
        var chain = target.GetSelfAndVisualAncestors().Reverse().ToList();
        if (!includeSelf) chain.RemoveAt(chain.Count - 1);
        for (int index = 0; index < chain.Count; index++)
        {
            var node = chain[index];
            under = Paint(under, Fill(node), node, target, area);
            // Siblings painted before the next link and covering the target: a text box's border element, a combo box's
            // background and focus highlight, a list item's selection.
            var next = index + 1 < chain.Count ? chain[index + 1] : includeSelf ? null : target;
            if (next is null) continue;
            foreach (var sibling in node.GetVisualChildren().OrderBy(child => child.ZIndex).TakeWhile(child => !ReferenceEquals(child, next)))
            {
                if (sibling is not Control { IsEffectivelyVisible: true } control || Fill(control) is null) continue;
                if (target.TranslatePoint(default, control) is not { } offset) continue;
                var covered = new Rect(offset, area.Size);
                if (!new Rect(control.Bounds.Size).Inflate(0.5).Contains(covered)) continue;
                under = Paint(under, Fill(control), control, target, area);
            }
        }
        return under;
    }

    private static IBrush? Fill(Visual node) => node switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        ContentPresenter presenter => presenter.Background,
        TextBlock text => text.Background,
        _ => null,
    };

    private static List<Color> Paint(List<Color> under, IBrush? brush, Visual layer, Visual target, Rect area)
    {
        if (brush is null) return under;
        double opacity = Opacity(layer) * brush.Opacity;
        var inks = Inks(brush, layer, target, area).Select(color => WithAlpha(color, color.A * opacity)).Where(color => color.A > 0).ToList();
        if (inks.Count == 0) return under;
        return under.SelectMany(color => inks.Select(ink => ThemeManager.Opaque(ink, color))).Distinct().ToList();
    }

    /// <summary>A brush's colors over <paramref name="area"/> of <paramref name="target"/>: a gradient sampled at its corners.</summary>
    private static IEnumerable<Color> Inks(IBrush brush, Visual layer, Visual target, Rect area)
    {
        switch (brush)
        {
            case ISolidColorBrush solid:
                yield return solid.Color;
                break;
            case ILinearGradientBrush linear when linear.GradientStops.Count > 0:
                var size = layer.Bounds.Size;
                var start = linear.StartPoint.ToPixels(size);
                var end = linear.EndPoint.ToPixels(size);
                var axis = end - start;
                double length = axis.X * axis.X + axis.Y * axis.Y;
                foreach (var corner in new[] { area.TopLeft, area.TopRight, area.BottomLeft, area.BottomRight, area.Center })
                {
                    if (target.TranslatePoint(corner, layer) is not { } point) continue;
                    double t = length <= 0 ? 0 : Math.Clamp(((point.X - start.X) * axis.X + (point.Y - start.Y) * axis.Y) / length, 0, 1);
                    yield return Sample(linear.GradientStops, t);
                }
                break;
            case IGradientBrush gradient:
                foreach (var stop in gradient.GradientStops) yield return stop.Color;
                break;
        }
    }

    private static Color Sample(IReadOnlyList<IGradientStop> stops, double t)
    {
        var ordered = stops.OrderBy(stop => stop.Offset).ToList();
        if (t <= ordered[0].Offset) return ordered[0].Color;
        for (int index = 1; index < ordered.Count; index++)
        {
            if (t > ordered[index].Offset) continue;
            var (a, b) = (ordered[index - 1], ordered[index]);
            double f = b.Offset - a.Offset <= 0 ? 1 : (t - a.Offset) / (b.Offset - a.Offset);
            byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * f);
            return Color.FromArgb(Mix(a.Color.A, b.Color.A), Mix(a.Color.R, b.Color.R), Mix(a.Color.G, b.Color.G), Mix(a.Color.B, b.Color.B));
        }
        return ordered[^1].Color;
    }

    internal static double Opacity(Visual visual) => visual.GetSelfAndVisualAncestors().Aggregate(1.0, (product, node) => product * node.Opacity);

    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 255)), color.R, color.G, color.B);

    internal static double Worst(IEnumerable<Color> inks, IReadOnlyList<Color> under) =>
        inks.SelectMany(ink => under.Select(fill => ThemeManager.Contrast(ThemeManager.Opaque(ink, fill), fill))).DefaultIfEmpty(21).Min();

    private static bool Large(double size, FontWeight weight) => size >= 24 || size >= 18.66 && weight >= FontWeight.Bold;

    private static void CheckText(TextBlock text, ThemeColors colors, string label, List<string> problems)
    {
        var under = Behind(text, includeSelf: text.Background is not null);
        double opacity = Opacity(text);
        var runs = text.Inlines is { Count: > 0 } inlines
            ? inlines.OfType<Run>().Where(run => !string.IsNullOrWhiteSpace(run.Text))
                .Select(run => (run.Text!, run.Foreground ?? text.Foreground, run.FontSize, run.FontWeight)).ToList()
            : string.IsNullOrWhiteSpace(text.Text) ? [] : [(text.Text!, text.Foreground, text.FontSize, text.FontWeight)];
        foreach (var (words, brush, size, weight) in runs)
        {
            if (brush is null) { problems.Add($"{label}: \"{Short(words)}\" has no foreground"); continue; }
            var inks = Inks(brush, text, text, new Rect(text.Bounds.Size)).Select(color => WithAlpha(color, color.A * brush.Opacity * opacity)).ToList();
            double ratio = Worst(inks, under);
            if (ratio < (Large(size, weight) ? 3 : 4.5))
                problems.Add($"{label}: \"{Short(words)}\" ({Describe(text)}) is {ratio:0.00}:1 ({string.Join("/", inks)} on {string.Join("/", under.Take(4))})");
            var stops = brush is IGradientBrush gradient ? gradient.GradientStops.Select(stop => stop.Color) : Inks(brush, text, text, new Rect(text.Bounds.Size));
            foreach (var color in stops)
                if (color.A > 0 && !colors.FromTheme(color))
                    problems.Add($"{label}: \"{Short(words)}\" ({Describe(text)}) is drawn in {Origin(color, colors)}");
        }
    }

    private static void CheckPresenter(TextPresenter presenter, ThemeColors colors, string label, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(presenter.Text) || presenter.Foreground is not { } brush) return;
        var under = Behind(presenter);
        var ink = Inks(brush, presenter, presenter, new Rect(presenter.Bounds.Size)).Select(color => WithAlpha(color, color.A * brush.Opacity * Opacity(presenter))).ToList();
        double ratio = Worst(ink, under);
        if (ratio < 4.5)
            problems.Add($"{label}: field text \"{Short(presenter.Text)}\" is {ratio:0.00}:1 ({string.Join("/", ink)} on {string.Join("/", under.Take(4))})");
        foreach (var color in ink) if (!colors.FromTheme(color)) problems.Add($"{label}: field text \"{Short(presenter.Text)}\" is drawn in {Origin(color, colors)}");
        if (presenter.SelectionStart == presenter.SelectionEnd) return;
        // Selected text: the highlight over the field, and the selected characters over the highlight.
        if (presenter.SelectionBrush is ISolidColorBrush highlight)
        {
            var fill = under.Select(color => ThemeManager.Opaque(WithAlpha(highlight.Color, highlight.Color.A * highlight.Opacity), color)).ToList();
            if (!colors.FromTheme(highlight.Color)) problems.Add($"{label}: selected text in \"{Short(presenter.Text)}\" is highlighted in {Origin(highlight.Color, colors)}");
            var selected = presenter.SelectionForegroundBrush as ISolidColorBrush ?? brush as ISolidColorBrush;
            if (selected is not null)
            {
                double selectedRatio = Worst([WithAlpha(selected.Color, selected.Color.A * selected.Opacity)], fill);
                if (selectedRatio < 4.5)
                    problems.Add($"{label}: selected text in \"{Short(presenter.Text)}\" is {selectedRatio:0.00}:1 ({selected.Color} on {string.Join("/", fill.Take(4))})");
                if (!colors.FromTheme(selected.Color)) problems.Add($"{label}: selected text in \"{Short(presenter.Text)}\" is drawn in {Origin(selected.Color, colors)}");
            }
        }
    }

    /// <summary>
    /// Fills, edges and glyphs take theme colors (a gradient by its stops). An opaque surface under text is one of FileCat's
    /// own, or plain black or white: Fluent's translucent tints over it follow the palette, an opaque Fluent gray inside a
    /// blue, purple or black dialog does not. (Fluent's neutral scroll bar thumbs carry no text and read at 3:1 or more.)
    /// </summary>
    private static void CheckFill(Control control, ThemeColors colors, string label, List<string> problems)
    {
        var paints = new List<(string Role, IBrush? Brush)> { ("fill", Fill(control)), ("edge", Edge(control)) };
        if (control is Avalonia.Controls.Shapes.Shape shape) paints.AddRange([("glyph", shape.Fill), ("glyph", shape.Stroke)]);
        if (control is PathIcon icon) paints.Add(("glyph", icon.Foreground));
        bool underText = control.GetVisualDescendants().Any(child => child is TextBlock or TextPresenter && ((Visual)child).IsEffectivelyVisible);
        foreach (var (role, brush) in paints)
        {
            if (brush is null || brush.Opacity <= 0) continue;
            if (role == "edge" && control is Border { BorderThickness: { Left: 0, Top: 0, Right: 0, Bottom: 0 } }) continue;
            if (role == "edge" && control is ContentPresenter { BorderThickness: { Left: 0, Top: 0, Right: 0, Bottom: 0 } }) continue;
            var stops = brush switch
            {
                ISolidColorBrush solid => [solid.Color],
                IGradientBrush gradient => gradient.GradientStops.Select(stop => stop.Color).ToList(),
                _ => new List<Color>(),
            };
            foreach (var color in stops)
            {
                if (color.A == 0) continue;
                if (!colors.FromTheme(color))
                    problems.Add($"{label}: {role} of {Describe(control)} is {Origin(color, colors)}");
                else if (role == "fill" && underText && color.A * brush.Opacity >= 240 && !colors.Tokens.Contains(Rgb(color)) && Rgb(color) is not (0 or 0xFFFFFF))
                    problems.Add($"{label}: fill of {Describe(control)} is {color}, an opaque surface that is not one of the theme's");
            }
        }
        // A glyph that carries meaning on its own (the close button's cross) is a graphic: 3:1 (WCAG 1.4.11).
        if (control is PathIcon { Foreground: ISolidColorBrush ink } && control.IsEffectivelyEnabled)
        {
            double ratio = Worst([WithAlpha(ink.Color, ink.Color.A * ink.Opacity * Opacity(control))], Behind(control));
            if (ratio < 3) problems.Add($"{label}: the glyph {Describe(control)} is only {ratio:0.00}:1 ({ink.Color} on {string.Join("/", Behind(control).Take(2))})");
        }
    }

    private static IBrush? Edge(Visual node) => node switch
    {
        Border border => border.BorderBrush,
        ContentPresenter presenter => presenter.BorderBrush,
        _ => null,
    };

    /// <summary>
    /// A selected row stands out from the rows around it by 3:1 (WCAG 1.4.11): by its fill, or by an edge drawn around it.
    /// </summary>
    private static void CheckSelection(ContentControl item, string label, List<string> problems)
    {
        var presenter = item.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(part => part.Name == "PART_ContentPresenter");
        if (presenter is null) return;
        var around = Behind(item);
        var selected = Behind(presenter, includeSelf: true);
        double fill = selected.SelectMany(a => around.Select(b => ThemeManager.Contrast(a, b))).Min();
        double edge = 1;
        if (presenter.BorderBrush is ISolidColorBrush border && presenter.BorderThickness.Left > 0)
        {
            var ink = WithAlpha(border.Color, border.Color.A * border.Opacity * Opacity(presenter));
            edge = Math.Max(Worst([ink], around), Worst([ink], selected));
        }
        if (Math.Max(fill, edge) < 3)
            problems.Add($"{label}: the selected row \"{Short(item.Content?.ToString() ?? item.DataContext?.ToString() ?? "")}\" stands out by only " +
                $"{Math.Max(fill, edge):0.00}:1 ({string.Join("/", selected.Take(2))} among {string.Join("/", around.Take(2))})");
    }

    /// <summary>A field shows where it is, 3:1 against what surrounds it: by its edge or by its own fill.</summary>
    private static void CheckBoundary(Control field, string label, List<string> problems)
    {
        var frame = field.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name is "PART_BorderElement" or "Background");
        if (frame is null) return;
        var around = Behind(field);
        double best = 1;
        if (frame.Background is ISolidColorBrush fillBrush)
        {
            var fill = Behind(frame, includeSelf: true);
            best = fill.SelectMany(a => around.Select(b => ThemeManager.Contrast(a, b))).Min();
        }
        if (frame.BorderBrush is ISolidColorBrush edge && frame.BorderThickness.Left > 0)
            best = Math.Max(best, Worst([WithAlpha(edge.Color, edge.Color.A * edge.Opacity * Opacity(frame))], around));
        if (best < 3)
            problems.Add($"{label}: {Describe(field)} shows its boundary by only {best:0.00}:1 against {string.Join("/", around.Take(2))}");
    }

    /// <summary>
    /// Keyboard focus on one control of each kind shows an indicator 3:1 against what surrounds the control: a text box
    /// its focused edge, the others Fluent's focus ring. Each indicator's colors come from the theme.
    /// </summary>
    internal static List<string> FocusProblems(Visual card, string label)
    {
        var colors = Colors();
        var problems = new List<string>();
        var focusManager = TopLevel.GetTopLevel(card)?.FocusManager;
        var kinds = new HashSet<string>();
        var candidates = card.GetVisualDescendants().OfType<Control>()
            .Where(control => control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.TemplatedParent is null &&
                control is Button or TextBox or CheckBox or RadioButton or ComboBox or ListBox or TabItem)
            .Where(control => kinds.Add(control.GetType().Name + string.Concat(control.Classes.Where(name => !name.StartsWith(':')).Select(name => "." + name))))
            .ToList();
        foreach (var candidate in candidates)
        {
            if (!candidate.Focus(Avalonia.Input.NavigationMethod.Tab)) continue;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var focused = focusManager?.GetFocusedElement() as Control ?? candidate;
            var around = Behind(focused);
            var inks = new List<Color>();
            if (focused is TextBox box)
            {
                if (box.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == "PART_BorderElement") is { BorderBrush: ISolidColorBrush edge } frame &&
                    frame.BorderThickness.Left > 0)
                    inks.Add(WithAlpha(edge.Color, edge.Color.A * edge.Opacity * Opacity(frame)));
            }
            else if (focused is ComboBox combo &&
                combo.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == "HighlightBackground") is { IsEffectivelyVisible: true } highlight)
            {
                // Fluent marks a focused combo box by its highlight, edged, instead of a focus ring.
                if (highlight.BorderBrush is ISolidColorBrush edge && highlight.BorderThickness.Left > 0)
                    inks.Add(WithAlpha(edge.Color, edge.Color.A * edge.Opacity * Opacity(highlight)));
                if (highlight.Background is ISolidColorBrush fill)
                    inks.Add(WithAlpha(fill.Color, fill.Color.A * fill.Opacity * Opacity(highlight)));
            }
            else if (AdornerLayer.GetAdornerLayer(focused) is { } layer &&
                layer.Children.FirstOrDefault(child => ReferenceEquals(AdornerLayer.GetAdornedElement(child), focused)) is Control adorner)
            {
                foreach (var ring in adorner.GetVisualDescendants().OfType<Border>().Prepend(adorner as Border).OfType<Border>())
                    if (ring.BorderBrush is ISolidColorBrush stroke && ring.BorderThickness.Left > 0)
                        inks.Add(WithAlpha(stroke.Color, stroke.Color.A * stroke.Opacity * Opacity(ring)));
            }
            if (inks.Count == 0)
            {
                problems.Add($"{label}: keyboard focus on {Describe(focused)} shows no indicator");
                continue;
            }
            double best = inks.Max(ink => Worst([ink], around));
            if (best < 3)
                problems.Add($"{label}: the focus indicator of {Describe(focused)} is only {best:0.00}:1 ({string.Join("/", inks)} around {string.Join("/", around.Take(2))})");
            foreach (var ink in inks)
                if (ink.A > 0 && !colors.FromTheme(ink)) problems.Add($"{label}: the focus indicator of {Describe(focused)} is {Origin(ink, colors)}");
        }
        return problems;
    }

    internal static string Origin(Color color, ThemeColors colors) =>
        colors.Accent.Contains(Rgb(color)) ? $"the system accent {color}" : $"{color}, which the theme does not supply";

    internal static string Describe(Visual visual)
    {
        var control = visual as Control;
        string name = string.IsNullOrEmpty(control?.Name) ? "" : "#" + control!.Name;
        string owner = control?.TemplatedParent is Control parent ? " in " + parent.GetType().Name : "";
        string classes = control is null || control.Classes.Count == 0 ? "" : "." + string.Join(".", control.Classes);
        return visual.GetType().Name + name + classes + owner;
    }

    private static string Short(string text) => text.Length <= 60 ? text : text[..57] + "…";
}
