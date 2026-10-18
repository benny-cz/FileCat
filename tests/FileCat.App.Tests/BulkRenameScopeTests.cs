using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class BulkRenameScopeTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Hidden_marks_are_explicit_and_the_reviewed_scope_survives_later_navigation(bool include, bool navigate)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            var listing = tab.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            listing.MarkNames(["a.txt", "b.txt"], true);
            tab.SetFilter("b*");
            await Until(() => listing.VisibleCount == 2);
            vm.Execute(CommandIds.BulkRename);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout(); // Realize the modal's scrollable body before querying visual controls.
            var boxes = window.GetVisualDescendants().OfType<CheckBox>();
            var hidden = Assert.Single(boxes, b => (b.Content as string)?.StartsWith("Include 1 marked item hidden", StringComparison.Ordinal) == true);
            Assert.False(hidden.IsChecked);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Only visible marked items", StringComparison.Ordinal) == true);
            TextBox Mask() => window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Name mask");
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(t => AutomationProperties.GetName(t) == "Preview of new names");
            var rename = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rename");
            Mask().Text = "reviewed_[C]";
            await Until(() => rename.IsEnabled);
            // The scope switch rebuilds counter order synchronously, including when clicked immediately after a preview.
            hidden.IsChecked = include;
            Assert.Equal(include ? 2 : 1, preview.ItemCount);
            Assert.Contains("b.txt  →  reviewed_1.txt", preview.ItemsSource!.Cast<string>());
            if (include) Assert.Contains("a.txt  →  reviewed_2.txt", preview.ItemsSource!.Cast<string>());
            Assert.Equal(2, listing.GetMarkStats().Count);
            string other = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
            File.WriteAllText(Path.Combine(other, "b.txt"), "new destination must stay unchanged");
            if (navigate)
            {
                tab.Navigate(Location.FileSystem(other));
                await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 2);
                Assert.False(listing.HasLastOperation);
            }
            rename.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => services.Jobs.Jobs.Count == 1 && services.Jobs.Jobs[0].State.IsFinished());
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(include ? new[] { "b.txt", "a.txt" } : new[] { "b.txt" }, job.Request.Sources.Select(i => i.Name));
            string files = Path.Combine(root, "files");
            Assert.Equal("beta", File.ReadAllText(Path.Combine(files, "reviewed_1.txt")));
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(files, include ? "reviewed_2.txt" : "a.txt")));
            Assert.False(File.Exists(Path.Combine(files, "b.txt")));
            Assert.Equal("new destination must stay unchanged", File.ReadAllText(Path.Combine(other, "b.txt")));
            if (navigate) Assert.False(listing.HasLastOperation);

            async Task Until(Func<bool> condition)
            {
                for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(20, ct);
                Assert.True(condition(), "Expected asynchronous UI/job state did not arrive.");
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_scope_with_only_hidden_marks_starts_empty_and_cannot_be_renamed_without_inclusion()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            for (int i = 0; i < 250 && tab.Listing.State != ListingState.Complete; i++) await Task.Delay(20, ct);
            tab.Listing.MarkNames(["a.txt"], true);
            tab.SetFilter("b*");
            for (int i = 0; i < 250 && tab.Listing.VisibleCount != 2; i++) await Task.Delay(20, ct);
            vm.Execute(CommandIds.BulkRename);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            window.UpdateLayout();
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(t => AutomationProperties.GetName(t) == "Preview of new names");
            Assert.Equal(0, preview.ItemCount);
            var rename = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Rename");
            Assert.False(rename.IsEnabled);
            rename.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(services.Jobs.Jobs);
            dialogs.CancelAll();
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(root, "files", "a.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
