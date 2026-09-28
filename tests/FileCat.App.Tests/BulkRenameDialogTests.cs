using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class BulkRenameDialogTests
{
    [AvaloniaFact]
    public async Task Ctrl_M_previews_disables_rename_on_collisions_and_renames_the_marked_files()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.BulkRename);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            TextBox Box(string name) => window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == name);
            Button Confirm() => window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rename");
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Preview of new names");

            // Both files getting one name is a collision: Rename is disabled until the rules make the names unique.
            Box("Name mask").Text = "same";
            for (int i = 0; i < 50 && Confirm().IsEnabled; i++) await Task.Delay(20, ct);
            Assert.False(Confirm().IsEnabled);
            Box("Name mask").Text = "doc_[C]";
            for (int i = 0; i < 50 && !Confirm().IsEnabled; i++) await Task.Delay(20, ct);
            Assert.True(Confirm().IsEnabled);
            Assert.Contains("a.txt  →  doc_1.txt", preview.ItemsSource!.Cast<string>());

            Confirm().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            for (int i = 0; i < 250 && !job.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(["doc_1.txt", "doc_2.txt"], Directory.GetFiles(Path.Combine(root, "files")).Select(Path.GetFileName).Order());
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
