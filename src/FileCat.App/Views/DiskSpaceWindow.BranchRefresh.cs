using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private const string BranchHelp = "Scan the selected captured folder in a new tab after verifying its live identity. The previous observations and review stay in their original tab.";

    private async Task RefreshBranchAsync(AnalysisIndexedAcquisition owner, AppServices services, SessionIdentity revision, ulong path,
        AnalysisSavedView previousView, CancellationToken cancellation, Action<string> report)
    {
        // Root-ID and relative-path filters from the earlier scope cannot be
        // silently reinterpreted inside a branch with a different root.
        var view = previousView with
        {
            Scope = AnalysisTree.RootKey,
            Root = null,
            RootDisplayIds = null,
            Query = "",
            CaseSensitive = false,
            ShowUnknown = false,
            Grouping = AnalysisGroupingDimension.FolderHierarchy
        };
        report("Revalidating the captured folder. Its previous observations remain available.");
        ulong? focus = null;
        var progress = new Progress<AnalysisProgress>(update =>
        {
            if (!cancellation.IsCancellationRequested)
                report($"Branch refresh: {update.Phase} · {update.ObservedPaths:N0} observed paths.");
        });
        var opened = await AddOwnedAcquisitionAsync(services,
            token => owner.BeginBranchRefreshAsync(revision, path, progress, token), view, cancellation,
            async (dataset, token) =>
            {
                var root = await dataset.ReadPathAsync(1, token);
                if (root.ParentId == 0 && root.Kind == AnalysisKind.Directory) focus = root.Id;
                return view;
            }, () => focus);
        if (opened.Memory?.Tree is { } tree && tree.Snapshot.Nodes.FirstOrDefault() is { ParentId: 0 } memoryRoot)
            opened.Memory.Focus(AnalysisTree.Key(memoryRoot.Id));
        report("The refreshed branch opened in a new tab with its own totals. The earlier observations and review were kept.");
    }

    private static void SetBranchAvailability(Button button, Border help, string? refusal, ref string? previous)
    {
        button.IsEnabled = refusal is null;
        string detail = refusal ?? BranchHelp;
        if (detail == previous) return;
        previous = detail;
        AutomationProperties.SetHelpText(help, detail);
        ToolTip.SetTip(help, new RichTip("Refresh captured folder", detail: detail));
    }

    private sealed partial class SessionView
    {
        private readonly Button _refreshBranch = new() { Content = "Refresh branch" };
        private readonly Button _stopBranchRefresh = new() { Content = "Stop branch refresh", IsVisible = false };
        private readonly Border _refreshBranchHelp = new() { Focusable = true };
        private CancellationTokenSource? _branchRefresh;
        private string? _branchHelpText;

        private void AddBranchRefreshControls(WrapPanel toolbar)
        {
            _refreshBranchHelp.Child = _refreshBranch;
            toolbar.Children.Add(_refreshBranchHelp);
            toolbar.Children.Add(_stopBranchRefresh);
            AutomationProperties.SetName(_refreshBranchHelp, "Refresh captured folder");
            _refreshBranch.Click += async (_, _) => await StartBranchRefreshAsync();
            _stopBranchRefresh.Click += (_, _) => _branchRefresh?.Cancel();
            ToolTip.SetTip(_stopBranchRefresh, new RichTip("Stop branch refresh", detail: "Close the pending result and keep the earlier captured observations."));
        }

        private ulong? BranchFolder
        {
            get
            {
                if (Model.Tree is not { } tree || tree.GroupingDimension != AnalysisGroupingDimension.FolderHierarchy) return null;
                if (Model.Selection.Scope.StartsWith("other:", StringComparison.Ordinal)) return null;
                if (Model.Selection.Focus is { } focus)
                    return tree.Items.GetValueOrDefault(focus) is { Kind: AnalysisKind.Directory, NodeId: { } focused } ? focused : null;
                return tree.Items.GetValueOrDefault(Model.Selection.Scope) is { Kind: AnalysisKind.Directory, NodeId: { } current } ? current : null;
            }
        }

        private void RefreshBranchAvailability()
        {
            _refreshBranchHelp.IsVisible = Model.Tree is not null;
            _stopBranchRefresh.IsVisible = _branchRefresh is not null;
            var refusal = _branchRefresh is not null ? "A branch refresh is already running." :
                Model.BranchRuntime is null ? "Available from a finished live result after its sources finish closing." :
                BranchFolder is null ? "Select a real folder in the captured folder hierarchy." :
                !_window._admissionPool.HasCapacity ? _window._admissionPool.Refusal : null;
            SetBranchAvailability(_refreshBranch, _refreshBranchHelp, refusal, ref _branchHelpText);
        }

        private async Task StartBranchRefreshAsync()
        {
            if (_disposed || _branchRefresh is not null || Model.BranchRuntime is not { } owner || BranchFolder is not { } path ||
                Model.HistoryServices is not { } services || Model.Tree is not { } tree || !_window._admissionPool.HasCapacity) return;
            using var actual = _actualWork.Enter();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Model.HistoryLifetime);
            _branchRefresh = cancel;
            RefreshBranchAvailability();
            try
            {
                await _window.RefreshBranchAsync(owner, services, tree.Snapshot.Identity, path, Model.PublishedReportView, cancel.Token,
                    text => { if (!_disposed && ReferenceEquals(_branchRefresh, cancel)) Model.SetStatus(text); });
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            { if (!_disposed) Model.SetStatus("Branch refresh stopped. The earlier observations were kept."); }
            catch (Exception error) when (IndexedExpectedError(error))
            {
                if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                if (!_disposed) Model.SetStatus("Branch refresh unavailable: " + AnalysisNames.Display(error.Message, 400) + " The earlier observations were kept.");
            }
            finally { _branchRefresh = null; if (!_disposed) RefreshBranchAvailability(); }
        }
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _runtimeBranch = new() { Content = "Refresh branch" };
        private readonly Button _runtimeStopBranch = new() { Content = "Stop branch refresh", IsVisible = false };
        private readonly Border _runtimeBranchHelp = new() { Focusable = true };
        private CancellationTokenSource? _runtimeBranchCancel;
        private string? _runtimeBranchHelpText;

        private void AddBranchRefreshControls(WrapPanel toolbar)
        {
            _runtimeBranchHelp.Child = _runtimeBranch;
            toolbar.Children.Add(_runtimeBranchHelp);
            toolbar.Children.Add(_runtimeStopBranch);
            AutomationProperties.SetName(_runtimeBranchHelp, "Refresh captured folder");
            _runtimeBranch.Click += async (_, _) => await StartBranchRefreshAsync();
            _runtimeStopBranch.Click += (_, _) => _runtimeBranchCancel?.Cancel();
            ToolTip.SetTip(_runtimeStopBranch, new RichTip("Stop branch refresh", detail: "Close the pending result and keep the earlier captured observations."));
        }

        private ulong? BranchFolder
        {
            get
            {
                if (_grouping is not null || _explorer.OtherScopeKey is not null) return null;
                if (_focus != 0)
                    return _shownInspection?.Row is { IsVisible: true, Path.Kind: AnalysisKind.Directory } selected && selected.Path.Id == _focus
                        ? selected.Path.Id : null;
                return _scope != 0 ? _scope : null;
            }
        }

        private void RefreshBranchAvailability()
        {
            _runtimeStopBranch.IsVisible = _runtimeBranchCancel is not null;
            var refusal = _runtimeBranchCancel is not null || _runtimeRescanCancel is not null ? "An analysis refresh is already running." :
                _runtimeAcquisition is not { HasRuntimeBranchContext: true } ? "Available from a finished live result after its sources finish closing." :
                _actualWork.HasUnconfirmedClose ? "Source cleanup could not be verified." :
                BranchFolder is null ? "Select a real folder in the captured folder hierarchy." :
                !_window._admissionPool.HasCapacity ? _window._admissionPool.Refusal : null;
            SetBranchAvailability(_runtimeBranch, _runtimeBranchHelp, refusal, ref _runtimeBranchHelpText);
        }

        private async Task StartBranchRefreshAsync()
        {
            if (_disposed || _runtimeBranchCancel is not null || _runtimeRescanCancel is not null || _services is not { } services ||
                _runtimeAcquisition is not { HasRuntimeBranchContext: true } owner || BranchFolder is not { } path ||
                _actualWork.HasUnconfirmedClose || !_window._admissionPool.HasCapacity) return;
            using var actual = _actualWork.Enter();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token);
            _runtimeBranchCancel = cancel;
            RefreshRuntimeRescanControls();
            try
            {
                await _window.RefreshBranchAsync(owner, services, _source.Descriptor.Identity, path, CapturePublishedView(), cancel.Token,
                    text => { if (!_disposed && ReferenceEquals(_runtimeBranchCancel, cancel)) _status.Text = text; });
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            { if (!_disposed) _status.Text = "Branch refresh stopped. The earlier observations were kept."; }
            catch (Exception error) when (Expected(error))
            {
                RememberViewCloseFailure(error);
                if (!_disposed) _status.Text = "Branch refresh unavailable: " + AnalysisNames.Display(error.Message, 400) + " The earlier observations were kept.";
            }
            finally { _runtimeBranchCancel = null; if (!_disposed) RefreshRuntimeRescanControls(); }
        }
    }
}
