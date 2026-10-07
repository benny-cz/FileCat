using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>
/// Runs a <see cref="JobKind.Elevated"/> job: writes the plan, starts the broker through UAC, waits while the user
/// reviews and the broker runs it, and turns the broker's per-step report into ordinary job outcomes. Canceling asks
/// the broker to stop at its next safe boundary; an elevated process cannot be killed from here.
/// </summary>
internal sealed class ElevatedJobExecutor(Job job, JobJournal journal) : IJobExecutor
{
    public void Execute()
    {
        var plan = job.Request.Elevation ?? throw new InvalidOperationException("Missing administrator plan.");
        job.AddTotals(plan.Steps.Count, 0);
        string broker = ElevationBroker.Locate(portable: false, out var reason) ?? throw new NotSupportedException(reason);
        using var exchange = ElevationExchange.Create(Path.Combine(Path.GetDirectoryName(journal.Path)!, "elevation"), plan);
        int step = journal.Intent("elevated-plan", exchange.VolumePlanPath, exchange.Hash);
        job.SetCurrent("Waiting for Windows to ask for administrator approval…");
        ElevatedProcess process;
        try { process = ElevationBroker.Launch(broker, exchange.VolumePlanPath, exchange.Hash, WindowsFileOperations.OwnerWindow); }
        catch (OperationCanceledException ex)
        {
            journal.Done(step, StepOutcome.CanceledBeforeChange, ex.Message);
            job.AddIssue(new JobIssue(IssueSeverity.Info, "", ex.Message, StepOutcome.CanceledBeforeChange));
            throw;
        }
        using (process)
        {
            job.SetCurrent("Review the plan in the FileCat administrator window, then it runs there.");
            bool stopSent = false;
            while (!process.WaitForExit(200))
            {
                if (job.IsCancellationRequested && !stopSent)
                {
                    exchange.RequestStop();
                    stopSent = true;
                    job.SetCurrent("Stopping at the next safe step…");
                }
            }
        }
        ElevationResult? result;
        try { result = exchange.ReadResult(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { result = null; }
        ApplyResult(plan, result, step);
    }

    /// <summary>Applies the latest broker report after its process has exited; no operation is retried here.</summary>
    internal void ApplyResult(ElevationPlan plan, ElevationResult? result, int step)
    {
        if (result is null || !ElevationPlanCodec.IsValidResult(result, plan))
        {
            journal.Done(step, StepOutcome.Uncertain, "no valid report");
            throw new IOException("The administrator helper ended without a valid report for this plan, so its effect is unknown. Refresh the affected locations and review them.");
        }
        if (!result.Consented)
        {
            journal.Done(step, StepOutcome.CanceledBeforeChange, result.Refused);
            if (result.Refused is { } refused && refused != ElevationMessages.Declined)
                throw new IOException("The administrator helper refused the plan: " + refused);
            job.AddIssue(new JobIssue(IssueSeverity.Info, "", "The plan was declined in the administrator window; nothing was changed.", StepOutcome.CanceledBeforeChange));
            throw new OperationCanceledException();
        }
        int done = 0;
        foreach (var r in result.Steps)
        {
            string what = ElevationPlanCodec.Describe(plan.Steps[r.Index], plan.UserSid);
            switch (r.Outcome)
            {
                case ElevatedOutcome.Committed:
                    job.ItemDone();
                    job.RootCompleted(r.Index);
                    done++;
                    break;
                case ElevatedOutcome.PartiallyApplied:
                    job.ItemFailed();
                    job.RootFailed(r.Index);
                    job.AddIssue(new JobIssue(IssueSeverity.Warning, what, r.Message, StepOutcome.PartiallyApplied));
                    break;
                case ElevatedOutcome.NotRun:
                    job.ItemSkipped();
                    job.RootFailed(r.Index);
                    job.AddIssue(new JobIssue(IssueSeverity.Warning, what, r.Message, StepOutcome.CanceledBeforeChange));
                    break;
                case ElevatedOutcome.Skipped:
                    job.ItemSkipped();
                    job.RootFailed(r.Index);
                    job.AddIssue(new JobIssue(IssueSeverity.Info, what, r.Message, StepOutcome.Skipped));
                    break;
                default:
                    job.ItemFailed();
                    job.RootFailed(r.Index);
                    job.AddIssue(new JobIssue(IssueSeverity.Error, what, r.Message, StepOutcome.Failed));
                    break;
            }
        }
        bool unreported = result.Steps.Count < plan.Steps.Count;
        if (unreported)
            job.AddIssue(new JobIssue(IssueSeverity.Error, "",
                $"The administrator helper stopped unexpectedly after {result.Steps.Count} of {plan.Steps.Count} steps; the rest were not reported. Review the affected locations.",
                StepOutcome.Uncertain));
        journal.Done(step, unreported ? StepOutcome.Uncertain : done == plan.Steps.Count ? StepOutcome.Committed : done > 0 ? StepOutcome.PartiallyApplied : StepOutcome.Failed);
        if (result.Stopped && job.IsCancellationRequested) throw new OperationCanceledException();
    }
}

/// <summary>Messages shared by the broker and FileCat.</summary>
public static class ElevationMessages
{
    public const string Declined = "declined";
}
