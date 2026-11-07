using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedSessionView
    {
        private sealed record IndexedOrder(object Owner, string? Group, ulong Folder, AnalysisTableSort Sort, bool Descending, ulong Total);
        private sealed record RangeAnchor(IndexedOrder Order, ulong Position);
        private sealed record KeyboardRange(IndexedOrder Order, ulong Position, bool Additive);
        private RangeAnchor? _rangeAnchor;
        private IndexedOrder? _rangeOwner, _keyboardOwner;
        private ImmutableHashSet<ulong>? _rangeBaseline, _rangePublishedMarks;
        private ulong? _keyboardPosition;
        private KeyboardRange? _keyboardRange;
        private CancellationTokenSource? _tableRangeCancel, _rangeKeyCancel;
        private int _rangeGeneration;
        private bool _rangeSelectionChanging, _rangeKeysRunning;
        private IndexedOrder? PublishedOrder() => _grouping is { } groups && _groupPage is { } grouped
            ? new(groups, _groupKey, 0, grouped.Sort, grouped.Descending, grouped.Total)
            : _shownPage is { } page ? new(_explorer, null, page.ParentId, TableSort(page.Sort), page.Descending, page.Children.Total) : null;
        private static AnalysisTableSort TableSort(AnalysisHierarchySort sort) => sort switch
        { AnalysisHierarchySort.Name => AnalysisTableSort.Name, AnalysisHierarchySort.Files => AnalysisTableSort.FilePaths, AnalysisHierarchySort.FileSize => AnalysisTableSort.FileSize,
            AnalysisHierarchySort.MetadataState => AnalysisTableSort.MeasurementState, _ => AnalysisTableSort.ActiveMetric };
        private static string RowKey(object row) => row switch
        { AnalysisHierarchyRow folder => AnalysisTree.Key(folder.Path.Id), AnalysisDatasetGroupRow member => AnalysisTree.Key(member.Member.Path.Id), AnalysisDatasetGroup group => group.Definition.Key, _ => "" };
        private static ulong? RowId(object row) => row switch
        { AnalysisHierarchyRow folder => folder.Path.Id, AnalysisDatasetGroupRow member => member.Member.Path.Id, _ => null };
        private ulong PageOffset => _grouping is not null ? _groupPage?.Offset ?? 0 : _shownPage?.Children.Offset ?? 0;
        private void SetRangeAnchor(object row)
        {
            var order = PublishedOrder(); int at = _list.Items.IndexOf(row); if (order is null || at < 0) return;
            _rangeAnchor = new(order, PageOffset + (ulong)at); _rangeOwner = null; _rangeBaseline = _rangePublishedMarks = null;
            _keyboardOwner = null; _keyboardPosition = null;
        }
        private void CancelTableRange()
        {
            _rangeGeneration++; _tableRangeCancel?.Cancel(); _rangeKeyCancel?.Cancel(); _keyboardRange = null;
            _keyboardOwner = null; _keyboardPosition = null; _rangeOwner = null; _rangeBaseline = _rangePublishedMarks = null;
        }
        private void ConfigureIndexedRanges()
        {
            const KeyModifiers toggle = KeyModifiers.Control | KeyModifiers.Meta, allowed = toggle | KeyModifiers.Shift;
            AutomationProperties.SetHelpText(_list, "Click focuses a row. Ctrl or Command-click toggles its real captured mark. Shift-click or Shift with arrows, Home, End or Page keys marks a captured range across pages. Accounting groups are not expanded; use Mark current members explicitly. The review basket stays separate.");
            bool captured = false;
            _list.AddHandler(PointerReleasedEvent, (_, args) => { if (captured) { captured = false; args.Pointer.Capture(null); } }, RoutingStrategies.Bubble, handledEventsToo: true);
            _list.PointerCaptureLost += (_, _) => captured = false;
            _list.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (_disposed || _window._dialogs.IsOpen || !args.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || (args.KeyModifiers & ~allowed) != 0 || args.Source is not Visual source) return;
                if (source is CheckBox || source.GetVisualAncestors().OfType<CheckBox>().Any()) return;
                var container = source as ListBoxItem ?? source.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
                if (container?.Content is not { } row || _list.Items.IndexOf(row) < 0 || PublishedOrder() is not { } order) return;
                ulong position = PageOffset + (ulong)_list.Items.IndexOf(row);
                if ((args.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    args.Handled = true; captured = true; args.Pointer.Capture(_list); _rangeKeyCancel?.Cancel(); _keyboardRange = null;
                    _ = MarkTableRangeAsync(order, row, position, (args.KeyModifiers & toggle) != 0); container.Focus();
                }
                else if ((args.KeyModifiers & toggle) != 0)
                {
                    args.Handled = true; captured = true; args.Pointer.Capture(_list); SelectRangeRow(row);
                    if (RowId(row) is { } id) ToggleMark(id); container.Focus();
                }
                else { CancelTableRange(); SetRangeAnchor(row); }
            }, RoutingStrategies.Tunnel);
            _list.AddHandler(KeyDownEvent, (_, args) =>
            {
                if (_disposed || _window._dialogs.IsOpen || (args.KeyModifiers & KeyModifiers.Shift) == 0 || (args.KeyModifiers & ~allowed) != 0 || PublishedOrder() is not { Total: > 0 } order) return;
                if (args.Key is not (Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)) return;
                args.Handled = true;
                string? key = _focus != 0 ? AnalysisTree.Key(_focus) : _groupFocus;
                int index = -1; for (int at = 0; at < _list.ItemCount; at++) if (RowKey(_list.Items[at]!) == key) { index = at; break; }
                ulong? current = _keyboardOwner == order ? _keyboardPosition : index < 0 ? null : PageOffset + (ulong)index;
                if (_rangeAnchor?.Order != order && current is { } anchor) _rangeAnchor = new(order, anchor);
                double height = _list.ContainerFromIndex(Math.Max(0, index))?.Bounds.Height ?? 0;
                ulong page = height > 0 && double.IsFinite(height) && double.IsFinite(_list.Bounds.Height) ? (ulong)Math.Clamp(_list.Bounds.Height / height, 1, 128) : 1;
                ulong next = args.Key switch
                {
                    Key.Home => 0, Key.End => order.Total - 1,
                    Key.Up => current is { } up ? up == 0 ? 0 : up - 1 : order.Total - 1,
                    Key.Down => current is { } down ? Math.Min(order.Total - 1, down + 1) : 0,
                    Key.PageUp => current is { } before ? before > page ? before - page : 0 : order.Total - 1,
                    _ => current is { } after ? after + Math.Min(page, order.Total - 1 - after) : 0
                };
                _keyboardOwner = order; _keyboardPosition = next; _keyboardRange = new(order, next, (args.KeyModifiers & toggle) != 0); _rangeKeyCancel?.Cancel();
                if (!_rangeKeysRunning) _ = RunRangeKeysAsync();
            }, RoutingStrategies.Tunnel);
        }
        private void SelectRangeRow(object row)
        {
            _rangeSelectionChanging = true;
            try { bool same = ReferenceEquals(_list.SelectedItem, row); _list.SelectedItem = row; if (same) SelectEntry(); }
            finally { _rangeSelectionChanging = false; }
        }
        private async Task RunRangeKeysAsync()
        {
            using var actualWork = _actualWork.Enter(); _rangeKeysRunning = true;
            try
            {
                while (_keyboardRange is { } request && !_disposed && !_actualWork.HasUnconfirmedClose)
                {
                    _keyboardRange = null; using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token); _rangeKeyCancel = cancel;
                    try
                    {
                        object row;
                        if (PublishedOrder() == request.Order && request.Position >= PageOffset && request.Position - PageOffset < (ulong)_list.ItemCount)
                            row = _list.Items[(int)(request.Position - PageOffset)]!;
                        else row = await ReadRangeRowAsync(request.Order, request.Position, cancel.Token);
                        cancel.Token.ThrowIfCancellationRequested();
                        if (PublishedOrder() == request.Order && (request.Position < PageOffset || request.Position - PageOffset >= (ulong)_list.ItemCount) &&
                            _sort.SelectedIndex == (int)SavedIndexedSort(request.Order.Sort) && _descending.IsChecked == request.Order.Descending)
                        {
                            bool loaded = request.Order.Owner is AnalysisDatasetGroupExplorer
                                ? await NavigateGroupAsync(request.Order.Group, request.Position, false, request.Order.Sort, request.Order.Descending, cancel.Token)
                                : await NavigateAsync(request.Order.Folder, request.Position, false, SavedIndexedSort(request.Order.Sort), request.Order.Descending, cancel.Token);
                            if (!loaded) continue;
                            row = _list.Items.Cast<object>().FirstOrDefault(item => RowKey(item) == RowKey(row)) ?? row;
                        }
                        await MarkTableRangeAsync(request.Order, row, request.Position, request.Additive, cancel.Token);
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                    catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "The captured range was not applied. " + AnalysisNames.Display(error.Message, 500); }
                    finally { if (ReferenceEquals(_rangeKeyCancel, cancel)) _rangeKeyCancel = null; }
                }
            }
            finally { _rangeKeysRunning = false; }
        }
        private static async Task<object> ReadRangeRowAsync(IndexedOrder order, ulong position, CancellationToken token)
        {
            if (order.Owner is AnalysisDatasetGroupExplorer groups)
            {
                if (order.Group is null) return (await groups.ReadGroupsAsync(position, 1, token, order.Sort, order.Descending)).Rows.Single();
                return (await groups.ReadMemberRowsAsync(order.Group, position, 1, order.Sort, order.Descending, token)).Rows.Single();
            }
            return (await ((AnalysisDatasetExplorer)order.Owner).ReadChildRowsAsync(order.Folder, SavedIndexedSort(order.Sort), position, 1, token, order.Descending)).Rows.Single();
        }
        private async Task MarkTableRangeAsync(IndexedOrder order, object row, ulong target, bool additive, CancellationToken cancellation = default)
        {
            if (_disposed || _actualWork.HasUnconfirmedClose) return;
            using var actualWork = _actualWork.Enter(); int generation = ++_rangeGeneration; _tableRangeCancel?.Cancel();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _selectionLifetime.Token); _tableRangeCancel = cancel;
            ulong anchor = _rangeAnchor?.Order == order ? _rangeAnchor.Position : target;
            var baseline = !additive && _rangeOwner == order && ReferenceEquals(_rangePublishedMarks, _selection.Marks) ? _rangeBaseline! : _selection.Marks;
            if (PublishedOrder() == order && _list.Items.Cast<object>().Any(item => ReferenceEquals(item, row))) SelectRangeRow(row);
            try
            {
                var captured = order.Owner is AnalysisDatasetGroupExplorer groups
                    ? await groups.CaptureTableRangeAsync(order.Group, Math.Min(anchor, target), Math.Max(anchor, target), order.Sort, order.Descending, cancel.Token)
                    : await ((AnalysisDatasetExplorer)order.Owner).CaptureTableRangeAsync(order.Folder, Math.Min(anchor, target), Math.Max(anchor, target), SavedIndexedSort(order.Sort), order.Descending, cancel.Token);
                cancel.Token.ThrowIfCancellationRequested(); if (_disposed || generation != _rangeGeneration) return;
                var identity = _source.Descriptor.Identity; var selection = captured.Selection;
                if (selection.SessionId != identity.SessionId || selection.Epoch != identity.Epoch || selection.Revision != identity.Revision) throw new InvalidOperationException("The table range belongs to another captured revision.");
                _selection.MarkRangeCaptured(baseline, selection.NodeIds); _rangeAnchor = new(order, anchor); _rangeOwner = order; _rangeBaseline = baseline; _rangePublishedMarks = _selection.Marks;
                _status.Text = $"Marked captured rows: {selection.NodeIds.Length:N0}. Existing marks outside this range are retained." +
                    (captured.SyntheticRows == 0 ? "" : $" Accounting groups not expanded: {captured.SyntheticRows:N0}. Use Mark current members explicitly.");
                RefreshSelection();
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "The captured range was not applied. " + AnalysisNames.Display(error.Message, 500); }
            finally { if (ReferenceEquals(_tableRangeCancel, cancel)) _tableRangeCancel = null; }
        }
    }
}
