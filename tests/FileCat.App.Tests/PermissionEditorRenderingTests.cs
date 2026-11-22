using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Permissions;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// G-UX: the permission inspector and editors as they render in a dialog card, in each palette and density, with the
/// states that bring out their less common colors: a selected rule, an invalid draft's notice, the legacy-form note.
/// </summary>
public sealed class PermissionEditorRenderingTests
{
    private static WindowsDacl Dacl(int count) => new(WindowsDaclForm.Entries, 2, [.. Enumerable.Range(0, count).Select(index =>
        WindowsPermissionAclPatch.SimpleEntry(index % 3 == 0 ? WindowsAceKind.Deny : WindowsAceKind.Allow,
            new(PrincipalKind.WindowsSid, $"S-1-5-21-3623811015-3361044348-30300820-{1000 + index}", "local"), 0x120089))], []);

    private static WindowsSacl Sacl() => new(true, 2, new(0x10, 1, null),
        [WindowsPermissionAuditPatch.FileEntry(new(PrincipalKind.WindowsSid, "S-1-1-0", "local"), 1, WindowsPermissionAuditPatch.Success)], [], []);

    private static PermissionSnapshot Snapshot()
    {
        var subject = new PermissionSubject(Location.FileSystem(@"C:\fixture"), @"C:\fixture\a long folder name\report.txt",
            @"C:\fixture\a long folder name\report.txt", new(PermissionAuthorityKind.Local, "local", 0), PermissionObjectKind.File,
            PermissionLinkPolicy.NoFollow);
        var now = DateTimeOffset.UtcNow;
        return PermissionSnapshots.Create(subject, PermissionModel.WindowsDescriptor, null,
            PermissionSections.Owner | PermissionSections.Group | PermissionSections.Access | PermissionSections.Inheritance, now, "test") with
        {
            Owner = new(PermissionReadStatus.Present, "test", now, new Principal(PrincipalKind.WindowsSid, "S-1-5-32-544", "local")),
            Group = new(PermissionReadStatus.Present, "test", now, new Principal(PrincipalKind.WindowsSid, "S-1-5-18", "local")),
            Access = new(PermissionReadStatus.Present, "test", now, new PermissionAclValue(Dacl(12))),
            Inheritance = new(PermissionReadStatus.Present, "test", now, new WindowsAclControl(0x8004, 1)),
        };
    }

    private static PermissionSnapshot UnixSnapshot(PermissionModel model, PermissionAcl acl)
    {
        var subject = new PermissionSubject(Location.FileSystem("/fixture"), "/fixture/report.txt", "/fixture/report.txt",
            new(PermissionAuthorityKind.Local, "local", 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var now = DateTimeOffset.UtcNow;
        return PermissionSnapshots.Create(subject, model, null, PermissionSections.Mode | PermissionSections.Access, now, "test") with
        {
            Mode = new(PermissionReadStatus.Present, "test", now, (ushort)0x1a0),
            Access = new(PermissionReadStatus.Present, "test", now, new PermissionAclValue(acl)),
        };
    }

    private static PosixAcl Posix() => new([new(PosixAclTag.Owner, 6, null), new(PosixAclTag.NamedUser, 4, new(PrincipalKind.UnixUser, "65534", "local")),
        new(PosixAclTag.OwningGroup, 4, null), new(PosixAclTag.Mask, 4, null), new(PosixAclTag.Other, 0, null)], []);

    private static DarwinAcl Darwin() => new(0, [
        new(new(PrincipalKind.DarwinUuid, "ABCDEFAB-CDEF-ABCD-EFAB-CDEF0000000C", "local"), DarwinAceKind.Allow, 0, 2),
        new(new(PrincipalKind.DarwinUuid, "ABCDEFAB-CDEF-ABCD-EFAB-CDEF0000000C", "local"), DarwinAceKind.Deny, 0, 4)], []);

    /// <summary>The surfaces in their less common states: selected rule, invalid mask and octal, legacy note.</summary>
    private static (Window Window, WindowsAccessEditor Access, UnixPermissionModeEditor Mode) Open(double width)
    {
        var inspector = new PermissionInspectorView([("report.txt", Snapshot())], copy: _ => Task.CompletedTask);
        var access = new WindowsAccessEditor(Dacl(12), "local", copy: _ => Task.CompletedTask);
        var audit = new WindowsAuditEditor(Sacl(), "local");
        var mode = new UnixPermissionModeEditor(bit => bit == UnixFileMode.UserRead, groupClassLimit: false);
        var owner = new ComboBox { ItemsSource = new[] { "S-1-5-32-544", "S-1-5-21-3623811015-3361044348-30300820-1013" }, SelectedIndex = 1 };
        AutomationProperties.SetName(owner, "Assignable owner SID");
        var diff = new ListBox { ItemsPanel = PermissionInspectorView.AllRows(), ItemsSource = Enumerable.Range(1, 8).Select(index =>
            $"Entry {index} · Allow · S-1-5-21-3623811015-3361044348-30300820-{1000 + index} · 0x00120089 → 0x001301BF").ToArray(),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((value, _) => new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap }) };
        AutomationProperties.SetName(diff, "Reviewed changes");
        diff.SelectedIndex = 2;
        var posix = new PosixAccessEditor(UnixSnapshot(PermissionModel.PosixAcl, Posix()), copy: _ => Task.CompletedTask);
        posix.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.SetValue(TextBox.TextProperty, "x");
        var darwin = new DarwinAccessEditor(UnixSnapshot(PermissionModel.DarwinAcl, Darwin()), copy: _ => Task.CompletedTask);
        var body = new StackPanel { Spacing = 12, Children = { inspector, access, audit, mode, owner, diff, posix, darwin } };
        var window = new Window { Width = width, Height = 3000, Content = new DialogCard("Edit access ACL", body, null, 900) };
        // The headless application has Fluent only; the product's own styles render the dialog as the app does.
        window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://FileCat/"))
            { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        access.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Add an explicit entry")).IsChecked = true;
        access.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Windows access mask").Text = "zz";
        mode.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Permissions as octal").Text = "9";
        Dispatcher.UIThread.RunJobs();
        return (window, access, mode);
    }

    private static Color ColorOf(string key) =>
        Application.Current!.TryGetResource(key, null, out var value) && value is ISolidColorBrush brush
            ? brush.Color : throw new KeyNotFoundException(key);

    private static Color Solid(IBrush? brush, double opacity) => brush is ISolidColorBrush solid
        ? Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity * opacity), solid.Color.R, solid.Color.G, solid.Color.B)
        : Colors.Transparent;

