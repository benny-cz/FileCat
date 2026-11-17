using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private StorageHistoryBookmark? _storageHistoryAfter;
    private bool _storageRetainedPhase, _storageHistoryAtEnd;
    private CancellationTokenSource? _storageHistoryReading;

    internal void StopStorageHistoryDiscovery()
    {
        _storageHistoryReading?.Cancel(); _storageAccounting?.Stop();
        if (Operations.IsStorageHistoryLoading) Operations.StorageHistoryStatus = "Stopping history work; waiting for the current read to retire…";
    }

    internal async Task LoadStorageHistoryPageAsync(bool restart = false)
    {
        if (!EditSessionsActive || !Services.Jobs.SupportsStorageHistory || Operations.IsStorageHistoryLoading) return;
        if (restart || _storageHistoryAtEnd)
        {
            _storageHistoryAfter = null; _storageRetainedPhase = _storageHistoryAtEnd = false;
            Operations.ResetStorageRowsOmitted();
        }
        using var stop = _storageHistoryReading = CancellationTokenSource.CreateLinkedTokenSource(Services.Io.StoppingToken);
        Operations.IsStorageHistoryLoading = true;
        Operations.StorageHistoryStatus = "Reading a bounded storage history page; accounting/verification is still in progress…";
        try
        {
            using var interactive = await StorageAccounting.EnterInteractiveAsync(stop.Token);
            if (restart) StorageAccounting.Invalidate(refresh: true);
            // The worker owns and closes its cursor before returning. Only this small detached bookmark crosses
            // the UI wait; a replay opens names only, never old headers or a full candidate array.
            var after = _storageHistoryAfter; bool retained = _storageRetainedPhase;
            var page = await Services.Io.RunOwned(PathUtil.GetDeviceKey(Services.Paths.JournalDirectory), IoPriority.Interactive,
                ct => Services.Jobs.ReadStorageHistoryPage(after, retained, ct), stop.Token);
            if (!retained && !page.More && page.Rows.Length == 0)
            {
                // With no journal row to publish, a separate closed-cursor slice exposes retained reports immediately.
                retained = true; _storageRetainedPhase = true; _storageHistoryAfter = null;
                page = await Services.Io.RunOwned(PathUtil.GetDeviceKey(Services.Paths.JournalDirectory), IoPriority.Interactive,
                    ct => Services.Jobs.ReadStorageHistoryPage(null, true, ct), stop.Token);
            }
            if (!EditSessionsActive || stop.IsCancellationRequested) return;
            foreach (var row in page.Rows)
            {
                if (row.RetainedReview)
                {
                    Operations.AddReviewedRecovery(new(row.FileName, "Retained storage history", nameof(JobKind.StorageTransform),
                        DateTime.MinValue, "Verification required", string.Empty, false) { StorageRetainedLeaf = row.FileName });
                }
                else Operations.AddInterrupted(Services.Jobs.StorageHistoryCandidate(row.FileName) with
                {
                    Title = "Storage changes — " + (row.Action is { } action ? StorageOperationText.Action(action) : "verification required"),
                    SourceCount = row.RootCount ?? 0,
                });
            }
            _storageHistoryAfter = page.Continuation;
            if (!page.More)
            {
                if (!retained) { _storageRetainedPhase = true; _storageHistoryAfter = null; }
                else _storageHistoryAtEnd = true;
            }
            Operations.HasMoreStorageHistory = !_storageHistoryAtEnd || Operations.StorageRowsOmitted;
            Operations.StorageHistoryStatus = Operations.StorageDescriptorCount == 0 && _storageHistoryAtEnd ? string.Empty
                : $"{Operations.StorageDescriptorCount:N0} storage history rows retained (maximum 64). " +
                  "Opening a row verifies its exact evidence; the accounting result is shown separately." +
                  (Operations.StorageRowsOmitted ? " Older rows remain in evidence; use More or Refresh to browse again." : "");
            if (Operations.StorageDescriptorCount > 0) Operations.IsOpen = true;
            Operations.UpdateSummary();
        }
        catch (OperationCanceledException)
        {
            if (EditSessionsActive)
            {
                Operations.HasMoreStorageHistory = true;
                Operations.StorageHistoryStatus = "Storage history discovery stopped; retained evidence is unchanged. More resumes the last completed page.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _storageAccounting?.Invalidate();
            if (EditSessionsActive)
            {
                Operations.HasMoreStorageHistory = true;
                Operations.StorageHistoryStatus = "Storage history discovery could not finish: " + Formatters.SafeName(ex.Message) + " Use Refresh to rescan; retained evidence is unchanged.";
            }
        }
        finally
        {
            if (ReferenceEquals(_storageHistoryReading, stop)) _storageHistoryReading = null;
            Operations.IsStorageHistoryLoading = false;
            StartStorageAccounting();
        }
    }
}
