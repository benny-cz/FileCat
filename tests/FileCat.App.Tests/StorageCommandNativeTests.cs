using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>Explicit disposable-client opt-in. Exercises the actual command through native job retirement.</summary>
public sealed class StorageCommandNativeTests
{
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("refresh")]
    [InlineData("marks")]
    [InlineData("navigation")]
    public async Task Real_compression_command_keeps_its_review_and_guards_completion_marks(string change)
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILECAT_TEST_STORAGE_UI") != "1" || !WindowsStorageReviewEvidence.IsSupportedHost,
            "Explicit disposable Windows 11 x64 client, medium integrity, NTFS command qualification.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420,
            storageTransforms: new WindowsStorageTransforms());
        try
        {
            var tab = vm.ActiveTab!; var listing = tab.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            var original = tab.Location!;
            string a = Path.Join(original.Path, "a.txt"), b = Path.Join(original.Path, "b.txt");
            File.WriteAllText(a, new string('A', 8192)); File.WriteAllText(b, new string('B', 8192));
            tab.Refresh(); await Until(() => listing.State == ListingState.Complete && !listing.IsRefreshing);
            listing.MarkNames(["a.txt", "b.txt"], true); long marks = listing.MarkRevision;
            vm.Execute(CommandIds.StorageAttributes);
            await Until(() => window.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Compress"));
            window.GetVisualDescendants().OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == "Compress").IsChecked = true;
            Button Primary() => window.GetVisualDescendants().OfType<Button>().Single(button => button is not RadioButton && button.Content as string == "Compress");
            await Until(() => Primary().IsEnabled);
            if (change == "refresh")
            {
                int generation = listing.Generation; tab.Refresh();
                await Until(() => listing.Generation != generation && listing.State == ListingState.Complete && !listing.IsRefreshing);
                Assert.Equal(marks, listing.MarkRevision);
            }
            else if (change == "marks")
            { listing.MarkNames(["b.txt"], false); listing.MarkNames(["b.txt"], true); }
            else
            {
                tab.Navigate(Location.FileSystem(root));
                await Until(() => listing.State == ListingState.Complete && tab.Location?.Path == root);
                tab.Navigate(original);
                await Until(() => listing.State == ListingState.Complete && tab.Location == original);
                listing.MarkNames(["a.txt"], true);
            }
            Assert.True(vm.Dialogs.IsOpen); Assert.True(Primary().IsEnabled);
            Primary().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => services.Jobs.Jobs.Count == 1 && services.Jobs.Jobs[0].State.IsFinished() &&
                vm.CurrentNotification?.Message.Contains("changed", StringComparison.OrdinalIgnoreCase) == true);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(2, job.StorageResult!.Counts.Changed);
            Assert.Equal(StorageScope.SelectedItems, job.Request.StorageTransform!.Scope);
            Assert.True((File.GetAttributes(a) & FileAttributes.Compressed) != 0);
            Assert.True((File.GetAttributes(b) & FileAttributes.Compressed) != 0);
            await Until(() => !listing.IsRefreshing && listing.State == ListingState.Complete);
            Assert.Equal(change == "refresh" ? 0 : change == "marks" ? 2 : 1, listing.MarkedCount);
        }
        finally { ((OverlayDialogService)vm.Dialogs).CancelAll(); AccessibilityTests.Close(services, window, root); }
    }
}
