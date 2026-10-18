using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class SyncGroupUiTests
{
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task Drawer_stop_targets_the_group_and_child_cancel_names_its_scope()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            string other = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
            string src = Path.Combine(source, "a.txt"), extra = Path.Combine(target, "extra.txt");
            File.WriteAllText(src, "a");
            File.WriteAllText(extra, "keep");
            services.Jobs.MaxConcurrent = 0;
            vm.Operations.IsOpen = true;
            var group = services.Jobs.SubmitSyncGroup([
                new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(src, EntryKind.File)],
                    Destination = Location.FileSystem(target), Mode = QueueMode.Queue },
                new JobRequest { Kind = JobKind.Delete, Sources = [ItemRef.ForFileSystemPath(extra, EntryKind.File)], Mode = QueueMode.Queue },
            ], source, target, "copy 1, remove 1");
            var unrelated = services.Jobs.Submit(new JobRequest { Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(src, EntryKind.File)], Destination = Location.FileSystem(other) });
            await Until(() => vm.Operations.SyncGroups.Count == 1 && vm.Operations.Jobs.Count == 3);
            vm.Operations.Selected = vm.Operations.Jobs.Single(j => j.Job == group.Steps[0]);
            window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains(group.ReviewRevision.ToString("N")[..8], StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Cancel this step");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Stop this synchronization")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => group.IsFinished);
            Assert.All(group.Steps, j => Assert.Equal(JobState.Canceled, j.State));
            Assert.Equal(JobState.Queued, unrelated.State);
            Assert.True(File.Exists(extra));
            services.Jobs.MaxConcurrent = 2;
            services.Jobs.Schedule();
            await Until(() => unrelated.State.IsFinished());
            Assert.Equal("a", File.ReadAllText(Path.Combine(other, "a.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Failed_transfer_exposes_review_before_releasing_pending_removal()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            string extra = Path.Combine(target, "extra.txt");
            File.WriteAllText(extra, "keep");
            vm.Operations.IsOpen = true;
            var group = services.Jobs.SubmitSyncGroup([
                new JobRequest { Kind = JobKind.Copy,
                    Sources = [ItemRef.ForFileSystemPath(Path.Combine(source, "missing.txt"), EntryKind.File)],
                    Destination = Location.FileSystem(target), Mode = QueueMode.Queue },
                new JobRequest { Kind = JobKind.Delete,
                    Sources = [ItemRef.ForFileSystemPath(extra, EntryKind.File)], Mode = QueueMode.Queue },
            ], source, target, "copy 1, remove 1");
            await Until(() => group.PendingRemovalReview && vm.Operations.SyncGroups.Count == 1);
            window.UpdateLayout();
            Assert.True(File.Exists(extra));
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Review pending removals…")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Dialogs.IsOpen);
            window.UpdateLayout();
            string text = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains(target, text);
            Assert.Contains(extra, text);
            Assert.Contains("Changes already committed are kept", text);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "List pending paths");
            Assert.True(File.Exists(extra));
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Run pending removals")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => group.IsFinished);
            Assert.False(File.Exists(extra));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
