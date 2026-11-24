using FileCat.Platform.Windows.Html;
using Decision = FileCat.Platform.Windows.Html.PageCompletionWatch.Decision;
using Report = FileCat.Platform.Windows.Html.PageCompletionWatch.Report;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Release issue I648: what the page view does when WebView2 never reports that a page finished loading, on a clock of
/// the test's own: a page whose content loaded counts as drawn once quiet, one that never began to load is navigated
/// once more, and nothing is decided while one of the page's own requests is still being answered.
/// </summary>
public sealed class PageCompletionWatchTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(4);
    private long _now = 1_000;

    private PageCompletionWatch Watch() => new(() => _now, Quiet);

    private void Pass(double seconds) => _now += (long)(seconds * 1000);

    [Fact]
    public void A_page_whose_content_loaded_counts_as_drawn_once_quiet_and_not_before()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(7);
        watch.RequestStarted();
        watch.Committed(7);
        watch.RequestEnded();
        watch.RequestStarted();
        watch.ContentLoaded(7);
        watch.RequestEnded();
        Pass(3.9);
        Assert.Equal(Decision.Wait, watch.Check(viewReady: true, out var again));
        Assert.Equal(TimeSpan.FromSeconds(0.1), again);
        Pass(0.1);
        Assert.Equal(Decision.TreatAsDrawn, watch.Check(viewReady: true, out again));
        Assert.Null(again);
        Assert.False(watch.Active);
        // Once counted as drawn, the late success is not reported twice, and nothing more is decided.
        Assert.Equal(Report.AlreadyDrawn, watch.Completed(7, success: true));
        Pass(60);
        Assert.Equal(Decision.Wait, watch.Check(viewReady: true, out again));
        Assert.Null(again);
    }

    [Fact]
    public void A_failure_that_comes_after_the_page_counted_as_drawn_is_still_reported()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(3);
        watch.ContentLoaded(3);
        Pass(4);
        Assert.Equal(Decision.TreatAsDrawn, watch.Check(viewReady: true, out _));
        Assert.Equal(Report.PassOn, watch.Completed(3, success: false));
    }

    [Fact]
    public void Nothing_is_decided_while_one_of_the_pages_own_requests_is_still_being_answered()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(1);
        watch.ContentLoaded(1);
        watch.RequestStarted(); // a picture on a slow disk
        Pass(600);
        Assert.Equal(Decision.Wait, watch.Check(viewReady: true, out var again));
        Assert.Equal(Quiet, again);
        watch.RequestEnded();
        Pass(3);
        Assert.Equal(Decision.Wait, watch.Check(viewReady: true, out _));
        Pass(1);
        Assert.Equal(Decision.TreatAsDrawn, watch.Check(viewReady: true, out _));
    }

    [Fact]
    public void Nothing_is_decided_while_the_browser_is_still_starting()
    {
        var watch = Watch();
        watch.Begin();
        Pass(30);
        Assert.Equal(Decision.Wait, watch.Check(viewReady: false, out var again));
        Assert.Equal(Quiet, again);
    }

    [Fact]
    public void A_page_that_never_began_to_load_is_navigated_once_more_then_left_to_Refresh()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(5);
        watch.RequestStarted();
        watch.RequestEnded(); // answered, and then nothing: no ContentLoading
        Pass(4);
        Assert.Equal(Decision.NavigateAgain, watch.Check(viewReady: true, out var again));
        Assert.Equal(Quiet, again);
        Assert.True(watch.Active);
        // The stuck navigation's late report belongs to it, not to the retry.
        watch.Started(6);
        Assert.Equal(Report.Superseded, watch.Completed(5, success: true));
        Assert.True(watch.Active);
        Pass(4);
        Assert.Equal(Decision.GiveUp, watch.Check(viewReady: true, out again));
        Assert.Null(again);
        Pass(60);
        Assert.Equal(Decision.Wait, watch.Check(viewReady: true, out again));
        Assert.Null(again);
        // Progress after all: the page is watched again, and its content loading makes it count as drawn.
        watch.ContentLoaded(6);
        Pass(4);
        Assert.Equal(Decision.TreatAsDrawn, watch.Check(viewReady: true, out _));
    }

    [Fact]
    public void A_retry_that_completes_is_reported_once()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(5);
        Pass(4);
        Assert.Equal(Decision.NavigateAgain, watch.Check(viewReady: true, out _));
        watch.Started(6);
        watch.ContentLoaded(6);
        Assert.Equal(Report.PassOn, watch.Completed(6, success: true));
        Assert.False(watch.Active);
        Pass(8);
        Assert.Equal(Decision.Wait, watch.Check(viewReady: true, out _));
    }

    [Fact]
    public void A_page_still_being_parsed_is_not_navigated_again()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(2);
        watch.Committed(2); // a very large document arriving, not yet parsed
        Pass(4);
        Assert.Equal(Decision.GiveUp, watch.Check(viewReady: true, out _));
        watch.ContentLoaded(2);
        Assert.Equal(Report.PassOn, watch.Completed(2, success: true));
    }

    [Fact]
    public void A_page_whose_process_ended_is_navigated_once_more_and_then_reported_as_failed()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(4);
        watch.ContentLoaded(4);
        watch.ProcessEnded();
        Pass(4);
        Assert.Equal(Decision.NavigateAgain, watch.Check(viewReady: true, out _));
        watch.Started(5);
        watch.ContentLoaded(5);
        watch.ProcessEnded();
        Pass(4);
        Assert.Equal(Decision.Fail, watch.Check(viewReady: true, out _));
        Assert.False(watch.Active);
    }

    [Fact]
    public void A_page_whose_process_ended_and_whose_retry_never_starts_is_reported_as_failed()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(4);
        watch.ProcessEnded(); // the browser itself ended: nothing starts any more
        Pass(4);
        Assert.Equal(Decision.NavigateAgain, watch.Check(viewReady: true, out _));
        Pass(4);
        Assert.Equal(Decision.Fail, watch.Check(viewReady: true, out _));
    }

    [Fact]
    public void A_retry_whose_new_navigation_loads_is_not_failed_by_the_process_that_ended_before_it()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(4);
        watch.ProcessEnded();
        Pass(4);
        Assert.Equal(Decision.NavigateAgain, watch.Check(viewReady: true, out _));
        watch.Started(5);
        watch.ContentLoaded(5);
        Pass(4);
        Assert.Equal(Decision.TreatAsDrawn, watch.Check(viewReady: true, out _));
    }

    [Fact]
    public void Showing_a_page_again_makes_the_previous_navigations_reports_old_news()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(10);
        watch.Committed(10);
        // Refresh while the page is still loading: its report may come after the new page was asked for.
        watch.Begin();
        Assert.Equal(Report.Superseded, watch.Completed(10, success: true));
        Assert.True(watch.Active);
        watch.Started(11);
        Assert.Equal(Report.Superseded, watch.Completed(10, success: false));
        Assert.Equal(Report.PassOn, watch.Completed(11, success: true));
        Assert.False(watch.Active);
    }

    [Fact]
    public void A_navigation_the_page_makes_after_it_loaded_reports_as_before()
    {
        var watch = Watch();
        watch.Begin();
        watch.Started(1);
        Assert.Equal(Report.PassOn, watch.Completed(1, success: true));
        // A link to another page in the folder: not watched, and its completion is passed on.
        watch.Started(2);
        Assert.False(watch.Active);
        Assert.Equal(Report.PassOn, watch.Completed(2, success: true));
    }
}
