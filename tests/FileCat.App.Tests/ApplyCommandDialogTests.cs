using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class ApplyCommandDialogTests
{
    [AvaloniaFact]
    public async Task Ctrl_G_previews_blocks_unknown_programs_and_runs_once_per_item()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.ApplyCommand);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            var command = window.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Command");
            var shell = window.GetVisualDescendants().OfType<CheckBox>().Single(c => (c.Content as string)?.StartsWith("Run through the shell", StringComparison.Ordinal) == true);
            Button Run() => window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Run");

            command.Text = "filecat-no-such-program {file}";
            for (int i = 0; i < 100 && Run().IsEnabled; i++) await Task.Delay(20, ct);
            Assert.False(Run().IsEnabled);

            shell.IsChecked = true;
            command.Text = "echo x{index}> {stem}.out";
            for (int i = 0; i < 100 && !Run().IsEnabled; i++) await Task.Delay(20, ct);
            Assert.True(Run().IsEnabled);
            Run().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            for (int i = 0; i < 250 && !job.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job.State);
            string folder = Path.Combine(root, "files");
            Assert.StartsWith("x1", File.ReadAllText(Path.Combine(folder, "a.out")));
            Assert.StartsWith("x2", File.ReadAllText(Path.Combine(folder, "b.out")));
            Assert.Equal("echo x{index}> {stem}.out", services.History.ApplyCommands[0]);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
