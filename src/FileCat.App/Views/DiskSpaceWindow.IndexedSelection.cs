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
        private readonly CancellationTokenSource _selectionLifetime = new();
        private CancellationTokenSource? _memberCaptureCancel;
        private bool _capturingMembers, _updatingMarks, _reviewingBasket;
        private ulong? _basketInspection;
        private readonly TextBlock _selectionSummary = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _markFocused = new() { Content = "Toggle focused mark" }, _markMembers = new() { Content = "Mark current members" },
            _clearMarks = new() { Content = "Clear marks" }, _stageBasket = new() { Content = "Add to review basket" },
            _reviewBasket = new() { Content = "Review captured basket" }, _clearBasket = new() { Content = "Clear review basket" };

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
            panel.Children.Add(row); return panel;
        }
        private void AddSelectionControls()
        {
            AutomationProperties.SetName(_selectionSummary, "Indexed captured selection");
            AutomationProperties.SetLiveSetting(_selectionSummary, AutomationLiveSetting.Polite);
            var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            foreach (var button in new[] { _markFocused, _markMembers, _clearMarks, _stageBasket, _reviewBasket, _clearBasket }) bar.Children.Add(button);
            _main.Children.Add(_selectionSummary); _main.Children.Add(bar);
            ToolTip.SetTip(_markMembers, new RichTip("Capture current group members", detail: "Marks the captured real entries of the focused group or current group scope. Hidden aliases are excluded. Marks do not include entries observed later. At most 100,000 distinct paths can be marked."));
            ToolTip.SetTip(_stageBasket, new RichTip("Add captured entries", detail: "Add the current marks, or the focused real entry when no paths are marked, to the review basket. Later filtering and navigation do not change this membership."));
            _markFocused.Click += (_, _) => { if (_focus != 0) ToggleMark(_focus); };
            _markMembers.Click += (_, _) => _ = MarkMembersAsync();
            _clearMarks.Click += (_, _) => { _memberCaptureCancel?.Cancel(); _selection.ClearMarks(); RefreshSelection(); };
            _stageBasket.Click += (_, _) =>
            {
                try { _selection.StageCaptured(_selection.Marks.Count != 0 ? _selection.Marks : _focus == 0 ? [] : [_focus]); RefreshSelection(); }
                catch (InvalidOperationException error) { _status.Text = error.Message; }
            };
            _reviewBasket.Click += (_, _) => _ = ReviewBasketAsync();
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
        }
        private void ToggleMark(ulong path)
        {
            if (_disposed) return;
            try { _selection.ToggleMark(path); }
            catch (InvalidOperationException error) { _status.Text = error.Message; }
            RefreshSelection();
        }
        private void RefreshSelection()
        {
            if (_disposed) return;
            _selectionSummary.Text = $"{_selection.Marks.Count:N0} marked · {_selection.Basket.Count:N0} in review basket. Marks may be outside this page or filter.";
            _markFocused.IsEnabled = _focus != 0;
            _markMembers.IsEnabled = _grouping is not null && !_capturingMembers;
            _clearMarks.IsEnabled = _selection.Marks.Count != 0 || _capturingMembers;
            _stageBasket.IsEnabled = _selection.Marks.Count != 0 || _focus != 0;
            _reviewBasket.IsEnabled = _selection.Basket.Count != 0 && !_reviewingBasket;
            _clearBasket.IsEnabled = _selection.Basket.Count != 0;
            _updatingMarks = true;
            try { foreach (var check in _list.GetVisualDescendants().OfType<CheckBox>()) if (check.Tag is ulong path) check.IsChecked = _selection.Marks.Contains(path); }
            finally { _updatingMarks = false; }
            UpdateCharts();
        }
        private async Task MarkMembersAsync()
        {
            if (_disposed || _capturingMembers || _actualWork.HasUnconfirmedClose || _grouping is not { } groups) return;
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

        private sealed record BasketEntry(ulong Id, string Label) { public override string ToString() => Label; }
        private async Task ReviewBasketAsync()
        {
            if (_disposed || _reviewingBasket || _selection.Basket.Count == 0 || _actualWork.HasUnconfirmedClose) return;
            using var actualWork = _actualWork.Enter();
            _reviewingBasket = true; RefreshSelection();
            using var reviewCancel = CancellationTokenSource.CreateLinkedTokenSource(_selectionLifetime.Token); var reviewToken = reviewCancel.Token;
            ulong[] captured = _selection.Basket.Order().ToArray(); int offset = 0; bool closed = false, loading = false;
            var closer = new DialogCloser(); var list = new ListBox { MinHeight = 120, MaxHeight = 320 };
            AutomationProperties.SetName(list, "Captured basket entries");
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var previous = new Button { Content = "Previous basket entries" }; var next = new Button { Content = "Next basket entries" };
            var remove = new Button { Content = "Remove selected from basket" }; var inspect = new Button { Content = "Inspect captured basket entry" };
            list.SelectionChanged += (_, _) => ReviewButtons();
            var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { previous, next, remove, inspect } };
            var body = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Captured entries for review. No files are changed here. Removing an entry changes only this basket.", TextWrapping = TextWrapping.Wrap }, label, list, bar } };
            using var registration = _selectionLifetime.Token.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
            try
            {
                await LoadAsync(0); if (_disposed) return;
                previous.Click += async (_, _) => { if (!loading && offset != 0) await LoadAsync(Math.Max(0, offset - 128)); };
                next.Click += async (_, _) => { if (!loading && offset + 128 < captured.Length) await LoadAsync(offset + 128); };
                remove.Click += (_, _) =>
                {
                    if (list.SelectedItem is not BasketEntry entry || !_selection.Basket.Contains(entry.Id)) return;
                    _selection.RemoveFromBasket(entry.Id);
                    var replacement = entry with { Label = entry.Label + " · removed from basket" };
                    list.ItemsSource = list.Items.OfType<BasketEntry>().Select(row => row.Id == entry.Id ? replacement : row).ToArray(); list.SelectedItem = replacement;
                    RefreshSelection(); ReviewLabel(); ReviewButtons();
                };
                inspect.Click += (_, _) => { if (list.SelectedItem is BasketEntry entry) { closer.Close(null); _basketInspection = entry.Id; _selectedGroupedItem = null; _groupFocus = null; _ = InspectAsync(entry.Id); } };
                await _window._dialogs.ShowCustomAsync("Review captured basket", body, [new DialogButton("Keep files / Close review", false, IsDefault: true, IsCancel: true)], closer: closer);
            }
            catch (OperationCanceledException) when (reviewToken.IsCancellationRequested) { }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "Basket review is unavailable. " + AnalysisNames.Display(error.Message, 500); }
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
                            (_selection.Basket.Contains(path.Id) ? "" : " · removed from basket")));
                    }
                    if (closed || _disposed) return;
                    offset = requestedOffset; list.ItemsSource = rows; ReviewLabel();
                }
                catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!closed && !_disposed) label.Text = "This page is unavailable. " + AnalysisNames.Display(error.Message, 500); }
                finally { loading = false; if (!closed && !_disposed) ReviewButtons(); }
            }
            void ReviewLabel() => label.Text = $"Captured entries {offset + 1:N0}–{offset + list.ItemCount:N0} of {captured.Length:N0}. Current basket: {_selection.Basket.Count:N0}.";
            void ReviewButtons()
            {
                previous.IsEnabled = !loading && offset != 0; next.IsEnabled = !loading && offset + list.ItemCount < captured.Length;
                inspect.IsEnabled = !loading && list.SelectedItem is BasketEntry;
                remove.IsEnabled = !loading && list.SelectedItem is BasketEntry entry && _selection.Basket.Contains(entry.Id);
            }
        }
    }
}
