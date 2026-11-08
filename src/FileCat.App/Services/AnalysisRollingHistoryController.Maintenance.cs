using FileCat.Core.Analysis;

namespace FileCat.App.Services;

internal sealed partial class AnalysisRollingHistoryController
{
    private readonly object _maintenanceGate = new();
    private readonly SemaphoreSlim _maintenanceWake = new(0, 1);
    private readonly CancellationTokenSource _maintenanceStop = new();
    private Task? _maintenanceTask;
    private AnalysisRollingCaptureOutcome? _lastMaintenance;
    internal AnalysisRollingCaptureOutcome? LastMaintenance => Volatile.Read(ref _lastMaintenance);

    internal void StartMaintenance()
    {
        lock (_maintenanceGate)
            if (!_maintenanceStop.IsCancellationRequested) _maintenanceTask ??= Task.Run(MaintenanceLoopAsync);
    }
    internal Task StopMaintenanceAsync()
    {
        lock (_maintenanceGate)
        {
            _maintenanceStop.Cancel();
            return _maintenanceTask ?? Task.CompletedTask;
        }
    }
    private void WakeMaintenance()
    {
        lock (_maintenanceGate)
        {
            if (_maintenanceTask is null || _maintenanceStop.IsCancellationRequested) return;
            try { _maintenanceWake.Release(); } catch (SemaphoreFullException) { } // Coalesce changes; never queue timer work.
        }
    }
    private async Task MaintenanceLoopAsync()
    {
        try
        {
            do
            {
                await RunIdleMaintenanceAsync(_maintenanceStop.Token).ConfigureAwait(false);
                await _maintenanceWake.WaitAsync(TimeSpan.FromMinutes(30), _maintenanceStop.Token).ConfigureAwait(false);
            } while (!_maintenanceStop.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (_maintenanceStop.IsCancellationRequested) { }
    }

    internal async Task<AnalysisRollingCaptureOutcome?> RunIdleMaintenanceAsync(CancellationToken cancellation)
    {
        if (!Policy.Enabled || services.SettingsReadOnly) return null; // Off creates no private store or inventory.
        AnalysisRollingCaptureOutcome? known = null;
        try
        {
            var outcome = await HistoryWorkAsync(async () =>
            {
                var policy = Policy;
                if (!policy.Enabled || services.SettingsReadOnly) return (AnalysisRollingCaptureOutcome?)null;
                // Prepare the same app-owned private namespaces, close that preparation,
                // then acquire only the history owner. No source binding or scan is created.
                await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, cancellation, false).ConfigureAwait(false)) { }
                await using var owner = await AnalysisRollingHistoryAccess.OpenAsync(services, cancellation).ConfigureAwait(false);
                known = await owner.PruneRollingAsync(policy, ProtectedHistory(), cancellation).ConfigureAwait(false);
                return known;
            }, cancellation).ConfigureAwait(false);
            Volatile.Write(ref _lastMaintenance, outcome); return outcome;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            RecordHistoryFailure(error);
            var outcome = (error as AnalysisRollingCaptureException)?.Outcome ?? known ?? Refused("Automatic history maintenance was deferred. " + AnalysisNames.Display(error.Message, 400));
            if (AnalysisCloseUnconfirmedException.IsPresent(error)) outcome = outcome with
            {
                State = AnalysisRollingCaptureState.Uncertain,
                Detail = "History cleanup could not be confirmed. " + (outcome.Pruned.Length == 0 ? "No completed removal is claimed."
                    : $"{outcome.Pruned.Length:N0} private payload removals were confirmed before the cleanup failure.") + " Keep remaining files and inspect history before retrying."
            };
            Volatile.Write(ref _lastMaintenance, outcome); return outcome;
        }
    }
}
