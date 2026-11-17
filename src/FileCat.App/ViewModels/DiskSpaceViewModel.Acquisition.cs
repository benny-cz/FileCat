using System.Collections.Immutable;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private AnalysisIndexedAcquisition? _acquisition;
    private TaskCompletionSource? _bindingCompletion;
    private Exception? _bindingCloseError;
    private bool _privateStorageUnavailable;
    private readonly AnalysisViewWork _actualWork = new();
    internal Task CloseCompletion { get; private set; } = Task.CompletedTask;
    internal Task HistoryObservation { get; private set; } = Task.CompletedTask;
    public string? HistoryNotice { get; internal set; }
    internal AppServices? HistoryServices => _services;
    internal CancellationToken HistoryLifetime => _selectionLifetime.Token;

    private static string? PrivateBoundaryNotice(AnalysisSnapshot observations)
    {
        var unavailable = observations.Roots.Where(r => r.Disposition == AnalysisRootDisposition.UnavailablePrivateBoundary).ToArray();
        return unavailable.Length == 0 ? null : "Not read because a private-state boundary could not be verified: " +
            string.Join(", ", unavailable.Select(r => AnalysisNames.Display(r.Label, 120) + " [" + AnalysisNames.Display(r.Id, 64) + "]")) + ". Other verified locations remain available.";
    }

    public async Task StartAsync(IReadOnlyList<string> roots)
    {
        if (_disposed || IsPreparing || Running || _services is null) return;
        if (_bindingCloseError is not null) { SetStatus("Analysis handles could not be closed. Close this analysis before starting another."); return; }
        // A tab owns one acquisition; numeric selections cannot be retagged to another dataset.
        if (_session is not null || _inventory is not null || _observation is not null || Comparison is not null)
        { SetStatus("Choose New analysis to start another scan. This view and its selections keep their existing observations."); return; }
        if (roots.Count is < 1 or > AnalysisLimits.Roots)
        { SetStatus("Choose between one and 32 explicit locations."); return; }
        roots = roots.Take(AnalysisLimits.Roots + 1).ToImmutableArray();
        if (roots.Count is < 1 or > AnalysisLimits.Roots || roots.Any(string.IsNullOrWhiteSpace))
        { SetStatus("Choose between one and 32 explicit locations."); return; }
        if (AnalysisAdmission.Refusal() is { } refusal) { SetStatus(refusal); return; }
        using var preparation = new CancellationTokenSource(); _preparationCancel = preparation;
        var binding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _bindingCompletion = binding;
        bool keepReview = false;
        IsPreparing = true; SetStatus("Binding the selected locations…");
        void HealthChanged(string device, DeviceHealth health)
        {
            if (health != DeviceHealth.NotResponding) return;
            _services.Ui.Post(() =>
            {
                if (_disposed || !ReferenceEquals(_preparationCancel, preparation) || device != _preparationDevice) return;
                preparation.Cancel();
                SetStatus("Location binding stopped because the device is not responding. Its native request remains owned until it returns.");
            });
        }
        _services.Io.HealthChanged += HealthChanged;
        try
        {
            if (!_preparedInput.SequenceEqual(roots, StringComparer.Ordinal) || _prepared.Count != roots.Count)
            {
                await ReleaseUnstartedAsync();
                preparation.Token.ThrowIfCancellationRequested();
                // The public lifecycle remains bounded-memory until indexed resource/native gates
                // qualify. It nevertheless uses the SAME native private-state exclusion boundary.
                // Prepare private directories before capturing sources so those writes cannot stale
                // a selected source directory version. No decoded inventory configures this mode.
                _acquisition = await AnalysisIndexedAcquisition.PrepareAsync(_services, preparation.Token,
                    indexedStorage: false, preparationDeviceChanged: key => _preparationDevice = key);
                SetContinuityNotice(_acquisition.Profile);
                _privateStorageUnavailable = _acquisition.MemoryPreparationNotice is not null;
                AcquisitionNotice = _acquisition.MemoryPreparationNotice ?? "This analysis uses bounded memory. It stops with partial results if the memory limit is reached.";
                foreach (string path in roots)
                {
                    preparation.Token.ThrowIfCancellationRequested();
                    string requested = Path.GetFullPath(path);
                    _preparationDevice = PathUtil.GetDeviceKey(requested);
                    var source = await AnalysisIo.Run(_services.Io, _preparationDevice, _ => _prepareSource(requested), preparation.Token);
                    if (_disposed || preparation.IsCancellationRequested)
                    { await ReleasePreparedSourceAsync(source); preparation.Token.ThrowIfCancellationRequested(); return; }
                    _prepared.Add(source);
                }
                _preparedInput = roots.ToImmutableArray();
                ResolvedTargets = string.Join(Environment.NewLine, _prepared.Select(p => AnalysisNames.Display(p.Root.CanonicalLocator)));
                bool changed = _prepared.Where((p, i) => !SameLocator(p.Root.CanonicalLocator, Path.GetFullPath(roots[i]))).Any();
                if (changed)
                {
                    keepReview = true;
                    SetStatus("Review the resolved locations shown below, then choose Start analysis again to use those captured targets."); return;
                }
            }
            preparation.Token.ThrowIfCancellationRequested();
            if (_disposed) return;
            // Publish the initial state before Start can dispatch a fast terminal progress report.
            Phase = AnalysisPhase.Scanning; SourceCloseFailures = 0; Dirty = true;
            SetStatus("Scanning metadata. Known results so far; allocation can share physical blocks.");
            // Changed subscribers may synchronously stop/close the tab at this notification.
            // That request must win before any source ownership or scan work transfers.
            preparation.Token.ThrowIfCancellationRequested();
            if (_disposed) return;
            var runtimeRoots = _prepared.Select(RuntimeReopenLocator).ToImmutableArray();
            _session = _acquisition!.Start(_prepared.ToArray(), new LatestProgress(_services.Ui, UpdateProgress), Components);
            _runtimeRoots = runtimeRoots;
            _runtimeComponents = Components;
            HistoryObservation = ObserveHistoryAsync(_acquisition);
            _prepared.Clear(); _preparedInput = [];
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested)
        { if (_session is null) Phase = AnalysisPhase.Ready; SetStatus("Location binding stopped. No new scan was started."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        { if (_session is null) Phase = AnalysisPhase.Ready; SetStatus("Analysis could not start: " + AnalysisNames.Display(ex.Message, 500)); }
        finally
        {
            try
            {
                if (_session is null && (!keepReview || _disposed || preparation.IsCancellationRequested)) await ReleaseUnstartedAsync();
            }
            catch (Exception error)
            {
                _bindingCloseError ??= error;
                SetStatus("Analysis handles could not be closed: " + error.GetType().Name);
            }
            finally
            {
                _services.Io.HealthChanged -= HealthChanged;
                if (ReferenceEquals(_preparationCancel, preparation)) { _preparationCancel = null; _preparationDevice = null; }
                IsPreparing = false; binding.TrySetResult(); if (!_disposed) Changed?.Invoke();
            }
        }
    }

    private async Task ObserveHistoryAsync(AnalysisIndexedAcquisition acquisition)
    {
        using var actual = _actualWork.Enter();
        var outcome = await acquisition.HistoryCompletion;
        if (_services?.AnalysisHistory.UnconfirmedClose is { } close) _actualWork.RecordUnconfirmedClose(close);
        if (_disposed) return;
        HistoryNotice = outcome?.Detail; Changed?.Invoke();
    }

    private async Task ReleasePreparedSourceAsync(IAnalysisBackend source)
    {
        if (_services is null) throw new InvalidOperationException("A prepared source requires its owning I/O scheduler.");
        await AnalysisIo.ReleaseAsync(_services.Io, source);
    }
    private async Task ReleaseUnstartedAsync()
    {
        var owned = _prepared.ToArray(); _prepared.Clear(); _preparedInput = []; ResolvedTargets = null;
        var acquisition = _acquisition; _acquisition = null;
        // Attempt every close even when one capability reports a failure. ReleaseAsync stays on
        // the actual admitted worker, including after cancellation or scheduler shutdown.
        var closes = owned.Select(ReleasePreparedSourceAsync).ToList();
        if (acquisition is not null) closes.Add(acquisition.DisposeAsync().AsTask());
        try { await Task.WhenAll(closes); }
        catch (Exception error) { _bindingCloseError ??= error; throw; }
    }
    private async Task CloseAcquisitionAsync(Task actualWork)
    {
        if (_bindingCompletion is { } binding) await binding.Task;
        Exception? failure = _bindingCloseError;
        try { await ReleaseUnstartedAsync(); } catch (Exception error) { failure = AnalysisViewWork.PreserveCloseFailure(failure, error); }
        try { await actualWork; } catch (Exception error) { failure = AnalysisViewWork.PreserveCloseFailure(failure, error); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
