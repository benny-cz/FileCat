using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.ViewModels;

public sealed partial class TabViewModel
{
    private const int MaxHistory = 50;
    private readonly List<HistoryVisit> _back = [];
    private readonly List<HistoryVisit> _forward = [];
    private HistoryJump? _loadingHistory;
    private ItemRef? _historyDetachedTop;

    private sealed record HistoryVisit(Location Location, ItemRef? Focus, ItemRef? Top);
    private sealed record HistoryJump(bool Forward, int Offset, long Revision, HistoryVisit Visit);

    public IReadOnlyList<Location> BackHistory => _back.Select(visit => visit.Location).ToArray();
    public IReadOnlyList<Location> ForwardHistory => _forward.Select(visit => visit.Location).ToArray();
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    internal long HistoryRevision { get; private set; }
    internal Func<ItemRef?>? ReadHistoryTop { get; set; }

    internal void DetachHistoryViewport(Func<ItemRef?> reader)
    {
        if (ReadHistoryTop != reader) return;
        _historyDetachedTop = reader();
        ReadHistoryTop = null;
    }

    internal Location[] HistoryLocations(bool forward) =>
        (forward ? _forward : _back).AsEnumerable().Reverse().Select(visit => visit.Location).ToArray();

    private HistoryVisit CaptureVisit()
    {
        ItemRef? focus = Listing.TryGetFocused(out var entry)
            ? entry.Kind == EntryKind.Parent ? new ItemRef(Location!, "..", EntryKind.Parent) : Listing.GetItemRef(Listing.FocusedStoreIndex)
            : null;
        return new(Location!, focus, ReadHistoryTop is { } read ? read() : _historyDetachedTop);
    }

    private void NotifyHistoryChanged()
    {
        HistoryRevision++;
        OnPropertyChanged(nameof(HistoryRevision));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    public void GoBack() => StepHistory(forward: false);
    public void GoForward() => StepHistory(forward: true);

    private void StepHistory(bool forward)
    {
        var visits = forward ? _forward : _back;
        if (visits.Count == 0 || Location is null) { CancelPendingNavigation(); return; }
        // Key repeat can arrive before a local listing's first batch. Advance from the pending
        // destination in the same direction, while an explicit menu choice always names its own visit.
        int step = _pendingNavigation is not null && _loadingHistory is { } jump && jump.Forward == forward
            ? Math.Min(jump.Offset + 1, visits.Count) : 1;
        if (_pendingNavigation is not null && _loadingHistory is { } current &&
            current.Forward == forward && current.Offset == step) return;
        JumpHistory(forward, step, HistoryRevision);
    }

    internal bool JumpHistory(bool forward, int step, long revision)
    {
        var visits = forward ? _forward : _back;
        if (_disposed || Location is null || revision != HistoryRevision || step < 1 || step > visits.Count) return false;
        var visit = visits[^step];
        if (!Services.Providers.IsRegistered(visit.Location.Scheme))
        {
            BeginNavigationIntent();
            Banner = "This type of location is unavailable here.";
            return true;
        }
        if (IsLocked && !ReturnToRoot)
        {
            long intent = BeginNavigationIntent();
            if (OwnsNavigation(intent)) Panel.OpenTab(visit.Location,
                visit.Focus?.Kind == EntryKind.Parent ? null : visit.Focus?.Name, activate: true);
        }
        else
            Navigate(visit.Location, null, record: false, refresh: false, new(forward, step, revision, visit));
        return true;
    }

    private void MoveHistory(HistoryJump jump)
    {
        var direction = jump.Forward ? _forward : _back;
        var opposite = jump.Forward ? _back : _forward;
        opposite.Add(CaptureVisit());
        for (int i = 1; i < jump.Offset; i++) opposite.Add(direction[^i]);
        direction.RemoveRange(direction.Count - jump.Offset, jump.Offset);
    }

    private async Task LoadHistoryAsync(Location location, HistoryJump jump, long intent, CancellationTokenSource pending,
        PreparedLocation? prepared)
    {
        Task? enumeration = null;
        bool retained = false;
        bool reportedFailure = false;
        string? fallback = null;
        var destination = location;
        try
        {
            while (OwnsNavigation(intent, pending) && jump.Revision == HistoryRevision)
            {
                try
                {
                    var stage = Listing.StageNavigation(destination);
                    enumeration = stage.Enumeration;
                    await stage.Ready;
                    if (OwnsNavigation(intent, pending) && jump.Revision == HistoryRevision)
                        retained = CommitNavigation(destination, null, record: false, intent, prepared, jump: jump, fallback: fallback);
                    break;
                }
                catch (LinkListingRefusedException refused) when (destination.IsFileSystem && refused.Target != destination)
                {
                    destination = refused.Target;
                    fallback = $"“{Formatters.SafeName(refused.Link)}” could not be listed. This is the folder it leads to.";
                }
                catch (DirectoryNotFoundException) when (destination.IsFileSystem &&
                    Services.Providers.For(destination).GetParent(destination) is { IsFileSystem: true } parent && parent != destination)
                {
                    destination = parent;
                    fallback = $"“{Formatters.SafeName(Services.Providers.Display(location))}” is no longer there. This is the nearest folder that still exists.";
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (OwnsNavigation(intent, pending))
            {
                Banner = ex is OperationCanceledException ? _bannerBeforePendingNavigation :
                    $"{Formatters.SafeName(Services.Providers.Display(location))} could not be opened: {Formatters.SafeName(ex.Message)}";
                reportedFailure = true;
            }
        }
        finally
        {
            if (ReferenceEquals(_pendingNavigation, pending))
            {
                Listing.CancelStagedNavigation();
                if (!retained && !reportedFailure) Banner = _bannerBeforePendingNavigation;
            }
            FinishPendingNavigation(pending);
            if (!retained && prepared is not null)
            {
                if (enumeration is { IsCompleted: false })
                    _ = ReleaseAfterEnumerationAsync(enumeration, prepared);
                else
                    prepared.Dispose();
            }
        }
    }

    private static async Task ReleaseAfterEnumerationAsync(Task enumeration, PreparedLocation prepared)
    {
        try { await enumeration; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* The navigation already owns reporting. */ }
        finally { prepared.Dispose(); }
    }
}