    /// <summary>The visible color behind a visual: every ancestor's background, laid over the window's, outermost first.</summary>
    private static Color Behind(Visual visual)
    {
        var color = ColorOf("FcWindow");
        foreach (var node in visual.GetSelfAndVisualAncestors().Reverse())
        {
            // A templated control paints nothing itself: its template parts draw its Background, so count only them.
            IBrush? brush = node switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                ContentPresenter presenter => presenter.Background,
                _ => null
            };
            var fill = Solid(brush, Opacity(node));
            if (fill.A > 0) color = ThemeManager.Opaque(fill, color);
        }
        return color;
    }

    private static double Opacity(Visual visual) => visual.GetSelfAndVisualAncestors().Aggregate(1.0, (product, node) => product * node.Opacity);

    private static IEnumerable<string> Failures(Window window, string theme)
    {
        foreach (var text in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!text.IsEffectivelyVisible || !text.IsEffectivelyEnabled || string.IsNullOrWhiteSpace(text.Text) ||
                text.Bounds.Width <= 0 || text.Bounds.Height <= 0) continue;
            var fill = Behind(text);
            var ink = ThemeManager.Opaque(Solid(text.Foreground, Opacity(text)), fill);
            bool large = text.FontSize >= 18.66 || text.FontSize >= 14 && text.FontWeight >= FontWeight.Bold;
            double ratio = ThemeManager.Contrast(ink, fill);
            if (ratio < (large ? 3 : 4.5))
                yield return $"{theme}: \"{text.Text}\" ({text.Name}, opacity {Opacity(text):0.##}: " +
                    string.Join(",", text.GetSelfAndVisualAncestors().Where(node => node.Opacity < 1).Select(node => $"{node.GetType().Name}#{(node as Control)?.Name}={node.Opacity}")) +
                    $") is {ratio:0.00}:1 ({ink} on {fill})";
        }
    }

    [AvaloniaFact]
    public void Inspector_and_editor_text_meets_contrast_in_every_palette_and_density()
    {
        string theme = ThemeManager.RequestedName;
        try
        {
            var failures = new List<string>();
            foreach (string density in new[] { "Compact", "Comfortable" })
            {
                ReadabilityPolicy.ApplyDensity(density);
                foreach (var name in ThemeManager.Names)
                {
                    ThemeManager.Apply(name);
                    var (window, _, _) = Open(900);
                    try
                    {
                        // The checked texts include the rarer states, not only the default labels.
                        var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
                            .Select(text => text.Text ?? "").ToArray();
                        foreach (string expected in new[] { "Enter a numeric ID from 0 to 4294967294.",
                                     "Enter three or four octal digits (0–7), or use the permission checkboxes.", "Inheritance: enabled, legacy form" })
                            Assert.Contains(expected, shown);
                        failures.AddRange(Failures(window, $"{name}/{density}"));
                    }
                    finally { window.Close(); }
                }
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Distinct().Take(40)));
        }
        finally
        {
            ThemeManager.Apply(theme);
            ReadabilityPolicy.ApplyDensity("Compact");
        }
    }

    [AvaloniaFact]
    public void A_theme_and_density_change_keeps_the_invalid_draft_and_the_selected_rule()
    {
        string theme = ThemeManager.RequestedName;
        var (window, access, mode) = Open(900);
        try
        {
            // Choosing a rule restarts the add-entry draft by design, so choose it first and type the invalid mask after.
            var entries = access.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Ordered Windows access entries");
            entries.SelectedIndex = 4;
            Dispatcher.UIThread.RunJobs();
            var addEntry = access.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Add an explicit entry"));
            addEntry.IsChecked = true;
            access.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Windows access mask").Text = "zz";
            var kind = access.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "New Windows entry type");
            kind.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            int round = 0;
            foreach (var name in ThemeManager.Names.Where(name => name != ThemeManager.RequestedName).Take(3))
            {
                ThemeManager.Apply(name);
                ReadabilityPolicy.ApplyDensity(round++ % 2 == 0 ? "Comfortable" : "Compact");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("zz", access.GetVisualDescendants().OfType<TextBox>()
                    .Single(box => AutomationProperties.GetName(box) == "Windows access mask").Text);
                Assert.Equal(4, entries.SelectedIndex);
                Assert.True(kind.IsDropDownOpen, $"{name}: the open entry type list closed");
                // The hosting dialog shows this draft error; the draft itself must still carry it.
                Assert.Equal("Enter up to eight hexadecimal digits for the access mask.", access.ReadChange().Error);
                Assert.Equal("9", mode.GetVisualDescendants().OfType<TextBox>()
                    .Single(box => AutomationProperties.GetName(box) == "Permissions as octal").Text);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block =>
                    block.IsEffectivelyVisible && block.Text?.StartsWith("Enter three or four octal digits", StringComparison.Ordinal) == true);
            }
        }
        finally
        {
            window.Close();
            ThemeManager.Apply(theme);
            ReadabilityPolicy.ApplyDensity("Compact");
        }
    }

    [AvaloniaTheory]
    [InlineData(21)]
    [InlineData(28)]
    public async Task Large_text_scrolls_the_editor_body_vertically_and_reveals_its_last_field(double fontSize)
    {
        var fallback = new TextBox { Text = "fallback" };
        var host = new Grid();
        var window = new Window { Width = 640, Height = 420, FontSize = fontSize, Content = new Grid { Children = { fallback, host } } };
        window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://FileCat/"))
            { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
        window.Show();
        fallback.Focus();
        var dialogs = new OverlayDialogService(host, () => fallback);
        try
        {
            var access = new WindowsAccessEditor(Dacl(12), "local", copy: _ => Task.CompletedTask);
            var mode = new UnixPermissionModeEditor(bit => bit == UnixFileMode.UserRead, groupClassLimit: false);
            var body = new StackPanel { Spacing = 8, Children = { access, mode } };
            var shown = dialogs.ShowCustomAsync("Edit access ACL", body,
                [new("Cancel", "close", IsCancel: true), new("Apply", "apply", IsDefault: true) { IsAvailable = () => false }],
                resizable: true, behavior: new DialogBehavior { DismissOnBackdrop = false, ConfirmOnOptionEnter = false, ConfirmOnListItemEnter = false });
            Settle(window);
            var card = window.GetVisualDescendants().OfType<DialogCard>().Single();
            var scroll = card.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.Name == "DialogBodyScroll");
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "At large text the editor must scroll within the dialog.");
            Assert.InRange(scroll.Extent.Width, 1, scroll.Viewport.Width + 1);
            var last = mode.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Permissions as octal");
            Assert.True(last.Focus(Avalonia.Input.NavigationMethod.Tab));
            var viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single(presenter => ReferenceEquals(presenter.TemplatedParent, scroll));
            for (int attempt = 0; attempt < 300 && !Inside(last, viewport); attempt++)
            {
                Settle(window);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(Inside(last, viewport), "Keyboard focus must reveal the last field of the editor.");
            Assert.True(scroll.Offset.Y > 0);
            dialogs.CancelAll();
            await shown;
        }
        finally { dialogs.CancelAll(); Dispatcher.UIThread.RunJobs(); window.Close(); }

        static bool Inside(Control child, Control viewport) => child.TranslatePoint(default, viewport) is { } p &&
            p.X >= -0.6 && p.Y >= -0.6 && p.X + child.Bounds.Width <= viewport.Bounds.Width + 0.6 &&
            p.Y + child.Bounds.Height <= viewport.Bounds.Height + 0.6;
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void A_narrow_dialog_wraps_long_identities_and_many_rules_inside_its_width()
    {
        var (window, _, _) = Open(420);
        try
        {
            var card = window.GetVisualDescendants().OfType<DialogCard>().Single();
            var overflow = card.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text) && text.Bounds.Width > 0)
                .Where(text => text.TranslatePoint(new Point(text.Bounds.Width, 0), card) is { } right && right.X > card.Bounds.Width + 0.5)
                .Select(text => text.Text).ToArray();
            Assert.True(overflow.Length == 0, "Past the dialog's right edge: " + string.Join(" | ", overflow.Take(10)));
        }
        finally { window.Close(); }
    }
}
