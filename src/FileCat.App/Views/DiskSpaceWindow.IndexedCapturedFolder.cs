using Avalonia.Controls;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedSessionView
    {
        private sealed record CapturedFolderRequest(SessionIdentity Identity, ulong Path, AnalysisMetric Metric,
            AnalysisFilterRequest Filter, AnalysisFilterRequest Draft, AnalysisHierarchySort Sort, bool Descending);
        private CapturedFolderRequest? _requestedFolder;
        private void CancelRequestedFolder()
        { if (_requestedFolder is not null) { _requestedFolder = null; _again = false; _buildCancel?.Cancel(); } }
        private readonly Button _showCapturedFolder = new() { Content = "Show in captured folder" };
        private string? _panelFilter;
        private AnalysisFilterRequest DraftFilter() => new(_query.Text ?? "", _case.IsChecked == true, _unknown.IsChecked == true, _rootId)
        { RootDisplayIds = (_grouping?.Filter?.Request ?? _explorer.Filter?.Request)?.RootDisplayIds, PanelFilter = _panelFilter };
        private Task ShowCapturedFolderAsync(ulong? selected = null)
        {
            ulong path = selected ?? _focus;
            if (_disposed || path == 0 || _actualWork.HasUnconfirmedClose) return Task.CompletedTask;
            CancelTableRange();
            var request = new CapturedFolderRequest(_source.Descriptor.Identity, path, _grouping?.Metric ?? _explorer.Metric,
                _grouping?.Filter?.Request ?? _explorer.Filter?.Request ?? new(""), DraftFilter(),
                _groupPage is { } group ? SavedIndexedSort(group.Sort) : _shownPage?.Sort ?? AnalysisHierarchySort.Metric,
                _groupPage?.Descending ?? _shownPage?.Descending ?? true);
            return RebuildAsync(request);
        }
        private bool ApplyCapturedFolderControls(CapturedFolderRequest captured, AnalysisFilterRequest effective)
        {
            bool keepDraft = DraftFilter() != captured.Draft;
            _publishing = true;
            try
            {
                _metric.SelectedIndex = (int)captured.Metric; _group.SelectedIndex = 0; _sort.SelectedIndex = (int)captured.Sort; _descending.IsChecked = captured.Descending;
                _rootId = effective.RootId;
                _roots.SelectedIndex = effective.RootId is null ? 0 : 1 + Array.FindIndex(_source.Descriptor.Roots.ToArray(), root => root.Id == effective.RootId);
                if (!keepDraft)
                {
                    _query.Text = effective.Text; _case.IsChecked = effective.CaseSensitive; _unknown.IsChecked = effective.UnknownOnly;
                    _panelFilter = effective.PanelFilter;
                }
            }
            finally { _publishing = false; }
            return keepDraft;
        }
    }
}
