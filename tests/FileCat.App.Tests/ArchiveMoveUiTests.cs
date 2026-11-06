using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task F6_and_F5_submit_distinct_move_or_copy_archive_jobs_and_refresh_both_panes(bool move, bool queue, bool rename)
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
            bool nameSet = false;
            while (!command.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                window.UpdateLayout();
                var overlay = window.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Classes.Contains("backdrop"));
                if (rename && !nameSet && overlay?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => AutomationProperties.GetName(t) == "Destination") is { } input)
                { input.Text = archive + Path.DirectorySeparatorChar + "renamed.txt"; nameSet = true; await Task.Delay(20, TestContext.Current.CancellationToken); continue; }
                var queueButton = queue ? overlay?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, "Queue")) : null;
                var button = queueButton ?? overlay?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, move ? "Move" : "Copy"));
                button ??= overlay?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, "Add"));
                if (button is { IsEffectivelyEnabled: true })
                {
                    if (rename) Assert.Contains(overlay!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("→ as “renamed.txt”", StringComparison.Ordinal) == true);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            await command.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.Single(services.Jobs.Jobs);
            var job = services.Jobs.Jobs.Single(); Assert.Equal(JobKind.ArchiveUpdate, job.Kind); Assert.Equal(move, job.Request.Archive!.MoveSources);
            Assert.Equal(queue ? QueueMode.Queue : QueueMode.Start, job.Request.Mode);
            await Wait(() => job.State.IsFinished()); Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(!move, File.Exists(Path.Combine(root, "files", "a.txt")));
            string member = rename ? "renamed.txt" : "a.txt";
            using (var zip = ZipFile.OpenRead(archive))
            { using var reader = new StreamReader(zip.GetEntry(member)!.Open()); Assert.Equal("alpha", reader.ReadToEnd()); if (rename) Assert.Null(zip.GetEntry("a.txt")); }
            await Wait(() => target.Listing.State == ListingState.Complete && target.Listing.FocusName(member));
            Assert.True(source.Listing.FocusName("b.txt"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Archive_conflict_Enter_keeps_existing_data_and_explicit_replace_is_honored(bool move, bool replace)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(nativeWindowsOriginMarks: true);
        try
        {
            string archive = Path.Combine(root, "destination.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            { using var writer = new StreamWriter(zip.CreateEntry("a.txt").Open()); writer.Write("old data"); }
            var source = vm.Workspace.Panels[0].ActiveTab!; var target = vm.Workspace.Panels[1].ActiveTab!;
            source.Navigate(Location.FileSystem(Path.Combine(root, "files"))); target.Navigate(FileCat.Core.Archives.ZipProvider.ForFile(archive));
            vm.Workspace.Activate(source.Panel);
            await Wait(() => source.Listing.State == ListingState.Complete && target.Listing.State == ListingState.Complete);
            Assert.True(source.Listing.FocusName("a.txt"));
            var command = vm.ExecuteAsync(move ? CommandIds.Move : CommandIds.Copy);
            Button? FindButton(string label) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Content as string == label);
            await Wait(() => FindButton(move ? "Move" : "Copy")?.IsEffectivelyEnabled == true);
            FindButton(move ? "Move" : "Copy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => FindButton("Skip existing")?.IsEffectivelyEnabled == true && FindButton("Skip existing")!.IsFocused);
            if (replace) FindButton("Replace")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await command.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            var job = Assert.Single(services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
            Assert.Equal(replace ? JobState.Completed : JobState.CompletedWithIssues, job.State);
            Assert.Equal(!move || !replace, File.Exists(Path.Combine(root, "files", "a.txt")));
            using var saved = ZipFile.OpenRead(archive); using var reader = new StreamReader(saved.GetEntry("a.txt")!.Open());
            Assert.Equal(replace ? "alpha" : "old data", reader.ReadToEnd());
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("existing", 1, "existing", null, true)]
    [InlineData("existing/new.txt", 1, "existing", "new.txt", true)]
    [InlineData("new.txt", 1, "", "new.txt", true)]
    [InlineData("newfolder/", 1, "newfolder", null, true)]
    [InlineData("newfolder", 2, "newfolder", null, true)]
    [InlineData("occupied.txt/", 1, null, null, false)]
    [InlineData("inner.zip/", 1, null, null, false)]
    public async Task Editing_an_archive_target_keeps_member_and_folder_intent(string suffix, int count, string? folder, string? name, bool valid)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string archive = Path.Combine(root, "destination.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            { zip.CreateEntry("existing/data.txt"); zip.CreateEntry("occupied.txt"); zip.CreateEntry("inner.zip"); }
            var archiveRoot = FileCat.Core.Archives.ZipProvider.ForFile(archive);
            var items = Enumerable.Range(0, count).Select(i => ItemRef.ForFileSystemPath(Path.Combine(root, "files", i == 0 ? "a.txt" : "b.txt"), EntryKind.File)).ToArray();
            var result = await vm.ResolveTransferDestinationAsync(archive + Path.DirectorySeparatorChar + suffix, items,
                Location.FileSystem(Path.Combine(root, "files")), archiveRoot, archive);
            Assert.Equal(valid, result.Valid);
            if (valid)
            { Assert.Equal(Schemes.Zip, result.Destination!.Scheme); Assert.Equal(folder, result.Destination.Path); Assert.Equal(name, result.NewName); }
            else Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.Empty(services.Jobs.Jobs); Assert.True(File.Exists(Path.Combine(root, "files", "a.txt")));
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
