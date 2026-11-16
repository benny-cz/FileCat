using System.Collections.Immutable;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    internal delegate Task<AnalysisIndexedComparison> IndexedComparisonBuilder(AnalysisDatasetLease previous, AnalysisDatasetLease current,
        AnalysisMetric metric, IReadOnlyDictionary<string, string>? structuralRoots, CancellationToken cancellation);

    internal async Task<bool> AddIndexedComparisonSessionAsync(AnalysisDatasetLease previous, AnalysisDatasetLease current,
        AnalysisMetric metric, IReadOnlyDictionary<string, string>? structuralRoots = null, string? previousName = null,
        CancellationToken cancellation = default, Func<AnalysisComparisonCompatibility, Task<bool>>? reviewLimited = null,
        IndexedComparisonBuilder? compare = null)
    {
        Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_closed, this);
        AnalysisSessionAdmission.Slot? admission = _admissionPool.Acquire();
        if (_pendingIndexed++ == 0) _indexedIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AnalysisDatasetLease? a = null, b = null; AnalysisIndexedComparison? result = null;
        Exception? unconfirmed = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _indexedLifetime.Token);
        try
        {
            compare ??= static (old, now, selected, roots, token) => now.CompareIndexedAsync(old, selected, roots, token);
            // Own the exact revisions independently of both originating tabs and catalog handles.
            var mapping = structuralRoots?.ToImmutableDictionary(StringComparer.Ordinal);
            a = await previous.RetainAsync(linked.Token); b = await current.RetainAsync(linked.Token);
            result = await compare(a, b, metric, mapping, linked.Token);
            if (!result.Compatibility.ComparableTotals && reviewLimited is not null && !await reviewLimited(result.Compatibility)) return false;
            var page = await IndexedComparisonView.ReadPageAsync(result, 0, linked.Token);
            linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this);
            var view = new IndexedComparisonView(this, admission, a, b, result, mapping, previousName, page, compare);
            a = b = null; result = null; admission = null;
            try { AddView(view); } catch { view.Dispose(); throw; }
            return true;
        }
        catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { unconfirmed = error; throw; }
        finally
        {
            try
            {
                var close = ClosePreparedAsync();
                try { if (admission is not null) await admission.CloseAfterAsync(close); else await close; }
                catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error)) { RecordIndexedCloseFailure(error); throw; }
            }
            finally
            {
                if (--_pendingIndexed == 0) { _indexedIdle.TrySetResult(); if (_closed) _indexedLifetime.Dispose(); }
            }
        }
        async Task ClosePreparedAsync()
        {
            Exception? failure = unconfirmed;
            try { await AnalysisIndexedComparison.CloseAsync(result, b, a); }
            catch (Exception error) { failure = AnalysisViewWork.PreserveCloseFailure(failure, new AnalysisCloseUnconfirmedException(error)); }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class IndexedComparisonView : StackPanel, IAnalysisSessionView
    {
        private const int PageSize = 32;
        private readonly DiskSpaceWindow _window;
        private readonly AnalysisSessionAdmission.Slot _admission;
        private readonly AnalysisDatasetLease _previousSource, _currentSource;
        private readonly ImmutableDictionary<string, string>? _mapping;
        private readonly IndexedComparisonBuilder _compare;
        private readonly AnalysisViewWork _work = new();
        private readonly CancellationTokenSource _lifetime = new();
        private AnalysisIndexedComparison? _result;
        private AnalysisIndexedComparison.Query? _queryResult;
        private AnalysisComparisonView _applied;
        private readonly ComboBox _metric = new() { ItemsSource = new[] { "File size", "Unique file size", "On disk — reported", "Files" }, MinWidth = 180 };
        private readonly TextBox _queryText = new() { PlaceholderText = "Filter comparison observations…", MaxLength = AnalysisLimits.QueryBytes, MinWidth = 200 };
        private readonly ComboBox _querySide = new() { ItemsSource = new[] { "Current, otherwise Previous", "Current", "Previous", "Either side" }, SelectedIndex = 0 };
        private readonly ComboBox _cohort = new() { ItemsSource = new[] { "All changes", "Growing", "Shrinking", "Added", "Removed", "Moved / renamed", "Attribution movement", "Different observed object", "Metadata changed", "No observed metadata change", "Unknown change" }, SelectedIndex = 0 };
        private readonly ComboBox _order = new() { ItemsSource = new[] { "Absolute difference", "Relative change", "Current amount", "Recorded path" }, SelectedIndex = 0 };
        private readonly CheckBox _case = new() { Content = "Case sensitive" }, _uncertain = new() { Content = "Inspect uncertain query results" };
        private readonly TextBlock _appliedDescription = Text("");
        private readonly Button _applyView, _resetView;
        private readonly TextBlock _summary = Text(""), _compatibility = Text(""), _status = Text(""), _range = Text("");
        private readonly Button _previousPage, _nextPage, _inspect, _retry;
        private readonly ListBox _rows = new() { MinHeight = 200, MaxHeight = 440, Focusable = true };
        private readonly List<ulong> _offsets = [];
        private readonly Side _before = new(true), _after = new(false);
        private DisplayPage _page;
        private AnalysisMetric _publishedMetric;
        private AnalysisIndexedComparisonRow? _selected;
        private DialogCloser? _reviewCloser;
        private bool _busy, _disposed, _publishing;
        public bool HasTemporaryWork => true;
        public bool IsComparison => true;

        internal sealed record DisplayRow(AnalysisIndexedComparisonRow Row, string PreviousName, string CurrentName, string Text);
        internal sealed record DisplayPage(AnalysisDatasetPage<AnalysisIndexedComparisonRow> Page, ImmutableArray<DisplayRow> Rows);
        private sealed class Side(bool previous)
        {
            internal readonly bool Previous = previous;
            internal readonly TextBox Details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 260 };
            internal readonly ListBox Members = new() { MinHeight = 90, MaxHeight = 220 };
            internal readonly TextBlock Range = Text("");
            internal readonly Button Back = new() { Content = "Previous members" }, Next = new() { Content = "Next members" };
            internal readonly List<ulong> Offsets = [];
            internal AnalysisDatasetPage<AnalysisPathMetadata>? Page;
            internal AnalysisComparisonAccountingEntity? Entity;
            internal string Heading = "";
        }

        internal IndexedComparisonView(DiskSpaceWindow window, AnalysisSessionAdmission.Slot admission,
            AnalysisDatasetLease previous, AnalysisDatasetLease current, AnalysisIndexedComparison result,
            ImmutableDictionary<string, string>? mapping, string? previousName, DisplayPage page, IndexedComparisonBuilder compare)
        {
            _window = window; _admission = admission; _previousSource = previous; _currentSource = current;
            _result = result; _mapping = mapping; _page = page; _publishedMetric = result.Metric; _compare = compare;
            _applied = new() { Metric = result.Metric };
            Spacing = 10; Margin = new Thickness(10);
            Children.Add(Text("Compare captured observations", true));
            Children.Add(Text("Previous: " + AnalysisNames.Display(previousName ?? "captured inventory", 240) + " · " + Interval(previous.Descriptor) +
                "\nCurrent: " + Interval(current.Descriptor)));
            Children.Add(Text("Both inputs are offline observations. Recorded paths grant no current file actions. File contents were not compared; metadata and locations can be sensitive." +
                (result.Taint.HasFlag(AnalysisTaint.ImportedUnverified) ? " Imported observations are unverified." : "") +
                (result.Taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? " Contains privileged-derived metadata." : "")));
            AutomationProperties.SetName(_metric, "Comparison metric"); _metric.SelectedIndex = (int)result.Metric;
            _retry = Button("Retry metric", () => _ = ChangeMetricAsync(), "Rebuild this metric from the same two captured revisions.");
            Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { _metric, _retry } });
            AutomationProperties.SetName(_queryText, "Comparison query"); AutomationProperties.SetName(_querySide, "Comparison query side");
            AutomationProperties.SetName(_cohort, "Comparison change cohort"); AutomationProperties.SetName(_order, "Comparison row order");
            AutomationProperties.SetName(_appliedDescription, "Applied comparison view");
            _applyView = Button("Apply comparison view", () => _ = ApplyViewAsync(), "Apply the query, recorded side, change cohort and row order together. The current view remains applied if the request is refused.");
            _resetView = Button("Reset comparison view", () =>
            {
                _queryText.Text = ""; _querySide.SelectedIndex = _cohort.SelectedIndex = _order.SelectedIndex = 0; _case.IsChecked = _uncertain.IsChecked = false;
                _ = ApplyViewAsync();
            }, "Clear the query and restore all changes in absolute-difference order.");
            Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { _queryText, _querySide, _cohort, _order, _case, _uncertain, _applyView, _resetView } });
            Children.Add(_appliedDescription);
            Children.Add(_summary); Children.Add(_compatibility); Children.Add(_status);
            AutomationProperties.SetName(_summary, "Comparison totals"); AutomationProperties.SetName(_status, "Comparison status");
            AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
            AutomationProperties.SetName(_rows, "Captured comparison rows");
            _rows.ItemTemplate = new FuncDataTemplate<DisplayRow>((item, _) => Text(item?.Text ?? ""));
            _previousPage = Button("Previous comparison page", () => _ = PageAsync(false), "Show the preceding captured comparison rows.");
            _nextPage = Button("Next comparison page", () => _ = PageAsync(true), "Show the next captured comparison rows.");
            _inspect = Button("Inspect comparison row", () => _ = InspectAsync(), "Read both captured sides and their independently paged observed paths.");
            Children.Add(Text("Each row is a complete accounting entity, including all observed aliases. Unavailable differences remain unknown; row IDs break ordering ties."));
            Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { _previousPage, _nextPage, _range, _inspect } });
            Children.Add(_rows);
            foreach (var side in new[] { _before, _after })
            {
                string label = side.Previous ? "Previous" : "Current";
                AutomationProperties.SetName(side.Details, label + " comparison details");
                AutomationProperties.SetName(side.Members, label + " observed comparison paths");
                side.Members.ItemTemplate = new FuncDataTemplate<AnalysisPathMetadata>((path, _) => Text(path is null ? "" :
                    "[" + AnalysisNames.Display(path.RootId, 64) + "] " + AnalysisNames.DisplayName(path, 160) + " · path " + path.Id.ToString(CultureInfo.InvariantCulture) + " · parent " + path.ParentId.ToString(CultureInfo.InvariantCulture)));
                AutomationProperties.SetName(side.Back, label + " previous observed paths"); AutomationProperties.SetName(side.Next, label + " next observed paths");
                side.Back.Click += async (_, _) => await MembersAsync(side, false); side.Next.Click += async (_, _) => await MembersAsync(side, true);
                side.Members.SelectionChanged += async (_, _) => { if (!_publishing && !_busy) await LocateAsync(side); };
                Children.Add(new Expander { Header = label + " recorded side", IsExpanded = true, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = new StackPanel { Spacing = 6, Children = { side.Details, new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { side.Back, side.Next, side.Range } }, side.Members } } });
            }
            _metric.SelectionChanged += async (_, _) => { if (!_publishing && !_busy) await ChangeMetricAsync(); };
            _rows.SelectionChanged += async (_, _) => { if (!_publishing && !_busy) await InspectAsync(); };
            Publish(page); ShowTotals(result); _status.Text = "Captured observations · offline."; Refresh();
        }

        private static TextBlock Text(string text, bool heading = false) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal };
        private static Button Button(string text, Action run, string help)
        {
            var button = new Button { Content = text }; AutomationProperties.SetHelpText(button, help); button.Click += (_, _) => run(); return button;
        }
        private static string Interval(AnalysisDatasetDescriptor source) => source.Identity.StartedUtc.ToString("u") + " – " + source.Identity.ObservedUtc.ToString("u");
        private static string Value(AnalysisMeasurement? value, AnalysisMetric metric) => value is not { } measurement ? "No recorded side" : measurement.IsKnown
            ? measurement.Value!.Value.ToString("N0") + (metric == AnalysisMetric.Files ? " paths" : " B") : "Unavailable · " + measurement.State;
        private static string Delta(AnalysisDelta? value, AnalysisMetric metric) => value is { } d
            ? (d.Magnitude == 0 ? "" : d.Negative ? "−" : "+") + d.Magnitude.ToString("N0") + (metric == AnalysisMetric.Files ? " paths" : " B") : "Unknown";
        private static string Change(AnalysisChange change) => change switch
        {
            AnalysisChange.Added => "+ Added", AnalysisChange.Removed => "− Removed", AnalysisChange.Growing => "↑ Growing", AnalysisChange.Shrinking => "↓ Shrinking",
            AnalysisChange.Moved => "Moved / renamed", AnalysisChange.Attribution => "Attribution movement", AnalysisChange.Replaced => "Different observed object",
            AnalysisChange.MetadataChanged => "Metadata changed", AnalysisChange.NoObservedMetadataChange => "No observed metadata change",
            AnalysisChange.PreviouslyUnknown => "Previously unknown", _ => "Unknown"
        };
        private static string Evidence(AnalysisComparisonEvidence evidence) => evidence switch
        { AnalysisComparisonEvidence.CompatibleIdentityAndVersion => "Compatible observed identity and version", AnalysisComparisonEvidence.Structural => "Explicit structural comparison", _ => "Path-based observations" };

        internal static async Task<DisplayPage> ReadPageAsync(AnalysisIndexedComparison result, ulong offset, CancellationToken cancellation, AnalysisIndexedComparison.Query? query = null)
        {
            var page = query is null ? await result.ReadAsync(offset, PageSize, cancellation) : await query.ReadAsync(offset, PageSize, cancellation);
            var items = ImmutableArray.CreateBuilder<DisplayRow>(page.Rows.Length);
            foreach (var row in page.Rows)
            {
                string a = await NameAsync(true, row.Previous), b = await NameAsync(false, row.Current);
                string relative = row.Delta is null ? "Relative change unknown" : row.RelativePercent is { } percent
                    ? percent.ToString("+0.###;-0.###;0", CultureInfo.CurrentCulture) + "%" : "New / zero baseline";
                items.Add(new(row, a, b, Change(row.Change) + " · " + (row.Current is null ? a : b) +
                    "\nPrevious: " + Value(row.Previous?.Value, result.Metric) + " · Current: " + Value(row.Current?.Value, result.Metric) +
                    " · Difference: " + Delta(row.Delta, result.Metric) + " · " + relative + "\n" + Evidence(row.Evidence) +
                    " · observed paths: " + (row.Previous?.ObservedPaths ?? 0).ToString("N0") + " → " + (row.Current?.ObservedPaths ?? 0).ToString("N0")));
            }
            return new(page, items.ToImmutable());
            async Task<string> NameAsync(bool previous, AnalysisComparisonAccountingEntity? entity)
            {
                if (entity is null) return "No recorded side";
                var member = query is null ? await result.ReadMembersAsync(previous, entity.Id, 0, 1, cancellation) : await query.ReadMembersAsync(previous, entity.Id, 0, 1, cancellation);
                var path = member.Rows.FirstOrDefault();
                return path is null ? "No representative path" : "[" + AnalysisNames.Display(path.RootId, 64) + "] " + AnalysisNames.DisplayName(path, 160);
            }
        }
        private void ShowTotals(AnalysisIndexedComparison result)
        {
            string Total(AnalysisTotals total) => !total.CompatibleBasis ? "No combined total · incompatible bases" :
                Value(AnalysisMeasurement.Known(total.Known), result.Metric) + " known" +
                (total.IdentityUnverified > 0 ? " + " + total.IdentityUnverified.ToString("N0") + " identity-unverified bytes" : "") +
                (total.Missing.Count > 0 ? " · " + total.Missing.Values.Aggregate(0UL, (a, b) => checked(a + b)).ToString("N0") + " unavailable measurements" : "");
            _summary.Text = "Previous: " + Total(result.PreviousTotals) + "\nCurrent: " + Total(result.CurrentTotals) + "\nObserved net change: " + Delta(result.NetChange, result.Metric);
            _compatibility.Text = (_mapping is not null ? "Explicit structural comparison" : result.Compatibility.VerifiedIdentityContext ? "Recorded comparison" : "Path-based comparison") +
                (result.Compatibility.ComparableTotals ? "" : " · limited comparison") + "\n" + string.Join("\n", result.Compatibility.Explanations);
        }
        private void Publish(DisplayPage page)
        {
            _publishing = true;
            try
            {
                _page = page; _selected = null; _rows.ItemsSource = page.Rows; _rows.SelectedIndex = -1;
                DescribeApplied();
                _range.Text = page.Page.Rows.Length == 0 ? "No comparison rows" : $"{page.Page.Offset + 1:N0}–{page.Page.NextOffset:N0} of {page.Page.Total:N0}";
                ClearSide(_before); ClearSide(_after);
            }
            finally { _publishing = false; }
        }
        private static void ClearSide(Side side)
        { side.Entity = null; side.Page = null; side.Offsets.Clear(); side.Members.ItemsSource = null; side.Details.Text = "Select a comparison row to inspect this recorded side."; side.Range.Text = ""; }
        private void Refresh()
        {
            bool usable = !_disposed && !_busy && !_work.HasUnconfirmedClose;
            _metric.IsEnabled = usable; _retry.IsVisible = _result is null; _retry.IsEnabled = usable;
            foreach (var control in new Control[] { _queryText, _querySide, _cohort, _order, _case, _uncertain, _applyView, _resetView }) control.IsEnabled = usable && _result is not null;
            _rows.IsEnabled = usable && _result is not null; _inspect.IsEnabled = usable && _result is not null && _rows.SelectedItem is DisplayRow;
            _previousPage.IsEnabled = usable && _result is not null && _offsets.Count != 0;
            _nextPage.IsEnabled = usable && _result is not null && _page.Page.HasMore;
            foreach (var side in new[] { _before, _after })
            {
                side.Members.IsEnabled = usable && _result is not null;
                side.Back.IsEnabled = usable && _result is not null && side.Offsets.Count != 0;
                side.Next.IsEnabled = usable && _result is not null && side.Page?.HasMore == true;
            }
        }
        private async Task RunAsync(string activity, Func<CancellationToken, Task> run)
        {
            if (_disposed || _busy || _work.HasUnconfirmedClose) return;
            using var actual = _work.Enter(); _busy = true; _status.Text = activity; Refresh();
            try { await run(_lifetime.Token); if (!_disposed) _status.Text = "Captured observations · offline."; }
            catch (OperationCanceledException) { if (!_disposed) _status.Text = "Canceled. No partial view was applied."; }
            catch (Exception error) when (IndexedExpectedError(error))
            {
                _work.RecordUnconfirmedClose(error);
                if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                if (!_disposed) _status.Text = "The requested view is unavailable. " + AnalysisNames.Display(error.Message, 400) +
                    (_result is null ? " The previous displayed page is retained; choose a metric and Retry metric to rebuild its index." : " The previous view is retained.");
            }
            finally
            {
                _busy = false;
                if (!_disposed)
                {
                    if (_result is null)
                    {
                        _publishing = true;
                        try { _metric.SelectedIndex = (int)_publishedMetric; } finally { _publishing = false; }
                    }
                    Refresh();
                }
            }
        }
        private Task PageAsync(bool next) => RunAsync("Reading comparison rows…", async token =>
        {
            if (_result is null || !next && _offsets.Count == 0 || next && !_page.Page.HasMore) return;
            ulong offset = next ? _page.Page.NextOffset : _offsets[^1]; var page = await ReadPageAsync(_result, offset, token, _queryResult);
            token.ThrowIfCancellationRequested();
            if (next) _offsets.Add(_page.Page.Offset); else _offsets.RemoveAt(_offsets.Count - 1);
            Publish(page);
        });
        private Task ChangeMetricAsync() => RunAsync("Comparing the selected metric…", async token =>
        {
            if (!Enum.IsDefined((AnalysisMetric)_metric.SelectedIndex)) throw new ArgumentOutOfRangeException(nameof(_metric));
            var metric = (AnalysisMetric)_metric.SelectedIndex;
            if (_result is not null && metric == _publishedMetric) return;
            // The comparison places cover the real lifetime, so retire the old owner before admission.
            var old = _result; var oldQuery = _queryResult; _result = null; _queryResult = null;
            await AnalysisIndexedComparison.CloseAsync(oldQuery, old);
            AnalysisIndexedComparison? replacement = null;
            AnalysisIndexedComparison.Query? replacementQuery = null;
            try
            {
                replacement = await _compare(_previousSource, _currentSource, metric, _mapping, token);
                if (!replacement.Compatibility.ComparableTotals)
                {
                    _reviewCloser = new DialogCloser();
                    var accepted = await _window._dialogs.ShowCustomAsync("Review limited comparison", Text(
                        "This metric does not support a qualified combined difference for these inputs. Known side values remain visible; affected differences stay unknown.\n\n" +
                        string.Join("\n", replacement.Compatibility.Explanations)),
                        [new DialogButton("Keep previous metric", false, IsDefault: true, IsCancel: true), new DialogButton("Use limited metric", true)], closer: _reviewCloser);
                    _reviewCloser = null; token.ThrowIfCancellationRequested();
                    if (accepted is not true)
                    {
                        await replacement.DisposeAsync(); replacement = null;
                        _publishing = true; try { _metric.SelectedIndex = (int)_publishedMetric; } finally { _publishing = false; }
                        // Restore the old metric's query owner before re-enabling its retained page.
                        replacement = await _compare(_previousSource, _currentSource, _publishedMetric, _mapping, token);
                        replacementQuery = await BuildQueryAsync(replacement, _applied, token);
                        _result = replacement; replacement = null; _queryResult = replacementQuery; replacementQuery = null;
                        return;
                    }
                }
                var applied = _applied with { Metric = metric };
                replacementQuery = await BuildQueryAsync(replacement, applied, token);
                bool retry = metric == _publishedMetric;
                var page = await ReadPageAsync(replacement, retry ? _page.Page.Offset : 0, token, replacementQuery); token.ThrowIfCancellationRequested();
                _result = replacement; replacement = null; _queryResult = replacementQuery; replacementQuery = null;
                _publishedMetric = metric; _applied = applied; if (!retry) _offsets.Clear(); Publish(page); ShowTotals(_result);
            }
            finally { await AnalysisIndexedComparison.CloseAsync(replacementQuery, replacement); }
        });
        private static Task<AnalysisIndexedComparison.Query?> BuildQueryAsync(AnalysisIndexedComparison result, AnalysisComparisonView view, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(view.Query) && view.Cohort == AnalysisComparisonCohort.All && view.Order == AnalysisComparisonOrder.AbsoluteDifference && view.QuerySide == AnalysisComparisonSide.CurrentOrPrevious)
                return Task.FromResult<AnalysisIndexedComparison.Query?>(null);
            return Build();
            async Task<AnalysisIndexedComparison.Query?> Build() => await result.QueryAsync(view, cancellation: token);
        }
        private Task ApplyViewAsync() => RunAsync("Applying comparison view…", async token =>
        {
            if (_result is null) return;
            if (string.IsNullOrWhiteSpace(_queryText.Text)) _uncertain.IsChecked = false;
            var view = new AnalysisComparisonView { Metric = _publishedMetric, Query = _queryText.Text ?? "", QuerySide = (AnalysisComparisonSide)_querySide.SelectedIndex,
                Cohort = (AnalysisComparisonCohort)_cohort.SelectedIndex, Order = (AnalysisComparisonOrder)_order.SelectedIndex, CaseSensitive = _case.IsChecked == true, ShowUnknown = _uncertain.IsChecked == true };
            AnalysisComparisonView.Validate(view); AnalysisIndexedComparison.Query? replacement = null;
            try
            {
                replacement = await BuildQueryAsync(_result, view, token);
                var page = await ReadPageAsync(_result, 0, token, replacement); token.ThrowIfCancellationRequested();
                if (_queryResult is not null) await _queryResult.DisposeAsync();
                _queryResult = replacement; replacement = null; _applied = view; _offsets.Clear(); Publish(page);
            }
            finally { if (replacement is not null) await replacement.DisposeAsync(); }
        });
        private void DescribeApplied()
        {
            _appliedDescription.Text = "Applied view · " + _cohort.Items[(int)_applied.Cohort] + " · " + _order.Items[(int)_applied.Order] +
                " · " + _querySide.Items[(int)_applied.QuerySide] + (string.IsNullOrWhiteSpace(_applied.Query) ? " · no text query" : " · " + AnalysisNames.Display(_applied.Query, 240)) +
                (_applied.CaseSensitive ? " · case sensitive" : "") +
                (_queryResult is null ? "" : $"\n{_queryResult.Definite:N0} definite accounting rows · {_queryResult.Unknown:N0} uncertain accounting rows in this change cohort" + (_applied.ShowUnknown ? " · showing uncertain rows" : " · showing definite rows"));
        }
        private Task InspectAsync() => RunAsync("Reading recorded comparison details…", async token =>
        {
            if (_result is null || _rows.SelectedItem is not DisplayRow selected) return;
            var before = await ReadSideAsync(_before, selected.Row.Previous, token);
            var after = await ReadSideAsync(_after, selected.Row.Current, token);
            token.ThrowIfCancellationRequested(); _selected = selected.Row;
            _publishing = true;
            try
            {
                ApplySide(_before, before); ApplySide(_after, after);
                _before.Details.Text = selected.Row.Explanation + "\n" + _before.Details.Text;
                _after.Details.Text = selected.Row.Explanation + "\n" + _after.Details.Text;
            }
            finally { _publishing = false; }
        });
        private sealed record SideRead(AnalysisComparisonAccountingEntity? Entity, AnalysisDatasetPage<AnalysisPathMetadata>? Page, string Details);
        private async Task<SideRead> ReadSideAsync(Side side, AnalysisComparisonAccountingEntity? entity, CancellationToken token)
        {
            if (entity is null) return new(null, null, side.Previous ? "No recorded Previous side. This does not assert a creation date." : "No recorded Current side. Historical paths have no current action target.");
            var full = await _result!.ReadSideAsync(side.Previous, entity.Id, token);
            var page = await _result.ReadMembersAsync(side.Previous, entity.Id, 0, PageSize, token);
            string location = page.Rows.Length == 0 ? "No representative path" : await new AnalysisReportLineage(side.Previous ? _previousSource : _currentSource).ResolveAsync(page.Rows[0], token);
            return new(full, page, "Recorded location: " + location + "\nValue: " + Value(full.Value, _publishedMetric) +
                "\nObserved paths: " + full.ObservedPaths.ToString("N0") + " · " + (full.DirectorySelf ? "Directory self" : "File observations") +
                "\nFresh captured evidence: " + (full.Fresh ? "Yes" : "No") + "\nModified: " + (full.ModifiedUtc?.ToString("u") ?? "Unavailable") +
                "\nObserved identity: " + AnalysisNames.Display(full.ObservedIdentity ?? "Unavailable", 4096) +
                "\nVersion evidence: " + AnalysisNames.Display(full.VersionEvidence ?? "Unavailable", 4096) +
                "\nAttribution parent: " + (full.AttributionParent?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable") +
                "\nThis is captured metadata, not proof of unchanged content or reclaimable physical space.");
        }
        private static void ApplySide(Side side, SideRead value)
        {
            side.Entity = value.Entity; side.Page = value.Page; side.Offsets.Clear(); side.Heading = value.Details; side.Details.Text = value.Details;
            side.Members.ItemsSource = value.Page?.Rows; side.Members.SelectedIndex = -1; SideRange(side);
        }
        private static void SideRange(Side side) => side.Range.Text = side.Page is not { } page || page.Rows.Length == 0 ? "No observed paths on this side" :
            $"{page.Offset + 1:N0}–{page.NextOffset:N0} of {page.Total:N0} observed paths";
        private Task MembersAsync(Side side, bool next) => RunAsync("Reading observed paths…", async token =>
        {
            if (_result is null || _selected is null || side.Entity is null || side.Page is null || !next && side.Offsets.Count == 0 || next && !side.Page.HasMore) return;
            ulong offset = next ? side.Page.NextOffset : side.Offsets[^1];
            var page = await _result.ReadMembersAsync(side.Previous, side.Entity.Id, offset, PageSize, token); token.ThrowIfCancellationRequested();
            if (next) side.Offsets.Add(side.Page.Offset); else side.Offsets.RemoveAt(side.Offsets.Count - 1);
            _publishing = true;
            try { side.Page = page; side.Members.ItemsSource = page.Rows; side.Members.SelectedIndex = -1; SideRange(side); }
            finally { _publishing = false; }
        });
        private Task LocateAsync(Side side) => RunAsync("Reading the recorded location…", async token =>
        {
            if (side.Members.SelectedItem is not AnalysisPathMetadata path) return;
            string location = await new AnalysisReportLineage(side.Previous ? _previousSource : _currentSource).ResolveAsync(path, token);
            token.ThrowIfCancellationRequested(); side.Details.Text = "Selected recorded path: " + location + "\nPath " + path.Id.ToString(CultureInfo.InvariantCulture) + "\n" + side.Heading;
        });
        public ScrollViewer CreateViewport() => new() { Content = this, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        public void HandleKeys(KeyEventArgs args)
        {
            if (_disposed || _busy || args.Handled || _window._dialogs.IsOpen) return;
            if (args.Key == Key.Enter && _rows.IsKeyboardFocusWithin) { _ = InspectAsync(); args.Handled = true; }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; _lifetime.Cancel(); _reviewCloser?.Close(false);
            _rows.ItemsSource = null; ClearSide(_before); ClearSide(_after); _selected = null;
            var drained = _work.CloseAsync(); _window.TrackIndexedClose(CloseAsync(drained), _admission);
        }
        private async Task CloseAsync(Task drained)
        {
            try { await drained; }
            finally
            {
                try { await AnalysisIndexedComparison.CloseAsync(_queryResult, _result, _currentSource, _previousSource); }
                finally { _result = null; _queryResult = null; _lifetime.Dispose(); }
            }
        }
    }
}
