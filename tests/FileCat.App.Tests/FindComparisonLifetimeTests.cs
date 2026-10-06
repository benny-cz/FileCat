using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Platform;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

public sealed class FindComparisonLifetimeTests
{
    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("stop")]
    [InlineData("complete")]
    public async Task Duplicate_identity_work_stops_after_a_held_call_when_its_demand_ends(string action)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        var originalPlatform = services.Platform;
        using var files = new HeldIdentityFiles();
        FindWindow? find = null;
        Task? grouping = null;
        var paths = Enumerable.Range(0, 3).Select(i => Path.Combine(root, "files", $"owned-{i}.bin")).ToArray();
        foreach (var path in paths) File.WriteAllText(path, "same owned duplicate content");
        string[] before = paths.Select(Hash).ToArray();
        int callsWhileHeld = -1, callsAfterRelease = -1, groupsAfterRelease = -1;
        try
        {
            typeof(AppServices).GetField("<Platform>k__BackingField", fields)!.SetValue(services, new IdentityPlatform(files));
            find = FindWindow.Open(vm, Path.Combine(root, "files"), null);
            find.NamesBox.Text = "owned-*.bin";
            find.StartSearch(RefineMode.Replace);
            for (int i = 0; i < 500 && find.Session?.Finished != true; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(find.Session?.Finished);
            Assert.Equal(3, find.Found.Count);
            Dispatcher.UIThread.RunJobs();
            grouping = (Task)typeof(FindWindow).GetMethod("GroupDuplicatesAsync", fields)!
                .Invoke(find, [DuplicateCriteria.Content])!;
            await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            callsWhileHeld = files.Calls;
            Assert.False(grouping.IsCompleted);
            if (action == "close") find.Close();
            else if (action == "stop")
                find.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Stop")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // Cancel/close returns while the synchronous identity call is still held.
            Assert.False(files.Exited.Task.IsCompleted);
            files.Release.Set();
            await grouping.WaitAsync(TimeSpan.FromSeconds(5));
            callsAfterRelease = files.Calls;
            groupsAfterRelease = find.Groups.Count;
            string[] after = paths.Select(Hash).ToArray();
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new
            {
                action, callsWhileHeld, callsAfterRelease, groupsAfterRelease,
                FilesUnchanged = before.SequenceEqual(after), OwnedRealFilesOnly = true,
                HeldIdentityCallReleased = files.Exited.Task.IsCompleted,
                NativeDesktopInteraction = false, PhysicalDeviceAccess = false
            }));
            Assert.Equal(before, after);
            Assert.Equal(1, callsWhileHeld);
            Assert.Equal(action == "complete" ? 3 : 1, callsAfterRelease);
            Assert.Equal(action == "complete" ? 1 : 0, groupsAfterRelease);
        }
        finally
        {
            files.Release.Set();
            if (grouping is not null) await grouping.WaitAsync(TimeSpan.FromSeconds(5));
            find?.Close();
            typeof(AppServices).GetField("<Platform>k__BackingField", fields)!.SetValue(services, originalPlatform);
            AccessibilityTests.Close(services, main, root);
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class IdentityPlatform : PortablePlatform
    {
        // Only the comparison's identity dependency is substituted; it hashes the real owned files.
        public IdentityPlatform(HeldIdentityFiles files) => FileOperations = files;
    }

    private sealed class HeldIdentityFiles : PortableFileOperations, IDisposable
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public readonly ManualResetEventSlim Release = new();
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string? GetFileIdentity(string path)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.TrySetResult();
                try
                {
                    if (!Release.Wait(TimeSpan.FromSeconds(15)))
                        throw new TimeoutException("Owned comparison identity call was not released.");
                }
                finally { Exited.TrySetResult(); }
            }
            return Path.GetFullPath(path); // These are distinct ordinary files, with deliberately identical bytes.
        }

        public void Dispose() => Release.Dispose();
    }
}
