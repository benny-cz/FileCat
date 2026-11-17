using System.Collections.Immutable;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.ViewModels;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Threading;

namespace FileCat.App.Views;

/// <summary>One immutable capture, one in-flight check and one replaceable latest revision.</summary>
internal sealed class StorageCompressionDialog : IDisposable
{
    private readonly StorageSelectionReview _review;
    private readonly DeviceIoScheduler _io;
    private readonly string _device;
    private readonly ImmutableArray<bool> _hidden;
    private readonly CancellationTokenSource _stop;
    private readonly DialogCloser _closer = new();
    private readonly DialogButtonPresentation _primary = new(new("Choose action"));
    private readonly TextBlock _status = Text(), _counts = Text(), _help = Text(), _scopeHelp = Text();
    private readonly TextBox _search = new() { PlaceholderText = "Find a selected name or original location" };
    private readonly TextBox _items = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 };
    private readonly Expander _disclosure;
    private readonly RadioButton _selectedOnly, _contents;
    private readonly RadioButton _compress, _remove;
    private readonly Button _retry;
    private readonly Button _showBlocker = new() { Content = "Cancel preparation and show operation", IsVisible = false };
    private readonly DispatcherTimer _resourceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private StorageAction? _selectedAction;
    private StorageScope _scope = StorageScope.SelectedItems;
    private Task? _work;
    private bool _pending, _open = true, _milestone, _frozen;
    private ReviewedStorageRoots? _approved;
    private StorageReviewApproval? _approval;
    private CancellationTokenRegistration _initialRegistration, _shutdownRegistration;
    private readonly CancellationToken _initial;
    private readonly Action? _onReady;

    internal StorageCompressionDialog(StorageSelectionReview review, DeviceIoScheduler io, string device,
        IReadOnlyList<bool> hidden, CancellationToken initial = default, Action? onReady = null)
    {
        if (hidden.Count != review.Targets.Length) throw new ArgumentException("Hidden-mark facts must match the immutable capture.", nameof(hidden));
        _review = review; _io = io; _device = device; _hidden = hidden.ToImmutableArray(); _initial = initial; _onReady = onReady;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(io.StoppingToken);
        string actionGroup = "storage-action-" + Guid.NewGuid().ToString("N");
        _compress = new RadioButton { Content = "Compress", GroupName = actionGroup };
        _remove = new RadioButton { Content = "Remove compression", GroupName = actionGroup };
        AutomationProperties.SetName(_compress, "Compress");
        AutomationProperties.SetName(_remove, "Remove compression");
        _compress.IsCheckedChanged += (_, _) => { if (_compress.IsChecked == true) { _selectedAction = StorageAction.Compress; Request(); } };
        _remove.IsCheckedChanged += (_, _) => { if (_remove.IsChecked == true) { _selectedAction = StorageAction.RemoveCompression; Request(); } };
        string group = "storage-scope-" + Guid.NewGuid().ToString("N");
        _selectedOnly = new RadioButton { Content = Text("Selected items only"), GroupName = group, IsChecked = true };
        _contents = new RadioButton { Content = Text("Selected folders and their existing contents"), GroupName = group };
        _selectedOnly.IsCheckedChanged += (_, _) => { if (_selectedOnly.IsChecked == true) { _scope = StorageScope.SelectedItems; Request(); } };
        _contents.IsCheckedChanged += (_, _) => { if (_contents.IsChecked == true) { _scope = StorageScope.ExistingDescendants; Request(); } };
        AutomationProperties.SetName(_selectedOnly, "Selected items only");
        AutomationProperties.SetName(_contents, "Selected folders and their existing contents");
        _retry = new Button { Content = "Retry inspection" };
        _retry.Click += (_, _) => Request(retry: true);
        _showBlocker.Click += (_, _) =>
        {
            if (StorageReviewResources.NativeBlocker is not { } blocker || blocker.State.IsFinished()) { RenderResourceWait(); return; }
            RequestedOperation = blocker; _closer.Close("blocker");
        };
        AutomationProperties.SetName(_showBlocker, "Cancel preparation and show the blocking operation");
        _resourceTimer.Tick += (_, _) => { if (_open && !_frozen) RenderResourceWait(); };
        AutomationProperties.SetName(_retry, "Retry inspection of the original selected items");
        AutomationProperties.SetName(_search, "Search selected items by name or original location");
        AutomationProperties.SetName(_items, "Selected items and original locations, select text to copy");
        _search.TextChanged += (_, _) => RenderItems(_review.State);
        _disclosure = new Expander { Header = $"Selected items ({review.Targets.Length})", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = 6, Children = { _search, _items } } };
        AutomationProperties.SetName(_disclosure, $"All {review.Targets.Length} selected items, including hidden marks");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        int hiddenCount = _hidden.Count(value => value);
        Body = new StackPanel { Spacing = 10, Children =
        {
            Text($"{review.Targets.Length} selected items. Includes {hiddenCount} marked items hidden by the filter."),
            _counts, Text("Current state of selected items. Folder contents have not been inspected."),
            Text("Choose action"), _compress, _remove, _help,
        } };
        if (review.Sources.Any(source => source.Kind == Core.Resources.EntryKind.Directory))
        {
            Body.Children.Add(_selectedOnly); Body.Children.Add(_contents); Body.Children.Add(_scopeHelp);
        }
        Body.Children.Add(_status); Body.Children.Add(_showBlocker); Body.Children.Add(_retry); Body.Children.Add(_disclosure);
        Body.Children.Add(Text("Changes the main file data or folder default. Alternate data streams keep their own compression settings."));
        Body.Children.Add(Text("Storage changes run separately from other tracked file operations. Later queued operations may wait while this change is queued, paused, or waiting for your decision."));
        _review.Request(null, StorageScope.SelectedItems);
        Render();
    }

    internal StackPanel Body { get; }
    internal bool Ready => _milestone;
    internal Task? PendingWork => _work;
    internal Job? RequestedOperation { get; private set; }

    /// <summary>The returned owner must be submitted or disposed by the caller.</summary>
    internal async Task<StorageCompressionChoice?> ShowAsync(IDialogService dialogs)
    {
        Task<object?>? dialog = null;
        try
        {
            dialog = dialogs.ShowCustomAsync("NTFS compression…", Body,
                [new DialogButton("Cancel", "cancel", IsCancel: true),
                 new DialogButton("Queue", "queue") { IsAvailable = CanApprove, Validate = Freeze },
                 new DialogButton("Choose action", "start", IsDefault: true) { Presentation = _primary, IsAvailable = CanApprove, Validate = Freeze }],
                _compress, closer: _closer, resizable: true, behavior: new DialogBehavior { KeepOperationsUsable = true });
            _initialRegistration = _initial.Register(() => Dispatcher.UIThread.Post(() => { if (!_milestone && _open) _closer.Close("cancel"); }));
            _shutdownRegistration = _stop.Token.Register(() => Dispatcher.UIThread.Post(() => { if (_open) _closer.Close("cancel"); }));
            _resourceTimer.Start();
            Request();
            object? result = await dialog;
            if (result is not ("start" or "queue") || _approved is null || _approval is null) return null;
            var choice = new StorageCompressionChoice(_approved, _approval, result is "queue" ? QueueMode.Queue : QueueMode.Start);
            _approved = null; return choice;
        }
        finally
        {
            _open = false; _stop.Cancel();
            _resourceTimer.Stop();
            _initialRegistration.Dispose(); _shutdownRegistration.Dispose();
            try
            {
                if (_work is not null) await _work; // Cancellation cannot retire a native delegate's owner early.
            }
            finally
            {
                _closer.Close("cancel");
                try { if (dialog is not null) await dialog; }
                finally { _approved?.Dispose(); _approved = null; _review.Dispose(); }
            }
        }
    }

    private bool CanApprove() => _open && _milestone && !_frozen && !_stop.IsCancellationRequested && _review.State.CanApprove;
    private bool Freeze()
    {
        if (!CanApprove()) return false;
        var state = _review.State;
        var frozen = _review.Freeze(state.Revision);
        try { _approval = frozen.Approve(state.Action!.Value, state.Scope); _approved = frozen; _frozen = true; return true; }
        catch { frozen.Dispose(); throw; }
    }
    private void Request(bool retry = false)
    {
        if (!_open || _frozen || _stop.IsCancellationRequested) return;
        if (!_milestone && _initial.IsCancellationRequested) { _closer.Close("cancel"); return; }
        _review.Request(_selectedAction, _scope, retry);
        _pending = _review.State.NeedsInspection;
        Render();
        if (_pending && (_work is null || _work.IsCompleted)) _work = InspectPendingAsync();
    }
    private async Task InspectPendingAsync()
    {
        while (_open && _pending && !_stop.IsCancellationRequested)
        {
            _pending = false; long revision = _review.State.Revision;
            try
            {
                await _review.InspectAsync(_io, _device, revision, _stop.Token, state => Dispatcher.UIThread.Post(() =>
                { if (_open && !_frozen && state.Revision == _review.State.Revision) Render(); }));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error)
            {
                AppLog.Warn("Storage compression inspection failed", error);
                if (_open) { Render(); _status.Text = "Cannot inspect selected items: " + error.Message; _disclosure.IsExpanded = true; }
                return;
            }
            if (!_open || _stop.IsCancellationRequested || _frozen) return;
            var current = _review.State;
            if (current.Revision != revision) { _pending = true; continue; }
            if (!_milestone && !current.NeedsInspection)
            {
                if (_initial.IsCancellationRequested) { _closer.Close("cancel"); return; }
                _milestone = true; _initialRegistration.Dispose(); _onReady?.Invoke();
            }
            Render();
        }
    }
    private void Render()
    {
        var state = _review.State;
        int enabled = 0, disabled = 0, unknown = 0, unsupported = 0;
        foreach (var root in state.Roots)
        {
            if (root.Problem is not null || root.Stale) unknown++;
            else switch (root.Observation?.Compression)
            {
                case StorageObservationState.Enabled: enabled++; break;
                case StorageObservationState.Disabled: disabled++; break;
                case StorageObservationState.Unsupported: unsupported++; break;
                default: unknown++; break;
            }
        }
        _counts.Text = $"Compression: {enabled} enabled, {disabled} disabled, {unknown} unknown or unreadable, {unsupported} unsupported.";
        _help.Text = state.Action switch
        {
            StorageAction.Compress => "NTFS compression may reduce disk space used. Results depend on the file and performance may change.",
            StorageAction.RemoveCompression => "Removing compression can require additional free space.",
            _ => "Choose an explicit action for every selected item.",
        };
        _scopeHelp.Text = state.Scope == StorageScope.SelectedItems
            ? "Changes the selected folder's default for newly created content. Existing files and subfolders are not converted."
            : "Includes hidden items. Links and unsupported items are left unchanged and reported. Files added or moved while this runs may be missed.";
        bool refused = state.Problem is not null || state.Roots.Any(root => root.Problem is not null || root.Stale || root.Observation?.Ineligible is not null ||
            !state.NeedsInspection && root.Observation?.Compression is not (StorageObservationState.Enabled or StorageObservationState.Disabled));
        _status.Text = state.Problem?.Message ?? (state.Inspecting ? $"Checking selected items: {state.Checked} of {state.Roots.Length}…"
            : state.NeedsInspection ? "Waiting for a storage inspection resource. Another storage check or review may be using this resource."
            : refused ? "Some selected items cannot use this action. Review Selected items below, retry inspection, or cancel and change the selection."
            : state.Action is null ? "Choose an action to continue." : "Every selected item passed inspection for this action.");
        if (refused) _disclosure.IsExpanded = true;
        _retry.IsEnabled = !_frozen && !_stop.IsCancellationRequested;
        RenderItems(state);
        string label = state.Action switch { StorageAction.Compress => "Compress", StorageAction.RemoveCompression => "Remove compression", _ => "Choose action" };
        string scope = state.Scope == StorageScope.SelectedItems ? "Selected items only" : "Selected folders and their existing contents";
        _primary.Update(new(label, label, $"{scope}. Changes main data or folder defaults; alternate data streams keep their compression settings."));
        RenderResourceWait();
    }
    private void RenderResourceWait()
    {
        var state = _review.State;
        bool waiting = _open && !_frozen && !_stop.IsCancellationRequested && state.NeedsInspection && !state.Inspecting;
        var blocker = waiting ? StorageReviewResources.NativeBlocker : null;
        _showBlocker.IsVisible = blocker is not null && !blocker.State.IsFinished();
        if (!waiting) return;
        _status.Text = _showBlocker.IsVisible
            ? $"Waiting for the storage resource owned by {JsonSerializer.Serialize(blocker!.Title)}. Cancel this preparation to show that operation."
            : "Waiting for a storage inspection resource. Another storage check or review may be using this resource.";
    }
    private void RenderItems(StorageSelectionState state)
    {
        string filter = _search.Text ?? string.Empty;
        var rows = new List<string>();
        for (int i = 0; i < _review.Targets.Length; i++)
        {
            var target = _review.Targets[i]; var root = state.Roots[i];
            string location = target.Path;
            if (filter.Length != 0 && !location.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !target.Source.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            string reason = root.Problem?.Message ?? root.Observation?.Ineligible?.Message ?? (root.Stale ? "Original binding changed; close and select again."
                : state.NeedsInspection ? "Inspection pending" : root.Observation is null ? "Not inspected" : "Inspection complete");
            rows.Add($"Name: {JsonSerializer.Serialize(target.Source.Name)}\nOriginal location: {JsonSerializer.Serialize(location)}\nProvider: Local filesystem\nHidden mark: {(_hidden[i] ? "Yes" : "No")}\nCompression: {root.Observation?.Compression.ToString() ?? "Not inspected"}\nEligibility: {JsonSerializer.Serialize(reason)}");
        }
        _items.Text = rows.Count == 0 ? "No selected items match this search." : string.Join("\n\n", rows);
    }
    private static TextBlock Text(string? text = null) => new() { Text = text, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };
    public void Dispose()
    {
        _open = false; _stop.Cancel(); _initialRegistration.Dispose(); _shutdownRegistration.Dispose();
        _resourceTimer.Stop();
        _review.Dispose(); _approved?.Dispose(); _stop.Dispose();
    }
}

internal sealed record StorageCompressionChoice(ReviewedStorageRoots Roots, StorageReviewApproval Approval, QueueMode Mode) : IDisposable
{
    public void Dispose() => Roots.Dispose();
}
