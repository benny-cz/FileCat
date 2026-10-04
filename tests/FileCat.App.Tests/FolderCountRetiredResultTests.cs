using System.Reflection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class FolderCountRetiredResultTests(ITestOutputHelper output)
{
    private sealed class Held(bool replaced) : PortableFileOperations
    {
        public readonly ManualResetEventSlim Release = new();
        public readonly ManualResetEventSlim Asked = new();
        public readonly ManualResetEventSlim Returned = new();
        public int Asks;
        public override string? GetFileIdentity(string path)
        {
            if (Path.GetFileName(path) != "counted") return "unchanged";
            int n = Interlocked.Increment(ref Asks);
            if (n == 2)
            {
                Asked.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new TimeoutException("Owned count identity was not released.");
                Returned.Set();
            }
            return n == 1 || !replaced ? "first" : "second";
        }
    }
    private sealed class TestPlatform : PortablePlatform
    {
        public TestPlatform(Held held) => FileOperations = held;
    }
    private sealed class Queued(IUiDispatcher inner) : IUiDispatcher
    {
        public bool Capture;
        public readonly ConcurrentQueue<Action> Pending = new();
        public bool CheckAccess() => inner.CheckAccess();
        public void Post(Action action)
        {
            if (Capture && action.Method.Name.Contains("<SizeFolder>", StringComparison.Ordinal)) Pending.Enqueue(action);
            else inner.Post(action);
        }
        public void Apply()
        {
            while (Pending.TryDequeue(out var action)) action();
        }
    }

    [AvaloniaTheory]
    [InlineData("cancel", false, 1, false)]
    [InlineData("cancel", true, 1, false)]
    [InlineData("cancel-retry", false, 32, false)]
    [InlineData("cancel-retry", true, 32, false)]
    [InlineData("replace-demand", false, 32, false)]
    [InlineData("replace-demand", true, 32, false)]
    [InlineData("complete", false, 1, false)]
    [InlineData("complete", true, 1, false)]
    [InlineData("cancel", false, 1, true)]
    [InlineData("cancel", true, 1, true)]
    [InlineData("cancel-retry", false, 32, true)]
    [InlineData("cancel-retry", true, 32, true)]
    [InlineData("replace-demand", false, 32, true)]
    [InlineData("replace-demand", true, 32, true)]
    [InlineData("complete", false, 1, true)]
    [InlineData("complete", true, 1, true)]
    public async Task Retired_count_results_do_not_publish(string action, bool replaced, int folders, bool queuedResult)
    {
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        string root = Path.GetFullPath(Path.Combine(parent, "filecat-count-retired-test-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(parent, Path.GetDirectoryName(root));
        string files = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        string counted = Directory.CreateDirectory(Path.Combine(files, "counted")).FullName;
        string payload = Path.Combine(counted, "one.bin");
        File.WriteAllBytes(payload, Enumerable.Repeat((byte)0x41, 1000).ToArray());
        var names = new List<string> { "counted" };
        for (int i = 1; i < folders; i++)
        {
            string name = "other-" + i.ToString("D2");
            string dir = Directory.CreateDirectory(Path.Combine(files, name)).FullName;
            File.WriteAllBytes(Path.Combine(dir, "one.bin"), Enumerable.Repeat((byte)0x43, 37).ToArray());
            names.Add(name);
        }
        string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
        string beforeHash = Hash(payload);
        string expectedHash = beforeHash;
        long expectedBytes = 1000;
        var held = new Held(replaced);
        AppServices? services = null;
        MainViewModel? vm = null;
        MainWindow? window = null;
        object? observation = null;
        var problems = new List<string>();
        try
        {
            services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            typeof(AppServices).GetField("<Platform>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(services, new TestPlatform(held));
            vm = new MainViewModel(services);
            window = new MainWindow(vm, null) { Width = 1200, Height = 800 };
            vm.Initialize(null);
            foreach (var panel in vm.Workspace.Panels) panel.ActiveTab?.Navigate(Location.FileSystem(files));
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var tab = vm.ActiveTab!;
            var listing = tab.Listing;
            await Wait(() => listing.State == ListingState.Complete && listing.FocusName("counted"));
            listing.MarkNames(names, true);
            Assert.Equal(folders, listing.MarkedCount);
            var queued = new Queued(services.Ui);
            typeof(AppServices).GetField("<Ui>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(services, queued);
            queued.Capture = queuedResult;
            vm.CountFolderSizes(tab);
            await Wait(() => held.Asked.IsSet && (queuedResult || tab.SizingFolders == 1));
            if (!queuedResult) await Wait(() => listing.Store[listing.FindStoreIndex("counted")].Size == 1000);
            Assert.Equal(2, held.Asks);
            int pendingBeforeCancel = 0;
            if (queuedResult)
            {
                held.Release.Set();
                await Wait(() => held.Returned.IsSet && queued.Pending.Count >= folders * 2);
                pendingBeforeCancel = queued.Pending.Count;
                Assert.Equal(folders, tab.SizingFolders);
                queued.Capture = false;
            }
            string focused = listing.GetVisible(listing.FocusedIndex).Name;
            if (action.StartsWith("cancel", StringComparison.Ordinal))
            {
                var cancel = typeof(MainViewModel).GetMethod("CancelSizing", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(TabViewModel)])!;
                Assert.True((bool)cancel.Invoke(vm, [tab])!);
                Assert.Equal(0, tab.SizingFolders);
                Assert.Contains("Stopped sizing", vm.Notification);
            }
            long? beforeReleaseBytes = null;
            if (action is "cancel-retry" or "replace-demand")
            {
                File.WriteAllBytes(payload, Enumerable.Repeat((byte)0x42, 2345).ToArray());
                expectedHash = Hash(payload);
                expectedBytes = 2345;
                vm.CountFolderSizes(tab);
                await Wait(() => held.Asks >= 4 && tab.SizingFolders == 0 && listing.Store[listing.FindStoreIndex("counted")].Has(EntryFlags.SizeComputed));
                beforeReleaseBytes = listing.Store[listing.FindStoreIndex("counted")].Size;
                Assert.Equal(expectedBytes, beforeReleaseBytes);
                Assert.Equal(expectedBytes + (folders - 1) * 37, listing.GetMarkStats().Bytes);
            }
            held.Release.Set();
            await Wait(() => held.Returned.IsSet);
            if (queuedResult) queued.Apply();
            if (action == "complete") await Wait(() => tab.SizingFolders == 0);
            await Task.Delay(250, TestContext.Current.CancellationToken);
            var actual = listing.Store[listing.FindStoreIndex("counted")];
            var stats = listing.GetMarkStats();
            if (action == "cancel")
            {
                if (actual.Has(EntryFlags.SizeComputed)) problems.Add("Canceled old result became complete.");
                if (!stats.SizesIncomplete || !tab.CanCountMarked) problems.Add("Canceled count is not retryable/incomplete.");
                if (vm.Notification?.Contains("was replaced", StringComparison.Ordinal) == true) problems.Add("Canceled old result published a replacement warning.");
            }
            else if (action == "complete" && replaced)
            {
                if (actual.Size >= 0 || !tab.CanCountMarked) problems.Add("Active replacement was not rejected.");
                if (vm.Notification?.Contains("was replaced", StringComparison.Ordinal) != true) problems.Add("Active replacement warning missing.");
            }
            else
            {
                if (actual.Size != expectedBytes) problems.Add($"Expected {expectedBytes} bytes, got {actual.Size} after the old count returned.");
                if (!actual.Has(EntryFlags.SizeComputed) || stats.SizesIncomplete || tab.CanCountMarked) problems.Add("A completed current count lost its completion state.");
                if (stats.Bytes != expectedBytes + (folders - 1) * 37) problems.Add($"Marked total changed to {stats.Bytes}.");
                if (vm.Notification?.Contains("was replaced", StringComparison.Ordinal) == true) problems.Add("Old result published a replacement warning.");
            }
            Assert.Equal(folders, listing.MarkedCount);
            Assert.Equal(focused, listing.GetVisible(listing.FocusedIndex).Name);
            observation = new { action, replaced, folders, queuedResult, pendingBeforeCancel, expectedBytes, beforeReleaseBytes, ActualBytes = actual.Size, Flags = actual.Flags.ToString(), stats, tab.SizingFolders, tab.CanCountMarked, tab.StatusMarked, vm.Notification, held.Asks, BeforeSHA256 = beforeHash, ExpectedAfterSHA256 = expectedHash, ActualAfterSHA256 = Hash(payload), OwnedRealFiles = true, ControlledIdentityHold = true, ControlledUiPosts = queuedResult, NativeDesktop = false, PhysicalDevice = false, Problems = problems.ToArray() };
        }
        finally
        {
            held.Release.Set();
            if (vm is not null) foreach (var job in vm.Services.Jobs.Jobs) job.Cancel();
            window?.Close();
            if (vm is not null) foreach (var tab in vm.Workspace.Panels.SelectMany(p => p.Tabs).ToList()) tab.Dispose();
            services?.Dispose();
            Assert.Equal(expectedHash, Hash(payload));
            Assert.Equal(parent, Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
            if (observation is not null) output.WriteLine(JsonSerializer.Serialize(observation));
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready(), "Owned count fixture did not settle within ten seconds.");
    }
}
