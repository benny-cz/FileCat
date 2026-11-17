using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private async Task ReviewIndexedDeferredSelectionAsync()
        {
            if (_disposed || _reviewingDeferredSelection || !Model.HasIndexedDeferredReview || _window._dialogs.IsOpen) return;
            using var actual = _actualWork.Enter(); using var review = Model.CaptureIndexedDeferredSelectionReview();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Model.HistoryLifetime);
            _reviewingDeferredSelection = true; var closer = new DialogCloser(); _deferredReviewCloser = closer;
            bool approved = false, closed = false, loading = false, pageAvailable = false; int offset = 0;
            Task load = Task.CompletedTask;
            var label = new TextBlock { Text = "Loading captured matches… No marks have changed.", TextWrapping = TextWrapping.Wrap };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var list = new ListBox { MinHeight = 100, MaxHeight = 300, ItemTemplate = DialogLayout.WrappingListText };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            var previous = new Button { Content = "Previous deferred matches" }; var next = new Button { Content = "Next deferred matches" };
            var apply = new Button { Content = "Open reviewed matches in a new tab" };
            AutomationProperties.SetName(list, "Deferred captured matches");
            AutomationProperties.SetAutomationId(label, "Deferred recorded page status"); AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
            AutomationProperties.SetAutomationId(error, "Deferred recorded page error"); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
            ToolTip.SetTip(apply, new RichTip("Open reviewed matches", detail: "Open the full recorded result with exactly these reviewed matching paths marked. Keep the earlier preview's marks and review basket unchanged. No files are changed."));
            ToolTip.SetTip(previous, new RichTip("Previous captured matches", detail: "Inspect the previous page of this fixed, completed selection."));
            ToolTip.SetTip(next, new RichTip("Next captured matches", detail: "Inspect the next page of this fixed, completed selection."));
            var body = new StackPanel { Spacing = 8, Children =
            {
                new TextBlock { Text = $"{review.Selection.NodeIds.Length:N0} matching file paths · final captured revision {review.Selection.Revision:N0}\nScope: " +
                    AnalysisNames.Display(review.ScopeLabel, 240) + "\nQuery: " + (review.Selection.FilterSummary.Length == 0 ? "All captured matches" : AnalysisNames.Display(review.Selection.FilterSummary, 512)) +
                    (review.Dataset.Descriptor.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "\nComplete under the recorded scan policy." : "\nPartial results: unread or excluded locations remain unknown. This set covers recorded matches only.") +
                    "\nAfter review, open these marks in a new recorded-result tab. This preview's marks and basket stay unchanged. No files are changed.", TextWrapping = TextWrapping.Wrap },
                label, list, new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { previous, next } }, error, apply
            } };
            void Eligibility()
            {
                if (closed || _disposed) return;
                previous.IsEnabled = !loading && offset > 0;
                next.IsEnabled = !loading && offset + list.ItemCount < review.Selection.NodeIds.Length;
                apply.IsEnabled = !loading && pageAvailable && review.Selection.NodeIds.Length != 0 && Model.CanApplyIndexedDeferredSelection(review) && _window.CanAddSession;
                AutomationProperties.SetHelpText(apply, !_window.CanAddSession ? _window._admissionPool.Refusal :
                    !Model.CanApplyIndexedDeferredSelection(review) ? "This review was canceled, replaced, or its runtime result is unavailable. No marks were changed." :
                    "Open exactly this captured set as marks in a new tab. No files are changed.");
            }
            Action capacityChanged = () => Dispatcher.UIThread.Post(Eligibility);
            previous.Click += async (_, _) => { if (!loading && offset > 0) { load = LoadAsync(Math.Max(0, offset - 128)); await load; } };
            next.Click += async (_, _) => { if (!loading && offset + 128 < review.Selection.NodeIds.Length) { load = LoadAsync(offset + 128); await load; } };
            apply.Click += (_, _) =>
            {
                if (!loading && pageAvailable && review.Selection.NodeIds.Length != 0 && Model.CanApplyIndexedDeferredSelection(review) && _window.CanAddSession)
                { approved = true; closer.Close(true); }
                else Eligibility();
            };
            Model.Interaction(true);
            try
            {
                try
                {
                    RefreshDeferredSelection();
                    load = LoadAsync(0);
                    if (_disposed || cancel.IsCancellationRequested || _window._dialogs.IsOpen) return;
                    Model.Changed += Eligibility; _window._admissionPool.Changed += capacityChanged;
                    try
                    {
                        await _window._dialogs.ShowCustomAsync("Review selection after scan", body,
                            [new DialogButton("Keep preview unchanged / Close review", false, IsDefault: true, IsCancel: true)], closer: closer);
                    }
                    finally { Model.Changed -= Eligibility; _window._admissionPool.Changed -= capacityChanged; }
                }
                finally { closed = true; closer.Close(null); }
                if (approved && !_disposed && Model.CanApplyIndexedDeferredSelection(review))
                    await OpenIndexedDeferredResultAsync(review, cancel.Token);
            }
            finally
            {
                closed = true; cancel.Cancel(); closer.Close(null); Model.Interaction(false);
                await load; // The retained review owns metadata reads through their actual return.
                if (ReferenceEquals(_deferredReviewCloser, closer)) _deferredReviewCloser = null;
                _reviewingDeferredSelection = false; if (!_disposed) Refresh();
            }

            async Task LoadAsync(int start)
            {
                loading = true; pageAvailable = false; error.Text = ""; Eligibility();
                try
                {
                    var rows = new List<string>(); var ids = review.Selection.NodeIds;
                    for (int i = start; i < Math.Min(ids.Length, start + 128); i++) rows.Add(await CaptionAsync(ids[i]));
                    cancel.Token.ThrowIfCancellationRequested(); if (closed || _disposed) return;
                    offset = start; list.ItemsSource = rows; pageAvailable = true;
                    label.Text = ids.Length == 0 ? "No matching file paths in this captured result." : $"Captured matches {offset + 1:N0}–{offset + rows.Count:N0} of {ids.Length:N0}.";
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                catch (Exception failure) when (IndexedExpectedError(failure))
                {
                    if (AnalysisCloseUnconfirmedException.IsPresent(failure)) _window.RecordIndexedCloseFailure(failure);
                    if (!closed && !_disposed) error.Text = "Captured page unavailable: " + AnalysisNames.Display(failure.Message, 400) + " No marks were changed.";
                }
                finally { loading = false; Eligibility(); }
            }
            async Task<string> CaptionAsync(ulong id)
            {
                var path = await review.Dataset.ReadPathAsync(id, cancel.Token); var current = path;
                var parts = new Stack<string>(); int depth = 0, characters = 0;
                while (current.ParentId != 0)
                {
                    if (++depth > AnalysisLimits.Depth) throw new InvalidDataException("Invalid captured review ancestry.");
                    string name = AnalysisNames.DisplayName(current, 256);
                    if (characters + name.Length > 500) { parts.Push("…"); break; }
                    parts.Push(name); characters += name.Length + 1;
                    current = await review.Dataset.ReadPathAsync(current.ParentId, cancel.Token);
                }
                var root = review.Dataset.Descriptor.Roots.First(r => r.Id == path.RootId);
                return AnalysisNames.DisplayName(path, 256) + " · " + AnalysisNames.Display(root.Label, 120) + " · " + AnalysisNames.Display(string.Join('/', parts), 512);
            }
        }

        private async Task OpenIndexedDeferredResultAsync(AnalysisDeferredIndexedReview review, CancellationToken cancellation)
        {
            if (!Model.CanApplyIndexedDeferredSelection(review) || Model.HistoryServices is not { } services) return;
            try
            {
                if (_disposed || !Model.CanApplyIndexedDeferredSelection(review)) return;
                await _window.AddPreparedIndexedSessionAsync(token => review.Dataset.RetainAsync(token), review.View.Metric, null, review.View,
                    cancellation, services, historyNotice: Model.HistoryNotice,
                    runtimeHandoff: () => Model.DetachRecordedResultOwner(review.Request), reviewedMarks: review.Selection);
            }
            catch (Exception failure) when (IndexedExpectedError(failure))
            {
                if (AnalysisCloseUnconfirmedException.IsPresent(failure)) _window.RecordIndexedCloseFailure(failure);
                if (!_disposed) Model.SetStatus("Reviewed matches were kept; a new tab could not open: " + AnalysisNames.Display(failure.Message, 400));
                return;
            }
            // Publication already owns the runtime. Notifications cannot roll back that ownership.
            Model.AcceptIndexedDeferredPublication(review);
        }
    }
}
