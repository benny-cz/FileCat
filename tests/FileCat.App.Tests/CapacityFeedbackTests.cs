using Avalonia.Headless.XUnit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class CapacityFeedbackTests
{
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), "Timed out waiting for capacity feedback.");
    }

    [AvaloniaFact]
    public async Task Explicit_refresh_and_completed_copy_and_delete_requery_the_current_destination()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            await Until(() => tab.FreeSpaceText.EndsWith(" free", StringComparison.Ordinal));
            int revision = 1, queries = 0;
            tab.CapacityQuery = _ =>
            {
                Interlocked.Increment(ref queries);
                int observed = Volatile.Read(ref revision);
                return new FileSystemCapacitySnapshot("volume-" + observed, (10 - observed) * 1024L * 1024);
            };
            tab.Refresh();
            await Until(() => tab.FreeSpaceVolumeIdentity == "volume-1");
            string first = tab.FreeSpaceText;
            string source = Path.Combine(root, "incoming.txt");
            File.WriteAllText(source, "incoming");
            Volatile.Write(ref revision, 2);
            var copy = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = tab.Location,
            });
            await Until(() => copy.State.IsFinished() && tab.FreeSpaceVolumeIdentity == "volume-2");
            Assert.Equal(JobState.Completed, copy.State);
            Assert.NotEqual(first, tab.FreeSpaceText);
            string copied = Path.Combine(tab.Location!.Path, "incoming.txt");
            Assert.True(File.Exists(copied));

            Volatile.Write(ref revision, 3);
            var delete = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Delete, Sources = [ItemRef.ForFileSystemPath(copied, EntryKind.File)],
            });
            await Until(() => delete.State.IsFinished() && tab.FreeSpaceVolumeIdentity == "volume-3");
            Assert.Equal(JobState.Completed, delete.State);
            Assert.False(File.Exists(copied));
            Assert.True(Volatile.Read(ref queries) >= 3);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Different_locations_on_one_scheduler_device_show_their_own_capacity_identity()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            string first = tab.Location!.Path;
            string second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            tab.CapacityQuery = path => path == first
                ? new FileSystemCapacitySnapshot("mount-a", 1L << 30)
                : new FileSystemCapacitySnapshot("mount-b", 2L << 30);
            tab.Refresh();
            await Until(() => tab.FreeSpaceVolumeIdentity == "mount-a");
            string firstText = tab.FreeSpaceText;
            tab.Navigate(Location.FileSystem(second));
            await Until(() => tab.FreeSpaceVolumeIdentity == "mount-b");
            Assert.NotEqual(firstText, tab.FreeSpaceText);
            tab.Navigate(Location.FileSystem(first));
            await Until(() => tab.FreeSpaceVolumeIdentity == "mount-a");
            Assert.Equal(firstText, tab.FreeSpaceText);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Late_capacity_from_a_previous_location_never_replaces_the_newer_value()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            string first = tab.Location!.Path;
            string second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            tab.CapacityQuery = path =>
            {
                if (path == first)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                    return new FileSystemCapacitySnapshot("old-mount", 111);
                }
                return new FileSystemCapacitySnapshot("new-mount", 222);
            };
            tab.Refresh();
            await Until(() => entered.IsSet);
            tab.Navigate(Location.FileSystem(second));
            await Until(() => tab.FreeSpaceVolumeIdentity == "new-mount");
            string shown = tab.FreeSpaceText;
            release.Set();
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Equal("new-mount", tab.FreeSpaceVolumeIdentity);
            Assert.Equal(shown, tab.FreeSpaceText);
        }
        finally { release.Set(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Repeated_refreshes_during_a_slow_query_coalesce_to_one_followup()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            int queries = 0;
            tab.CapacityQuery = _ =>
            {
                int n = Interlocked.Increment(ref queries);
                if (n == 1) { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
                return new FileSystemCapacitySnapshot("revision-" + n, n * 1024L);
            };
            tab.Refresh();
            await Until(() => entered.IsSet);
            for (int i = 0; i < 5; i++) tab.Refresh();
            await Until(() => tab.Listing.State == FileCat.Core.Listing.ListingState.Complete && !tab.Listing.IsRefreshing);
            release.Set();
            await Until(() => tab.FreeSpaceVolumeIdentity == "revision-2");
            Assert.Equal(2, Volatile.Read(ref queries));
        }
        finally { release.Set(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Failed_query_replaces_the_old_number_with_an_unavailable_message()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            await Until(() => tab.FreeSpaceText.EndsWith(" free", StringComparison.Ordinal));
            tab.CapacityQuery = _ => null;
            tab.Refresh();
            await Until(() => tab.FreeSpaceText == "Free space unavailable");
            Assert.Null(tab.FreeSpaceVolumeIdentity);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Expired_capacity_is_labeled_stale_and_rechecked_when_the_tab_becomes_active()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            int queries = 0;
            tab.CapacityQuery = _ => new FileSystemCapacitySnapshot("same-volume", Interlocked.Increment(ref queries) * 1024L);
            tab.Refresh();
            await Until(() => Volatile.Read(ref queries) >= 1 && tab.FreeSpaceText.EndsWith(" free", StringComparison.Ordinal));
            string prior = tab.FreeSpaceText;
            tab.IsActiveTab = false;
            tab.CapacityTtl = TimeSpan.FromMilliseconds(500);
            await Task.Delay(550, TestContext.Current.CancellationToken);
            tab.UpdateStatus();
            Assert.Equal("Free space needs refresh", tab.FreeSpaceText);
            tab.IsActiveTab = true;
            await Until(() => Volatile.Read(ref queries) >= 2);
            await Until(() => tab.FreeSpaceText.EndsWith(" free", StringComparison.Ordinal) && tab.FreeSpaceText != prior);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
