using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<bool> CleanupArchiveRetainedAsync(InterruptedJob job, RecoveryActionScope scope)
    {
        bool creation = ArchiveCreationCleanupReview.CanOffer(job);
        bool exchange = !creation && Services.ArchiveExchangeRecovery is not null && ArchiveExchangeCleanupReview.CanOffer(job);
        if (!(creation ? Services.ArchiveCreationCleanupLease is not null : exchange || Services.ArchiveCleanupLease is not null && ArchiveCleanupReview.CanOffer(job)))
        { Notify(ArchiveReviewRequired, true); return false; }
        IArchiveRetainedCleanupReview? review = null; bool approved = false, closed = false;
        RecoveryReviewRecord? record = null;
        try
        {
            scope.SetPhase(RecoveryActionPhase.Reviewing, "Reviewing the archive and its retained files…");
            // Retain ownership even if cancellation fires during native acquisition and the IO helper's
            // post-call check throws before returning its result to this method.
            await MutationIoAsync(job.JournalPath, () => review = creation
                ? new ArchiveCreationCleanupReview(Services.ArchiveCreationCleanupLease!(job.JournalPath,
                    job.ArchiveTransactions.Transactions[0].Nonce, scope.StopSource.Token), job.ArchiveTransactions.Transactions[0].ArchivePath)
                : exchange ? new ArchiveExchangeCleanupReview(Services.ArchiveExchangeRecovery!(job.JournalPath,
                    job.ArchiveTransactions.Transactions[0].Nonce, scope.StopSource.Token), scope.StopSource.Token)
                : new ArchiveCleanupReview(Services.ArchiveCleanupLease!(job.JournalPath,
                    job.ArchiveTransactions.Transactions[0].Nonce, scope.StopSource.Token)), scope.Check);
            if (review is null) throw new IOException("The native archive cleanup review did not open.");
            string details = "Keep this archive: " + Formatters.SafeName(review.ArchivePath) + "\n\n" + review.TargetDescription +
                $"\n\nPermanently remove {Formatters.Plural(review.FilesToRemove.Count, "retained file", "retained files")}:\n" +
                InterruptedJobText.Bullets(review.FilesToRemove) +
                (review.ObservedAbsent.Count == 0 ? "" : $"\n\n{review.ObservedAbsent.Count:N0} retained file(s) were already absent; their absence will be recorded.");
            string[] warnings = review.RemovesBackup
                ? ["Removal includes the backup from before this operation. You will no longer be able to restore from that backup."] : [];
            scope.SetPhase(RecoveryActionPhase.AwaitingApproval, "Awaiting approval to keep the archive and remove retained files…");
            const string action = "Keep archive and clean up";
            approved = Dialogs is OverlayDialogService overlay
                ? await overlay.ConfirmWithCancellationAsync("Review archive cleanup", details, action, danger: true,
                    scope.StopSource.Token, ["Keep current archive", $"Remove {review.FilesToRemove.Count:N0} retained files"],
                    summaryHeading: "Cleanup summary", detailsHeading: "Reviewed retained files", collapseDetails: false, warnings: warnings)
                : await Dialogs.ConfirmAsync("Review archive cleanup", details + (warnings.Length == 0 ? "" : "\n\n" + warnings[0]), action, danger: true);
            scope.Check(); if (!approved) return false;
            scope.SetPhase(RecoveryActionPhase.Applying, "Cleaning up reviewed retained files; the archive is kept…");
            var location = Location.FileSystem(review.ArchivePath);
            // Once native work may have happened, do not turn a late cancellation into an unreported outcome.
            await Services.Io.Run(Services.Providers.For(location).GetDeviceKey(location), IoPriority.Normal,
                _ => { review.Execute(scope.StopSource.Token); return true; });
            record = new(job.JournalPath, job.Title, job.Kind, DateTime.UtcNow, "Archive cleanup completed; closing tracking",
                review.Report("User-approved exact cleanup completed; tracking closure has not yet been confirmed."), ClosureConfirmed: false);
            await SaveArchiveCleanupReport(record);
            await Services.Io.Run(Services.Providers.For(location).GetDeviceKey(location), IoPriority.Normal, _ =>
            { review.Finish(); closed = true; return true; });
            record = record with { ClosureConfirmed = true, Action = "Archive cleanup completed",
                Report = review.Report("User-approved exact cleanup completed.") + "\nRecovery tracking closure confirmed.\n" };
            string? reportProblem = null;
            try { await SaveArchiveCleanupReport(record); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            { reportProblem = Formatters.SafeName(error.Message); }
            scope.SetPhase(RecoveryActionPhase.Completed, "Archive cleanup finished; the review report is retained.");
            Notify(reportProblem is null ? "Kept the reviewed archive and finished cleanup of its retained files."
                : "Archive cleanup finished and tracking is closed. The saved report could not be updated: " + reportProblem, reportProblem is not null);
            return true;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException or NotSupportedException or
            System.ComponentModel.Win32Exception or InvalidDataException)
        {
            if (approved && review is not null && !closed)
            {
                try
                {
                    record = new(job.JournalPath, job.Title, job.Kind, DateTime.UtcNow, "Archive cleanup stopped; review remains open",
                        review.Report("Cleanup stopped or failed. Recorded outcomes below are retained; an unrecorded unlink may require fresh review."), ClosureConfirmed: false);
                    await SaveArchiveCleanupReport(record);
                }
                catch (Exception reportError) when (reportError is IOException or UnauthorizedAccessException or InvalidDataException)
                { if (EditSessionsActive) Notify("The recovery record remains available, but its cleanup report could not be saved: " + Formatters.SafeName(reportError.Message), true); }
            }
            if (EditSessionsActive) Notify(error is OperationCanceledException ? "Archive cleanup stopped. Recorded changes are kept; review remains available."
                : "Archive cleanup could not finish: " + Formatters.SafeName(error.Message) + " Retained recovery records remain available.", true);
            return closed;
        }
        finally { review?.Dispose(); }
    }

    private async Task SaveArchiveCleanupReport(RecoveryReviewRecord record)
    {
        var location = Location.FileSystem(Services.Paths.JournalDirectory);
        await Services.Io.Run(Services.Providers.For(location).GetDeviceKey(location), IoPriority.Normal, _ =>
        { RecoveryReviewStore.Save(Services.Paths.JournalDirectory, record); return true; });
        if (EditSessionsActive) Operations.AddReviewedRecovery(record);
    }
}
