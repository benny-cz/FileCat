using System.Collections.Immutable;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private ImmutableArray<string> _runtimeRoots = [];
    private AnalysisComponentPreset _runtimeComponents;
    private AnalysisSession? _rescanSession;
    private bool _rescanIssued;
    public bool IsRescanning { get; private set; }
    public string? RescanUnavailableReason => _disposed ? "This analysis is closed." :
        _services is null || _acquisition is null || _session is null || _runtimeRoots.IsEmpty || Comparison is not null
            ? "Only an analysis started in this window can rescan its captured locations. Choose New analysis for offline observations." :
        _rescanIssued ? "This result already has a newer observation epoch. Rescan from its newer tab." :
        IsPreparing || Running || IsRescanning || !_session.Completion.IsCompletedSuccessfully
            ? "Wait for the current scan and its source handles to finish closing." :
        _session.SourceCloseFailures != 0 || _bindingCloseError is not null || _actualWork.HasUnconfirmedClose
            ? "Source closure could not be confirmed. Keep this result and restart FileCat before another scan." :
        Tree is null ? "Wait for the captured result to be displayed." : AnalysisAdmission.Refusal();
    public bool CanRescan => RescanUnavailableReason is null;
    internal AnalysisIndexedAcquisition? BranchRuntime => !_disposed && !IsPreparing && !IsRescanning && !Running &&
        !IsIndexedRuntimePreview && _bindingCloseError is null && !_actualWork.HasUnconfirmedClose &&
        _acquisition is { HasRuntimeBranchContext: true } acquisition ? acquisition : null;
    internal string? RuntimeChangeHints() => _disposed ? null : _acquisition?.ReadRuntimeChangeHints();

    private static string RuntimeReopenLocator(IAnalysisBackend source)
        => AnalysisIndexedAcquisition.RuntimeReopenLocator(source);

    // A sibling tab keeps every old mark, basket reference, immutable revision and save owner
    // in its original epoch. Numeric path IDs are never silently reassigned to the new result.
    internal async Task<DiskSpaceViewModel?> CreateRescannedAsync()
    {
        if (RescanUnavailableReason is { } refusal) { SetStatus(refusal); return null; }
        var services = _services!; var previous = _acquisition!; var previousPhase = Phase;
        var view = _publishedView!; var before = Tree!;
        string scope = Selection.Scope; string? focus = Selection.Focus;
        using var actualWork = _actualWork.Enter();
        using var preparation = new CancellationTokenSource(); _preparationCancel = preparation;
        var binding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _bindingCompletion = binding;
        AnalysisIndexedAcquisition? next = null; DiskSpaceViewModel? result = null;
        var prepared = new List<IAnalysisBackend>();
        IsRescanning = IsPreparing = true;
        void HealthChanged(string device, DeviceHealth health)
        {
            if (health != DeviceHealth.NotResponding) return;
            services.Ui.Post(() =>
            {
                if (_disposed || !ReferenceEquals(_preparationCancel, preparation) || device != _preparationDevice) return;
                preparation.Cancel(); SetStatus("Rescan binding stopped because the device is not responding. Waiting for its owned native request.");
            });
        }
        services.Io.HealthChanged += HealthChanged;
        try
        {
            SetStatus("Revalidating the captured locations. This result and its selections remain available.");
            preparation.Token.ThrowIfCancellationRequested();
            next = await AnalysisIndexedAcquisition.PrepareRescanAsync(previous, preparation.Token, key => _preparationDevice = key);
            foreach (string path in _runtimeRoots)
            {
                preparation.Token.ThrowIfCancellationRequested();
                _preparationDevice = PathUtil.GetDeviceKey(path);
                var source = await AnalysisIo.Run(services.Io, _preparationDevice, _ => _prepareSource(path), preparation.Token);
                prepared.Add(source); // Own a late native return before observing cancellation.
                preparation.Token.ThrowIfCancellationRequested();
            }
            if (_disposed) return null;
            Phase = AnalysisPhase.Refreshing;
            SetStatus("Rescanning metadata. The previous result stays in this tab; refreshed observations open in a new tab.");
            preparation.Token.ThrowIfCancellationRequested();
            if (_disposed) return null;
            var progress = new LatestProgress(services.Ui, update =>
            {
                if (_disposed || !IsRescanning || _rescanSession is null || update.SessionId != before.Snapshot.Identity.SessionId ||
                    update.Epoch != before.Snapshot.Identity.Epoch + 1) return;
                Phase = update.Phase;
                SetStatus($"Rescan: {update.Phase} · {update.ObservedPaths:N0} observed paths · {update.Issues:N0} issues. Previous observations remain displayed.");
            });
            var session = next.Start(prepared.ToArray(), progress, _runtimeComponents);
            _rescanSession = session; _rescanIssued = true; prepared.Clear();
            _preparationCancel = null; _preparationDevice = null; IsPreparing = false; Changed?.Invoke();
            await session.Completion; // Stop retains a partial successor; actual native work remains owned.
            if (_disposed) return null;
            var snapshot = session.Snapshot();
            result = new(services, _prepareSource, _indexedStorage)
            {
                _session = session, _acquisition = next, _runtimeRoots = _runtimeRoots, _runtimeComponents = _runtimeComponents,
                Components = _runtimeComponents, Phase = snapshot.Phase, SourceCloseFailures = session.SourceCloseFailures,
                Metric = view.Metric, Chart = Chart, ColorDimension = ColorDimension, Grouping = view.Grouping,
                Query = view.Query, RootFilter = view.Root, CaseSensitive = view.CaseSensitive,
                ShowUnknownFilterResults = view.ShowUnknown, Sort = view.Sort, SortDescending = view.SortDescending,
                _rootDisplayIds = view.RootDisplayIds,
                _privateStorageUnavailable = next.MemoryPreparationNotice is not null
            };
            next = null;
            result.SetContinuityNotice(result._acquisition!.Profile);
            result.HistoryObservation = result.ObserveHistoryAsync(result._acquisition);
            string newScope = AnalysisRefreshNavigation.Map(before, snapshot, scope) ?? AnalysisTree.RootKey;
            result.Selection.ResetNavigation(newScope);
            await result.RebuildAsync(true, preparation.Token);
            if (_disposed) return null;
            if (result.Tree is null) throw new InvalidOperationException("The refreshed view could not be prepared.");
            string? newFocus = focus is null ? null : AnalysisRefreshNavigation.Map(before, snapshot, focus);
            if (newFocus is not null && result.Tree.Items.ContainsKey(newFocus)) result.Focus(newFocus);
            result.SetStatus($"Rescan {snapshot.Phase}. The previous result, marks and review basket remain in the earlier tab." +
                (session.SourceCloseFailures == 0 ? "" : " Source closure could not be confirmed; another rescan is unavailable.") +
                (focus is not null && newFocus is null ? " The previous focused entry could not be verified in this epoch." : ""));
            SetStatus("Rescan finished. Refreshed observations open in a new tab; this result and its selections were kept.");
            var published = result; result = null; return published;
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested)
        { SetStatus("Rescan binding stopped. The previous observations and selections were kept."); return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        { SetStatus("Rescan unavailable: " + AnalysisNames.Display(error.Message, 400) + " Previous observations were kept."); return null; }
        finally
        {
            try
            {
                var closes = prepared.Select(ReleasePreparedSourceAsync).ToList();
                if (next is not null) closes.Add(next.DisposeAsync().AsTask());
                if (result is not null) { result.Dispose(); closes.Add(result.CloseCompletion); }
                await Task.WhenAll(closes);
            }
            catch (Exception error)
            { _bindingCloseError ??= error; SetStatus("Rescan handles could not be closed. Keep these observations and restart FileCat."); }
            finally
            {
                services.Io.HealthChanged -= HealthChanged;
                if (ReferenceEquals(_preparationCancel, preparation)) _preparationCancel = null;
                _preparationDevice = null; _rescanSession = null; IsPreparing = IsRescanning = false; Phase = previousPhase;
                binding.TrySetResult(); if (!_disposed) Changed?.Invoke();
            }
        }
    }
}
