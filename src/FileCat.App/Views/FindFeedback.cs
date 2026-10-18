using FileCat.Core.Search;

namespace FileCat.App.Views;

/// <summary>Short, truthful search feedback kept separate from the changing folder path.</summary>
internal sealed record FindFeedback(string Headline, string Qualifications, string Activity, string EmptyHeadline,
    string EmptyExplanation, bool HasLog)
{
    internal static FindFeedback Create(bool finished, bool complete, int resultCount, long folders, long files,
        long matches, string? currentFolder, IReadOnlyList<SearchLogEntry> log, bool regexTimedOut, string scope)
    {
        int inaccessible = log.Count(e => e.Kind == SearchLogKind.Inaccessible);
        int ignored = log.Count(e => e.Kind == SearchLogKind.Ignored);
        int skipped = log.Count(e => e.Kind == SearchLogKind.Skipped);
        int gone = log.Count(e => e.Kind == SearchLogKind.Gone);
        int warnings = log.Count(e => e.Kind == SearchLogKind.Warning);
        int notSearched = inaccessible + ignored + skipped + gone;
        bool limited = inaccessible + skipped + gone + warnings > 0 || regexTimedOut;

        string headline = !finished
            ? resultCount == 0 ? "Searching · no matches yet" : "Searching · results are appearing"
            : !complete ? resultCount == 0 ? "Stopped · no results yet" : "Stopped · partial results"
            : resultCount == 0 ? matches > 0 ? "No results in the list"
                : limited ? "No matches in searched portion" : ignored > 0 ? "No matches outside exclusions" : "No matches"
            : limited ? "Finished · coverage limited" : ignored > 0 ? "Finished · exclusions applied" : "Finished";

        var reasons = new List<string>();
        if (inaccessible > 0) reasons.Add($"{inaccessible:N0} inaccessible");
        if (ignored > 0) reasons.Add($"{ignored:N0} ignored");
        if (skipped > 0) reasons.Add($"{skipped:N0} skipped");
        if (gone > 0) reasons.Add($"{gone:N0} missing");
        var qualifications = new List<string>();
        if (notSearched > 0) qualifications.Add($"{notSearched:N0} not searched ({string.Join(", ", reasons)})");
        if (warnings > 0) qualifications.Add($"{warnings:N0} {Plural(warnings, "warning", "warnings")}");
        if (regexTimedOut) qualifications.Add("regular expression timed out for some files");

        string activity = $"{folders:N0} {Plural(folders, "folder", "folders")} · {files:N0} files read · {matches:N0} matched";
        if (!finished && !string.IsNullOrWhiteSpace(currentFolder)) activity += $" · {currentFolder}";

        string emptyHeadline = !finished ? "Searching… no matches yet"
            : !complete ? "Search stopped with no results"
            : headline;
        string emptyExplanation = $"Criteria and scope: {scope}";
        if (finished && complete && resultCount == 0 && matches > 0)
            emptyExplanation += " Matches were found, but the current list is empty after refinement or hiding.";
        if (finished && log.Count > 0)
            emptyExplanation += " Review the search log for exclusions and items that could not be checked.";
        if (finished && regexTimedOut)
            emptyExplanation += " Some content checks timed out.";
        if (finished && !complete)
            emptyExplanation += " Only part of the scope was searched.";
        return new FindFeedback(headline, string.Join(" · ", qualifications), activity, emptyHeadline,
            emptyExplanation, log.Count > 0);
    }

    private static string Plural(long count, string one, string many) => count == 1 ? one : many;
}
