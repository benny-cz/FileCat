using FileCat.App.Views;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

public sealed class FindFeedbackTests
{
    private const string Scope = "names \"*.txt\" in C:\\work";

    [Fact]
    public void Successful_and_active_zero_results_describe_the_search_not_an_empty_folder()
    {
        var active = Feedback(finished: false, complete: false);
        Assert.Equal("Searching · no matches yet", active.Headline);
        Assert.Contains("Searching", active.EmptyHeadline);
        Assert.Contains(Scope, active.EmptyExplanation);

        var finished = Feedback(finished: true, complete: true);
        Assert.Equal("No matches", finished.Headline);
        Assert.Equal("No matches", finished.EmptyHeadline);
        Assert.Contains(Scope, finished.EmptyExplanation);
        Assert.False(finished.HasLog);
    }

    [Fact]
    public void Stopped_search_never_claims_complete_coverage()
    {
        var stopped = Feedback(finished: true, complete: false);
        Assert.Equal("Stopped · no results yet · Only part of the scope was searched", stopped.Headline);
        Assert.Contains("Only part of the scope", stopped.EmptyExplanation);
        Assert.Equal("Stopped · partial results · Only part of the scope was searched",
            Feedback(finished: true, complete: false, resultCount: 2).Headline);
    }

    [Fact]
    public void Access_failures_and_deliberate_ignores_have_different_qualifications()
    {
        var denied = Feedback(finished: true, complete: true,
            log: [new SearchLogEntry(SearchLogKind.Inaccessible, "C:\\work\\private", "Access denied")]);
        Assert.Equal("No matches in searched portion", denied.Headline);
        Assert.Contains("1 not searched (1 inaccessible)", denied.Qualifications);
        Assert.True(denied.HasLog);

        var ignored = Feedback(finished: true, complete: true,
            log: [new SearchLogEntry(SearchLogKind.Ignored, "C:\\work\\cache")]);
        Assert.Equal("No matches outside exclusions", ignored.Headline);
        Assert.Contains("1 not searched (1 ignored)", ignored.Qualifications);
        Assert.True(ignored.HasLog);
    }

    [Fact]
    public void Skips_warnings_missing_items_and_regex_timeouts_are_visible()
    {
        var feedback = Feedback(finished: true, complete: true, resultCount: 1, regexTimedOut: true,
            log:
            [
                new SearchLogEntry(SearchLogKind.Skipped, "C:\\work\\skip"),
                new SearchLogEntry(SearchLogKind.Gone, "C:\\work\\gone"),
                new SearchLogEntry(SearchLogKind.Warning, "C:\\work\\partial"),
            ]);
        Assert.Equal("Finished · coverage limited", feedback.Headline);
        Assert.Contains("2 not searched (1 skipped, 1 missing)", feedback.Qualifications);
        Assert.Contains("1 warning", feedback.Qualifications);
        Assert.Contains("regular expression timed out", feedback.Qualifications);
    }

    [Fact]
    public void A_changing_folder_is_only_in_secondary_progress()
    {
        var feedback = Feedback(finished: false, complete: false, currentFolder: "C:\\work\\very-long-path");
        Assert.DoesNotContain("very-long-path", feedback.Headline);
        Assert.Contains("very-long-path", feedback.Activity);
    }

    [Fact]
    public void An_emptied_result_list_does_not_claim_the_search_found_no_matches()
    {
        var feedback = FindFeedback.Create(true, true, 0, 2, 3, 4, null, [], false, Scope);
        Assert.Equal("No results in the list", feedback.Headline);
        Assert.Contains("Matches were found", feedback.EmptyExplanation);
    }

    private static FindFeedback Feedback(bool finished, bool complete, int resultCount = 0,
        IReadOnlyList<SearchLogEntry>? log = null, bool regexTimedOut = false, string? currentFolder = null) =>
        FindFeedback.Create(finished, complete, resultCount, 2, 3, resultCount, currentFolder, log ?? [], regexTimedOut, Scope);
}
