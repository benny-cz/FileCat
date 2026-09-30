using FileCat.App.Services;
using FileCat.Core.Jobs;

namespace FileCat.App.ViewModels;

/// <summary>Wording shared by both reviews of an interrupted operation: Operations' review and Run again.</summary>
internal static class InterruptedJobText
{
    /// <summary>Up to <paramref name="max"/> bullet lines, then how many more there are.</summary>
    public static string Bullets(IReadOnlyList<string> items, int max = 10) =>
        string.Join("\n", items.Take(max).Select(i => "• " + i)) + (items.Count > max ? $"\n• … and {items.Count - max:N0} more" : string.Empty);

    /// <summary>
    /// What recovery leaves as it is, as paragraphs to append (empty when there is nothing to say): files that differ from
    /// their source in a way an interrupted copy does not explain are never deleted (release issue I19).
    /// </summary>
    public static string CopyNotes(CopyReview review)
    {
        var text = string.Empty;
        if (review.Differing.Count > 0)
            text += $"\n\n{Formatters.Plural(review.Differing.Count, "file this operation may have copied differs", "files this operation may have copied differ")} from the source but not as a copy cut short does (changed since, or its source changed since). FileCat leaves them as they are; compare them with their sources yourself:\n"
                + Bullets(review.Differing);
        if (review.LimitReached)
            text += "\n\nNot every file this operation copied could be checked or listed here; after this, compare the destination with the source.";
        return text;
    }
}
