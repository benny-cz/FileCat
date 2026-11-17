using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<T> StorageHistoryIoAsync<T>(InterruptedJob job, RecoveryActionScope scope, Func<CancellationToken, T> work)
    {
        using var interactive = await StorageAccounting.EnterInteractiveAsync(scope.StopSource.Token);
        return await Services.Io.RunOwned(PathUtil.GetDeviceKey(Services.Paths.JournalDirectory), IoPriority.Interactive,
            ct => { scope.Check(); return work(ct); }, scope.StopSource.Token);
    }

    private async Task<ReportWindow?> ShowStorageHistoryReportAsync(InterruptedJob job)
    {
        if (!TryBeginRecoveryAction(job, out var scope)) return null;
        try
        {
            scope.SetPhase(RecoveryActionPhase.Reviewing, "Reading the exact retained storage history…");
            var status = await StorageHistoryIoAsync(job, scope,
                ct => Services.Jobs.ReadStorageReview(job.StorageJournalLeaf ?? Path.GetFileName(job.JournalPath), ct));
            scope.Check();
            InvalidateStorageAccounting();
            if (!status.ReviewPending)
            {
                var pending = Operations.Interrupted.FirstOrDefault(r => r.Job.JournalPath == job.JournalPath);
                if (pending is not null) Operations.Interrupted.Remove(pending);
                if (status.Acknowledgment is not null) Operations.AddReviewedRecovery(new(job.JournalPath, job.Title, job.Kind,
                    status.Acknowledgment.AcknowledgedUtc, "Verified retained storage review", string.Empty, true) { StorageAcknowledgment = status.Acknowledgment });
                Operations.UpdateSummary();
            }
            string report = StorageHistoryReport.Format(status);
            var window = new ReportWindow("Storage history snapshot", Formatters.SafeName(job.Title), _ => Task.FromResult(report));
            window.ConfigureStorageReview(report, () => SaveStorageCallDetailsAsync(job, status.Current), status.ReviewPending
                ? async () => { if (await AcknowledgeStorageHistoryAsync(job, status, report)) window.Close(); } : null);
            window.Show();
            return window;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { if (EditSessionsActive) Notify("Storage history could not be reviewed: " + Formatters.SafeName(ex.Message), true); return null; }
        finally { EndRecoveryAction(scope); }
    }

    internal async Task<ReportWindow?> ShowRetainedStorageHistoryAsync(RecoveryReviewRecord row)
    {
        if (row.StorageRetainedLeaf is not { } leaf || !EditSessionsActive) return null;
        try
        {
            using var interactive = await StorageAccounting.EnterInteractiveAsync(Services.Io.StoppingToken);
            var snapshot = await Services.Io.RunOwned(PathUtil.GetDeviceKey(Services.Paths.JournalDirectory), IoPriority.Interactive,
                ct => Services.Jobs.ReadRetainedStorageReview(leaf, ct), Services.Io.StoppingToken);
            if (!EditSessionsActive) return null;
            StorageAccounting.Invalidate();
            var ack = snapshot.Acknowledgment;
            string status = ack.Reviewed.EffectUncertain || ack.Reviewed.Damaged || ack.Reviewed.LimitReached
                ? "Reviewed; effect remains uncertain." : "Retained reviewed storage snapshot.";
            string text = status + "\nAcknowledged (UTC): " + ack.AcknowledgedUtc.ToString("O") + "\n" +
                (snapshot.JournalPresent ? "The original journal still exists; this retained summary does not establish its current review status.\n" : "Detailed journal evidence is no longer present.\n") +
                "\n" + snapshot.Report.ReviewText;
            Operations.AddReviewedRecovery(row with { JournalPath = ack.Reviewed.JournalLeaf, CapturedUtc = ack.AcknowledgedUtc, StorageAcknowledgment = ack });
            var window = new ReportWindow("Retained storage review", Formatters.SafeName(row.Title), _ => Task.FromResult(text));
            window.Show(); return window;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { if (EditSessionsActive) Notify("Retained storage history could not be verified: " + Formatters.SafeName(ex.Message), true); }
        return null;
    }

    private async Task SaveStorageCallDetailsAsync(InterruptedJob job, StorageReviewedJournal reviewed)
    {
        if (View.TopLevel is not { } top || !TryBeginRecoveryAction(job, out var scope)) return;
        try
        {
            using var picked = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save exact retained storage call evidence", SuggestedFileName = reviewed.JournalLeaf,
                DefaultExtension = "fcj", FileTypeChoices = [new FilePickerFileType("FileCat storage evidence") { Patterns = ["*.fcj"] }],
            });
            scope.Check();
            if (picked is null) return;
            if (picked.TryGetLocalPath() is not { } destination) { Notify("Choose a local file to export the retained evidence."); return; }
            string privateRoot = Path.GetFullPath(Services.Paths.JournalDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(destination).StartsWith(privateRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            { Notify("Choose an export location outside FileCat's private evidence directory.", true); return; }
            scope.SetPhase(RecoveryActionPhase.Applying, "Saving exact call evidence; Stop review cancels this export…");
            await StorageHistoryIoAsync(job, scope, ct =>
            {
                string temp = Path.Join(Path.GetDirectoryName(destination)!, ".filecat-storage-export-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
                    { Services.Jobs.CopyStorageReview(reviewed, stream, ct); stream.Flush(true); }
                    ct.ThrowIfCancellationRequested(); File.Move(temp, destination, overwrite: true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                return true;
            });
            scope.SetPhase(RecoveryActionPhase.Completed, "Exact call evidence saved.");
            Notify("Saved exact storage call evidence to " + Formatters.SafeName(destination) + ".");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { if (EditSessionsActive) Notify("Storage call evidence was not exported: " + Formatters.SafeName(ex.Message), true); }
        finally { EndRecoveryAction(scope); }
    }

    private async Task<bool> AcknowledgeStorageHistoryAsync(InterruptedJob job, StorageReviewStatus status, string report)
    {
        if (Dialogs.IsOpen || !TryBeginRecoveryAction(job, out var scope)) return false;
        try
        {
            scope.SetPhase(RecoveryActionPhase.AwaitingApproval, "Awaiting acknowledgment of the displayed storage snapshot…");
            const string message = "Acknowledge that you reviewed this exact storage history? This does not establish missing effects or change user files. " +
                "The reviewed summary will remain. Detailed call records may later be removed under the retention policy. " +
                "Use Save call details in the report first if you want a separate copy of all retained evidence.";
            bool approved = Dialogs is OverlayDialogService overlay
                ? await overlay.ConfirmWithCancellationAsync("Acknowledge reviewed storage history", message, "Acknowledge reviewed history", true, scope.StopSource.Token)
                : await Dialogs.ConfirmAsync("Acknowledge reviewed storage history", message, "Acknowledge reviewed history", danger: true);
            scope.Check(); if (!approved) return false;
            scope.SetPhase(RecoveryActionPhase.Applying, "Saving the immutable reviewed summary and its matching acknowledgment…");
            var acknowledged = await StorageHistoryIoAsync(job, scope, ct => Services.Jobs.AcknowledgeStorageReview(status.Current,
                report, StorageReviewChoice.AcknowledgeReviewedHistory, ct));
            if (acknowledged.ReviewPending || acknowledged.Acknowledgment is null) throw new IOException("Durable acknowledgment was not verified; history remains pending.");
            InvalidateStorageAccounting();
            // Core owns the durable pair. Never send it through generic Save/Close/rewrite recovery.
            Operations.AddReviewedRecovery(new(job.JournalPath, job.Title, job.Kind, DateTime.UtcNow,
                "Acknowledged reviewed storage history", report, true) { StorageAcknowledgment = acknowledged.Acknowledgment });
            var pending = Operations.Interrupted.FirstOrDefault(row => PathUtil.SafetyComparer.Equals(row.Job.JournalPath, job.JournalPath));
            if (pending is not null) Operations.Interrupted.Remove(pending);
            Operations.UpdateSummary();
            scope.SetPhase(RecoveryActionPhase.Completed, "Storage history acknowledged; historical uncertainty is preserved.");
            Notify(acknowledged.Current.History.EffectUncertain || acknowledged.Current.History.Damaged || acknowledged.Current.History.LimitReached
                ? "Reviewed; effect remains uncertain." : "Storage history acknowledged. The reviewed summary remains.");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { if (EditSessionsActive) Notify("Storage review remains pending: " + Formatters.SafeName(ex.Message), true); return false; }
        finally { EndRecoveryAction(scope); }
    }
}
