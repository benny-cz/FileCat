using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class OverlayDialogPointerDismissTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Registry_choice_can_be_cancelled_by_visible_close_or_backdrop_click(bool closeButton)
    {
        var (window, host, dialogs) = Open();
        try
        {
            var pending = dialogs.ChooseAsync(new ChoiceOptions("Create in Registry",
                [new("Key", "A navigable subkey"), new("Value", "A typed value")]));
            Layout(window);
            var close = Assert.Single(host.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "DialogCloseButton");
            Assert.Equal("Close dialog", AutomationProperties.GetName(close));
            Assert.True(close.IsEffectivelyVisible && close.IsEffectivelyEnabled);
            var icon = Assert.IsType<PathIcon>(close.Content);
            var iconCenter = icon.TranslatePoint(new Point(icon.Bounds.Width / 2, icon.Bounds.Height / 2), close)!.Value;
            Assert.InRange(Math.Abs(iconCenter.X - close.Bounds.Width / 2), 0, 0.5);
            Assert.InRange(Math.Abs(iconCenter.Y - close.Bounds.Height / 2), 0, 0.5);
            if (closeButton) Click(window, Center(close, window));
            else Click(window, new Point(5, 5));
            Assert.Equal(-1, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Index);
            Assert.False(dialogs.IsOpen);
            Assert.Empty(host.Children);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Backdrop_click_closes_only_top_dialog_and_preserves_underlying_draft()
    {
        var (window, host, dialogs) = Open();
        try
        {
            var outer = dialogs.PromptAsync(new PromptOptions("Create key", "New key name:") { Text = "retained" });
            Layout(window);
            var draft = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
            var inner = dialogs.ChooseAsync(new ChoiceOptions("Create in Registry", [new("Key"), new("Value")]));
            Layout(window);
            Click(window, new Point(5, 5));
            Assert.Equal(-1, (await inner.WaitAsync(TimeSpan.FromSeconds(5))).Index);
            Assert.False(outer.IsCompleted);
            Assert.True(dialogs.IsOpen);
            Assert.Equal("retained", draft.Text);
            Layout(window);
            Assert.True(draft.IsFocused);
            dialogs.CancelAll();
            Assert.Null(await outer);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Close_button_uses_the_custom_dialogs_cancel_result()
    {
        var (window, host, dialogs) = Open();
        try
        {
            var pending = dialogs.ShowCustomAsync("Review changes", new TextBlock { Text = "No changes are applied yet." },
                [new DialogButton("Keep editing", "cancel", IsCancel: true), new DialogButton("Apply", "apply", IsDefault: true)]);
            Layout(window);
            var close = Assert.Single(host.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "DialogCloseButton");
            Click(window, Center(close, window));
            Assert.Equal("cancel", await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(dialogs.IsOpen);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Clicking_inside_a_confirmation_does_not_dismiss_it_but_backdrop_does()
    {
        var (window, host, dialogs) = Open();
        try
        {
            var pending = dialogs.ConfirmAsync("Review changes", "Review before continuing", "Continue");
            Layout(window);
            var text = Assert.Single(host.GetVisualDescendants().OfType<SelectableTextBlock>());
            Click(window, Center(text, window));
            Assert.False(pending.IsCompleted);
            Click(window, new Point(5, 5));
            Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }

    private static (Window Window, Grid Host, OverlayDialogService Dialogs) Open()
    {
        var host = new Grid();
        var window = new Window { Width = 900, Height = 650, Content = host };
        window.Styles.Add(new StyleInclude(new Uri("avares://FileCat/"))
            { Source = new Uri("avares://FileCat/Themes/Styles.axaml") });
        window.Show();
        return (window, host, new OverlayDialogService(host, () => null));
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static Point Center(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point target)
    {
        for (int i = 0; i < 4 && window.InputHitTest(target) is null; i++)
        {
            Layout(window);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Assert.True(new Rect(window.ClientSize).Contains(target));
        Assert.True(window.InputHitTest(target) is not null,
            $"No pointer hit at {target}; client {window.ClientSize}, open visuals {window.GetVisualDescendants().Count()}.");
        window.MouseMove(target);
        window.MouseDown(target, MouseButton.Left);
        window.MouseUp(target, MouseButton.Left);
    }
}
