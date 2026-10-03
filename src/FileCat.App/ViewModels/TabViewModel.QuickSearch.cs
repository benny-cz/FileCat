using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.Core.Listing;
using FileCat.Core.Selection;

namespace FileCat.App.ViewModels;

public sealed partial class TabViewModel
{
    private const int BackgroundQuickSearchFrom = 4096;
    private enum QuickSearchKey { Text, Backspace, Next, Previous }
    private readonly Queue<(QuickSearchKey Key, string Text)> _quickSearchKeys = new();
    private CancellationTokenSource? _quickSearchCancel;
    private Task? _quickSearchWork;
    private string? _quickSearchCandidate;
    private string? _pendingQuickSearchText;
    [ObservableProperty] private bool _isQuickSearchPending;

    public string? ShownQuickSearch => _pendingQuickSearchText ?? QuickSearch;
    public bool ShownQuickSearchNoMatch => QuickSearchNoMatch && !IsQuickSearchPending;

    partial void OnQuickSearchNoMatchChanged(bool value) => OnPropertyChanged(nameof(ShownQuickSearchNoMatch));
    partial void OnIsQuickSearchPendingChanged(bool value) => OnPropertyChanged(nameof(ShownQuickSearchNoMatch));

    /// <summary>The current scan/queue lifetime, for diagnostics; cancellation acknowledges before this finishes.</summary>
    internal Task QuickSearchWork => _quickSearchWork ?? Task.CompletedTask;

    /// <summary>
    /// Extends quick search, rejecting text that matches nothing. Large listings accept input for background
    /// processing; its answer and any subsequent keys apply in order without holding the window's thread.
    /// </summary>
    public bool QuickSearchType(string text)
    {
        if (_disposed) return false;
        if (_quickSearchCancel is null && Listing.VisibleCount <= BackgroundQuickSearchFrom)
        {
            string candidate = (QuickSearch ?? string.Empty) + text;
            return ApplyQuickSearch(QuickSearchKey.Text, candidate, FindQuickMatch(candidate, 0, true));
        }
        QueueQuickSearch(QuickSearchKey.Text, text);
        return true;
    }

    public void QuickSearchBackspace()
    {
        if (QuickSearch is null || _disposed) return;
        if (_quickSearchCancel is not null || Listing.VisibleCount > BackgroundQuickSearchFrom)
        {
            QueueQuickSearch(QuickSearchKey.Backspace);
            return;
        }
        if (QuickSearch.Length == 0) { EndQuickSearch(); return; }
        string candidate = QuickSearch[..^1];
        ApplyQuickSearch(QuickSearchKey.Backspace, candidate, FindQuickMatch(candidate, 0, true));
    }

    public void QuickSearchCycle(bool forward)
    {
        if (_disposed || string.IsNullOrEmpty(QuickSearch) && _quickSearchCancel is null) return;
        var key = forward ? QuickSearchKey.Next : QuickSearchKey.Previous;
        if (_quickSearchCancel is not null || Listing.VisibleCount > BackgroundQuickSearchFrom)
        {
            QueueQuickSearch(key);
            return;
        }
        ApplyQuickSearch(key, QuickSearch!, FindQuickMatch(QuickSearch!, Listing.FocusedIndex + (forward ? 1 : -1), forward));
    }

    public void EndQuickSearch()
    {
        _quickSearchCancel?.Cancel();
        _quickSearchCancel = null;
        _quickSearchKeys.Clear();
        _quickSearchCandidate = null;
        IsQuickSearchPending = false;
        SetPendingQuickSearch(null);
        QuickSearch = null;
        QuickSearchNoMatch = false;
    }

    private void QueueQuickSearch(QuickSearchKey key, string text = "")
    {
        QuickSearch ??= string.Empty;
        _quickSearchKeys.Enqueue((key, text));
        UpdatePendingQuickSearch();
        if (_quickSearchCancel is not null) return;
        var cancel = new CancellationTokenSource();
        _quickSearchCancel = cancel;
        _quickSearchWork = ProcessQuickSearchAsync(cancel);
    }

