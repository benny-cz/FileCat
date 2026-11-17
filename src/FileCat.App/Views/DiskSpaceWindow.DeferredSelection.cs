using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private readonly Button _deferSelection = new() { Content = "Select all after scan completes" },
            _cancelDeferredSelection = new() { Content = "Cancel deferred selection" },
            _reviewDeferredSelection = new() { Content = "Review deferred matches" };
        private readonly TextBlock _deferredSelectionSummary = new() { TextWrapping = TextWrapping.Wrap };
        private readonly StackPanel _deferredSelectionHost = new() { Spacing = 4 };
        private DialogCloser? _deferredReviewCloser;
        private bool _reviewingDeferredSelection;
        private bool? _reviewTipUsesRecordedResult;
        private void AddDeferredSelectionControls()
        {
            var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8,
                Children = { _deferSelection, _cancelDeferredSelection, _reviewDeferredSelection } };
            _deferredSelectionHost.Children.Add(bar); _deferredSelectionHost.Children.Add(_deferredSelectionSummary);
            _basketHost.Children.Add(_deferredSelectionHost);
            AutomationProperties.SetAutomationId(_deferredSelectionSummary, "Deferred selection summary");
            AutomationProperties.SetLiveSetting(_deferredSelectionSummary, AutomationLiveSetting.Polite);
            _deferSelection.Click += (_, _) => Model.RequestSelectionAfterScan();
            _cancelDeferredSelection.Click += (_, _) => Model.CancelDeferredSelection();
            _reviewDeferredSelection.Click += async (_, _) => await ReviewDeferredSelectionAsync();
        }
        private void RefreshDeferredSelection()
        {
            _deferredSelectionHost.MaxWidth = Math.Max(120, Bounds.Width - 48);
            _deferSelection.IsEnabled = Model.CanRequestDeferredSelection;
            _cancelDeferredSelection.IsVisible = Model.HasDeferredSelection;
            _reviewDeferredSelection.IsVisible = Model.CanReviewDeferredSelection;
            _reviewDeferredSelection.IsEnabled = Model.CanReviewDeferredSelection && !_reviewingDeferredSelection;
            bool recorded = Model.HasIndexedDeferredReview || Model.IsIndexedRuntimePreview;
            if (_reviewTipUsesRecordedResult != recorded)
            {
                _reviewTipUsesRecordedResult = recorded;
                string detail = recorded
                    ? "Inspect the exact captured matches, then explicitly open them as marks in a new recorded-result tab. This preview's marks and review basket stay unchanged. No files are changed."
                    : "Inspect the exact bounded set captured after this scan completes. Applying adds only these reviewed paths to marks. The review basket and filesystem remain unchanged.";
                ToolTip.SetTip(_reviewDeferredSelection, new RichTip("Review final captured matches", detail: detail));
                AutomationProperties.SetHelpText(_reviewDeferredSelection, detail);
            }
            string text = Model.DeferredSelectionText ?? Model.DeferredSelectionUnavailableReason ?? "Capture this displayed query and scope, then review its matches after the scan completes.";
            if (_deferredSelectionSummary.Text != text) _deferredSelectionSummary.Text = text;
            AutomationProperties.SetHelpText(_deferSelection, Model.DeferredSelectionUnavailableReason ?? "Request captured matching files for explicit review after this live scan finishes.");
            ToolTip.SetTip(_deferSelection, new RichTip("Select after scan completion", detail: Model.DeferredSelectionUnavailableReason ??
                "Request matching regular-file paths from this displayed query, location and scope after the scan finishes. Other small items uses the final observed ranking. Later edits do not change this request. At most 100,000 paths; review is required before marks change."));
        }
        private async Task ReviewDeferredSelectionAsync()
        {
            if (_disposed || _reviewingDeferredSelection || !Model.CanReviewDeferredSelection) return;
            if (Model.HasIndexedDeferredReview) { await ReviewIndexedDeferredSelectionAsync(); return; }
            using var actual = _actualWork.Enter(); using var review = Model.CaptureDeferredSelectionReview();
            _reviewingDeferredSelection = true; var closer = new DialogCloser(); _deferredReviewCloser = closer; Model.Interaction(true);
            try
            {
                RefreshDeferredSelection();
                var snapshot = review.Snapshot; var ids = review.Selection.NodeIds;
                if (_disposed || _window._dialogs.IsOpen) return;
                int offset = 0;
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var list = new ListBox { MinHeight = 100, MaxHeight = 300, ItemTemplate = DialogLayout.WrappingListText };
                ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
                AutomationProperties.SetName(list, "Deferred captured matches");
                var previous = new Button { Content = "Previous deferred matches" }; var next = new Button { Content = "Next deferred matches" };
                var apply = new Button { Content = "Add reviewed matches to marks" };
                var body = new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = $"{ids.Length:N0} matching file paths · final captured revision {review.Selection.Revision:N0}\nScope: " +
                        AnalysisNames.Display(review.ScopeLabel, 240) + "\nQuery: " + (review.Selection.FilterSummary.Length == 0 ? "All captured matches" : AnalysisNames.Display(review.Selection.FilterSummary, 512)) +
                        (snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "\nComplete under the recorded scan policy." : "\nPartial results: unread or excluded locations remain unknown; this set covers only recorded matches.") +
                        "\nAdding marks changes no files and leaves the independent review basket unchanged.", TextWrapping = TextWrapping.Wrap },
                    label, list, new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { previous, next } }, error, apply
                } };
                previous.Click += (_, _) => { if (offset > 0) Load(Math.Max(0, offset - 128)); };
                next.Click += (_, _) => { if (offset + 128 < ids.Length) Load(offset + 128); };
                apply.Click += (_, _) =>
                {
                    try { Model.ApplyReviewedDeferredSelection(review); closer.Close(true); }
                    catch (InvalidOperationException exception) { error.Text = AnalysisNames.Display(exception.Message, 400); apply.IsEnabled = false; }
                };
                void UpdateEligibility() => apply.IsEnabled = ids.Length != 0 && Model.CanApplyDeferredSelection(review);
                Load(0); Model.Changed += UpdateEligibility;
                try
                {
                    await _window._dialogs.ShowCustomAsync("Review selection after scan", body,
                        [new DialogButton("Keep marks unchanged / Close review", false, IsDefault: true, IsCancel: true)], closer: closer);
                }
                finally { Model.Changed -= UpdateEligibility; }
                void Load(int start)
                {
                    offset = start;
                    list.ItemsSource = ids.Skip(offset).Take(128).Select(id =>
                    {
                        var node = review.Node(id); var parts = new Stack<string>(); var current = node; int depth = 0, characters = 0;
                        while (current.ParentId != 0)
                        {
                            if (++depth > AnalysisLimits.Depth) throw new InvalidDataException("Invalid captured review ancestry.");
                            string name = AnalysisNames.DisplayName(current, 256);
                            if (characters + name.Length > 500) { parts.Push("…"); break; }
                            parts.Push(name); characters += name.Length + 1; current = review.Node(current.ParentId);
                        }
                        string path = AnalysisNames.Display(string.Join('/', parts), 512);
                        return AnalysisNames.DisplayName(node, 256) + " · " + path + " · captured location " + AnalysisNames.Display(node.RootId, 64) + " · entry " + id;
                    }).ToArray();
                    label.Text = ids.Length == 0 ? "No matching file paths in this captured result." : $"Captured matches {offset + 1:N0}–{offset + list.ItemCount:N0} of {ids.Length:N0}.";
                    previous.IsEnabled = offset > 0; next.IsEnabled = offset + list.ItemCount < ids.Length; UpdateEligibility();
                }
            }
            finally
            {
                closer.Close(null); if (ReferenceEquals(_deferredReviewCloser, closer)) _deferredReviewCloser = null;
                _reviewingDeferredSelection = false; Model.Interaction(false); if (!_disposed) Refresh();
            }
        }
    }
}
