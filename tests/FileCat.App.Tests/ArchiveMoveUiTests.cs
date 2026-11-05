using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class ArchiveMoveUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task F6_and_F5_submit_distinct_move_or_copy_archive_jobs_and_refresh_both_panes(bool move, bool queue)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(nativeWindowsOriginMarks: true);
        try
        {
            string archive = Path.Combine(root, "destination.zip"); using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry("kept");
            var source = vm.Workspace.Panels[0].ActiveTab!; var target = vm.Workspace.Panels[1].ActiveTab!;
            source.Navigate(Location.FileSystem(Path.Combine(root, "files"))); target.Navigate(FileCat.Core.Archives.ZipProvider.ForFile(archive));
            vm.Workspace.Activate(source.Panel);
            await Wait(() => source.Listing.State == ListingState.Complete && target.Listing.State == ListingState.Complete);
            Assert.True(source.Listing.FocusName("a.txt"));
            var command = vm.ExecuteAsync(move ? CommandIds.Move : CommandIds.Copy);
            var clock = Stopwatch.StartNew();
            while (!command.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                window.UpdateLayout();
                var overlay = window.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Classes.Contains("backdrop"));
                var queueButton = queue ? overlay?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, "Queue")) : null;
                var button = queueButton ?? overlay?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, move ? "Move" : "Copy"));
                button ??= overlay?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, "Add"));
                if (button is { IsEffectivelyEnabled: true }) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            await command.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.Single(services.Jobs.Jobs);
            var job = services.Jobs.Jobs.Single(); Assert.Equal(JobKind.ArchiveUpdate, job.Kind); Assert.Equal(move, job.Request.Archive!.MoveSources);
            Assert.Equal(queue ? QueueMode.Queue : QueueMode.Start, job.Request.Mode);
            await Wait(() => job.State.IsFinished()); Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(!move, File.Exists(Path.Combine(root, "files", "a.txt")));
            using (var zip = ZipFile.OpenRead(archive))
            { using var reader = new StreamReader(zip.GetEntry("a.txt")!.Open()); Assert.Equal("alpha", reader.ReadToEnd()); }
            await Wait(() => target.Listing.State == ListingState.Complete && target.Listing.FocusName("a.txt"));
            Assert.True(source.Listing.FocusName("b.txt"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready(), "The owned archive UI did not reach its expected state.");
    }
}
