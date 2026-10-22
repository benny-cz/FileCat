using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

public sealed class BulkRenameCounterValidationTests
{
    [AvaloniaTheory]
    [InlineData("Counter start", "10x", "Counter start validation")]
    [InlineData("Counter start", "999999999999999999999999", "Counter start validation")]
    [InlineData("Counter step", "1.5", "Counter step validation")]
    [InlineData("Counter step", "999999999999999999999999", "Counter step validation")]
    [InlineData("Counter digits", "0", "Counter digits validation")]
    [InlineData("Counter digits", "13", "Counter digits validation")]
    [InlineData("Counter digits", "999999999999999999999999", "Counter digits validation")]
    public async Task Invalid_counter_text_keeps_its_field_and_cannot_run(string field, string invalid, string validation)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.BulkRename);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var options = window.GetVisualDescendants().OfType<Expander>().Single(control =>
                AutomationProperties.GetName(control) == "Counter, search and case options");
            options.IsExpanded = true;
            window.UpdateLayout();
            TextBox Box(string name) => window.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == name);
            var rename = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Rename"));
            var review = window.GetVisualDescendants().OfType<BulkRenameReview>().Single();
            Box("Name mask").Text = "number_[C]";
            await Until(() => rename.IsEnabled);

            Box(field).Text = invalid;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(invalid, Box(field).Text);
            rename.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialogs.IsOpen); // The execution gate rejects the invalid draft before the next visual requery.
            await Until(() => !rename.IsEnabled);
            Assert.False(rename.IsEnabled);
            Assert.Empty(review.AllRows);
            var error = window.GetVisualDescendants().OfType<TextBlock>().Single(b => AutomationProperties.GetAutomationId(b) == validation);
            Assert.True(error.IsVisible);
            Assert.Contains("must be", error.Text);
            Assert.Contains(error.Text!, AutomationProperties.GetHelpText(Box(field)));
            await Task.Delay(220, ct); // The debounce must not make a fallback preview runnable.
            Assert.False(rename.IsEnabled);
            Assert.Empty(review.AllRows);
            dialogs.CancelAll();

            async Task Until(Func<bool> ready)
            {
                for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, ct);
                Assert.True(ready());
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Empty_intermediate_input_is_neutral_and_zero_negative_steps_have_exact_meaning()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.BulkRename);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var options = window.GetVisualDescendants().OfType<Expander>().Single(control =>
                AutomationProperties.GetName(control) == "Counter, search and case options");
            options.IsExpanded = true;
            window.UpdateLayout();
            TextBox Box(string name) => window.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == name);
            var rename = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Rename"));
            var review = window.GetVisualDescendants().OfType<BulkRenameReview>().Single();
            Box("Name mask").Text = "[N]_[C]";
            Box("Counter start").Text = "";
            Dispatcher.UIThread.RunJobs();
            var message = window.GetVisualDescendants().OfType<TextBlock>().Single(b =>
                AutomationProperties.GetAutomationId(b) == "Counter start validation");
            Assert.True(message.IsVisible);
            Assert.DoesNotContain("error", message.Classes);
            Assert.False(rename.IsEnabled);

            Box("Counter start").Text = "5";
            Box("Counter step").Text = "0";
            await Until(() => rename.IsEnabled && review.AllRows.Count == 2);
            Assert.Equal(["a_5.txt", "b_5.txt"], review.AllRows.Select(r => r.Plan.NewName).Order());
            Box("Counter step").Text = "-1";
            await Until(() => rename.IsEnabled && review.AllRows.Any(r => r.Plan.NewName == "b_4.txt"));
            Assert.Equal("-1", Box("Counter step").Text);
            dialogs.CancelAll();

            async Task Until(Func<bool> ready)
            {
                for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, ct);
                Assert.True(ready());
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
