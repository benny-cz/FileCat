using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class DialogButtonPresentationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changing_action_updates_text_accessibility_and_eligibility_before_immediate_activation(bool enter)
    {
        var input = new TextBox();
        var host = new Grid();
        var window = new Window { Content = new Grid { Children = { new TextBox(), host } }, Width = 640, Height = 420 };
        var dialogs = new OverlayDialogService(host, () => input);
        window.Show();
        try
        {
            string action = "Choose action";
            bool ready = false;
            string? accepted = null;
            var presentation = new DialogButtonPresentation(new(action));
            var pending = dialogs.ShowCustomAsync("Compression", input,
                [new DialogButton("Cancel", "cancel", IsCancel: true),
                 new DialogButton("Fallback", "submit", IsDefault: true)
                 {
                     Presentation = presentation, IsAvailable = () => ready,
                     Validate = () => { accepted = action; return true; },
                 }], input);
            Dispatcher.UIThread.RunJobs();
            var primary = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => b.IsDefault);
            Assert.Equal("Choose action", primary.Content);
            Assert.False(primary.IsEnabled);
            action = "Compress"; ready = true;
            presentation.Update(new(action, action + " selected items", "Main file data only"));
            Assert.True(primary.IsEnabled);
            action = "Remove compression"; ready = false;
            presentation.Update(new(action, action + " selected items", "Checking current eligibility"));
            Assert.False(primary.IsEnabled);
            Assert.Equal(action, primary.Content);
            ready = true;
            presentation.Update(new(action, action + " selected items", "Main data and folder defaults"));
            Assert.True(primary.IsEnabled);
            Assert.Equal("Remove compression selected items", AutomationProperties.GetName(primary));
            Assert.Equal("Main data and folder defaults", AutomationProperties.GetHelpText(primary));
            // No dispatcher/timer turn between the new action and activation.
            if (enter) window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            else primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("submit", await pending);
            Assert.Equal("Remove compression", accepted);
            presentation.Update(new("After close"));
            Assert.Equal("Remove compression", primary.Content);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Worker_presentation_after_cancel_cannot_update_the_retired_dialog()
    {
        var input = new TextBox();
        var host = new Grid();
        var window = new Window { Content = new Grid { Children = { new TextBox(), host } }, Width = 640, Height = 420 };
        var dialogs = new OverlayDialogService(host, () => input);
        window.Show();
        try
        {
            var presentation = new DialogButtonPresentation(new("Compress"));
            var pending = dialogs.ShowCustomAsync("Compression", new TextBlock(),
                [new DialogButton("Cancel", "cancel", IsCancel: true),
                 new DialogButton("Fallback", "submit", IsDefault: true) { Presentation = presentation }]);
            Dispatcher.UIThread.RunJobs();
            var primary = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => b.IsDefault);
            dialogs.CancelAll();
            Assert.Equal("cancel", await pending);
            await Task.Run(() => presentation.Update(new("Late removal")));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Compress", primary.Content);
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }
}
