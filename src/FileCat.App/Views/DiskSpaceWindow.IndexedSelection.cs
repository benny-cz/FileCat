using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedSessionView
    {
        private readonly AnalysisSelectionState _selection = new();
        internal void SetReviewedMarks(AnalysisSelection selection) { _selection.MarkCaptured(selection.NodeIds); RefreshSelection(); }
        private readonly CancellationTokenSource _selectionLifetime = new();
        private CancellationTokenSource? _memberCaptureCancel;
        private bool _capturingMembers, _updatingMarks, _reviewingBasket;
        private ulong? _basketInspection;
        private string _referenceInspectionLabel = "basket";
        private readonly TextBlock _selectionSummary = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _markFocused = new() { Content = "Toggle focused mark" }, _markMembers = new() { Content = "Mark current members" },
            _clearMarks = new() { Content = "Clear marks" }, _stageBasket = new() { Content = "Add to review basket" },
            _reviewBasket = new() { Content = "Review captured basket" }, _clearBasket = new() { Content = "Clear review basket" },
            _reviewMarked = new() { Content = "Review marked entries" }, _recountMarks = new() { Content = "Update mark counts", IsVisible = false },
            _markMatches = new() { Content = "Mark matching files in this scope" };

        private Control MarkedRow(AnalysisTreeItem item, Control row)
        {
            var panel = new DockPanel { LastChildFill = true };
            if (item.NodeId is { } id)
            {
                var check = new CheckBox { Tag = id, IsChecked = _selection.Marks.Contains(id), Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(check, "Mark captured entry " + item.Name + " [" + id + "]");
                check.IsCheckedChanged += (_, _) => { if (!_updatingMarks && !_disposed && check.IsChecked != _selection.Marks.Contains(id)) ToggleMark(id); };
                panel.Children.Add(check);
            }
            else panel.Children.Add(new Border { Width = 30 });
            // The list item takes its name from this panel, else from the row's record ("AnalysisHierarchyRow { Path = …", I653).
            AutomationProperties.SetName(panel, AutomationProperties.GetName(row));
            panel.Children.Add(row); return panel;
        }
        private void AddSelectionControls()
        {
            AutomationProperties.SetAutomationId(_selectionSummary, "Indexed captured selection");
            AutomationProperties.SetLiveSetting(_selectionSummary, AutomationLiveSetting.Polite);
            var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            foreach (var button in new[] { _markFocused, _markMembers, _markMatches, _clearMarks, _reviewMarked, _reviewDuplicates, _reviewCleanup, _stageBasket, _reviewBasket, _reviewBasketActions, _clearBasket, _recountMarks }) bar.Children.Add(button);
            bar.Children.Add(_openPanelResults);
            _openPanelResults.Click += (_, _) => _ = OpenPanelResultsAsync();
            _main.Children.Add(_selectionSummary); _main.Children.Add(bar);
            var deferred = new Button { Content = "Select all after scan completes", IsEnabled = false };
            const string deferredReason = "This is a captured inventory. Use Mark matching files in this scope, or start a new live analysis to request selection after its scan.";
            ToolTip.SetTip(deferred, new RichTip("Captured inventory", detail: deferredReason));
            AutomationProperties.SetHelpText(deferred, deferredReason);
            _main.Children.Add(deferred);
            _showCapturedFolder.Click += (_, _) => _ = ShowCapturedFolderAsync();
            ToolTip.SetTip(_showCapturedFolder, new RichTip("Show the captured folder", detail: "Find the selected entry in this inventory's recorded folder structure. No live location is opened. Display filters are cleared with an explanation if they hide the entry."));
            ToolTip.SetTip(_markMembers, new RichTip("Capture current group members", detail: "Marks the captured real entries of the focused group or current group scope. Hidden aliases are excluded. Marks do not include entries observed later. At most 100,000 distinct paths can be marked."));
            ToolTip.SetTip(_stageBasket, new RichTip("Add captured entries", detail: "Add the current marks, or the focused real entry when no paths are marked, to the review basket. Later filtering and navigation do not change this membership."));
            _markFocused.Click += (_, _) => { if (_focus != 0) ToggleMark(_focus); };
            _markMembers.Click += (_, _) => _ = MarkMembersAsync();
            ToolTip.SetTip(_markMatches, new RichTip("Capture matching files", detail: "Marks regular-file matches in the current captured scope and query. Focus does not narrow the scope. Context folders, hidden aliases and future observations are excluded. At most 100,000 distinct paths can be marked."));
            _markMatches.Click += (_, _) => _ = MarkMatchingFilesAsync();
            _clearMarks.Click += (_, _) => { CancelTableRange(); _memberCaptureCancel?.Cancel(); _selection.ClearMarks(); RefreshSelection(); };
            _stageBasket.Click += (_, _) =>
            {
                try { _selection.StageCaptured(_selection.Marks.Count != 0 ? _selection.Marks : _focus == 0 ? [] : [_focus]); RefreshSelection(); }
                catch (InvalidOperationException error) { _status.Text = error.Message; }
            };
            _reviewBasket.Click += (_, _) => _ = ReviewCapturedAsync(basket: true);
            _reviewCleanup.Click += (_, _) => _ = ReviewCleanupAsync(AnalysisVerb.Recycle);
            AutomationProperties.SetHelpText(_reviewCleanup, "Review marked entries, or the focused real entry when there are no marks.");
            _reviewBasketActions.Click += (_, _) => _ = ReviewCleanupAsync(AnalysisVerb.Recycle, basket: true);
            _reviewMarked.Click += (_, _) => _ = ReviewCapturedAsync(basket: false);
            _reviewDuplicates.Click += (_, _) => _ = ReviewDuplicatesAsync();
            _recountMarks.Click += (_, _) => { _visibilityOwner = null; RefreshSelection(); };
            _clearBasket.Click += (_, _) => { _selection.ClearBasket(); RefreshSelection(); };
            _list.AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args.Key == Key.Space && _window.FocusManager?.GetFocusedElement() is not CheckBox) HandleKeys(args);
            }, RoutingStrategies.Tunnel);
            bool pressed = false, wired = false; IPointer? pointer = null;
            _list.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (!pressed && args.GetCurrentPoint(_list).Properties.IsLeftButtonPressed)
                { pressed = true; pointer = args.Pointer; Interaction(true); }
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            void EndPress() { if (pressed) { pressed = false; pointer = null; Interaction(false); } }
            void Release(object? sender, PointerReleasedEventArgs args) { if (ReferenceEquals(pointer, args.Pointer)) EndPress(); }
            void Deactivate(object? sender, EventArgs args) => EndPress();
            _list.AddHandler(PointerReleasedEvent, Release, RoutingStrategies.Bubble, handledEventsToo: true);
            _list.PointerCaptureLost += (_, _) => EndPress();
            _list.AttachedToVisualTree += (_, _) =>
            {
                if (wired) return; wired = true;
                _window.AddHandler(PointerReleasedEvent, Release, RoutingStrategies.Bubble, handledEventsToo: true); _window.Deactivated += Deactivate;
            };
            _list.DetachedFromVisualTree += (_, _) =>
            {
                if (wired) { wired = false; _window.RemoveHandler(PointerReleasedEvent, Release); _window.Deactivated -= Deactivate; }
                EndPress();
            };
            ConfigureIndexedRanges();
        }
        private void ToggleMark(ulong path)
        {
            if (_disposed) return;
            CancelTableRange();
            try { _selection.ToggleMark(path); }
            catch (InvalidOperationException error) { _status.Text = error.Message; }
            RefreshSelection();
        }
        private void RefreshSelection()
        {
            if (_disposed) return;
            RefreshBranchAvailability();
            UpdatePreviewButton(_previewFile, PreviewRefusal(FocusedPreviewPath, _runtimeAcquisition));
            UpdateRevealButton(_revealInPanel, PanelRevealRefusal(_services, _runtimeAcquisition, FocusedRevealPath));
            UpdateResultsButton(_openPanelResults, _services, Math.Max(_selection.Marks.Count, _focus == 0 ? 0 : 1));
            RefreshClipboardButton();
            _bindIndexedSource.IsVisible = IndexedRecordedChoice() is not null;
            ScheduleMarkVisibility();
            _markFocused.IsEnabled = _focus != 0;
            _showCapturedFolder.IsEnabled = _focus != 0;
            _markMembers.IsEnabled = _grouping is not null && !_capturingMembers;
            _markMatches.IsEnabled = !_capturingMembers && !_actualWork.HasUnconfirmedClose;
            _clearMarks.IsEnabled = _selection.Marks.Count != 0 || _capturingMembers;
            _stageBasket.IsEnabled = _selection.Marks.Count != 0 || _focus != 0;
            _reviewBasket.IsEnabled = _selection.Basket.Count != 0 && !_reviewingBasket;
            _reviewCleanup.IsEnabled = _selection.Marks.Count != 0 || _focus != 0;
            _reviewBasketActions.IsEnabled = _selection.Basket.Count != 0;
            _reviewMarked.IsEnabled = _selection.Marks.Count != 0 && !_reviewingBasket;
            _reviewDuplicates.IsEnabled = _reviewMarked.IsEnabled;
            _clearBasket.IsEnabled = _selection.Basket.Count != 0;
            _updatingMarks = true;
            try { foreach (var check in _list.GetVisualDescendants().OfType<CheckBox>()) if (check.Tag is ulong path) check.IsChecked = _selection.Marks.Contains(path); }
            finally { _updatingMarks = false; }
            UpdateCharts();
        }
        private async Task MarkMembersAsync()
        {
            if (_disposed || _capturingMembers || _actualWork.HasUnconfirmedClose || _grouping is not { } groups) return;
            CancelTableRange();
            if (_focus != 0) { try { _selection.MarkCaptured([_focus]); } catch (InvalidOperationException error) { _status.Text = error.Message; } RefreshSelection(); return; }
            using var actualWork = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token);
            _memberCaptureCancel = cancel; _capturingMembers = true;
            string? key = _groupFocus ?? _groupKey; _status.Text = "Capturing this group's observed members…"; RefreshSelection();
            try
            {
                var captured = await groups.CaptureMembersAsync(key, cancellation: cancel.Token);
                cancel.Token.ThrowIfCancellationRequested(); if (_disposed) return;
                var identity = _source.Descriptor.Identity;
                if (captured.SessionId != identity.SessionId || captured.Epoch != identity.Epoch || captured.Revision != identity.Revision)
                    throw new InvalidOperationException("The captured group belongs to another observation revision.");
                _selection.MarkCaptured(captured.NodeIds); _status.Text = $"Captured {captured.NodeIds.Length:N0} real group members. Existing marks are retained.";
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { if (!_disposed) _status.Text = "Member capture canceled. No captured members were added."; }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "Member capture was not applied. " + AnalysisNames.Display(error.Message, 500); }
            finally { _capturingMembers = false; if (ReferenceEquals(_memberCaptureCancel, cancel)) _memberCaptureCancel = null; RefreshSelection(); }
        }

        private async Task MarkMatchingFilesAsync()
        {
            if (_disposed || _capturingMembers || _actualWork.HasUnconfirmedClose) return;
            CancelTableRange();
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token);
            _memberCaptureCancel = cancel; _capturingMembers = true;
            var groups = _grouping; var explorer = _explorer; string? key = _groupKey; ulong folder = _scope;
            _status.Text = "Capturing matching files in this displayed scope and revision…"; RefreshSelection();
            try
            {
                var captured = groups is not null ? await groups.CaptureMatchingFilesAsync(key, cancellation: cancel.Token) :
                    await explorer.CaptureMatchingFilesAsync(folder, cancellation: cancel.Token);
                cancel.Token.ThrowIfCancellationRequested(); if (_disposed) return;
                var identity = _source.Descriptor.Identity;
                if (captured.SessionId != identity.SessionId || captured.Epoch != identity.Epoch || captured.Revision != identity.Revision)
                    throw new InvalidOperationException("These captured matches belong to another observation revision.");
                _selection.MarkCaptured(captured.NodeIds);
                _status.Text = $"Marked {captured.NodeIds.Length:N0} matching file paths from this captured scope. Existing marks are retained; future entries are not selected.";
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { if (!_disposed) _status.Text = "Matching-file capture canceled. No captured matches were added."; }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "Matching-file capture was not applied. " + AnalysisNames.Display(error.Message, 500); }
            finally { _capturingMembers = false; if (ReferenceEquals(_memberCaptureCancel, cancel)) _memberCaptureCancel = null; RefreshSelection(); }
        }

        private sealed record BasketEntry(ulong Id, string Label) { public override string ToString() => Label; }
        private async Task ReviewCapturedAsync(bool basket)
        {
            if (_disposed || _reviewingBasket || Current().Count == 0 || _actualWork.HasUnconfirmedClose) return;
            using var actualWork = _actualWork.Enter();
            _reviewingBasket = true; RefreshSelection();
            using var reviewCancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token); var reviewToken = reviewCancel.Token;
            ulong[] captured = Current().Order().ToArray(); int offset = 0; bool closed = false, loading = false;
            var closer = new DialogCloser(); var list = new ListBox { MinHeight = 120, MaxHeight = 320 };
            AutomationProperties.SetName(list, basket ? "Captured basket entries" : "Captured marked entries");
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var previous = new Button { Content = "Previous basket entries" }; var next = new Button { Content = "Next basket entries" };
            var remove = new Button { Content = basket ? "Remove selected from basket" : "Unmark selected entry" }; var inspect = new Button { Content = basket ? "Inspect captured basket entry" : "Inspect captured marked entry" };
            list.SelectionChanged += (_, _) => ReviewButtons();
            var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { previous, next, remove, inspect } };
            var body = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Captured entries for review, including entries hidden by the current scope or filters. No files are changed here. " +
                (basket ? "Removing an entry changes only this basket." : "Unmarking an entry does not change the review basket."), TextWrapping = TextWrapping.Wrap }, label, list, bar } };
            using var registration = _selectionLifetime.Token.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
            try
            {
                await LoadAsync(0); if (_disposed) return;
                previous.Click += async (_, _) => { if (!loading && offset != 0) await LoadAsync(Math.Max(0, offset - 128)); };
                next.Click += async (_, _) => { if (!loading && offset + 128 < captured.Length) await LoadAsync(offset + 128); };
                remove.Click += (_, _) =>
                {
                    if (list.SelectedItem is not BasketEntry entry || !Current().Contains(entry.Id)) return;
                    if (basket) _selection.RemoveFromBasket(entry.Id); else { CancelTableRange(); _selection.ToggleMark(entry.Id); }
                    var replacement = entry with { Label = entry.Label + RemovedLabel() };
                    list.ItemsSource = list.Items.OfType<BasketEntry>().Select(row => row.Id == entry.Id ? replacement : row).ToArray(); list.SelectedItem = replacement;
                    RefreshSelection(); ReviewLabel(); ReviewButtons();
                };
                inspect.Click += (_, _) => { if (list.SelectedItem is BasketEntry entry) { closer.Close(null); _basketInspection = entry.Id; _referenceInspectionLabel = basket ? "basket" : "marked"; _selectedGroupedItem = null; _groupFocus = null; _ = InspectAsync(entry.Id); } };
                await _window._dialogs.ShowCustomAsync(basket ? "Review captured basket" : "Review marked entries", body, [new DialogButton("Keep files / Close review", false, IsDefault: true, IsCancel: true)], closer: closer);
            }
            catch (OperationCanceledException) when (reviewToken.IsCancellationRequested) { }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "Captured selection review is unavailable. " + AnalysisNames.Display(error.Message, 500); }
            finally { closed = true; reviewCancel.Cancel(); closer.Close(null); _reviewingBasket = false; RefreshSelection(); }
            async Task LoadAsync(int requestedOffset)
            {
                if (closed || _disposed) return;
                using var actualPage = _actualWork.Enter();
                loading = true; previous.IsEnabled = next.IsEnabled = remove.IsEnabled = inspect.IsEnabled = false;
                try
                {
                    var rows = new List<BasketEntry>();
                    for (int at = requestedOffset; at < Math.Min(captured.Length, requestedOffset + 128); at++)
                    {
                        if (closed || _disposed) return;
                        var path = await _source.ReadPathAsync(captured[at], reviewToken);
                        rows.Add(new(path.Id, AnalysisNames.DisplayName(path, 256) + " · captured location " + AnalysisNames.Display(path.RootId, 64) + " · entry " + path.Id +
                            (Current().Contains(path.Id) ? "" : RemovedLabel())));
                    }
                    if (closed || _disposed) return;
                    offset = requestedOffset; list.ItemsSource = rows; ReviewLabel();
                }
                catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!closed && !_disposed) label.Text = "This page is unavailable. " + AnalysisNames.Display(error.Message, 500); }
                finally { loading = false; if (!closed && !_disposed) ReviewButtons(); }
            }
            ImmutableHashSet<ulong> Current() => basket ? _selection.Basket : _selection.Marks;
            string RemovedLabel() => basket ? " · removed from basket" : " · no longer marked";
            void ReviewLabel() => label.Text = $"Captured entries {offset + 1:N0}–{offset + list.ItemCount:N0} of {captured.Length:N0}. " + (basket ? $"Current basket: {Current().Count:N0}." : $"Currently marked: {Current().Count:N0}.");
            void ReviewButtons()
            {
                previous.IsEnabled = !loading && offset != 0; next.IsEnabled = !loading && offset + list.ItemCount < captured.Length;
                inspect.IsEnabled = !loading && list.SelectedItem is BasketEntry;
                remove.IsEnabled = !loading && list.SelectedItem is BasketEntry entry && Current().Contains(entry.Id);
            }
        }
    }
}
