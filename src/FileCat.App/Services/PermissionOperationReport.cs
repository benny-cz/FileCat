using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;

namespace FileCat.App.Services;

internal static class PermissionOperationReport
{
    internal const string DescendantsUnverified = "Descendant effects are unverified.";
    internal const string PropagationScope = "Windows may update inheriting descendants. FileCat verifies the selected root.";

    internal static void Write(JobState state, PermissionScope? scope, int planned, IReadOnlyList<PermissionResult> results, Action<string> line,
        PermissionTreeSummary? tree = null, string? treeTemplate = null, bool serverPath = false)
    {
        line("FileCat permissions and ownership");
        line("Operation state · " + state.Describe());
        if (serverPath) line("Verification · SFTP server path");
        bool propagation = scope == PermissionScope.NativePropagation;
        if (propagation)
        {
            line("Scope · Windows inheritance propagation");
            line(PropagationScope);
            if (results.Count == 0) line("Awaiting the selected root result.");
        }
        else
        {
            if (tree is { } summary)
                foreach (string detail in TreeScopeLines(summary, scope, treeTemplate)) line(detail);
            line($"Reviewed objects · {planned:N0}");
            foreach (var group in results.GroupBy(result => result.Verdict)) line($"{Describe(group.Key)} · {group.Count():N0}");
            if (results.Count < planned) line($"Awaiting a result · {planned - results.Count:N0}");
        }
        foreach (var result in results)
        {
            line(string.Empty);
            line(propagation ? DescribePropagation(result) : $"Object {result.SourceIndex + 1} · {Describe(result.Verdict)}");
            if (tree is not null && result.Detail is { Length: > 0 } observed) line(Formatters.SafeName(observed));
            foreach (var component in result.Components)
            {
                line($"{component.Section} · {Describe(component.Verdict)} · {Verification(component.Verification, propagation)}" +
                    (component.NativeError is int error ? $" · native code {error}" : string.Empty));
                if (tree is not null && component.Detail is { Length: > 0 } detail && detail != result.Detail) line(Formatters.SafeName(detail));
            }
        }
    }

    internal static string Interrupted(InterruptedJob job)
    {
        var report = new StringBuilder();
        report.AppendLine("Interrupted permissions and ownership");
        report.AppendLine($"Journal started {job.CreatedUtc.ToLocalTime():g}");
        report.AppendLine("Inspect the object's current permissions before preparing a new change.");
        if (job.PermissionReview is not { } history) return report.ToString();
        if (history.ServerPath)
            report.AppendLine("Verification · SFTP server path. Reopen the item through its original SFTP connection for a fresh review.");
        if (history.Damaged) report.AppendLine("The journal contains incomplete or damaged evidence. Only the readable prefix is shown.");
        bool propagation = history.Scope == PermissionScope.NativePropagation;
        if (propagation)
        {
            report.AppendLine("Scope · Windows inheritance propagation");
            report.AppendLine(PropagationScope);
            if (history.Damaged || history.Calls.Any(call => call.Outcome is null or PermissionVerdict.Verified or
                    PermissionVerdict.AppliedWithFolderDrift or PermissionVerdict.Partial or PermissionVerdict.Uncertain))
                report.AppendLine(DescendantsUnverified);
        }
        else if (history.Scope is null) report.AppendLine("The verification scope could not be recovered.");
        else
        {
            if (history.Tree is { } summary)
                foreach (string detail in TreeScopeLines(summary, history.Scope, history.TreeTemplate)) report.AppendLine(detail);
            report.AppendLine($"Reviewed objects · {job.SourceCount:N0}");
        }
        foreach (var root in history.ObjectVerdicts.OrderBy(entry => entry.Key))
            report.AppendLine($"{Subject(root.Key)} · {Describe(root.Value)}");
        foreach (var call in history.Calls)
            report.AppendLine($"{Subject(call.Target)} · {call.Section} · " +
                (call.Outcome.HasValue ? Describe(call.Outcome.Value) : "Call outcome unknown") +
                (call.NativeError.HasValue ? $" · native code {call.NativeError}" : string.Empty));
        return report.ToString();

        string Subject(int index) => propagation ? "Selected root" : $"Object {index + 1}";
    }

    internal static IEnumerable<string> TreeScopeLines(PermissionTreeSummary tree, PermissionScope? scope = null, string? template = null)
    {
        if (scope == PermissionScope.ResetTree)
        {
            yield return "Scope · Reset access ACLs on captured descendants";
        }
        else if (scope == PermissionScope.ReviewedTree && template is not null)
            yield return "Scope · Refresh inherited access rules on captured descendants";
        else yield return $"Scope · Captured tree · {Formatters.Plural(tree.Roots, "root", "roots")}";
        if (template is not null) yield return $"ACL source · {Formatters.SafeName(template)}";
        if (tree.InaccessibleBranches > 0) yield return $"Unreadable or unverified entries and branches · {tree.InaccessibleBranches:N0}";
        if (tree.ExcludedEntries > 0) yield return $"Excluded links, special files and mount points · {tree.ExcludedEntries:N0}";
        if (tree.Aliases > 0) yield return $"Additional captured hard-link names · {tree.Aliases:N0}";
        if (tree.ProtectedEntries > 0) yield return $"Protected entries and branches excluded · {tree.ProtectedEntries:N0}";
    }

    internal static string DescribePropagation(PermissionResult? root)
    {
        if (root is null) return PropagationScope;
        string outcome = $"Selected root · {Describe(root.Verdict)}";
        return root.Components.Any(component => component.NativeCall is not null)
            ? $"{outcome}. {DescendantsUnverified}" : outcome;
    }

    internal static string Describe(PermissionVerdict verdict) => verdict switch
    {
        PermissionVerdict.Verified => "Verified changed",
        PermissionVerdict.AlreadyMatching => "Already matched",
        PermissionVerdict.AppliedWithFolderDrift => "Applied while the folder changed",
        PermissionVerdict.Partial => "Partially applied",
        PermissionVerdict.FailedBeforeChange => "Failed before change",
        PermissionVerdict.Conflict => "Reviewed state changed",
        PermissionVerdict.Uncertain => "Outcome uncertain",
        PermissionVerdict.Skipped => "Skipped",
        _ => "Unattempted"
    };

    private static string Verification(PermissionVerification verification, bool propagation) => verification switch
    {
        PermissionVerification.SameObject => propagation ? "same root read back" : "same object read back",
        PermissionVerification.ServerPath => "server path read back",
        PermissionVerification.Unavailable => "read-back unavailable",
        _ => "no call dispatched"
    };
}
