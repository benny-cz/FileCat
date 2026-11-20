using FileCat.Core.Analysis;

namespace FileCat.App.Services;

/// <summary>Bounded direct-child change hints for explicitly selected native roots.</summary>
internal sealed class AnalysisRootChangeHints : IDisposable
{
    internal enum State { Watching, PossiblyStale, WatchLost, SourceUnverified, IdentityMismatch, Unmonitored }

    // The limit is process wide. No recursive watches are installed, including
    // on Linux where recursive watching can allocate a watch per directory.
    private const int MaximumWatches = 8;
    private static int s_active;
    private readonly object _gate = new();
    private readonly List<RootHint> _roots = [];
    private int _version;
    private int _describedVersion = -1;
    private string? _description;
    private bool _disposed;

    private sealed class RootHint(string label, State state, Action changed)
    {
        private int _state = (int)state;
        internal string Label { get; } = AnalysisNames.Display(label, 72);
        internal State Current => (State)Volatile.Read(ref _state);
        internal FileSystemWatcher? Watcher;
        internal IDisposable? HandleBudget;
        internal void Changed()
        {
            if (Interlocked.CompareExchange(ref _state, (int)State.PossiblyStale, (int)State.Watching) == (int)State.Watching)
                changed();
        }

        internal void Lost()
        {
            if (Interlocked.Exchange(ref _state, (int)State.WatchLost) != (int)State.WatchLost) changed();
        }
    }

    private void InvalidateDescription() => Interlocked.Increment(ref _version);

    internal void Record(string label, State state)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _roots.Add(new(label, state, InvalidateDescription));
            InvalidateDescription();
        }
    }

    internal bool Watch(string directory, string label)
    {
        while (true)
        {
            int active = Volatile.Read(ref s_active);
            if (active == MaximumWatches) return false;
            if (Interlocked.CompareExchange(ref s_active, active + 1, active) == active) break;
        }

        var hint = new RootHint(label, State.Watching, InvalidateDescription);
        try
        {
            hint.HandleBudget = AnalysisNativeHandles.Acquire();
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes |
                    NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Security
            };
            hint.Watcher = watcher;
            watcher.Created += (_, _) => hint.Changed();
            watcher.Deleted += (_, _) => hint.Changed();
            watcher.Changed += (_, _) => hint.Changed();
            watcher.Renamed += (_, _) => hint.Changed();
            watcher.Error += (_, _) => hint.Lost();
            watcher.EnableRaisingEvents = true;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _roots.Add(hint);
                InvalidateDescription();
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Release(hint);
            return false;
        }
        catch
        {
            Release(hint);
            throw;
        }
    }

    private static void Release(RootHint hint)
    {
        // Keep the reservation if watcher teardown itself fails. A failed
        // release cannot make that native place available to another analysis.
        hint.Watcher?.Dispose();
        hint.HandleBudget?.Dispose();
        Interlocked.Decrement(ref s_active);
    }

    internal string? Describe()
    {
        lock (_gate)
        {
            if (_disposed || _roots.Count == 0) return null;
            int version = Volatile.Read(ref _version);
            if (version == _describedVersion) return _description;
            var state = _roots.Select(root => (root.Label, Status: root.Current)).ToArray();
            var changed = state.Where(root => root.Status is State.PossiblyStale or State.WatchLost or State.IdentityMismatch or State.SourceUnverified)
                .Select(root => root.Label + (root.Status switch
                {
                    State.WatchLost => " (change notifications were interrupted)",
                    State.IdentityMismatch => " (source identity changed)",
                    State.SourceUnverified => " (source could not be revalidated)",
                    _ => ""
                })).ToArray();
            string detail = changed.Length == 0 ? "" :
                "Possibly stale: " + string.Join(", ", changed.Take(6)) + (changed.Length > 6 ? $" and {changed.Length - 6} more." : ".") +
                " Refresh a folder or rescan the locations to record new observations. ";
            int monitored = state.Count(root => root.Status is State.Watching or State.PossiblyStale);
            _description = detail + (monitored == 0 ? "Live change hints are unavailable for these locations." :
                $"Direct changes watched at {monitored:N0} of {state.Length:N0} selected locations. Other changes can go undetected.");
            _describedVersion = version;
            return _description;
        }
    }

    public void Dispose()
    {
        RootHint[] roots;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            roots = _roots.ToArray();
            _roots.Clear();
        }

        List<Exception>? failures = null;
        foreach (var root in roots)
        {
            if (root.Watcher is null) continue;
            try { Release(root); }
            catch (Exception error) { (failures ??= []).Add(AnalysisCloseUnconfirmedException.From(error)); }
        }
        if (failures is not null) throw new AggregateException(failures);
    }
}
