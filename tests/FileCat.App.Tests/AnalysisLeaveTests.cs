using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// V12 (expensive sorting asked for and cancelled; I88's kind): an analysis (View → Analyze folder: a column's value
/// for every item, then a sort by it) stops when its tab leaves the folder or closes. It must not read the folder's
/// listing after it was let go, keep saying it is analyzing, or label and re-sort the next folder.
/// </summary>
public sealed class AnalysisLeaveTests
{
    [AvaloniaFact]
    public Task Leaving_the_folder_stops_its_analysis() => Leave(close: false);

    [AvaloniaFact]
    public Task Closing_the_tab_stops_its_analysis() => Leave(close: true);

    private static async Task Leave(bool close)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The analyzed column (streams beside files) is read file by file on Windows.");
            return;
        }
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
            string many = Directory.CreateDirectory(Path.Combine(root, "many")).FullName;
            for (int i = 0; i < 20000; i++) File.WriteAllBytes(Path.Combine(many, $"f{i:00000}.txt"), [1]);
            string next = Directory.CreateDirectory(Path.Combine(root, "next")).FullName;
            File.WriteAllText(Path.Combine(next, "one.txt"), "1");
            var panel = vm.Workspace.Panels[0];
            var tab = panel.OpenTab(Location.FileSystem(many));
            var listing = tab.Listing;
            for (int i = 0; i < 500 && !(listing.State == ListingState.Complete && listing.VisibleCount >= 20000); i++) await Task.Delay(20, ct);
            tab.SortByMetadata("hidden", analyzing: true);
            var analysis = tab.AnalyzeAsync("hidden");
            for (int i = 0; i < 100 && tab.AnalysisStatus is null; i++) await Task.Delay(5, ct);
            await Task.Delay(100, ct);
            Assert.False(analysis.IsCompleted, "the analysis ended before the folder was left; the test needs more files");

            if (close) panel.CloseTab(tab);
            else tab.Navigate(Location.FileSystem(next));
            // It ends, without an error, soon after.
            var ended = await Task.WhenAny(analysis, Task.Delay(TimeSpan.FromSeconds(20), ct));
            Assert.Same(analysis, ended);
            Assert.True(analysis.IsCompletedSuccessfully, analysis.Exception?.GetBaseException().ToString());
            if (!close)
            {
                var nextListing = tab.Listing;
                for (int i = 0; i < 300 && !(nextListing.State == ListingState.Complete && nextListing.FocusName("one.txt")); i++) await Task.Delay(20, ct);
                Assert.Null(tab.AnalysisStatus);
                Assert.DoesNotContain("Sorted by", tab.Banner ?? "");
                Assert.False(tab.CancelAnalysis(), "Esc in the next folder would stop an analysis of the folder left");
            }
            for (int i = 0; i < 20; i++) await Task.Delay(50, ct);
            lock (seen) Assert.True(seen.Count == 0, $"{seen.Count} exception(s) on the window's thread: {string.Join(" | ", seen.Take(3))}");
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= Handler;
            AccessibilityTests.Close(services, window, root);
        }
    }
}
