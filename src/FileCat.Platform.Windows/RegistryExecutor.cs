using System.ComponentModel;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows;

/// <summary>
/// One narrow, captured Registry plan per job. Every step records intent before mutating; each committed change
/// leaves a guarded inverse for undo. A plan stops at its first failed change unless its steps are independent (undo).
/// </summary>
internal sealed class RegistryExecutor(Job job, JobJournal journal) : IJobExecutor, IRegistryStepLog
{
    public void Execute()
    {
        var changes = job.Request.RegistryChanges.Count > 0 ? job.Request.RegistryChanges :
            job.Request.Registry is { } single ? [single] : throw new InvalidOperationException("Missing Registry plan.");
        job.AddTotals(changes.Count, changes.Sum(c => (long)(c.Desired?.Data.Length ?? 0)));
        bool independent = job.Request.IndependentSteps;
        for (int i = 0; i < changes.Count; i++)
        {
            job.Checkpoint();
            var change = changes[i];
            job.SetCurrent(RegistryChangeRunner.Describe(change));
            try
            {
                var inverse = RegistryChangeRunner.Apply(change, this, job.Checkpoint, job.Token);
                if (inverse is not null)
                    job.AddUndo(new UndoStep(UndoKind.RegistryInverse, change.Key.ToString(), change.Key.ToString(), 0, 0, Registry: inverse));
                job.ItemDone();
                job.RootCompleted(i);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException
                                           or NotSupportedException or InvalidOperationException or System.Security.SecurityException)
            {
                job.ItemFailed();
                job.RootFailed(i);
                string message = RegistryChangeRunner.IsAccessDenied(ex)
                    ? "Access denied. An administrator retry can apply this change."
                    : ex.Message;
                job.AddIssue(new JobIssue(IssueSeverity.Error, RegistryChangeRunner.Describe(change), message, StepOutcome.Failed)
                    { Cause = RegistryChangeRunner.IsAccessDenied(ex) ? "access" : ErrorText.Classify(ex) });
                if (independent || i + 1 == changes.Count) continue;
                job.AddIssue(new JobIssue(IssueSeverity.Warning, "",
                    $"The plan stopped here: {i} of {changes.Count} changes completed and {changes.Count - i - 1} later changes were not attempted.",
                    StepOutcome.CanceledBeforeChange));
                for (int rest = i + 1; rest < changes.Count; rest++) job.RootFailed(rest);
                return;
            }
        }
    }

    int IRegistryStepLog.Intent(string op, string path, string? target) => journal.Intent(op, path, target);
    void IRegistryStepLog.Done(int step, StepOutcome outcome, string? message) => journal.Done(step, outcome, message);
    void IRegistryStepLog.Issue(JobIssue issue) => job.AddIssue(issue);
}

public sealed class RegistryConflictException(string message) : IOException(message);
