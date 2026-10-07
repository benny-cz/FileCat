using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// V12 (release issue I88's kind): windows and views closed while their background work runs raise nothing on the
/// window's thread. FileCat's crash guard ends the process past five such exceptions in three seconds, so work that
/// reports to a closed view can bring FileCat down. A comparison, a viewer, a hex editor and a search are closed while
/// they work, the quick view is driven across large files and closed, and a tab is closed while its folder is counted;
/// the window's thread is watched meanwhile.
/// </summary>
public sealed class ClosedWhileBusyTests
{
    [AvaloniaFact]
    public async Task Views_closed_while_they_work_raise_nothing()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var seen = new List<string>();
        void Handler(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            lock (seen) seen.Add(e.Exception.GetType().Name + ": " + e.Exception.Message);
            e.Handled = true;
        }
        Dispatcher.UIThread.UnhandledException += Handler;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string files = Path.Combine(root, "files");
            // Large text files, and a tree of many small ones to search.
            var line = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog ", 20)) + "\n");
            foreach (var name in new[] { "big1.txt", "big2.txt", "big3.txt" })
            {
                using var stream = File.Create(Path.Combine(files, name));
                for (int i = 0; i < (24 << 20) / line.Length; i++) stream.Write(line);
            }
            string many = Directory.CreateDirectory(Path.Combine(files, "many")).FullName;
            for (int d = 0; d < 60; d++)
            {
                string dir = Directory.CreateDirectory(Path.Combine(many, $"d{d:00}")).FullName;
                for (int f = 0; f < 200; f++) File.WriteAllBytes(Path.Combine(dir, $"f{f:000}.txt"), [(byte)f]);
            }

            // A comparison of two large contents, closed while it compares.
            var a = new byte[48 << 20];
            new Random(1).NextBytes(a);
            var b = (byte[])a.Clone();
            b[^1] ^= 1;
            var compare = CompareWindow.Open("a.bin", new MemoryContentSource("a.bin", a), "b.bin", new MemoryContentSource("b.bin", b));
            await Task.Delay(150, ct);
            compare.Close();

            // A viewer and a hex editor of a large file, closed as they open.
            await ViewerLauncher.OpenPath(services, Path.Combine(files, "big1.txt"));
            for (int i = 0; i < 100 && ViewerWindow.OpenWindows.Count == 0; i++) await Task.Delay(10, ct);
            foreach (var w in ViewerWindow.OpenWindows.ToList()) w.Close();
            Assert.Null(HexEditorWindow.OpenOrActivate(services, Path.Combine(files, "big2.txt")));
            for (int i = 0; i < 100 && HexEditorWindow.OpenWindows.Count == 0; i++) await Task.Delay(10, ct);
            foreach (var w in HexEditorWindow.OpenWindows.ToList()) w.Close();

            // A search through 12,000 files, closed as it starts.
            var listing = vm.ActiveTab!.Listing;
            vm.ActiveTab.Refresh();
            for (int i = 0; i < 300 && !(listing.State == ListingState.Complete && listing.FocusName("big3.txt")); i++) await Task.Delay(20, ct);
            vm.Execute(CommandIds.FindFiles);
            for (int i = 0; i < 250 && FindWindow.OpenWindows.Count == 0; i++) await Task.Delay(20, ct);
            var find = Assert.Single(FindWindow.OpenWindows);
            find.NamesBox.Text = "*.txt";
            find.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            await Task.Delay(30, ct);
            find.Close();

            // The quick view across the large files, then closed.
            Assert.True(listing.FocusName("big1.txt"));
            vm.Execute(CommandIds.QuickView);
            foreach (var name in new[] { "big2.txt", "big3.txt", "big1.txt", "big2.txt" })
            {
                await Task.Delay(40, ct);
                listing.FocusName(name);
            }
            vm.Execute(CommandIds.QuickView);

            // A tab closed while one of its folders is counted (I88).
            var panel = vm.Workspace.Panels[0];
            var counting = panel.OpenTab(Location.FileSystem(files));
            var countingListing = counting.Listing;
            for (int i = 0; i < 300 && !(countingListing.State == ListingState.Complete && countingListing.FocusName("many")); i++) await Task.Delay(20, ct);
            vm.CountFolderSizes(counting);
            panel.CloseTab(counting);

            // Whatever they were still doing has had time to report.
            for (int i = 0; i < 60; i++) await Task.Delay(50, ct);
            lock (seen) Assert.True(seen.Count == 0, $"{seen.Count} exception(s) on the window's thread: {string.Join(" | ", seen.Take(5))}");
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= Handler;
            foreach (var w in FindWindow.OpenWindows.ToList()) w.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
