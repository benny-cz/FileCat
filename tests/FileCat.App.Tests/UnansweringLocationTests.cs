using System.Diagnostics;
using System.Reflection;
using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// A location that does not answer must not stop the window. On rc.4, going to a share whose server does not answer
/// (\\192.0.2.1\share in the Windows qualification VM) left FileCat's window without an answer for 21 s: the window's
/// thread asked whether the typed path was a file, and started the folder's watcher, which opens the folder. Here a held
/// answer stands in for the server.
/// </summary>
public sealed class UnansweringLocationTests
{
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(5);

    [AvaloniaFact]
    public async Task A_folder_whose_watcher_is_slow_to_start_leaves_the_window_answering_and_is_watched_when_it_comes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var previous = TabViewModel.NewChangeMonitor;
        using var gate = new ManualResetEventSlim();
        try
        {
            string slow = Directory.CreateDirectory(Path.Join(root, "slow")).FullName;
            ChangeMonitor? held = null;
            TabViewModel.NewChangeMonitor = (path, changed) =>
            {
                if (!SamePath(path, slow)) return new ChangeMonitor(path, changed);
                Assert.True(gate.Wait(TimeSpan.FromSeconds(30)));
                return held = new ChangeMonitor(path, changed);
            };
            var tab = vm.Workspace.ActiveTab!;

            var waited = Stopwatch.StartNew();
            tab.Navigate(Location.FileSystem(slow));
            waited.Stop();
            Assert.True(waited.Elapsed < Promptly, $"The window waited {waited.Elapsed} for the folder's watcher.");
            Assert.False(tab.IsWatching);
            await Until(() => tab.Listing.State == ListingState.Complete);

            gate.Set();
            await Until(() => tab.IsWatching); // the tab still shows the folder: its watcher is taken when it comes
            Assert.Same(held, WatcherOf(tab));
            File.WriteAllText(Path.Join(slow, "new.txt"), "x");
            await Until(() => Names(tab).Contains("new.txt"));
        }
        finally
        {
            TabViewModel.NewChangeMonitor = previous;
            gate.Set();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_watcher_that_comes_after_the_tab_moved_on_is_closed_not_taken()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var previous = TabViewModel.NewChangeMonitor;
        using var gate = new ManualResetEventSlim();
        try
        {
            string slow = Directory.CreateDirectory(Path.Join(root, "slow")).FullName;
            string other = Directory.CreateDirectory(Path.Join(root, "other")).FullName;
            ChangeMonitor? held = null;
            TabViewModel.NewChangeMonitor = (path, changed) =>
            {
                if (!SamePath(path, slow)) return new ChangeMonitor(path, changed);
                Assert.True(gate.Wait(TimeSpan.FromSeconds(30)));
                return held = new ChangeMonitor(path, changed);
            };
            var tab = vm.Workspace.ActiveTab!;

            tab.Navigate(Location.FileSystem(slow));
            tab.Navigate(Location.FileSystem(other));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.IsWatching);
            var watching = WatcherOf(tab);

            gate.Set();
            await Until(() => held is not null && Disposed(held));
            Assert.Same(watching, WatcherOf(tab));
            Assert.True(SamePath(other, tab.Location!.Path), tab.Location?.Path);
        }
        finally
        {
            TabViewModel.NewChangeMonitor = previous;
            gate.Set();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_typed_path_that_is_slow_to_answer_opens_at_once_and_then_as_its_file()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var previous = Views.MainWindow.IsTypedFile;
        using var gate = new ManualResetEventSlim();
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "typed")).FullName;
            string file = Path.Join(folder, "typed.txt");
            File.WriteAllText(file, "x");
            Views.MainWindow.IsTypedFile = path =>
            {
                if (SamePath(path, file)) Assert.True(gate.Wait(TimeSpan.FromSeconds(30)));
                return File.Exists(path);
            };
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;

            // While the answer is held, the window goes to the path at once, as to a folder (whose read fails).
            var waited = Stopwatch.StartNew();
            window.NavigateToText(panel, file);
            waited.Stop();
            Assert.True(waited.Elapsed < Promptly, $"The window waited {waited.Elapsed} to learn whether the path was a file.");
            Assert.True(SamePath(file, tab.Location!.Path), tab.Location?.Path);

            // The answer comes: a file, so its folder opens with it focused.
            gate.Set();
            await Until(() => SamePath(folder, tab.Location!.Path) && tab.Listing.State == ListingState.Complete);
            await Until(() => tab.Listing.TryGetFocused(out var row) && row.Name == "typed.txt");

            // An answering path is settled before the window goes on, as before: straight to the file's folder.
            tab.Navigate(Location.FileSystem(root));
            await Until(() => tab.Listing.State == ListingState.Complete);
            window.NavigateToText(panel, file);
            Assert.True(SamePath(folder, tab.Location!.Path), tab.Location?.Path);
        }
        finally
        {
            Views.MainWindow.IsTypedFile = previous;
            gate.Set();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_late_answer_does_not_move_a_tab_that_went_elsewhere()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var previous = Views.MainWindow.IsTypedFile;
        using var gate = new ManualResetEventSlim();
        bool answered = false;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "typed")).FullName;
            string other = Directory.CreateDirectory(Path.Join(root, "other")).FullName;
            string file = Path.Join(folder, "typed.txt");
            File.WriteAllText(file, "x");
            Views.MainWindow.IsTypedFile = path =>
            {
                if (!SamePath(path, file)) return File.Exists(path);
                Assert.True(gate.Wait(TimeSpan.FromSeconds(30)));
                answered = true;
                return true;
            };
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;

            window.NavigateToText(panel, file);
            tab.Navigate(Location.FileSystem(other)); // the user went on meanwhile
            await Until(() => tab.Listing.State == ListingState.Complete);
            gate.Set();
            await Until(() => Volatile.Read(ref answered));
            for (int i = 0; i < 20; i++) await Task.Delay(10, TestContext.Current.CancellationToken); // its post, if any
            Assert.True(SamePath(other, tab.Location!.Path), tab.Location?.Path);
        }
        finally
        {
            Views.MainWindow.IsTypedFile = previous;
            gate.Set();
            AccessibilityTests.Close(services, window, root);
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static ChangeMonitor? WatcherOf(TabViewModel tab) =>
        (ChangeMonitor?)typeof(TabViewModel).GetField("_monitor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tab);

    private static bool Disposed(ChangeMonitor monitor) =>
        (bool)typeof(ChangeMonitor).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(monitor)!;

    private static List<string> Names(TabViewModel tab) =>
        Enumerable.Range(0, tab.Listing.VisibleCount).Select(i => tab.Listing.GetVisible(i).Name).ToList();

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
