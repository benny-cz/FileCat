using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Window = Avalonia.Controls.Window;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class QuickSearchTests(ITestOutputHelper output)
{
    private static async Task Load(AppServices services, TabViewModel tab, int count = 20_000, int nameLength = 0)
    {
        services.Providers.Register(new ManyNames(count, nameLength));
        tab.Navigate(new Location("quickfixture", "root"));
        var deadline = Stopwatch.StartNew();
        while (tab.Listing.State != ListingState.Complete)
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Quick-search fixture did not load.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(count + 3, tab.Listing.VisibleCount);
        tab.Listing.SetFocus(1000);
        tab.Listing.SetMark(1001, true);
    }

    [AvaloniaFact]
    public async Task Rapid_keys_keep_rejected_text_backspace_and_both_cycle_directions_in_order()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            await Load(services, tab);
            tab.QuickSearchType("zz-missing"); // refused; the next key must start from the last accepted text.
            tab.QuickSearchType("b");
            tab.QuickSearchType("r");
            tab.QuickSearchType("x");
            tab.QuickSearchBackspace();
            tab.QuickSearchCycle(true);
            tab.QuickSearchCycle(false);
            await tab.QuickSearchWork.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Equal("b", tab.QuickSearch);
            Assert.Equal("b", tab.ShownQuickSearch);
            Assert.False(tab.QuickSearchNoMatch);
            Assert.False(tab.IsQuickSearchPending);
            Assert.Equal("b.txt", tab.Listing.GetVisible(tab.Listing.FocusedIndex).Name);
            Assert.Equal(1, tab.Listing.MarkedCount);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_large_miss_reports_no_match_without_moving_focus_or_hiding_the_search_text()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            await Load(services, tab);
            var clock = Stopwatch.StartNew();
            tab.QuickSearchType("zz-missing");
            double acknowledgement = clock.Elapsed.TotalMilliseconds;
            Assert.True(tab.IsQuickSearchActive);
            await tab.QuickSearchWork.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.True(tab.QuickSearchNoMatch);
            Assert.Equal(string.Empty, tab.QuickSearch);
            Assert.Equal(string.Empty, tab.ShownQuickSearch);
            Assert.False(tab.IsQuickSearchPending);
            Assert.Equal(1000, tab.Listing.FocusedIndex);
            Assert.Equal(1, tab.Listing.MarkedCount);
            output.WriteLine($"20,003-name miss: handler acknowledgement {acknowledgement:F3} ms; completed {clock.Elapsed.TotalMilliseconds:F3} ms.");
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Escape_discards_queued_keys_and_late_answers()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            await Load(services, tab);
            vm.View.FocusActivePanel();
            window.KeyTextInput("zz-missing");
            window.KeyTextInput("b");
            var work = tab.QuickSearchWork;
            output.WriteLine($"Before Escape input: pending={tab.IsQuickSearchPending}; focused row={tab.Listing.FocusedIndex}.");
            int focusAtEscape = PressEscape(tab, window);
            output.WriteLine($"Escape acknowledged with focused row={focusAtEscape}.");
            Assert.Null(tab.QuickSearch);
            Assert.Null(tab.ShownQuickSearch);
            Assert.False(tab.IsQuickSearchPending);
            await work.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Null(tab.QuickSearch);
            Assert.Null(tab.ShownQuickSearch);
            Assert.False(tab.IsQuickSearchPending);
            Assert.Equal(focusAtEscape, tab.Listing.FocusedIndex);
            Assert.Equal(1, tab.Listing.MarkedCount);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Escape_preserves_completed_matches_and_rejects_answers_released_after_cancellation(bool completeBeforeEscape)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var held = new HeldQuickSearchContext();
        try
        {
            var tab = vm.ActiveTab!;
            await Load(services, tab);
            vm.View.FocusActivePanel();
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(held);
                tab.QuickSearchType("zz-missing");
                tab.QuickSearchType("b");
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            var work = tab.QuickSearchWork;
            await held.Posted.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.True(tab.IsQuickSearchPending);
            Assert.Equal(1000, tab.Listing.FocusedIndex);
            if (completeBeforeEscape)
            {
                await held.Drain(work);
                Assert.Equal("b", tab.QuickSearch);
                Assert.Equal("b.txt", tab.Listing.GetVisible(tab.Listing.FocusedIndex).Name);
            }
            int expectedFocus = completeBeforeEscape ? 0 : 1000;
            Assert.Equal(expectedFocus, tab.Listing.FocusedIndex);
            int focusAtEscape = PressEscape(tab, window);
            Assert.Equal(expectedFocus, focusAtEscape);
            Assert.Null(tab.QuickSearch);
            Assert.Null(tab.ShownQuickSearch);
            Assert.False(tab.IsQuickSearchPending);
            await held.Drain(work);
            Assert.Null(tab.QuickSearch);
            Assert.Null(tab.ShownQuickSearch);
            Assert.False(tab.IsQuickSearchPending);
            Assert.Equal(expectedFocus, tab.Listing.FocusedIndex);
            Assert.Equal(1, tab.Listing.MarkedCount);
            output.WriteLine($"Completed before Escape={completeBeforeEscape}; cancellation focus={focusAtEscape}; final focus={tab.Listing.FocusedIndex}.");
        }
        finally
        {
            held.ReleaseAll();
            AccessibilityTests.Close(services, window, root);
        }
    }

    private static int PressEscape(TabViewModel tab, Window window)
    {
        // Headless input can process queued answers before delivering the key. Completed matches stay applied;
        // only answers after the actual cancellation boundary must leave focus unchanged.
        int? focusAtCancellation = null;
        void SearchEnded(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TabViewModel.QuickSearch) && tab.QuickSearch is null)
                focusAtCancellation = tab.Listing.FocusedIndex;
        }
        tab.PropertyChanged += SearchEnded;
        try { window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); }
        finally { tab.PropertyChanged -= SearchEnded; }
        Assert.NotNull(focusAtCancellation);
        return focusAtCancellation.Value;
    }

    private sealed class HeldQuickSearchContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public readonly TaskCompletionSource Posted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Enqueue((d, state));
            Posted.TrySetResult();
        }
        public void ReleaseAll()
        {
            Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            while (_queue.TryDequeue(out var call)) call.Callback(call.State);
        }
        public async Task Drain(Task work)
        {
            var deadline = Stopwatch.StartNew();
            while (!work.IsCompleted)
            {
                ReleaseAll();
                if (deadline.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Held quick-search answer did not settle.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            await work;
            Assert.True(_queue.IsEmpty);
        }
    }

    [AvaloniaFact]
    public Task Navigating_away_discards_pending_search() => Leave(close: false);

    [AvaloniaFact]
    public Task Closing_the_tab_discards_pending_search() => Leave(close: true);

    [AvaloniaFact]
    public async Task A_filter_changing_during_a_scan_keeps_the_key_and_checks_the_new_rows()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            await Load(services, tab, count: 100_000, nameLength: 240);
            tab.QuickSearchType("zz-ñ-missing");
            Assert.True(tab.IsQuickSearchPending, "The long-name control must enter the pending scan before changing its view.");
            tab.SetFilter("b*");
            var work = tab.QuickSearchWork;
            var deadline = Stopwatch.StartNew();
            while (tab.Listing.VisibleCount != 2)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("The replacement view did not arrive.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            await work.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "b.txt", "bravo.txt" }, Enumerable.Range(0, 2).Select(i => tab.Listing.GetVisible(i).Name));
            Assert.Equal(string.Empty, tab.QuickSearch);
            Assert.True(tab.QuickSearchNoMatch);
            Assert.True(tab.ShownQuickSearchNoMatch);
            Assert.False(tab.IsQuickSearchPending);
            Assert.Equal(1, tab.Listing.MarkedCount);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Leave(bool close)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[0];
            var tab = close ? panel.OpenTab(Location.FileSystem(Path.Combine(root, "files"))) : vm.ActiveTab!;
            await Load(services, tab);
            tab.QuickSearchType("zz-missing");
            tab.QuickSearchType("b");
            var work = tab.QuickSearchWork;
            if (close) panel.CloseTab(tab);
            else tab.Navigate(Location.FileSystem(Path.Combine(root, "files")));
            Assert.Null(tab.QuickSearch);
            Assert.False(tab.IsQuickSearchPending);
            await work.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Null(tab.QuickSearch);
            if (close) Assert.True(tab.Listing.IsDisposed);
            else Assert.Equal(Path.Combine(root, "files"), tab.Location!.Path);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Anywhere_wildcards_and_non_ASCII_keep_their_matching_rules()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            await Load(services, tab);
            foreach (var (anywhere, typed, expected) in new[] { (false, "b?", "b.txt"),
                         (false, "br*", "bravo.txt"), (false, "ñ", "ñandú.txt"), (true, "andú", "ñandú.txt") })
            {
                tab.EndQuickSearch();
                services.Settings.QuickSearchMatchAnywhere = anywhere;
                tab.QuickSearchType(typed);
                await tab.QuickSearchWork.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                Assert.Equal(typed, tab.QuickSearch);
                Assert.Equal(expected, tab.Listing.GetVisible(tab.Listing.FocusedIndex).Name);
                Assert.False(tab.QuickSearchNoMatch);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class ManyNames(int count, int nameLength) : ResourceProvider
    {
        public override string Scheme => "quickfixture";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            for (int start = 0; start < count; start += 500)
                sink.AddBatch(Enumerable.Range(start, Math.Min(500, count - start)).Select(i =>
                {
                    string name = $"file-{i:00000}-long-name.txt";
                    return new EntryData(nameLength > name.Length ? name.PadRight(nameLength, 'x') : name, EntryKind.File);
                }).ToArray());
            sink.AddBatch([new EntryData("b.txt", EntryKind.File), new EntryData("bravo.txt", EntryKind.File), new EntryData("ñandú.txt", EntryKind.File)]);
            return Task.CompletedTask;
        }
    }
}
