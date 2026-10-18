using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class DialogFocusOwnershipTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirm_initial_focus_and_Enter_follow_the_explicit_consequence(bool danger)
    {
        var (window, host, _, dialogs) = Open();
        try
        {
            var confirmation = dialogs.ConfirmAsync("Owned confirmation", "Review these exact changes", "Continue", danger);
            Dispatcher.UIThread.RunJobs();
            var cancel = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Cancel");
            var proceed = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Continue");
            Assert.Equal(danger, cancel.IsDefault); Assert.Equal(!danger, proceed.IsDefault);
            Assert.True((danger ? cancel : proceed).IsFocused);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(!danger, await confirmation);

            // The fallback for Enter from selectable review text must make the same safe decision.
            var fromText = dialogs.ConfirmAsync("Owned confirmation", "Review these exact changes", "Continue", danger);
            Dispatcher.UIThread.RunJobs();
            var text = Assert.Single(host.GetVisualDescendants().OfType<SelectableTextBlock>());
            text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(!danger, await fromText);
        }
        finally { dialogs.CancelAll(); Dispatcher.UIThread.RunJobs(); window.Close(); }
    }

    [AvaloniaFact]
    public void Open_and_close_before_dispatch_cannot_focus_a_retired_prompt_or_steal_the_new_prompt()
    {
        var (window, host, fallback, dialogs) = Open();
        try
        {
            var first = dialogs.PromptAsync(new PromptOptions("First", "First draft"));
            dialogs.CancelAll();
            var second = dialogs.PromptAsync(new PromptOptions("Second", "Second draft") { Text = "second" });
            Dispatcher.UIThread.RunJobs();
            Assert.True(first.IsCompletedSuccessfully);
            Assert.False(second.IsCompleted);
            var box = host.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.True(box.IsFocused);
            Assert.Equal("second", box.Text);
            Assert.False(fallback.IsFocused);
        }
        finally { dialogs.CancelAll(); Dispatcher.UIThread.RunJobs(); window.Close(); }
    }

    [AvaloniaFact]
    public void Closing_an_inner_prompt_returns_to_the_outer_input_and_preserves_its_caret()
    {
        var (window, host, _, dialogs) = Open();
        try
        {
            var outer = dialogs.PromptAsync(new PromptOptions("Outer", "Outer draft") { Text = "unchanged draft" });
            Dispatcher.UIThread.RunJobs();
            var outerBox = host.GetVisualDescendants().OfType<TextBox>().Single();
            outerBox.SelectionStart = outerBox.SelectionEnd = 3;
            var inner = dialogs.ShowCustomAsync("Inner", new TextBlock { Text = "Inner message" }, [new DialogButton("Close", "close", IsCancel: true)]);
            var innerLayer = host.Children.Last();
            Assert.False(host.Children.First().IsEnabled);
            innerLayer.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(inner.IsCompletedSuccessfully);
            Assert.False(outer.IsCompleted);
            Assert.True(outerBox.IsFocused);
            Assert.Equal(3, outerBox.CaretIndex);
            Assert.True(host.Children.Single().IsEnabled);
        }
        finally { dialogs.CancelAll(); Dispatcher.UIThread.RunJobs(); window.Close(); }
    }

    [AvaloniaFact]
    public void Deliberate_focus_before_queued_initial_focus_is_preserved()
    {
        var (window, host, _, dialogs) = Open();
        try
        {
            _ = dialogs.PromptAsync(new PromptOptions("Prompt", "Draft") { Text = "leave caret alone" });
            var cancel = host.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel");
            cancel.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(cancel.IsFocused);
        }
        finally { dialogs.CancelAll(); Dispatcher.UIThread.RunJobs(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_disabled_or_removed_return_target_uses_the_current_fallback(bool disable)
    {
        var (window, host, fallback, dialogs) = Open();
        try
        {
            var layout = Assert.IsType<Grid>(window.Content);
            var original = new TextBox { Text = "original" };
            layout.Children.Insert(0, original); original.Focus();
            _ = dialogs.PromptAsync(new PromptOptions("Prompt", "Draft"));
            Dispatcher.UIThread.RunJobs();
            if (disable) original.IsEnabled = false; else layout.Children.Remove(original);
            dialogs.CancelAll(); Dispatcher.UIThread.RunJobs();
            Assert.True(fallback.IsFocused);
            Assert.False(original.IsFocused);
        }
        finally { dialogs.CancelAll(); Dispatcher.UIThread.RunJobs(); window.Close(); }
    }

    private static (Window, Grid, TextBox, OverlayDialogService) Open()
    {
        var fallback = new TextBox { Text = "fallback" };
        var host = new Grid();
        var layout = new Grid { Children = { fallback, host } };
        var window = new Window { Content = layout, Width = 900, Height = 650 };
        window.Show(); fallback.Focus();
        return (window, host, fallback, new OverlayDialogService(host, () => fallback));
    }
}
