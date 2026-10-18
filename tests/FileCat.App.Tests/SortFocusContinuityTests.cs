using Avalonia.Headless.XUnit;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>FCUX-027: explicit sorts reveal the focused item without overriding newer viewport intent.</summary>
public sealed class SortFocusContinuityTests
{
    private static string[] Names() => Enumerable.Range(0, 100).Select(i => $"a{i:D3}").ToArray();

    [AvaloniaFact]
    public async Task Explicit_sort_toggles_reveal_the_same_focused_item_in_both_directions()
    {
        using var f = new FileListFixture(Names());
        await f.Load();
        f.Tab.Listing.SetFocus(2);
        Assert.Equal("a002", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);

        f.Tab.SortBy(SortField.Name);
        int descending = f.Tab.Listing.ViewSpecVersion;
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == descending);
        Assert.Equal("a002", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);
        AssertInViewport(f);
        Assert.True(f.List.TopRow > 0);

        f.Tab.SortBy(SortField.Name);
        int ascending = f.Tab.Listing.ViewSpecVersion;
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == ascending);
        Assert.Equal("a002", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);
        AssertInViewport(f);
    }

    [AvaloniaFact]
    public async Task Metadata_sort_reveals_the_focused_item_after_reordering()
    {
        using var f = new FileListFixture(Names());
        await f.Load();
        f.Tab.Listing.SetFocus(2);
        f.Tab.Listing.MetadataKeys = (_, index, _) => -index;

        f.Tab.SortByMetadata("test-order");
        int version = f.Tab.Listing.ViewSpecVersion;
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == version);
        Assert.Equal("a002", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);
        AssertInViewport(f);
        Assert.True(f.List.TopRow > 0);
    }

    [AvaloniaFact]
    public async Task Late_metadata_sort_does_not_override_a_newer_manual_scroll()
    {
        using var f = new FileListFixture(Names());
        await f.Load();
        f.Tab.Listing.SetFocus(2);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int first = 0;
        f.Tab.Listing.MetadataKeys = (store, index, _) =>
        {
            if (Interlocked.Exchange(ref first, 1) == 0)
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
            }
            return store[index].Name;
        };
        try
        {
            f.Tab.SortByMetadata("test-order");
            int version = f.Tab.Listing.ViewSpecVersion;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            f.List.ScrollToTopRow(50);
            Assert.Equal(50, f.List.TopRow);
            release.Set();
            await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == version);
            Assert.Equal(50, f.List.TopRow);
            Assert.Equal("a002", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);
        }
        finally { release.Set(); }
    }

    [AvaloniaFact]
    public async Task Late_metadata_sort_does_not_override_focus_chosen_after_the_sort()
    {
        using var f = new FileListFixture(Names());
        await f.Load();
        f.Tab.Listing.SetFocus(2);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int first = 0;
        f.Tab.Listing.MetadataKeys = (store, index, _) =>
        {
            if (Interlocked.Exchange(ref first, 1) == 0)
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
            }
            return store[index].Name;
        };
        try
        {
            f.Tab.SortByMetadata("test-order");
            int version = f.Tab.Listing.ViewSpecVersion;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            f.Tab.Listing.SetFocus(60);
            int chosenTop = f.List.TopRow;
            release.Set();
            await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == version);
            Assert.Equal(chosenTop, f.List.TopRow);
            Assert.Equal("a060", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);
        }
        finally { release.Set(); }
    }

    [AvaloniaFact]
    public async Task Late_sort_does_not_reveal_an_item_in_a_tab_the_user_left()
    {
        using var f = new FileListFixture(Names());
        await f.Load();
        f.Provider.Items = Enumerable.Range(0, 100).Select(i => $"b{i:D3}").ToArray();
        var second = f.Panel.OpenTab(new FileCat.Core.Resources.Location(FileListFixture.Scheme, "second"));
        await FileListFixture.Wait(() => second.Listing.State == ListingState.Complete);
        second.Listing.SetFocus(60);
        f.List.Tab = f.Tab;
        f.Tab.Listing.SetFocus(2);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int first = 0;
        f.Tab.Listing.MetadataKeys = (store, index, _) =>
        {
            if (Interlocked.Exchange(ref first, 1) == 0)
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
            }
            return store[index].Name;
        };
        try
        {
            f.Tab.SortByMetadata("test-order");
            int version = f.Tab.Listing.ViewSpecVersion;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            f.List.Tab = second;
            f.List.ScrollToTopRow(50);
            int chosenTop = f.List.TopRow;
            release.Set();
            await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == version);
            Assert.Same(second, f.List.Tab);
            Assert.Equal(chosenTop, f.List.TopRow);
            Assert.Equal("b060", second.Listing.GetVisible(second.Listing.FocusedIndex).Name);
        }
        finally { release.Set(); }
    }

    private static void AssertInViewport(FileListFixture fixture)
    {
        int focus = fixture.Tab.Listing.FocusedIndex;
        Assert.InRange(focus, fixture.List.TopRow, fixture.List.TopRow + fixture.List.VisibleRowCapacity - 1);
    }
}
