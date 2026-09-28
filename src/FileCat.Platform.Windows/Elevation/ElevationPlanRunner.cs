using System.ComponentModel;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>
/// Runs the steps of a validated plan, in order, through the same guarded code paths FileCat uses unelevated
/// (Registry) or the link-refusing file operations. Each step's outcome is reported as soon as it is known.
/// A stop request is honored between steps and inside trees; completed work is never rolled back (AI-11).
/// </summary>
public static class ElevationPlanRunner
{
    public static IReadOnlyList<ElevatedStepResult> Run(ElevationPlan plan, Func<bool> stopRequested,
        Action<IReadOnlyList<ElevatedStepResult>> progress)
    {
        var results = new List<ElevatedStepResult>();
        void Checkpoint()
        {
            if (stopRequested()) throw new OperationCanceledException("Stopped by FileCat.");
        }
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            if (stopRequested())
            {
                for (int rest = i; rest < plan.Steps.Count; rest++)
                    results.Add(new ElevatedStepResult(rest, ElevatedOutcome.NotRun, "Stopped before this step; nothing was changed by it."));
                break;
            }
            results.Add(RunStep(i, plan.Steps[i], Checkpoint));
            progress(results);
        }
        progress(results);
        return results;
    }

    private static ElevatedStepResult RunStep(int index, ElevatedStep step, Action checkpoint)
    {
        var ops = new SecureFileOps(checkpoint, ElevationPaths.ToDisplayPath);
        try
        {
            switch (step.Verb)
            {
                case ElevatedVerb.Registry:
                    var log = new Log();
                    RegistryChangeRunner.Apply(ElevationPlanCodec.ToChange(step.Registry!), log, checkpoint, CancellationToken.None);
                    return log.Issues.Count == 0
                        ? new ElevatedStepResult(index, ElevatedOutcome.Committed, "Done.", 1)
                        : new ElevatedStepResult(index, ElevatedOutcome.PartiallyApplied, string.Join(" ", log.Issues), 1);
                case ElevatedVerb.DeleteTree:
                    return FromReport(index, ops.DeleteTree(step.Path!), "deleted");
                case ElevatedVerb.CopyTree:
                    return FromReport(index, ops.CopyTree(step.Path!, step.Destination!, step.Name!, step.ReplaceExisting), "copied");
                case ElevatedVerb.MoveItem:
                    ops.MoveItem(step.Path!, step.Destination!, step.Name!);
                    return new ElevatedStepResult(index, ElevatedOutcome.Committed, "Moved.", 1);
                case ElevatedVerb.Rename:
                    ops.Rename(step.Path!, step.Name!);
                    return new ElevatedStepResult(index, ElevatedOutcome.Committed, "Renamed.", 1);
                case ElevatedVerb.CreateDirectory:
                    ops.CreateDirectory(step.Destination!, step.Name!);
                    return new ElevatedStepResult(index, ElevatedOutcome.Committed, "Created.", 1);
                case ElevatedVerb.SetAttributes:
                    ops.SetAttributes(step.Path!, step.SetAttributes, step.ClearAttributes);
                    return new ElevatedStepResult(index, ElevatedOutcome.Committed, "Attributes changed.", 1);
                default:
                    return new ElevatedStepResult(index, ElevatedOutcome.Failed, "Unknown step.");
            }
        }
        catch (OperationCanceledException)
        {
            return new ElevatedStepResult(index, ElevatedOutcome.PartiallyApplied, "Stopped during this step; work completed before the stop was kept.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException
                                       or NotSupportedException or InvalidOperationException or System.Security.SecurityException or FormatException)
        {
            return new ElevatedStepResult(index, ElevatedOutcome.Failed, ex.Message, 0, 1);
        }
    }

    private static ElevatedStepResult FromReport(int index, TreeReport report, string verb)
    {
        var notes = new List<string>();
        if (report.Kept > 0) notes.Add($"{report.Kept:N0} existing files kept");
        if (report.LinksSkipped > 0) notes.Add($"{report.LinksSkipped:N0} links not copied");
        if (report.StreamsNotCopied > 0) notes.Add($"{report.StreamsNotCopied:N0} alternate data streams not copied");
        if (report.MarksLost > 0) notes.Add($"{report.MarksLost:N0} download marks lost");
        string summary = $"{report.Done:N0} items {verb}" + (report.Failed > 0 ? $", {report.Failed:N0} failed" : "") +
                         (notes.Count > 0 ? "; " + string.Join(", ", notes) : "") + "." +
                         (report.Problems.Count > 0 ? " " + string.Join(" ", report.Problems.Take(5)) : "");
        var outcome = report.Failed == 0 ? (report.MarksLost > 0 || report.LinksSkipped > 0 ? ElevatedOutcome.PartiallyApplied : ElevatedOutcome.Committed)
            : report.Done > 0 ? ElevatedOutcome.PartiallyApplied : ElevatedOutcome.Failed;
        return new ElevatedStepResult(index, outcome, summary, report.Done, report.Failed);
    }

    private sealed class Log : IRegistryStepLog
    {
        public readonly List<string> Issues = [];
        public int Intent(string op, string path, string? target = null) => 0;
        public void Done(int step, StepOutcome outcome, string? message = null) { }
        public void Issue(JobIssue issue) => Issues.Add(issue.Message);
    }
}
