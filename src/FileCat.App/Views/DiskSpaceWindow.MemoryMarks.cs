using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private readonly Button _reviewMarks = new() { Content = "Review marked entries" },
            _markMatches = new() { Content = "Mark matching files in this scope" },
            _clearMemoryMarks = new() { Content = "Clear marks" },
            _retryMarkCounts = new() { Content = "Update mark counts", IsVisible = false };
        private bool _reviewingMarks;
        private DialogCloser? _markReviewCloser;
        private void AddMemorySelectionControls()
        {
            AutomationProperties.SetAutomationId(_basket, "Captured selection summary");
            AutomationProperties.SetLiveSetting(_basket, AutomationLiveSetting.Polite);
            _basketHost.Children.Add(_markMatches); _basketHost.Children.Add(_clearMemoryMarks); _basketHost.Children.Add(_reviewMarks); _basketHost.Children.Add(_retryMarkCounts);
            ToolTip.SetTip(_markMatches, new RichTip("Capture matching files", detail: "Marks regular-file matches in the displayed scope and revision. Focus does not narrow this scope. Context folders, hidden aliases and future observations are excluded. At most 100,000 distinct paths can be marked."));
            ToolTip.SetTip(_reviewMarks, new RichTip("Review captured marks", detail: "Inspect marked entries across locations and filters. Unmarking an entry leaves the independent review basket unchanged."));
            _markMatches.Click += async (_, _) => await Model.MarkMatchingFilesAsync();
            _clearMemoryMarks.Click += (_, _) => Model.ClearMarks();
            _reviewMarks.Click += async (_, _) => await ReviewMarksAsync();
            _retryMarkCounts.Click += (_, _) => { Model.ScheduleMarkVisibility(retry: true); Refresh(); };
            AddDeferredSelectionControls();
        }
        private void RefreshMemorySelection()
        {
            Model.ScheduleMarkVisibility();
            RefreshDeferredSelection();
            _reviewMarks.IsEnabled = Model.Tree is not null && Model.Selection.Marks.Count != 0 && !_reviewingMarks;
            _markMatches.IsEnabled = Model.Tree is not null && !Model.IsCapturingMatches;
            _clearMemoryMarks.IsEnabled = Model.Selection.Marks.Count != 0 || Model.IsCapturingMatches;
            _retryMarkCounts.IsVisible = Model.MarkVisibilityError is not null;
            string text = $"Review basket: {Model.Selection.Basket.Count:N0} references · expected free-space gain unknown · {Model.Selection.Marks.Count:N0} marked";
            if (Model.Selection.Marks.Count != 0)
            {
                if (Model.MarkVisibility is { } counts)
                    text += $" · {counts.InScope:N0} in this scope · {Model.ListedMarks:N0} in this table · {counts.Elsewhere:N0} elsewhere · {counts.HiddenByFilter:N0} marked items hidden by filters" +
                        (counts.Missing == 0 ? "." : $" · {counts.Missing:N0} absent from this captured revision.");
                else text += Model.MarkVisibilityError is { } error ? " · Mark scope counts unavailable: " + error : " · Counting marks in this scope and filter…";
            }
            if (_basket.Text != text) _basket.Text = text;
        }
        private sealed record MarkedEntry(ulong Id, string Label) { public override string ToString() => Label; }
        private async Task ReviewMarksAsync()
        {
            if (_disposed || _reviewingMarks || Model.Tree is not { } tree || Model.Selection.Marks.Count == 0) return;
            using var actual = _actualWork.Enter(); using var owner = Model.RetainDisplayedOwner();
            _reviewingMarks = true; var closer = new DialogCloser(); _markReviewCloser = closer;
            Model.Interaction(true);
            try
            {
                var captured = Model.Selection.Marks; var ids = await Task.Run(() => captured.Order().ToImmutableArray());
                if (_disposed || _window._dialogs.IsOpen) return;
                int offset = 0;
                var list = new ListBox { MinHeight = 120, MaxHeight = 300 };
                AutomationProperties.SetName(list, "Captured marked entries");
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var details = new TextBlock { TextWrapping = TextWrapping.Wrap };
                AutomationProperties.SetName(details, "Captured marked entry details");
                var previous = new Button { Content = "Previous marked entries" }; var next = new Button { Content = "Next marked entries" };
                var unmark = new Button { Content = "Unmark selected entry" };
                var body = new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = "Captured marks, including entries elsewhere or hidden by filters. Unmarking changes only marks; the review basket is independent. No files are changed here.", TextWrapping = TextWrapping.Wrap },
                    label, list, new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { previous, next, unmark } }, details
                } };
                list.SelectionChanged += (_, _) =>
                {
                    unmark.IsEnabled = list.SelectedItem is MarkedEntry entry && Model.Selection.Marks.Contains(entry.Id);
                    details.Text = list.SelectedItem is MarkedEntry selected ? Describe(selected.Id) : "Choose a captured entry for its observation details.";
                };
                previous.Click += (_, _) => { if (offset > 0) Load(Math.Max(0, offset - 128)); };
                next.Click += (_, _) => { if (offset + 128 < ids.Length) Load(offset + 128); };
                unmark.Click += (_, _) =>
                {
                    if (list.SelectedItem is not MarkedEntry entry || !Model.Selection.Marks.Contains(entry.Id)) return;
                    Model.Selection.ToggleMark(entry.Id); Load(offset, entry.Id); Refresh();
                };
                Load(0);
                await _window._dialogs.ShowCustomAsync("Review marked entries", body,
                    [new DialogButton("Keep files / Close review", false, IsDefault: true, IsCancel: true)], closer: closer);
                void Load(int start, ulong? focus = null)
                {
                    offset = start;
                    list.ItemsSource = ids.Skip(offset).Take(128).Select(id => new MarkedEntry(id,
                        AnalysisNames.Display(tree.Items.GetValueOrDefault(AnalysisTree.Key(id))?.Name ?? "Missing observation", 256) + " · entry " + id +
                        (Model.Selection.Marks.Contains(id) ? "" : " · no longer marked"))).ToArray();
                    if (focus is { } id) list.SelectedItem = list.Items.OfType<MarkedEntry>().FirstOrDefault(row => row.Id == id);
                    previous.IsEnabled = offset > 0; next.IsEnabled = offset + list.ItemCount < ids.Length;
                    unmark.IsEnabled = list.SelectedItem is MarkedEntry selected && Model.Selection.Marks.Contains(selected.Id);
                    label.Text = $"Captured entries {offset + 1:N0}–{offset + list.ItemCount:N0} of {ids.Length:N0}. Currently marked: {Model.Selection.Marks.Count:N0}.";
                }
                string Describe(ulong id)
                {
                    var node = tree.Snapshot.Nodes.FirstOrDefault(node => node.Id == id);
                    if (node is null) return "This entry is absent from the captured revision. Its retained mark can be removed here.";
                    return $"Captured entry {id} · {node.Kind} · location {AnalysisNames.Display(node.RootId, 64)}\n" + AnalysisNames.DisplayName(node, 512) +
                        $"\nMetadata: {node.MetadataState} · " + (node.IncludedByPolicy ? "included by scan policy" : "excluded by scan policy") +
                        $"\nObservation revision {tree.Snapshot.Identity.Revision:N0}. This reference does not authorize filesystem actions.";
                }
            }
            finally
            {
                closer.Close(null); if (ReferenceEquals(_markReviewCloser, closer)) _markReviewCloser = null;
                _reviewingMarks = false; Model.Interaction(false); if (!_disposed) Refresh();
            }
        }
    }
}
