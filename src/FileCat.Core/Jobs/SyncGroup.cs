namespace FileCat.Core.Jobs;

/// <summary>
/// One reviewed synchronization submission. Child jobs remain ordinary guarded jobs; this owner coordinates
/// cancellation and keeps target-only removals behind the transfer outcome.
/// </summary>
public sealed class SyncGroup
{
    private readonly object _lock = new();
    private readonly JobManager _manager;
    private readonly Job[] _transfers;
    private readonly Job[] _removals;
    private bool _stopRequested;
    private bool _pendingRemovalReview;
    private bool _removalsSkipped;

    internal SyncGroup(JobManager manager, string source, string target, string reviewedPlan, Job[] jobs)
    {
        _manager = manager;
        Source = source;
        Target = target;
        ReviewedPlan = reviewedPlan;
        Steps = Array.AsReadOnly(jobs);
        ChildIds = Array.AsReadOnly(jobs.Select(j => j.Id).ToArray());
        _transfers = jobs.Where(j => j.Kind == JobKind.Copy).ToArray();
        _removals = jobs.Where(j => j.Kind is JobKind.Delete or JobKind.Recycle).ToArray();
        PendingRemovalPaths = Array.AsReadOnly(_removals.SelectMany(j => j.Request.Sources)
            .Select(source => source.FileSystemPath ?? source.ToString()).ToArray());
        manager.JobFinished += OnJobFinished;
    }

    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>Unique revision of this approved plan, even when the same endpoints are submitted again.</summary>
    public Guid ReviewRevision { get; } = Guid.NewGuid();
    public DateTime ReviewedUtc { get; } = DateTime.UtcNow;
    public string Source { get; }
    public string Target { get; }
    public string ReviewedPlan { get; }
    public IReadOnlyList<Job> Steps { get; }
    public IReadOnlyList<Guid> ChildIds { get; }
    public IReadOnlyList<string> PendingRemovalPaths { get; }
    public bool IsFinished => Steps.All(j => j.State.IsFinished());
    public bool CanStop => !IsFinished && !StopRequested;
    public bool StopRequested { get { lock (_lock) return _stopRequested; } }
    public bool PendingRemovalReview { get { lock (_lock) return _pendingRemovalReview; } }
    public bool RemovalsSkipped { get { lock (_lock) return _removalsSkipped; } }
    public event Action<SyncGroup>? Changed;

    internal void Begin()
    {
        if (_transfers.Length == 0)
            foreach (var removal in _removals) removal.Resume();
    }

    private void OnJobFinished(Job job)
    {
        if (!Steps.Contains(job)) return;
        lock (_lock)
        {
            if (!_stopRequested && _removals.Length > 0 && _transfers.All(j => j.State.IsFinished()) &&
                _removals.Any(j => !j.State.IsFinished() && j.IsPaused))
            {
                bool failed = _transfers.Any(j => j.State is JobState.Failed or JobState.Canceled or JobState.Interrupted ||
                    j.ItemsFailed > 0 || j.IssueTotals.Error > 0);
                if (failed) _pendingRemovalReview = true;
                else
                    foreach (var removal in _removals) removal.Resume();
            }
            if (_removals.All(j => j.State.IsFinished())) _pendingRemovalReview = false;
        }
        if (IsFinished) _manager.JobFinished -= OnJobFinished;
        Changed?.Invoke(this);
    }

    /// <summary>Stops only this submission. Already committed child work remains in its ordinary reports.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_stopRequested || IsFinished) return;
            _stopRequested = true;
            _pendingRemovalReview = false;
            _manager.CancelTogether(Steps.Where(j => !j.State.IsFinished()).ToArray());
        }
        Changed?.Invoke(this);
    }

    /// <summary>Explicit post-failure approval; the ordinary compared-target guards still decide each removal.</summary>
    public bool ContinueRemovals()
    {
        lock (_lock)
        {
            if (!_pendingRemovalReview || _stopRequested) return false;
            _pendingRemovalReview = false;
            foreach (var removal in _removals.Where(j => !j.State.IsFinished())) removal.Resume();
        }
        Changed?.Invoke(this);
        _manager.Schedule();
        return true;
    }

    public bool SkipRemovals()
    {
        lock (_lock)
        {
            if (!_pendingRemovalReview || _stopRequested) return false;
            _pendingRemovalReview = false;
            _removalsSkipped = true;
            _manager.CancelTogether(_removals.Where(j => !j.State.IsFinished()).ToArray());
        }
        Changed?.Invoke(this);
        return true;
    }

    public string Outcome
    {
        get
        {
            long done = Steps.Sum(j => j.ItemsDone), skipped = Steps.Sum(j => j.ItemsSkipped), failed = Steps.Sum(j => j.ItemsFailed);
            string counts = $"{Steps.Count(j => j.State.IsFinished()):N0}/{Steps.Count:N0} steps finished; {done:N0} done, {skipped:N0} skipped, {failed:N0} failed.";
            if (PendingRemovalReview) return "Transfers stopped or failed. Pending removals are held for review. " + counts;
            if (StopRequested) return (IsFinished ? "Synchronization stopped. " : "Stopping synchronization. ") +
                "Completed changes remain. " + counts;
            if (RemovalsSkipped) return "Pending removals were skipped. " + counts;
            if (IsFinished) return "Synchronization finished. " + counts;
            return "Synchronization running. " + counts;
        }
    }
}
