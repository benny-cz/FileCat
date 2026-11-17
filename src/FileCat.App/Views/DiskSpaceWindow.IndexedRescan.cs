using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedSessionView
    {
        private readonly Button _runtimeRescan = new() { Content = "Rescan all" },
            _runtimePause = new() { Content = "Pause rescan", IsVisible = false }, _runtimeStop = new() { Content = "Stop rescan", IsVisible = false };
        private readonly Border _runtimeRescanHelp = new() { Focusable = true };
        private CancellationTokenSource? _runtimeRescanCancel;
        private AnalysisIndexedAcquisition? _runtimeRefresh;
        private bool _runtimeRescanIssued;
        private AnalysisPhase _runtimePhase;

        private void AddRuntimeRescanControls(WrapPanel bar)
        {
            _runtimeRescanHelp.Child = _runtimeRescan; bar.Children.Add(_runtimeRescanHelp); bar.Children.Add(_runtimePause); bar.Children.Add(_runtimeStop);
            AutomationProperties.SetName(_runtimeRescanHelp, "Rescan captured locations");
            _runtimeRescan.Click += async (_, _) => await RescanRuntimeAsync();
            _runtimePause.Click += (_, _) =>
            {
                if (_runtimeRefresh is null) return;
                try
                {
                    if (_runtimePhase is AnalysisPhase.Paused or AnalysisPhase.Pausing) _runtimeRefresh.RuntimeSession.Resume();
                    else _runtimeRefresh.RuntimeSession.Pause();
                }
                catch (ObjectDisposedException) { }
            };
            _runtimeStop.Click += (_, _) => StopRuntimeRescan();
            ToolTip.SetTip(_runtimePause, new RichTip("Pause or continue the rescan", detail: "Pause new work in the current rescan. The previous result remains available."));
            ToolTip.SetTip(_runtimeStop, new RichTip("Stop the rescan", detail: "Stop scheduling new work and keep partial observations. Native requests remain owned until they return."));
            RefreshRuntimeRescanControls();
        }

        internal void RefreshRuntimeRescanControls()
        {
            if (_disposed) return;
            string? refusal = _runtimeAcquisition is null ? "These captured observations have no runtime source context. Choose New analysis to scan locations." :
                _runtimeRescanIssued ? "This epoch has started a successor. Rescan from its tab, or choose New analysis if it could not be opened." :
                _runtimeRescanCancel is not null ? "A rescan is already in progress." :
                !_runtimeAcquisition.HasRuntimeRescanContext ? "Wait for actual source closure. Unconfirmed source cleanup prevents another scan." :
                !_window._admissionPool.HasCapacity ? _window._admissionPool.Refusal :
                _actualWork.HasUnconfirmedClose ? "An owner could not close. Preserve this result and restart FileCat." : null;
            _runtimeRescan.IsEnabled = refusal is null;
            string detail = refusal ?? "Scan the same captured locations and component policy again. Keep this result, marks and review basket; open refreshed observations in another tab.";
            AutomationProperties.SetHelpText(_runtimeRescanHelp, detail);
            ToolTip.SetTip(_runtimeRescanHelp, new RichTip("Rescan captured locations", detail: detail));
            bool active = _runtimeRescanCancel is not null;
            _runtimeStop.IsVisible = active; _runtimePause.IsVisible = active && _runtimeRefresh is not null;
            _runtimePause.Content = _runtimePhase is AnalysisPhase.Paused or AnalysisPhase.Pausing ? "Continue rescan" : "Pause rescan";
        }

        private void StopRuntimeRescan()
        {
            if (_runtimeRefresh is null) { _runtimeRescanCancel?.Cancel(); return; }
            try { _runtimeRefresh.RuntimeSession.Stop(); } catch (ObjectDisposedException) { }
        }

        private async Task RescanRuntimeAsync()
        {
            if (_disposed || _runtimeRescanCancel is not null || _runtimeRescanIssued || _runtimeAcquisition is not { HasRuntimeRescanContext: true } original || _services is null)
                return;
            using var actual = _actualWork.Enter();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token);
            _runtimeRescanCancel = cancel; _runtimePhase = AnalysisPhase.Scanning;
            var captured = CapturePublishedView(); ulong scope = _scope, focus = _focus;
            ulong? restored = null;
            string? preparingDevice = null; bool bindingUnresponsive = false;
            void HealthChanged(string device, DeviceHealth health)
            {
                if (health != DeviceHealth.NotResponding) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed || !ReferenceEquals(_runtimeRescanCancel, cancel) || _runtimeRefresh is not null ||
                        !StringComparer.OrdinalIgnoreCase.Equals(device, Volatile.Read(ref preparingDevice))) return;
                    bindingUnresponsive = true; cancel.Cancel();
                    _status.Text = "Rescan binding stopped because the device is not responding. Waiting for its outstanding request to return. Previous observations remain available.";
                });
            }
            _services.Io.HealthChanged += HealthChanged;
            var progress = new Progress<AnalysisProgress>(update => Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || !ReferenceEquals(_runtimeRescanCancel, cancel) || update.SessionId != _source.Descriptor.Identity.SessionId ||
                    update.Epoch != _source.Descriptor.Identity.Epoch + 1) return;
                _runtimePhase = update.Phase;
                _status.Text = $"Rescan: {update.Phase} · {update.ObservedPaths:N0} observed paths · {update.Issues:N0} issues. Previous observations remain displayed.";
                RefreshRuntimeRescanControls();
            }));
            RefreshRuntimeRescanControls();
            try
            {
                await _window.AddOwnedAcquisitionAsync(_services, async token =>
                {
                    var next = await original.BeginRescanAsync(progress, token, device => Volatile.Write(ref preparingDevice, device));
                    _runtimeRefresh = next; _runtimeRescanIssued = true;
                    if (_disposed) { await next.DisposeAsync(); token.ThrowIfCancellationRequested(); throw new ObjectDisposedException(nameof(IndexedSessionView)); }
                    RefreshRuntimeRescanControls(); return next;
                }, captured with { Scope = AnalysisTree.RootKey }, cancel.Token, async (current, token) =>
                {
                    ulong? mappedScope = captured.Grouping == AnalysisGroupingDimension.FolderHierarchy
                        ? await AnalysisRefreshNavigation.MapAsync(_source, current, scope, token) : null;
                    restored = focus == 0 || captured.Grouping != AnalysisGroupingDimension.FolderHierarchy ? null : await AnalysisRefreshNavigation.MapAsync(_source, current, focus, token);
                    return captured with { Scope = mappedScope is > 0 ? AnalysisTree.Key(mappedScope.Value) : AnalysisTree.RootKey };
                }, () => restored);
                if (!_disposed) _status.Text = "Refreshed observations opened in another tab. This result, marks and review basket were kept.";
            }
            catch (Exception error) when (Expected(error))
            {
                RememberViewCloseFailure(error);
                if (!_disposed) _status.Text = bindingUnresponsive
                    ? "Rescan unavailable: the device is not responding. Wait for its outstanding work to finish. Previous observations were kept."
                    : "Rescan unavailable: " + AnalysisNames.Display(error.Message, 400) + " Previous observations were kept.";
            }
            finally
            {
                _services.Io.HealthChanged -= HealthChanged;
                _runtimeRescanCancel = null; _runtimeRefresh = null;
                if (!_disposed) RefreshRuntimeRescanControls();
            }
        }

        internal async Task RestoreRuntimeFocusAsync(ulong focus)
        {
            _publishing = true;
            try
            {
                _list.SelectedItem = _shownPage?.Children.Rows.FirstOrDefault(row => row.Path.Id == focus);
                if (_list.SelectedItem is { } selected) _list.ScrollIntoView(selected);
            }
            finally { _publishing = false; }
            await InspectAsync(focus);
        }
    }
}
