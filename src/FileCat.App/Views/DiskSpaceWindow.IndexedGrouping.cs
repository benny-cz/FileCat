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
        private AnalysisDatasetGroupExplorer? _grouping;
        private GroupPage? _groupPage;
        private string? _groupKey, _groupFocus, _chargeScope;
        private AnalysisTreeItem? _selectedGroupedItem;
        private AnalysisDatasetPage<AnalysisObjectCharge>? _chargePage;
        private ulong? _chargeOwner;
        private readonly List<(string? Group, ulong Offset, int Skipped)> _groupPast = [], _groupFuture = [];

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
            AnalysisSavedView? saved, string? notice, CancellationToken cancellation, AppServices? services = null)
        {
            AnalysisDatasetGroupExplorer? groups = null;
            try
            {
                GroupPage? page = null;
                if (saved is not null && saved.Grouping != AnalysisGroupingDimension.FolderHierarchy)
                {
                    groups = await AnalysisDatasetGroupExplorer.CreateAsync(source, saved.Metric, saved.Grouping, filter, cancellation);
                    var aggregate = AnalysisSavedCatalog.SplitAggregationScope(saved.Scope);
                    string? key = aggregate.Scope == AnalysisTree.RootKey ? null : aggregate.Scope;
                    try
                    {
                        if (key is not null) _ = await groups.ReadGroupAsync(key, cancellation);
                        if (aggregate.Skipped != 0)
                        {
                            var other = await groups.CreateScopeAsync(key, aggregate.Skipped, cancellation); var previous = groups; groups = other; await previous.DisposeAsync();
                        }
                    }
                    catch (KeyNotFoundException)
                    { key = null; AddNotice("The saved group is absent from these matching observations. Showing all groups."); }
                    catch (ArgumentOutOfRangeException) when (aggregate.Skipped != 0)
                    { AddNotice("The saved Other subset is absent. Showing its captured parent; no selection was restored."); }
                    page = await LoadGroupedPageAsync(groups, key, 0, saved.Sort, saved.SortDescending, cancellation);
                }
                cancellation.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(window._closed, window);
                var result = new IndexedSessionView(window, source, explorer, filter, admission, saved, notice, groups, page, services); groups = null; return result;
                void AddNotice(string text) => notice = notice is null ? text : notice + " " + text;
            }
            finally { if (groups is not null) await groups.DisposeAsync(); }
        }
        private static async Task<GroupPage> LoadGroupedPageAsync(AnalysisDatasetGroupExplorer groups, string? key, ulong offset,
            AnalysisTableSort sort, bool descending, CancellationToken cancellation)
        {
            if (key is null)
            {
                var rows = await groups.ReadGroupsAsync(offset, cancellation: cancellation, sort: sort, descending: descending);
                var chart = await AnalysisGroupedChart.BuildAsync(groups, cancellation: cancellation);
                return new(null, rows.Offset, rows.Total, rows.Rows.Cast<object>().ToImmutableArray(), chart, sort, descending);
            }
            var scope = await groups.ReadGroupAsync(key, cancellation);
            var members = await groups.ReadMemberRowsAsync(key, offset, sort: sort, descending: descending, cancellation: cancellation);
            var detail = await AnalysisGroupedChart.BuildAsync(groups, key, cancellation);
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
            AnalysisDatasetGroupRow member when _grouping is { } groups => GroupMemberItem(member, groups.Metric),
            _ => null
        };
        private AnalysisTreeItem GroupMemberItem(AnalysisDatasetGroupRow member, AnalysisMetric metric)
        {
            var item = AnalysisGroupedChart.MemberItem(member, _groupKey ?? AnalysisTree.RootKey, metric);
            return _groupPage?.Scope?.Kind == AnalysisGroupKind.SharedAcrossGroups ? item with { Key = "group-reference:" + _groupKey + ":" + item.Key } : item;
        }
        private void SelectEntry()
        {
            if (_publishing || _disposed) return;
            _chartFocusKey = null;
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
        private Task<bool> ParentAsync() => _grouping is not null ? NavigateGroupAsync(_grouping.GeometrySkipped != 0 ? _groupKey : null, geometrySkipped: 0) :
            NavigateAsync(_explorer.GeometrySkipped != 0 ? _scope : _shownPage?.Scope?.Path.ParentId ?? 0, geometrySkipped: 0);

        private async Task<bool> NavigateGroupAsync(string? key, ulong offset = 0, bool record = true, AnalysisTableSort? sort = null, bool? descending = null,
            CancellationToken cancellation = default, int? geometrySkipped = null, Action? onPublished = null)
        {
            if (_disposed || _actualWork.HasUnconfirmedClose || _grouping is not { } groups) return false;
            CancelRequestedFolder();
            using var actualWork = _actualWork.Enter();
            int version = ++_navigation; ++_inspection; _navigationCancel?.Cancel();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _selectionLifetime.Token); _navigationCancel = cancel;
            int skipped = geometrySkipped ?? (key == _groupKey ? groups.GeometrySkipped : 0);
            var old = (_groupKey, _offset, groups.GeometrySkipped); AnalysisDatasetGroupExplorer? candidate = null; bool published = false;
            _status.Text = "Loading captured groups and members…";
            try
            {
                if (skipped != groups.GeometrySkipped || groups.GeometrySkipped != 0 && key != groups.GeometryParent)
                    candidate = await groups.CreateScopeAsync(key, skipped, cancel.Token);
                var owner = candidate ?? groups;
                var page = await LoadGroupedPageAsync(owner, key, offset, sort ?? GroupSort(), descending ?? _descending.IsChecked == true, cancel.Token);
                await WaitForInteractionAsync(cancel.Token);
                if (_disposed || cancel.IsCancellationRequested || version != _navigation || !ReferenceEquals(groups, _grouping)) return false;
                if (record && old != (key, offset, skipped)) { PushGroup(_groupPast, old); _groupFuture.Clear(); }
                ++_inspection; // A late response from the old displayed page cannot replace the new inspector.
                if (key != _groupKey || skipped != groups.GeometrySkipped) { _pageOffsets.Clear(); _focus = 0; _chartFocusKey = null; _groupFocus = null; _selectedGroupedItem = null; }
                _groupKey = key; _offset = offset; _groupPage = page; bool replaced = candidate is not null;
                if (replaced) { _grouping = owner; candidate = null; }
                published = true;
                try { onPublished?.Invoke(); Publish(_explorer); }
                finally { if (replaced) await groups.DisposeAsync(); }
                return !_disposed && version == _navigation && ReferenceEquals(owner, _grouping);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return false; }
            catch (Exception error) when (Expected(error))
            { RememberViewCloseFailure(error); if (!_disposed && version == _navigation) _status.Text = (published ? "The scope is displayed, but its previous owner did not close successfully. " : "The previous view remains displayed. ") + AnalysisNames.Display(error.Message, 500); return false; }
            finally
            {
                try { if (candidate is not null) await candidate.DisposeAsync(); }
                catch (Exception error) { RememberViewCloseFailure(error); throw; }
                finally { if (ReferenceEquals(_navigationCancel, cancel)) _navigationCancel = null; }
            }
        }
        private async Task GroupHistoryAsync(bool forward)
        {
            var source = forward ? _groupFuture : _groupPast; if (source.Count == 0) return;
            var target = source[^1]; int count = source.Count; var old = (_groupKey, _offset, _grouping!.GeometrySkipped);
            await NavigateGroupAsync(target.Group, target.Offset, false, geometrySkipped: target.Skipped, onPublished: () =>
            { if (source.Count == count && source[^1] == target) { source.RemoveAt(source.Count - 1); PushGroup(forward ? _groupPast : _groupFuture, old); } });
        }
        private static void PushGroup(List<(string? Group, ulong Offset, int Skipped)> history, (string? Group, ulong Offset, int Skipped) value)
        { if (history.Count == AnalysisLimits.NavigationHistory) history.RemoveAt(0); history.Add(value); }
        private void GroupButtons()
        {
            _previous.IsEnabled = _offset > 0; _next.IsEnabled = _groupPage?.HasMore == true;
            _parent.IsEnabled = _parent.IsVisible = _groupKey is not null || _grouping?.GeometrySkipped > 0;
            _back.IsEnabled = _groupPast.Count != 0; _forward.IsEnabled = _groupFuture.Count != 0;
            _back.IsVisible = _forward.IsVisible = _groupPast.Count != 0 || _groupFuture.Count != 0;
            _exploreGroup.IsEnabled = _groupKey is null;
        }
        private void PublishGroup(AnalysisDatasetGroupExplorer groups, GroupPage page)
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
            var totals = page.Scope?.Totals ?? groups.Totals; var logical = page.Scope?.MemberLogical ?? groups.LogicalTotals;
            _summary.Text = AnalysisGroupingRules.Title(groups.Dimension) + " · " + _metric.Items[(int)groups.Metric] + ": " + Amount(totals, groups.Metric) +
                $" · {logical.Files:N0} {(page.Scope?.Kind == AnalysisGroupKind.SharedAcrossGroups ? "captured reference paths" : "matching file paths")} · File size: {DiskSpaceViewModel.Bytes(logical.KnownBytes)} known" +
                (logical.UnknownFiles == 0 ? "" : $" · {logical.UnknownFiles:N0} unknown file sizes");
            if (totals.IdentityUnverified != 0) _summary.Text += groups.Metric == AnalysisMetric.OnDisk
                ? " · Includes identity-unverified path allocation; aliases may repeat."
                : " · Identity-unverified path bytes are excluded from unique chart area.";
            _status.Text = "Grouped view ready. " + CoverageHeadline(groups.Descriptor.Coverage, groups.Descriptor.Freshness) + ".";
            if (groups.Filter is { } applied) _status.Text += $" {applied.DefinitePaths:N0} definite matches; {applied.UnknownPaths:N0} uncertain results.";
            _coverageDetail = "Missing measurements: " + (totals.Missing.Count == 0 ? "0" : Missing(totals)) + "." +
                (page.Chart.DetailLimited ? " Chart detail is summarized; every captured member remains in the paged table." : "");
            if (groups.Filter?.Request.UnknownOnly == true) _status.Text += " Showing uncertain filter results.";
            if (groups.Filter?.Request.PanelFilter is not null) _status.Text += " Panel filter applied to results.";
            _range.Text = (page.Total == 0 ? "No entries" : $"Entries {page.Offset + 1:N0}–{page.NextOffset:N0} of {page.Total:N0}") + " · " +
                (page.Sort switch { AnalysisTableSort.Name => "Name", AnalysisTableSort.FilePaths => "File paths", AnalysisTableSort.FileSize => "File size", AnalysisTableSort.MeasurementState => "Metadata state", _ => "Active metric" }) +
                (page.Descending ? " · Descending" : " · Ascending");
            _breadcrumbs.Children.Clear(); _breadcrumbs.Children.Add(ActionButton("All groups", () => _ = NavigateGroupAsync(null, geometrySkipped: 0), "Explore every accounting group in this captured query."));
            if (page.Scope is { } scope) _breadcrumbs.Children.Add(ActionButton(AnalysisNames.Display(scope.Definition.Label, 128), () => _ = NavigateGroupAsync(scope.Definition.Key, geometrySkipped: 0), "Explore this captured accounting group."));
            if (groups.GeometrySkipped != 0)
            {
                _breadcrumbs.Children.Add(new TextBlock { Text = "Other small items", TextWrapping = TextWrapping.Wrap });
                _status.Text += " Exact Other subset."; _range.Text += " · exact geometry subset";
            }
            _buckets.Children.Clear();
            if (page.Scope is { HasSharedAccounting: true } shared)
                _buckets.Children.Add(ActionButton("Shared files — charged once · " + Amount(shared.Shared, groups.Metric), () =>
                { InspectGroup(shared); ResetCharges(shared.Definition.Key, 0); _ = ChargesAsync(0); }, "Inspect shared accounting charges independently of their captured path references."));
            ResetCharges(page.Scope?.Definition.Key, _focus == 0 ? null : _focus);
            foreach (var button in _evidence) button.IsEnabled = false;
            _details.Text = "Choose a group or captured member. Group rows are accounting categories; member file sizes and assigned amounts are distinct. Related evidence is paged.";
            _inspectorStatus.Text = "No entry selected.";
            AppendPersistenceNotice();
            UpdateCharts(); Buttons();
            if (_focus != 0) _ = InspectAsync(_focus);
            else if (_list.SelectedItem is AnalysisDatasetGroup selected) InspectGroup(selected);
        }
        private void ActivateGroupChart(string key, bool enter, bool mark = false)
        {
            if (_disposed || _groupPage is not { } page) return;
            if (page.Chart.Remainders.TryGetValue(key, out var remainder))
            {
                if (!enter) { FocusOtherChartItem(); return; }
                _ = NavigateGroupAsync(remainder.Parent, geometrySkipped: remainder.Skipped); return;
            }
            var aggregate = AnalysisSavedCatalog.SplitAggregationScope(key);
            if (aggregate.Skipped != 0 && (aggregate.Scope == (_groupKey ?? AnalysisTree.RootKey) || page.Chart.Items.TryGetValue(aggregate.Scope, out var parent) && parent.GroupDefinition is not null))
            {
                if (enter) _ = NavigateGroupAsync(aggregate.Scope == AnalysisTree.RootKey ? null : aggregate.Scope, geometrySkipped: aggregate.Skipped);
                else FocusOtherChartItem(); return;
            }
            if (!page.Chart.Items.TryGetValue(key, out var item)) return;
            if (item.GroupDefinition is not null)
            {
                var group = page.Rows.OfType<AnalysisDatasetGroup>().FirstOrDefault(group => group.Definition.Key == key);
                if (enter) _ = NavigateGroupAsync(key, geometrySkipped: 0);
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
                if (enter && item.Kind == AnalysisKind.Directory) _ = ShowCapturedFolderAsync(path);
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
            _details.Text = AnalysisNames.Display(group.Definition.Label, 256) + "\nAccounting group · " + AnalysisGroupingRules.Title(groups.Dimension) + "\nActive metric: " + Amount(group.Totals, groups.Metric) +
                $"\n{group.Members:N0} captured members / references; {group.Charges:N0} accounting charges.\nFile size: {DiskSpaceViewModel.Bytes(group.MemberLogical.KnownBytes)} known; {group.MemberLogical.UnknownFiles:N0} unknown path sizes.\n" +
                (group.Kind == AnalysisGroupKind.SharedAcrossGroups ? "Shared charges are counted here once. Member references keep their captured file sizes and receive no duplicate area.\n" :
                 group.Kind == AnalysisGroupKind.DirectoryAllocation ? "Directory metadata allocation is separate from file contents. These members reference captured directories.\n" : "Charges are assigned using all captured aliases before the display filter.\n") +
                "Shared subtotal: " + Amount(group.Shared, groups.Metric) + "\nMissing: " + Missing(group.Totals) +
                "\nThis group is an accounting category, with no filesystem entry or action authority.";
            _inspectorStatus.Text = "Captured accounting group ready."; ResetCharges(group.Definition.Key, null); UpdateCharts();
        }
        private static string Amount(AnalysisTotals totals, AnalysisMetric metric) => !totals.CompatibleBasis ? "Incompatible measurement bases" :
            AnalysisGroupedChart.Area(totals, metric) == 0 && (totals.Missing.Count != 0 || metric == AnalysisMetric.UniqueFileSize && totals.IdentityUnverified != 0) ? "Unknown" :
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
