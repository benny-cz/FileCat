using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedSessionView
    {
        private readonly ComboBox _group = new()
        {
            ItemsSource = Enum.GetValues<AnalysisGroupingDimension>().Select(AnalysisGroupingRules.Title).ToArray(), SelectedIndex = 0
        };
        private readonly Button _exploreGroup, _exploreFolder;
        private readonly WrapPanel _groupEvidence = new() { ItemSpacing = 8, LineSpacing = 8, IsVisible = false };
        private readonly Button _groupCharges = new() { Content = "Accounting charges", IsEnabled = false };
        private readonly Button _firstCharges = new() { Content = "First charges", IsEnabled = false };
        private readonly Button _moreCharges = new() { Content = "More charges", IsEnabled = false };
        private AnalysisDatasetGrouping? _grouping;
        private GroupPage? _groupPage;
        private string? _groupKey, _groupFocus, _chargeScope;
        private AnalysisTreeItem? _selectedGroupedItem;
        private AnalysisDatasetPage<AnalysisObjectCharge>? _chargePage;
        private ulong? _chargeOwner;
        private readonly List<(string? Group, ulong Offset)> _groupPast = [], _groupFuture = [];

        // Both table and geometry are bounded. The chart always uses the largest first page,
        // independently of table order, with the undisplayed amount represented exactly once.
        internal sealed record GroupPage(AnalysisDatasetGroup? Scope, ulong Offset, ulong Total,
            ImmutableArray<object> Rows, AnalysisGroupedChart Chart, AnalysisTableSort Sort, bool Descending)
        {
            internal ulong NextOffset => checked(Offset + (ulong)Rows.Length);
            internal bool HasMore => NextOffset < Total;
        }
        internal static async Task<IndexedSessionView> CreateAsync(DiskSpaceWindow window, AnalysisDatasetLease source,
            AnalysisDatasetExplorer explorer, AnalysisFilterRequest filter, AnalysisSessionAdmission.Slot admission,
            AnalysisSavedView? saved, string? notice, CancellationToken cancellation)
        {
            AnalysisDatasetGrouping? groups = null;
            try
            {
                GroupPage? page = null;
                if (saved is not null && saved.Grouping != AnalysisGroupingDimension.FolderHierarchy)
                {
                    groups = await source.BuildGroupingAsync(saved.Metric, saved.Grouping, filter, cancellation);
                    var aggregate = AnalysisSavedCatalog.SplitAggregationScope(saved.Scope);
                    string? key = aggregate.Scope == AnalysisTree.RootKey ? null : aggregate.Scope; ulong offset = (ulong)aggregate.Skipped;
                    try { page = await LoadGroupedPageAsync(groups, key, offset, saved.Sort, saved.SortDescending, cancellation); }
                    catch (KeyNotFoundException)
                    { key = null; offset = 0; AddNotice("The saved group is absent from these matching observations. Showing all groups."); }
                    catch (ArgumentOutOfRangeException) when (offset != 0)
                    { offset = 0; AddNotice("The saved table page is absent. Showing the first page."); }
                    page ??= await LoadGroupedPageAsync(groups, key, offset, saved.Sort, saved.SortDescending, cancellation);
                    if (offset != 0 && page.Rows.IsEmpty)
                    { offset = 0; page = await LoadGroupedPageAsync(groups, key, offset, saved.Sort, saved.SortDescending, cancellation); AddNotice("The saved table page is absent. Showing the first page."); }
                    if (aggregate.Skipped != 0 && offset != 0) AddNotice("The saved small-item scope is shown as a table page within its captured group.");
                }
                cancellation.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(window._closed, window);
                var result = new IndexedSessionView(window, source, explorer, filter, admission, saved, notice, groups, page); groups = null; return result;
                void AddNotice(string text) => notice = notice is null ? text : notice + " " + text;
            }
            finally { if (groups is not null) await groups.DisposeAsync(); }
        }
        private static async Task<GroupPage> LoadGroupedPageAsync(AnalysisDatasetGrouping groups, string? key, ulong offset,
            AnalysisTableSort sort, bool descending, CancellationToken cancellation)
        {
            if (key is null)
            {
                var rows = await groups.ReadGroupsAsync(offset, cancellation: cancellation, sort: sort, descending: descending);
                var largest = await groups.ReadGroupsAsync(maximum: 32, cancellation: cancellation);
                var chart = AnalysisGroupedChart.Overview(groups.Descriptor.Identity, groups.Dimension, groups.Metric, groups.Totals, groups.LogicalTotals, largest);
                return new(null, rows.Offset, rows.Total, rows.Rows.Cast<object>().ToImmutableArray(), chart, sort, descending);
            }
            var scope = await groups.ReadGroupAsync(key, cancellation);
            var members = await groups.ReadMemberRowsAsync(key, offset, sort: sort, descending: descending, cancellation: cancellation);
            var first = await groups.ReadMemberRowsAsync(key, maximum: 32, cancellation: cancellation);
            var detail = AnalysisGroupedChart.Members(groups.Descriptor.Identity, groups.Dimension, groups.Metric, scope, first);
            return new(scope, members.Offset, members.Total, members.Rows.Cast<object>().ToImmutableArray(), detail, sort, descending);
        }
        private AnalysisTableSort GroupSort() => _sort.SelectedIndex switch
        {
            1 => AnalysisTableSort.FilePaths, 2 => AnalysisTableSort.FileSize, 3 => AnalysisTableSort.Name,
            4 => AnalysisTableSort.MeasurementState, _ => AnalysisTableSort.ActiveMetric
        };
        private AnalysisTreeItem? DisplayItem(object? row) => row switch
        {
            AnalysisHierarchyRow folder => AnalysisIndexedChart.Row(folder, _explorer.Metric),
            AnalysisDatasetGroup group when _grouping is { } groups => AnalysisGroupedChart.GroupItem(group, groups.Dimension, groups.Metric, AnalysisTree.RootKey),
            AnalysisDatasetGroupRow member when _grouping is { } groups => AnalysisGroupedChart.MemberItem(member, _groupKey ?? AnalysisTree.RootKey, groups.Metric),
            _ => null
        };
        private void SelectEntry()
        {
            if (_publishing || _disposed) return;
            _basketInspection = null;
            if (!_rangeSelectionChanging && _list.SelectedItem is { } selected) { CancelTableRange(); SetRangeAnchor(selected); }
            switch (_list.SelectedItem)
            {
                case AnalysisDatasetGroup group: InspectGroup(group); break;
                case AnalysisDatasetGroupRow row:
                    _groupFocus = null; _selectedGroupedItem = DisplayItem(row); ResetCharges(_groupKey, row.Member.Path.Id);
                    _ = InspectAsync(row.Member.Path.Id); break;
                case AnalysisHierarchyRow row:
                    _selectedGroupedItem = null; _ = InspectAsync(row.Path.Id); break;
            }
        }
        private void OpenEntry()
        {
            if (_list.SelectedItem is AnalysisDatasetGroup group) _ = NavigateGroupAsync(group.Definition.Key);
            else if (_list.SelectedItem is AnalysisDatasetGroupRow { Member.Path.Kind: AnalysisKind.Directory } member) _ = ShowCapturedFolderAsync(member.Member.Path.Id);
            else if (_list.SelectedItem is AnalysisHierarchyRow { Path.Kind: AnalysisKind.Directory } row) _ = NavigateAsync(row.Path.Id);
            else SelectEntry();
        }
        private void ExploreGroup()
        { if (_list.SelectedItem is AnalysisDatasetGroup group) _ = NavigateGroupAsync(group.Definition.Key); }
        private Task<bool> ParentAsync() => _grouping is not null ? NavigateGroupAsync(null) : NavigateAsync(_shownPage?.Scope?.Path.ParentId ?? 0);

        private async Task<bool> NavigateGroupAsync(string? key, ulong offset = 0, bool record = true, AnalysisTableSort? sort = null, bool? descending = null, CancellationToken cancellation = default)
        {
            if (_disposed || _actualWork.HasUnconfirmedClose || _grouping is not { } groups) return false;
            CancelRequestedFolder();
            using var actualWork = _actualWork.Enter();
            int version = ++_navigation; ++_inspection; _navigationCancel?.Cancel();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _selectionLifetime.Token); _navigationCancel = cancel;
            var old = (_groupKey, _offset); _status.Text = "Loading captured groups and members…";
            try
            {
                var page = await LoadGroupedPageAsync(groups, key, offset, sort ?? GroupSort(), descending ?? _descending.IsChecked == true, cancel.Token);
                await WaitForInteractionAsync(cancel.Token);
                if (_disposed || cancel.IsCancellationRequested || version != _navigation || !ReferenceEquals(groups, _grouping)) return false;
                if (record && old != (key, offset)) { PushGroup(_groupPast, old); _groupFuture.Clear(); }
                ++_inspection; // A late response from the old displayed page cannot replace the new inspector.
                if (key != _groupKey) { _pageOffsets.Clear(); _focus = 0; _groupFocus = null; _selectedGroupedItem = null; }
                _groupKey = key; _offset = offset; _groupPage = page; Publish(_explorer); return true;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return false; }
            catch (Exception error) when (Expected(error))
            { RememberViewCloseFailure(error); if (!_disposed && version == _navigation) _status.Text = "The previous view remains displayed. " + AnalysisNames.Display(error.Message, 500); return false; }
            finally { if (ReferenceEquals(_navigationCancel, cancel)) _navigationCancel = null; }
        }
        private async Task GroupHistoryAsync(bool forward)
        {
            var source = forward ? _groupFuture : _groupPast; if (source.Count == 0) return;
            var target = source[^1]; var old = (_groupKey, _offset);
            if (await NavigateGroupAsync(target.Group, target.Offset, false))
            { source.RemoveAt(source.Count - 1); PushGroup(forward ? _groupPast : _groupFuture, old); Buttons(); }
        }
        private static void PushGroup(List<(string? Group, ulong Offset)> history, (string? Group, ulong Offset) value)
        { if (history.Count == AnalysisLimits.NavigationHistory) history.RemoveAt(0); history.Add(value); }
        private void GroupButtons()
        {
            _previous.IsEnabled = _offset > 0; _next.IsEnabled = _groupPage?.HasMore == true;
            _parent.IsEnabled = _parent.IsVisible = _groupKey is not null;
            _back.IsEnabled = _groupPast.Count != 0; _forward.IsEnabled = _groupFuture.Count != 0;
            _back.IsVisible = _forward.IsVisible = _groupPast.Count != 0 || _groupFuture.Count != 0;
            _exploreGroup.IsEnabled = _groupKey is null;
        }
        private void PublishGroup(AnalysisDatasetGrouping groups, GroupPage page)
        {
            _shownPage = null; _shownChart = null; _shownInspection = null;
            bool focusTable = _list.IsKeyboardFocusWithin; _publishing = true;
            try
            {
                _list.ItemsSource = page.Rows;
                _list.SelectedItem = page.Rows.FirstOrDefault(row => row is AnalysisDatasetGroup group ? group.Definition.Key == _groupFocus :
                    row is AnalysisDatasetGroupRow member && member.Member.Path.Id == _focus);
            }
            finally { _publishing = false; }
            if (focusTable) _list.Focus();
            var totals = page.Scope?.Totals ?? groups.Totals; var logical = page.Scope?.Logical ?? groups.LogicalTotals;
            _summary.Text = AnalysisGroupingRules.Title(groups.Dimension) + " · " + _metric.Items[(int)groups.Metric] + ": " + Amount(totals, groups.Metric) +
                $" · {logical.Files:N0} matching file paths · File size: {DiskSpaceViewModel.Bytes(logical.KnownBytes)} known" +
                (logical.UnknownFiles == 0 ? "" : $" · {logical.UnknownFiles:N0} unknown file sizes");
            if (totals.IdentityUnverified != 0) _summary.Text += groups.Metric == AnalysisMetric.OnDisk
                ? " · Includes identity-unverified path allocation; aliases may repeat."
                : " · Identity-unverified path bytes are excluded from unique chart area.";
            if (totals.Missing.Count != 0) _summary.Text += " · Missing: " + Missing(totals);
            _status.Text = "Grouped view ready. " + (groups.Descriptor.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Complete under captured policy." : "Partial captured observations.") +
                (groups.Filter is { } filter ? $" {filter.DefinitePaths:N0} definite matches; {filter.UnknownPaths:N0} uncertain results." : "") +
                (page.Chart.RemainderKey is null ? "" : " Chart detail is summarized; every captured member remains in the paged table.");
            _range.Text = (page.Total == 0 ? "No entries" : $"Entries {page.Offset + 1:N0}–{page.NextOffset:N0} of {page.Total:N0}") + " · " +
                (page.Sort switch { AnalysisTableSort.Name => "Name", AnalysisTableSort.FilePaths => "File paths", AnalysisTableSort.FileSize => "File size", AnalysisTableSort.MeasurementState => "Metadata state", _ => "Active metric" }) +
                (page.Descending ? " · Descending" : " · Ascending");
            _breadcrumbs.Children.Clear(); _breadcrumbs.Children.Add(ActionButton("All groups", () => _ = NavigateGroupAsync(null), "Explore every accounting group in this captured query."));
            if (page.Scope is { } scope) _breadcrumbs.Children.Add(new TextBlock { Text = scope.Definition.Label, TextWrapping = TextWrapping.Wrap });
            _buckets.Children.Clear();
            if (page.Scope is { } shared && (shared.Shared.Known != 0 || shared.Shared.IdentityUnverified != 0 || shared.Shared.Missing.Count != 0))
                _buckets.Children.Add(ActionButton("Shared files — charged once · " + Amount(shared.Shared, groups.Metric), () =>
                { InspectGroup(shared); ResetCharges(shared.Definition.Key, 0); _ = ChargesAsync(0); }, "Inspect shared accounting charges independently of their captured path references."));
            ResetCharges(page.Scope?.Definition.Key, _focus == 0 ? null : _focus);
            foreach (var button in _evidence) button.IsEnabled = false;
            _details.Text = "Choose a group or captured member. Group rows are accounting categories; member file sizes and assigned amounts are distinct. Related evidence is paged.";
            _inspectorStatus.Text = "No entry selected.";
            UpdateCharts(); Buttons();
            if (_focus != 0) _ = InspectAsync(_focus);
            else if (_list.SelectedItem is AnalysisDatasetGroup selected) InspectGroup(selected);
        }
        private void ActivateGroupChart(string key, bool enter, bool mark = false)
        {
            if (_disposed || _groupPage is not { } page) return;
            if (key == page.Chart.RemainderKey)
            {
                _publishing = true;
                try { _sort.SelectedIndex = 0; _descending.IsChecked = true; } finally { _publishing = false; }
                _pageOffsets.Clear(); _ = NavigateGroupAsync(_groupKey, page.Chart.RemainderOffset, false); return;
            }
            if (!page.Chart.Items.TryGetValue(key, out var item)) return;
            if (item.GroupDefinition is not null)
            {
                var group = page.Rows.OfType<AnalysisDatasetGroup>().FirstOrDefault(group => group.Definition.Key == key);
                if (enter) _ = NavigateGroupAsync(key);
                else if (group is not null) { _list.SelectedItem = group; InspectGroup(group); }
                else _ = InspectChartGroupAsync(key);
            }
            else if (item.NodeId is { } path)
            {
                if (mark) ToggleMark(path);
                _selectedGroupedItem = item; _groupFocus = null; ResetCharges(_groupKey, path);
                _publishing = true;
                try { _list.SelectedItem = page.Rows.OfType<AnalysisDatasetGroupRow>().FirstOrDefault(row => row.Member.Path.Id == path); } finally { _publishing = false; }
                _ = InspectAsync(path);
            }
            else if (page.Scope is { } scope) { InspectGroup(scope); ResetCharges(scope.Definition.Key, 0); _ = ChargesAsync(0); }
        }
        private async Task InspectChartGroupAsync(string key)
        {
            if (_disposed || _actualWork.HasUnconfirmedClose || _grouping is not { } groups) return;
            using var work = _actualWork.Enter(); int version = ++_inspection;
            try
            {
                var group = await groups.ReadGroupAsync(key);
                if (!_disposed && version == _inspection && ReferenceEquals(groups, _grouping)) InspectGroup(group);
            }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed && version == _inspection) _details.Text = AnalysisNames.Display(error.Message, 500); }
        }
        private void InspectGroup(AnalysisDatasetGroup group)
        {
            if (_disposed || _grouping is not { } groups) return;
            _basketInspection = null;
            ++_inspection; _focus = 0; _groupFocus = group.Definition.Key; _selectedGroupedItem = null; _shownInspection = null;
            RefreshSelection();
            foreach (var button in _evidence) button.IsEnabled = false;
            _details.Text = group.Definition.Label + "\nAccounting group · " + AnalysisGroupingRules.Title(groups.Dimension) + "\nActive metric: " + Amount(group.Totals, groups.Metric) +
                $"\n{group.Members:N0} captured members / references; {group.Charges:N0} accounting charges.\nFile size: {DiskSpaceViewModel.Bytes(group.Logical.KnownBytes)} known; {group.Logical.UnknownFiles:N0} unknown path sizes.\n" +
                (group.Kind == AnalysisGroupKind.SharedAcrossGroups ? "Shared charges are counted here once. Member references keep their captured file sizes and receive no duplicate area.\n" :
                 group.Kind == AnalysisGroupKind.DirectoryAllocation ? "Directory metadata allocation is separate from file contents. These members reference captured directories.\n" : "Charges are assigned using all captured aliases before the display filter.\n") +
                "Shared subtotal: " + Amount(group.Shared, groups.Metric) + "\nMissing: " + Missing(group.Totals) +
                "\nThis group is an accounting category, with no filesystem entry or action authority.";
            _inspectorStatus.Text = "Captured accounting group ready."; ResetCharges(group.Definition.Key, null); UpdateCharts();
        }
        private static string Amount(AnalysisTotals totals, AnalysisMetric metric) => !totals.CompatibleBasis ? "Incompatible measurement bases" :
            (metric == AnalysisMetric.Files ? totals.Known.ToString("N0", CultureInfo.CurrentCulture) : DiskSpaceViewModel.Bytes(AnalysisGroupedChart.Area(totals, metric))) + " known";
        private static string Missing(AnalysisTotals totals) => totals.Missing.Count == 0 ? "None recorded" : string.Join(", ", totals.Missing.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value:N0}"));
        private void AddGroupEvidence()
        {
            _groupCharges.Click += (_, _) => _ = ChargesAsync(0); _firstCharges.Click += (_, _) => _ = ChargesAsync(0);
            _moreCharges.Click += (_, _) => { if (_chargePage is { HasMore: true } page) _ = ChargesAsync(page.NextOffset); };
            _groupEvidence.Children.Add(_groupCharges); _groupEvidence.Children.Add(_firstCharges); _groupEvidence.Children.Add(_moreCharges);
            _inspector.Children.Add(_groupEvidence);
            AutomationProperties.SetName(_groupEvidence, "Captured grouped accounting evidence");
        }
        private void ResetCharges(string? key, ulong? owner)
        {
            _chargeScope = key; _chargeOwner = owner; _chargePage = null;
            _groupCharges.IsEnabled = _grouping is not null && key is not null;
            _firstCharges.IsEnabled = _moreCharges.IsEnabled = false;
        }
        private async Task ChargesAsync(ulong offset)
        {
            if (_disposed || _actualWork.HasUnconfirmedClose || _grouping is not { } groups || _chargeScope is not { } key) return;
            using var work = _actualWork.Enter(); int version = ++_inspection; ulong? owner = _chargeOwner;
            _inspectorStatus.Text = "Loading captured accounting charges…";
            try
            {
                var page = await groups.ReadChargesAsync(key, owner, offset, maximum: 32);
                if (_disposed || version != _inspection || !ReferenceEquals(groups, _grouping)) return;
                _chargePage = page; _shownInspection = null;
                foreach (var button in _evidence) button.IsEnabled = false;
                var text = new StringBuilder("Captured accounting charges · " + (owner == 0 ? "shared subtotal" : owner is null ? "whole group" : "assigned member") + "\n");
                text.AppendLine($"Charges {page.Offset:N0}–{page.NextOffset:N0} of {page.Total:N0}. Captured aliases are references, not additional chart area.");
                foreach (var charge in page.Rows)
                    text.AppendLine(AnalysisNames.Display(charge.ComponentId ?? "Primary content", 128) + ": " + (charge.Measurement.IsKnown ? charge.Measurement.Value!.Value.ToString(CultureInfo.InvariantCulture) : charge.Measurement.State.ToString()) +
                        (groups.Metric == AnalysisMetric.Files ? " paths" : " bytes") + " · basis " + AnalysisNames.Display(charge.BasisId ?? "Not reported", 128) +
                        $" · {charge.ObservedAliases:N0} observed aliases · " + (charge.IdentityUnverified ? "Identity unverified" : "Captured identity") + (charge.DirectorySelf ? " · Directory metadata" : ""));
                text.AppendLine("These accounting records do not authorize filesystem actions.");
                _details.Text = text.ToString(); _inspectorStatus.Text = "Captured accounting charges ready.";
                _firstCharges.IsEnabled = offset != 0; _moreCharges.IsEnabled = page.HasMore;
            }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed && version == _inspection) _inspectorStatus.Text = "Accounting charges unavailable. " + AnalysisNames.Display(error.Message, 500); }
        }
    }
}
