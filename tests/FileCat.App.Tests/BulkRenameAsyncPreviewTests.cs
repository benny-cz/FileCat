using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

public sealed class BulkRenameAsyncPreviewTests
{
    [AvaloniaFact]
    public async Task A_new_collision_between_preview_and_confirmation_prevents_job_admission()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        using var occupied = new ManualResetEventSlim();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            vm.BulkRenameTargetExists = path => occupied.IsSet && Path.GetFileName(path) == "new_1.txt";
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && listing.State != ListingState.Complete; i++) await Task.Delay(20, ct);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.BulkRename);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            window.UpdateLayout();
            var mask = window.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Name mask");
            var rename = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Rename"));
            mask.Text = "new_[C]";
            for (int i = 0; i < 250 && !rename.IsEnabled; i++) await Task.Delay(20, ct);
            Assert.True(rename.IsEnabled);

            occupied.Set();
            rename.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && vm.Notification?.Contains("preview changed", StringComparison.Ordinal) != true; i++)
                await Task.Delay(20, ct);
            Assert.Contains("preview changed", vm.Notification);
            Assert.Empty(services.Jobs.Jobs);
            Assert.True(File.Exists(Path.Combine(root, "files", "a.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Slow_existence_check_does_not_block_edits_cancel_or_publish_an_old_preview()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            vm.BulkRenameTargetExists = path =>
            {
                if (Path.GetFileName(path).StartsWith("slow_", StringComparison.Ordinal))
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
                return false;
            };
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.BulkRename);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var mask = window.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Name mask");
            var rename = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Rename"));
            var review = window.GetVisualDescendants().OfType<BulkRenameReview>().Single();
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(b =>
                AutomationProperties.GetAutomationId(b) == "Bulk rename validation status");

            mask.Text = "slow_[C]";
            await Until(() => entered.IsSet);
            Assert.False(rename.IsEnabled);
            Assert.Empty(review.AllRows);
            Assert.Equal("Checking names…", status.Text);

            mask.Text = "fast_[C]";
            await Until(() => rename.IsEnabled && review.AllRows.Count == 2 &&
                review.AllRows.All(row => row.Plan.NewName.StartsWith("fast_", StringComparison.Ordinal)));
            Assert.False(release.IsSet); // A blocked old check did not hold the UI or the current preview.

            dialogs.CancelAll();
            await Until(() => !dialogs.IsOpen);
            release.Set();
            await Task.Delay(100, ct);
            Assert.All(review.AllRows, row => Assert.StartsWith("fast_", row.Plan.NewName));
            Assert.Empty(services.Jobs.Jobs);

            async Task Until(Func<bool> ready)
            {
                for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, ct);
                Assert.True(ready(), "The current preview or dialog did not respond before the delayed disk check returned.");
            }
        }
        finally
        {
            release.Set();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
