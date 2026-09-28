using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class CreateLinkDialogTests
{
    [AvaloniaFact]
    public async Task Create_link_blocks_a_link_in_place_of_its_target_and_creates_a_named_link()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            listing.SetFocus(1);
            vm.Execute(CommandIds.CreateLink);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            var path = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Link path");
            Button Confirm() => window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Create");

            // Both panels show the same folder: the file's own name is taken, so a free one is proposed ("- link"), and
            // a link in the file's own place is refused.
            string folder = Path.Combine(root, "files");
            Assert.Equal(Path.Combine(folder, "a - link.txt"), path.Text);
            path.Text = Path.Combine(folder, "a.txt");
            for (int i = 0; i < 100 && Confirm().IsEnabled; i++) await Task.Delay(20, ct);
            Assert.False(Confirm().IsEnabled);

            path.Text = Path.Combine(folder, "a-link.txt");
            for (int i = 0; i < 100 && !Confirm().IsEnabled; i++) await Task.Delay(20, ct);
            Assert.True(Confirm().IsEnabled);
            Confirm().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            for (int i = 0; i < 250 && !job.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(folder, "a-link.txt")));
            Assert.True(job.CanUndo || !OperatingSystem.IsWindows());
            File.Delete(Path.Combine(folder, "a-link.txt"));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