    private async Task ProcessQuickSearchAsync(CancellationTokenSource cancel)
    {
        IsQuickSearchPending = true;
        try
        {
            while (_quickSearchKeys.TryDequeue(out var input))
            {
                if (_disposed || !ReferenceEquals(_quickSearchCancel, cancel)) return;
                string accepted = QuickSearch ?? string.Empty;
                if (input.Key == QuickSearchKey.Backspace && accepted.Length == 0) { EndQuickSearch(); return; }
                string candidate = input.Key switch
                {
                    QuickSearchKey.Text => accepted + input.Text,
                    QuickSearchKey.Backspace => accepted[..^1],
                    _ => accepted,
                };
                _quickSearchCandidate = candidate;
                UpdatePendingQuickSearch();
                bool forward = input.Key != QuickSearchKey.Previous;
                int start = input.Key is QuickSearchKey.Next or QuickSearchKey.Previous
                    ? Listing.FocusedIndex + (forward ? 1 : -1) : 0;
                if (candidate.Length == 0)
                {
                    ApplyQuickSearch(input.Key, candidate, -1);
                }
                else
                {
                    ListingModel.VisibleSearchResult result;
                    do
                    {
                        // A refresh or view change may retire the captured rows. Keep the user's key, but search
                        // the current display order before applying it. Navigation/Esc/closure cancel the queue.
                        start = input.Key is QuickSearchKey.Next or QuickSearchKey.Previous
                            ? Listing.FocusedIndex + (forward ? 1 : -1) : 0;
                        result = await Listing.FindVisibleAsync(start, forward, QuickMatcher(candidate), cancel.Token);
                        if (_disposed || cancel.IsCancellationRequested || !ReferenceEquals(_quickSearchCancel, cancel)) return;
                    } while (!result.IsCurrent);
                    ApplyQuickSearch(input.Key, candidate, result.Row);
                }
                _quickSearchCandidate = null;
                UpdatePendingQuickSearch();
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_disposed && ReferenceEquals(_quickSearchCancel, cancel))
            {
                EndQuickSearch();
                Banner = "Quick search could not finish: " + ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_quickSearchCancel, cancel))
            {
                _quickSearchCancel = null;
                _quickSearchCandidate = null;
                IsQuickSearchPending = false;
                SetPendingQuickSearch(null);
            }
            cancel.Dispose();
        }
    }

    private bool ApplyQuickSearch(QuickSearchKey key, string candidate, int row)
    {
        if (key == QuickSearchKey.Text && row < 0)
        {
            QuickSearchNoMatch = true;
            QuickSearch ??= string.Empty;
            return false;
        }
        if (key is QuickSearchKey.Text or QuickSearchKey.Backspace)
        {
            QuickSearch = candidate;
            QuickSearchNoMatch = false;
        }
        if (row >= 0) Listing.SetFocus(row);
        return true;
    }

    private int FindQuickMatch(string text, int start, bool forward) =>
        text.Length == 0 ? -1 : Listing.FindVisible(start, forward, QuickMatcher(text));

    private ListingModel.NameMatch QuickMatcher(string text)
    {
        bool wildcard = text.Contains('*') || text.Contains('?');
        bool anywhere = Services.Settings.QuickSearchMatchAnywhere;
        string pattern = wildcard ? text.TrimEnd('*') + "*" : text;
        var comparison = System.Text.Ascii.IsValid(text) ? StringComparison.OrdinalIgnoreCase : StringComparison.CurrentCultureIgnoreCase;
        return name => wildcard ? Wildcard.IsMatch(name, pattern)
            : anywhere ? name.Contains(text, comparison) : name.StartsWith(text, comparison);
    }

    private void UpdatePendingQuickSearch()
    {
        string shown = _quickSearchCandidate ?? QuickSearch ?? string.Empty;
        foreach (var input in _quickSearchKeys)
        {
            if (input.Key == QuickSearchKey.Text) shown += input.Text;
            else if (input.Key == QuickSearchKey.Backspace && shown.Length > 0) shown = shown[..^1];
        }
        SetPendingQuickSearch(shown);
    }

    private void SetPendingQuickSearch(string? text)
    {
        if (_pendingQuickSearchText == text) return;
        _pendingQuickSearchText = text;
        OnPropertyChanged(nameof(ShownQuickSearch));
    }
}
