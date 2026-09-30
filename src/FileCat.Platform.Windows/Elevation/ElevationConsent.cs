using System.Text;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>
/// What the administrator helper shows for consent (ADR-14, AI-13). The displayed plan is the consent boundary: a
/// same-user program can hand the helper a plan of its own, so every step the helper would run is shown, in pages the
/// consent window moves through, with a count of each kind of step (release issue I17: steps after the sixtieth used
/// to be summed up as "more steps of the same plan", and any HKU hive was called the requesting user's own).
/// </summary>
public static class ElevationConsent
{
    /// <summary>
    /// Steps per page: the task dialog does not scroll, so a page must leave its buttons on a 1080-pixel screen at 150%
    /// scaling (60 lines pushed them below the screen's edge in a runtime check).
    /// </summary>
    public const int PageSize = 20;

    /// <summary>Every step, numbered, in pages of <see cref="PageSize"/>; a plan of more than one page says which steps each shows.</summary>
    public static IReadOnlyList<string> Pages(ElevationPlan plan)
    {
        var pages = new List<string>();
        for (int start = 0; start < plan.Steps.Count; start += PageSize)
        {
            int end = Math.Min(start + PageSize, plan.Steps.Count);
            var page = new StringBuilder();
            if (plan.Steps.Count > PageSize) page.Append($"Steps {start + 1:N0}–{end:N0} of {plan.Steps.Count:N0}:\n");
            for (int i = start; i < end; i++)
                page.Append($"{i + 1}. {ElevationPlanCodec.Describe(plan.Steps[i], plan.UserSid)}\n");
            pages.Add(page.ToString().TrimEnd('\n'));
        }
        return pages;
    }

    /// <summary>How many steps of each kind the plan holds, all kinds named: "940 steps: 900 permanent deletions, 40 Registry changes".</summary>
    public static string Kinds(ElevationPlan plan)
    {
        var parts = plan.Steps.GroupBy(s => s.Verb).OrderBy(g => g.Key)
            .Select(g => $"{g.Count():N0} {KindName(g.Key, g.Count())}");
        return $"{plan.Steps.Count:N0} {(plan.Steps.Count == 1 ? "step" : "steps")}: {string.Join(", ", parts)}";
    }

    private static string KindName(ElevatedVerb verb, int count)
    {
        bool one = count == 1;
        return verb switch
        {
            ElevatedVerb.Registry => one ? "Registry change" : "Registry changes",
            ElevatedVerb.DeleteTree => one ? "permanent deletion" : "permanent deletions",
            ElevatedVerb.CopyTree => one ? "copy" : "copies",
            ElevatedVerb.MoveItem => one ? "move" : "moves",
            ElevatedVerb.Rename => one ? "rename" : "renames",
            ElevatedVerb.CreateDirectory => one ? "new folder" : "new folders",
            ElevatedVerb.SetAttributes => one ? "attribute change" : "attribute changes",
            ElevatedVerb.ReadDevice => one ? "drive read" : "drive reads",
            _ => verb.ToString(),
        };
    }
}
