using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>
/// AT16 / plan §11.8 layout: the archive Create dialog in a 640×480 window at 100, 125, 150 and 200 % text, with the most
/// rows it can show (7z method, preprocessing, dictionary, header compression and both password rows) and a long target
/// path. Title, body and actions never overlap; every action stays inside the window; the body scrolls instead of growing
/// sideways; keyboard focus reveals the last field.
/// </summary>
public sealed class ArchiveDialogViewportTests
{
    [AvaloniaTheory]
    [InlineData(14.0)] [InlineData(17.5)] [InlineData(21.0)] [InlineData(28.0)]
    public async Task The_create_dialog_keeps_its_actions_and_last_field_reachable_at_640_by_480(double fontSize)
    {
        var backends = new ArchiveJobBackends(ArchiveCreationFormat.All.Where(value => value.Format != ArchiveWriteFormat.Zip)
            .Select(value => (IArchiveJobBackend)new ArchiveCreationWorkflowTests.RecordingBackend(value.Format)));
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 480, fontSize: fontSize, archiveBackends: backends);
        try
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            Assert.True(vm.ActiveTab!.Listing.FocusName("a.txt"));
            var operation = vm.ExecuteAsync(CommandIds.Pack);
            await Until(() => window.GetVisualDescendants().OfType<DialogCard>().Any(card =>
                card.GetVisualDescendants().OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "Archive format")));
            var format = Combo(window, "Archive format");
            format.SelectedItem = format.Items.Cast<ArchiveCreationFormat>().Single(value => value.Format == ArchiveWriteFormat.SevenZip);
            Layout(window);
            Combo(window, "Password protection").SelectedIndex = 2;
            Layout(window);
            var path = Input(window, "Archive path");
            path.Text = Path.Combine(root, "files", string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("a long folder name", 12)), "archive.7z");
            Layout(window);
            Assert.Equal(new Size(640, 480), window.ClientSize);

            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            AssertChrome(window, card);
            var scroll = Assert.Single(card.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "DialogBodyScroll");
            Assert.InRange(scroll.Extent.Width, 1, scroll.Viewport.Width + 1); // a long path wraps or scrolls inside its box, never the dialog
            var last = Input(window, "Confirm archive password");
            Assert.True(last.Focus(NavigationMethod.Tab));
            for (int attempt = 0; attempt < 300; attempt++)
            {
                Layout(window);
                var viewport = Viewport(scroll);
                if (last.TranslatePoint(default, viewport) is { } p && p.Y >= -0.6 && p.Y + last.Bounds.Height <= viewport.Bounds.Height + 0.6) break;
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            AssertInside(last, Viewport(scroll));
            AssertChrome(window, card);
            // Every format's own rows stay inside the body as well.
            foreach (var choice in format.Items.Cast<ArchiveCreationFormat>())
            {
                format.SelectedItem = choice; Layout(window);
                AssertChrome(window, Assert.Single(window.GetVisualDescendants().OfType<DialogCard>()));
            }
            Buttons(window).Single(b => b.Content as string == "Cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await operation.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }


    /// <summary>
    /// In a wide window a long path widens the dialog past its text width: the text lines still start where the path box
    /// starts. A stretched block narrower than its row was centered (theme captures showed "Pack … into:" and the
    /// filename 50 px right of the path).
    /// </summary>
    [AvaloniaFact]
    public async Task The_create_dialog_text_starts_where_its_path_box_starts_in_a_wide_window()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1200, height: 800);
        try
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            Assert.True(vm.ActiveTab!.Listing.FocusName("a.txt"));
            var operation = vm.ExecuteAsync(CommandIds.Pack);
            await Until(() => window.GetVisualDescendants().OfType<DialogCard>().Any(card =>
                card.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Archive path")));
            var path = Input(window, "Archive path");
            path.Text = Path.Combine(root, "files", string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("a long folder name", 12)), "archive.zip");
            Layout(window);
            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            Assert.True(path.Bounds.Width > 640, "The path box did not widen the dialog: " + path.Bounds.Width);
            var lines = card.GetVisualDescendants().OfType<TextBlock>().Where(t =>
                t.Text?.StartsWith("Pack ", StringComparison.Ordinal) == true || t.Text?.StartsWith("Filename: ", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(2, lines.Length);
            foreach (var line in lines)
                Assert.InRange(line.TranslatePoint(default, card)!.Value.X - path.TranslatePoint(default, card)!.Value.X, -1, 1);
            Buttons(window).Single(b => b.Content as string == "Cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await operation.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static IEnumerable<Button> Buttons(Window window) => window.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible);
    private static ComboBox Combo(Window window, string name)
    {
        var all = window.GetVisualDescendants().OfType<ComboBox>().ToArray();
        return all.SingleOrDefault(value => AutomationProperties.GetName(value) == name)
            ?? throw new InvalidOperationException($"No combo box \"{name}\"; found: " + string.Join(", ", all.Select(AutomationProperties.GetName)));
    }
    private static TextBox Input(Window window, string name) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(value => AutomationProperties.GetName(value) == name);
    private static ScrollContentPresenter Viewport(ScrollViewer scroll) =>
        Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>(), p => ReferenceEquals(p.TemplatedParent, scroll));

    private static void AssertChrome(Window window, Control card)
    {
        AssertInside(card, window);
        var footer = Assert.Single(card.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("dialogFooter"));
        var header = Assert.Single(card.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("dialogHeader"));
        AssertInside(footer, card); AssertInside(header, card);
        var viewport = Viewport(Assert.Single(card.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "DialogBodyScroll"));
        Assert.True(viewport.Bounds.Height >= 24, "The body must retain a readable viewport.");
        double headerBottom = header.TranslatePoint(new Point(0, header.Bounds.Height), card)!.Value.Y;
        double bodyTop = viewport.TranslatePoint(default, card)!.Value.Y, bodyBottom = viewport.TranslatePoint(new Point(0, viewport.Bounds.Height), card)!.Value.Y;
        double footerTop = footer.TranslatePoint(default, card)!.Value.Y;
        Assert.True(headerBottom <= bodyTop + 0.5 && bodyBottom <= footerTop + 0.5, "Title, body and actions must not overlap.");
        foreach (var button in footer.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible))
        {
            Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
            AssertInside(button, footer); AssertInside(button, window);
        }
    }

    private static void AssertInside(Control child, Control viewport)
    {
        var p = child.TranslatePoint(default, viewport)!.Value;
        Assert.True(p.X >= -0.6 && p.Y >= -0.6 && p.X + child.Bounds.Width <= viewport.Bounds.Width + 0.6 &&
            p.Y + child.Bounds.Height <= viewport.Bounds.Height + 0.6,
            $"{child.GetType().Name} {AutomationProperties.GetName(child)} at {p}, size {child.Bounds.Size}, is outside {viewport.GetType().Name} {viewport.Bounds.Size}.");
    }

    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 1000 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.True(condition());
    }
}
