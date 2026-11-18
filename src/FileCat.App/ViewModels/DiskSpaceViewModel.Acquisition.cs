using System.Collections.Immutable;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private readonly bool _indexedStorage;
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
    internal long? ScratchReservedBytes => _session?.ScratchReservedBytes;
    internal bool CanChooseStandardIndex => _services is not null && !_indexedStorage;
    private ImmutableDictionary<string, AnalysisVolumeCapacity> _capturedVolumeCapacities = ImmutableDictionary<string, AnalysisVolumeCapacity>.Empty;
    internal ImmutableDictionary<string, AnalysisVolumeCapacity> VolumeCapacities => _session?.VolumeCapacities ?? _capturedVolumeCapacities;

    internal static DiskSpaceViewModel ForCurrentSource(AppServices services, AnalysisKind kind) => new(services, path =>
    {
        var source = Prepare(path);
        try
        {
            if (source.RootEntry.Kind != kind || PathUtil.IsOnNetwork(source.Root.CanonicalLocator) ||
                OperatingSystem.IsWindows() && source.Root.CanonicalLocator.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The chosen item changed type or resolves to a network location. Choose a current local item again.");
            return source;
        }
        catch { source.Dispose(); throw; }
    });

    internal static DiskSpaceViewModel AdoptAcquisition(AnalysisIndexedAcquisition acquisition, AppServices services, AnalysisSavedView? view)
    {
        var session = acquisition.RuntimeSession;
        if (!session.Completion.IsCompletedSuccessfully || session.DatasetDescriptor is not null)
            throw new InvalidOperationException("Adopt only a completed bounded-memory acquisition here.");
        var settings = acquisition.RuntimeSettings;
        var model = new DiskSpaceViewModel(services, acquisition.IndexedStorage)
        {
            _acquisition = acquisition, _session = session, _runtimeRoots = settings.Locations.ToImmutableArray(),
            _runtimeComponents = settings.Components, Components = settings.Components, Phase = session.Snapshot().Phase,
            SourceCloseFailures = session.SourceCloseFailures, _privateStorageUnavailable = acquisition.MemoryPreparationNotice is not null
        };
        model.SetContinuityNotice(acquisition.Profile); model.HistoryObservation = model.ObserveHistoryAsync(acquisition);
        if (view is not null)
        {
            model.Metric = view.Metric; model.Chart = view.Chart; model.ColorDimension = view.ColorDimension;
            model.Grouping = view.Grouping; model.Query = view.Query; model.CaseSensitive = view.CaseSensitive; model.PanelFilter = view.PanelFilter;
            model.ShowUnknownFilterResults = view.ShowUnknown; model.RootFilter = view.Root; model._rootDisplayIds = view.RootDisplayIds;
            model.Sort = view.Sort; model.SortDescending = view.SortDescending;
            // Numeric IDs from an indexed predecessor are never reused for a memory fallback.
        }
        model.SetStatus("Viewing the captured result. Rescan all opens refreshed observations in another tab.");
        return model;
    }

    private static string? PrivateBoundaryNotice(AnalysisSnapshot observations)
    {
        var unavailable = observations.Roots.Where(r => r.Disposition == AnalysisRootDisposition.UnavailablePrivateBoundary).ToArray();
        return unavailable.Length == 0 ? null : "Not read because a private-state boundary could not be verified: " +
            string.Join(", ", unavailable.Select(r => AnalysisNames.Display(r.Label, 120) + " [" + AnalysisNames.Display(r.Id, 64) + "]")) + ". Other verified locations remain available.";
    }

    internal string? OfferedPanelFilter { get; init; }
    internal string? PanelFilter { get; set; }
    internal bool PanelFilterApplied => _publishedView?.PanelFilter is not null;

    public Task StartAsync(IReadOnlyList<string> roots, ImmutableArray<AnalysisFolderExclusion> exclusions = default, bool usePanelFilter = false) =>
        StartAnalysisAsync(roots, null, exclusions, usePanelFilter);

    internal Task StartAsync(IReadOnlyList<string> roots, ImmutableArray<AnalysisFolderExclusion> exclusions, bool usePanelFilter, bool usePrivateIndex) =>
        StartAnalysisAsync(roots, null, exclusions, usePanelFilter, usePrivateIndex: usePrivateIndex);

    internal Task StartReviewedCurrentSourceAsync(string path, Func<string, CancellationToken, Task<bool>> review, CancellationToken cancellation) =>
        StartAnalysisAsync([path], null, [], false, review, cancellation);

    private async Task StartAnalysisAsync(IReadOnlyList<string> roots, FastNtfsRequest? fast, ImmutableArray<AnalysisFolderExclusion> exclusions,
        bool usePanelFilter, Func<string, CancellationToken, Task<bool>>? reviewCurrentSource = null, CancellationToken cancellation = default,
        bool usePrivateIndex = false)
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
        string? selection = null;
        if (fast is not null)
        {
            if (!exclusions.IsDefaultOrEmpty) { SetStatus(FastNtfsFolderExclusionsReason); return; }
            if (FastNtfsUnavailableReason is { } unavailable) { SetStatus(unavailable); return; }
            if (roots.Count != 1 || (selection = NtfsMetadataVolume.NormalizeSelection(roots[0])) is null)
            { SetStatus("Fast NTFS scan needs one whole local NTFS volume. Enter a drive root such as C:\\ or its volume GUID. Use standard analysis for files and folders."); return; }
        }
        using var preparation = cancellation.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellation) : new CancellationTokenSource();
        _preparationCancel = preparation;
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
            WindowsAnalysisBackend.NtfsScan? ntfs = null;
            var components = Components;
            bool indexedStorage = _indexedStorage || usePrivateIndex || fast is not null;
            if (fast is not null || _acquisition?.IndexedStorage != indexedStorage ||
                !_preparedInput.SequenceEqual(roots, StringComparer.Ordinal) || _prepared.Count != roots.Count)
            {
                await ReleaseUnstartedAsync();
                preparation.Token.ThrowIfCancellationRequested();
                // Prepare private directories before capturing sources so those writes cannot stale
                // a selected source directory version. No decoded inventory configures this mode.
                _acquisition = await AnalysisIndexedAcquisition.PrepareAsync(_services, preparation.Token,
                    indexedStorage: indexedStorage, preparationDeviceChanged: key => _preparationDevice = key);
                SetContinuityNotice(_acquisition.Profile);
                _privateStorageUnavailable = _acquisition.MemoryPreparationNotice is not null;
                AcquisitionNotice = _acquisition.MemoryPreparationNotice ?? (_acquisition.IndexedStorage
                    ? "A private index retains recorded paths. The live preview is bounded."
                    : "This analysis uses bounded memory. It stops with partial results if the memory limit is reached.");
                if (fast is not null)
                {
                    _preparationDevice = PathUtil.GetDeviceKey(selection!);
                    ntfs = await _acquisition.PrepareNtfsAsync(selection!, fast.Owner, preparation.Token);
                    _prepared.Add(ntfs.Source);
                    preparation.Token.ThrowIfCancellationRequested();
                    if (_disposed) return;
                    var scope = new FastNtfsScopeReview(selection!, ntfs.Volume.GuidPath, components);
                    if (!await fast.Confirm(scope, preparation.Token))
                    { SetStatus("Fast NTFS scan canceled. Standard analysis is available."); return; }
                }
                else
                {
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
                    if (reviewCurrentSource is not null)
                    {
                        // The review names the object we already hold. Start transfers that same binding.
                        if (!await reviewCurrentSource(RuntimeReopenLocator(_prepared.Single()), preparation.Token))
                        { SetStatus("Current source review canceled."); return; }
                    }
                    else if (_prepared.Where((p, i) => !SameLocator(p.Root.CanonicalLocator, Path.GetFullPath(roots[i]))).Any())
                    {
                        keepReview = true;
                        SetStatus("Review the resolved locations shown below, then choose Start analysis again to use those captured targets."); return;
                    }
                }
            }
            preparation.Token.ThrowIfCancellationRequested();
            if (_disposed) return;
            // Publish the initial state before Start can dispatch a fast terminal progress report.
            Phase = AnalysisPhase.Scanning; SourceCloseFailures = 0; Dirty = true;
            SetStatus(ntfs is null ? "Scanning metadata. Known results so far; allocation can share physical blocks." :
                "Waiting for authorization to read the approved NTFS volume. You can stop the scan.");
            // Changed subscribers may synchronously stop/close the tab at this notification.
            // That request must win before any source ownership or scan work transfers.
            preparation.Token.ThrowIfCancellationRequested();
            if (_disposed) return;
            var runtimeRoots = ntfs is null ? _prepared.Select(RuntimeReopenLocator).ToImmutableArray() : [];
            var progress = new LatestProgress(_services.Ui, UpdateProgress);
            _fastNtfs = ntfs is not null;
            _session = ntfs is null ? _acquisition!.Start(_prepared.ToArray(), progress, components, exclusions) :
                _acquisition!.StartNtfs(ntfs, progress, components);
            PanelFilter = usePanelFilter && OfferedPanelFilter is { Length: > 0 } ? OfferedPanelFilter : null;
            _runtimeRoots = runtimeRoots;
            _runtimeComponents = components;
            HistoryObservation = ObserveHistoryAsync(_acquisition);
            _prepared.Clear(); _preparedInput = [];
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested)
        { if (_session is null) Phase = AnalysisPhase.Ready; SetStatus("Location binding stopped. No new scan was started."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        { if (_session is null) Phase = AnalysisPhase.Ready; SetStatus("Analysis could not start: " + AnalysisNames.Display(ex.Message, 500) + (fast is null ? "" : " Standard analysis is available.")); }
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
