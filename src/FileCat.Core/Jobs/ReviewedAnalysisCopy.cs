using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Diagnostics;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

internal sealed record ReviewedAnalysisCopyEntry(ItemRef Source, long Bytes);

/// <summary>Single-use native authority for a finite set of create-new copies. Never stored in JobRequest.</summary>
internal sealed class ReviewedAnalysisCopyOperation : IAsyncDisposable
{
    internal const int MaximumEntries = 16;
    private readonly Func<int, CancellationToken, Task> _copy;
    private IAsyncDisposable? _owner;
    private int _submitted;

    internal ReviewedAnalysisCopyOperation(Location destination, ImmutableArray<ReviewedAnalysisCopyEntry> entries,
        Func<int, CancellationToken, Task> copy, IAsyncDisposable owner)
    {
        if (!destination.IsFileSystem || entries.IsDefaultOrEmpty || entries.Length > MaximumEntries ||
            entries.Any(entry => entry.Source.Kind != EntryKind.File || entry.Source.FileSystemPath is null || entry.Bytes < 0 ||
                !AnalysisNames.IsOrdinaryChild(entry.Source.Name, windows: true)) ||
            entries.Select(entry => entry.Source.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            throw new ArgumentException("Choose a finite set of reviewed files with distinct output names.");
        AnalysisExportDestination.ValidateFolder(destination.Path);
        Destination = destination;
        Entries = entries;
        _copy = copy;
        _owner = owner;
    }

    internal Location Destination { get; }
    internal ImmutableArray<ReviewedAnalysisCopyEntry> Entries { get; }
    internal void Consume()
    {
        if (Interlocked.Exchange(ref _submitted, 1) != 0)
            throw new InvalidOperationException("This copy approval has already been submitted. Review the files again.");
    }

    internal Task CopyAsync(int index, CancellationToken cancellation) => _copy(index, cancellation);
    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _owner, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public sealed partial class JobManager
{
    internal async Task<Job> SubmitReviewedAnalysisCopyAsync(ReviewedAnalysisCopyOperation operation, CancellationToken cancellation)
    {
        operation.Consume();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            return Submit(new JobRequest
            {
                Kind = JobKind.ReviewedAnalysisCopy,
                Sources = operation.Entries.Select(entry => entry.Source).ToImmutableArray(),
                Destination = operation.Destination,
                Mode = QueueMode.Queue,
            }, null, reviewedAnalysisCopy: operation);
        }
        catch
        {
            await operation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static bool ReleaseReviewedAnalysisCopy(Job job)
    {
        if (job.TakeReviewedAnalysisCopy() is not { } operation) return true;
        try
        {
            operation.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception error)
        {
            AppLog.Warn("Reviewed copy binding release is unconfirmed", error);
            job.AddIssue(new(IssueSeverity.Error, "", "Release of the reviewed copy bindings could not be confirmed.", StepOutcome.Uncertain));
            return false;
        }
    }
}

internal sealed class ReviewedAnalysisCopyExecutor(Job job, JobJournal journal, ReviewedAnalysisCopyOperation operation) : IJobExecutor
{
    public void Execute()
    {
        job.AddTotals(operation.Entries.Length, operation.Entries.Sum(entry => entry.Bytes));
        job.TotalsFinal = true;
        for (int index = 0; index < operation.Entries.Length; index++)
        {
            job.Checkpoint();
            var entry = operation.Entries[index];
            string path = entry.Source.FileSystemPath!;
            job.SetCurrent(AnalysisNames.Display(path));
            job.BeginItem(entry.Bytes, 0);
            int step = journal.Intent(JobJournal.ReviewedAnalysisCopyOp, path, Path.Join(operation.Destination.Path, entry.Source.Name));
            try
            {
                operation.CopyAsync(index, job.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (job.IsCancellationRequested)
            {
                Complete(step, StepOutcome.CanceledBeforeChange, "Canceled before this copy was published.");
                throw;
            }
            catch (Exception error) when (error is AnalysisExportUncertainException || AnalysisCloseUnconfirmedException.IsPresent(error))
            {
                journal.RequireRecovery();
                Issue(StepOutcome.Uncertain, IssueSeverity.Error, "The copy or release could not be confirmed. Inspect the destination; a new copy requires fresh review.");
                return;
            }
            catch (AnalysisExportTargetExistsException)
            {
                Issue(StepOutcome.Skipped, IssueSeverity.Warning, "A file already has this output name. It was kept.");
                continue;
            }
            catch (AnalysisSourceChangedException)
            {
                Issue(StepOutcome.Skipped, IssueSeverity.Warning, "The approved source or destination binding changed. Refresh and review the entry again.");
                continue;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                Issue(StepOutcome.Failed, IssueSeverity.Error, AnalysisNames.Display(error.Message, 320));
                continue;
            }
            catch
            {
                // Native work was called and its outcome is unknown. Retain the durable intent for inspection.
                journal.RequireRecovery();
                throw;
            }
            Complete(step, StepOutcome.Committed);
            job.AddBytes(entry.Bytes);
            job.ItemDone();
            job.RootCompleted(index);

            void Issue(StepOutcome outcome, IssueSeverity severity, string message)
            {
                Complete(step, outcome, message);
                if (outcome == StepOutcome.Skipped) job.ItemSkipped();
                else { job.ItemFailed(); job.RootFailed(index); }
                job.AddIssue(new(severity, AnalysisNames.Display(path), message, outcome));
            }
        }
        job.SetCurrent(null);
    }

    private void Complete(int step, StepOutcome outcome, string? message = null)
    {
        try
        {
            journal.Done(step, outcome, message);
            journal.Flush();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            journal.RequireRecovery();
            throw new IOException("The reviewed copy receipt could not be saved. Inspect the destination before making a fresh review.", error);
        }
    }
}
