using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

/// <summary>One bounded preorder walk. Held traversal owners retire before summaries and terminal publication.</summary>
internal sealed class StorageTransformExecutor(Job job, IStorageTransforms adapter, StorageReviewOwner owner,
    StorageTransformJournalWriter writer, int observationLimit = StorageVisitLedger.ObservationLimit,
    long byteLimit = StorageVisitLedger.ByteLimit) : IJobExecutor
{
    private sealed class Frame(IStorageObjectLease held, ulong inherited, bool? directory)
    {
        internal readonly IStorageObjectLease Held = held;
        internal readonly ulong Inherited = inherited;
        internal ulong Owners = inherited, Selected;
        internal bool? Directory = directory;
        internal int Receipt = -1;
        internal bool Started, Settled, Complete = true, Descend;
        internal IEnumerator<StorageTraversalEntry>? Entries;
    }
    private readonly StorageTransformRequest _request = job.Request.StorageTransform ?? throw new InvalidOperationException("Missing storage approval.");
    private readonly StorageEfsRecoverySession? _recovery = job.Request.StorageTransform?.EfsConsent is { } consent ? new(consent) : null;
    private readonly Stack<Frame> _frames = new();
    private readonly StorageVisitLedger _visited = new(observationLimit, byteLimit);
    private StorageExecutionAccounting _account = null!;
    private StorageExecutionPlan? _plan;
    private readonly Dictionary<StorageObjectIdentity, (int Pin, ulong Sources)> _selected = [];
    private bool _stop;
    private int _source;
    private StorageObjectIdentity _volumeRoot = null!;

    public void Execute()
    {
        ReviewedStorageRoots.RequireAction(adapter, _request.Action);
        if (!owner.IsAlive || writer.Header.JobId != job.Id) throw new InvalidOperationException("Execution requires its reviewed owner and durable writer.");
        _account = new(_request);
        var pins = owner.Leases;
        foreach (var root in _request.Roots)
        {
            var id = root.Observation.Identity;
            _selected[id] = _selected.TryGetValue(id, out var prior) ? (root.PinIndex, prior.Sources | (1UL << root.SourceIndex)) : (root.PinIndex, 1UL << root.SourceIndex);
        }
        try
        {
            _plan = StorageExecutionPlan.Build(owner, _request, OpenRoot, job.Checkpoint, job.Token);
            foreach (var root in _plan.Roots)
            {
                if (_stop) break;
                job.Checkpoint();
                if (root.ParentPin is not null || _account.Reached(root.Sources)) continue;
                _source = System.Numerics.BitOperations.TrailingZeroCount(root.Sources);
                if (root.Problem is { } problem) { KnownRoot(root.PinIndex, problem); Publish(); continue; }
                try
                {
                    using var traversal = OpenRoot(pins[root.PinIndex]);
                    if (traversal.Root.Identity != pins[root.PinIndex].Observation.Identity) throw Changed("The reviewed root no longer has its pinned identity.");
                    _volumeRoot = traversal.Root.Identity;
                    _frames.Push(new(traversal.Root, root.Sources, pins[root.PinIndex].Observation.IsDirectory));
                    try { Walk(traversal); }
                    finally { Drain(); }
                }
                catch (Exception error) when (Operational(error)) { KnownRoot(root.PinIndex, Problem(error, "root-open")); }
                Publish();
            }
        }
        catch (OperationCanceledException) { _account.Canceled = true; _stop = true; }
        catch (Exception error) when (Operational(error) || error is InvalidOperationException)
        {
            _stop = true;
            _account.Reason(Problem(error, "execution-failed"));
            if (writer.IsFaulted || writer.Collector.Pending is not null) { _account.PersistenceFault = true; _account.ReviewPending = true; }
        }
        finally
        {
            Drain();
            _account.Canceled |= job.IsCancellationRequested;
            for (int pin = 0; pin < pins.Length; pin++)
                if (!_account.Reached(_selected[pins[pin].Observation.Identity].Sources))
                    KnownRoot(pin, new(StorageReasonKind.FileCatBoundary, "root-not-reached", "This reviewed root was not reached; its descendants were not counted.", false));
            if (!writer.IsFaulted && writer.Collector.Pending is null)
            {
                try { for (int i = 0; i < _request.Roots.Length; i++) writer.RootSummary(_account.RootSummary(i)); }
                catch (IOException error) { _account.PersistenceFault = true; _account.ReviewPending = true; _account.Reason(Problem(error, "root-summary-failed")); }
            }
            else { _account.PersistenceFault = true; _account.ReviewPending = true; }
            job.SetCurrent(null); Publish();
        }
    }

    private IStorageTraversal OpenRoot(IStorageReviewLease pin) => StorageExecutionRetry.Run(job, pin.Target.Path,
        () => _recovery is { } recovery && adapter is IStorageEfsJobAdapter efs
            ? efs.OpenReviewedEfsRoot(pin, job, System.Numerics.BitOperations.TrailingZeroCount(_selected[pin.Observation.Identity].Sources), recovery, job.Token)
            : adapter.OpenReviewedRoot(pin, _request.Action, job.Token));

    private void Walk(IStorageTraversal traversal)
    {
        while (_frames.Count > 0 && !_stop)
        {
            job.Checkpoint(); var frame = _frames.Peek(); job.SetCurrent(frame.Held.Path);
            if (!frame.Started)
            {
                Begin(frame);
                if (!frame.Descend || _stop)
                {
                    if (_stop && frame.Descend) { frame.Complete = false; _account.Incomplete(frame.Owners, true); }
                    Pop(); continue;
                }
                if (_frames.Count >= 64)
                {
                    ScopeProblem(frame, new(StorageReasonKind.FileCatBoundary, "depth-limit", "The 64-level traversal limit was reached; additional contents were not discovered.", false));
                    Pop(); continue;
                }
                try { frame.Entries = traversal.Entries(frame.Held, job.Token).GetEnumerator(); }
                catch (Exception error) when (Operational(error)) { ScopeProblem(frame, Problem(error, "enumeration-failed")); Pop(); }
                continue;
            }
            StorageTraversalEntry entry = null!; bool more;
            try
            {
                more = frame.Entries!.MoveNext();
                if (more) entry = Resolve(frame.Entries.Current);
            }
            catch (Exception error) when (Operational(error)) { ScopeProblem(frame, Problem(error, "enumeration-failed")); Pop(); continue; }
            if (!more) { Pop(); continue; }
            if (entry.Object is not { } held)
            {
                _account.ObservationCounts = true;
                if (!_visited.ObserveUnbound())
                {
                    _stop = true;
                    _account.Outcome(frame.Owners, entry.Path, StorageEffect.NotProcessed,
                        new(StorageReasonKind.FileCatBoundary, "observation-limit", "The bounded observation limit was reached; this known entry was not admitted.", true), false);
                    _account.Incomplete(frame.Owners, true); frame.Complete = false; Publish();
                    break;
                }
                var problem = entry.Problem ?? new(StorageReasonKind.Unknown, "child-unavailable", "A discovered child could not be admitted.", true);
                _account.Outcome(frame.Owners, entry.Path, problem.Code == StorageExecutionRetry.SharingSkipped ? StorageEffect.Skipped : StorageEffect.Failed, problem, false);
                _account.Incomplete(frame.Owners, entry.IsDirectory != false); frame.Complete = false; Publish(); continue;
            }
            if (entry.Problem is not null || entry.Path != held.Path)
            {
                held.Dispose(); ScopeProblem(frame, Changed("The discovered lease did not match its cursor entry.").Problem); continue;
            }
            _frames.Push(new(held, frame.Owners, entry.IsDirectory));
        }
    }

    private StorageTraversalEntry Resolve(StorageTraversalEntry entry)
    {
        if (entry.Object is not null || entry.Problem?.Code != "sharing-violation" || entry.Retry is null) return entry;
        bool first = true;
        try
        {
            return StorageExecutionRetry.Run(job, entry.Path, () =>
            {
                var current = first ? entry : entry.Retry(job.Token); first = false;
                if (current.Object is null && current.Problem?.Code == "sharing-violation") throw new StorageInspectionException(current.Problem);
                return current;
            });
        }
        catch (StorageInspectionException skipped) when (skipped.Problem.Code == StorageExecutionRetry.SharingSkipped) { return entry with { Problem = skipped.Problem, Retry = null }; }
    }

    private void Begin(Frame frame)
    {
        frame.Started = true;
        var identity = frame.Held.Identity;
        if (identity.Volume != _volumeRoot.Volume || identity.VolumeBinding != _volumeRoot.VolumeBinding)
        {
            _account.ObservationCounts = true;
            if (!_visited.ObserveUnbound()) _stop = true;
            NoCall(frame, StorageEffect.NotProcessed, Changed("A discovered object crossed the admitted root's volume binding.").Problem); return;
        }
        frame.Selected = _selected.TryGetValue(identity, out var selected) ? selected.Sources : 0;
        frame.Owners |= frame.Selected;
        var admitted = _visited.Admit(identity, frame.Owners, out int receipt);
        if (admitted is StorageVisitAdmission.LiveDuplicate or StorageVisitAdmission.ClosedDuplicate ||
            frame.Selected != 0 && _account.Reached(frame.Selected))
        {
            frame.Owners = frame.Inherited; frame.Selected = 0;
            _account.ObservationCounts = true; _account.AmbiguousDuplicates++;
            NoCall(frame, StorageEffect.NotProcessed, Changed("This identity was already encountered; its earlier outcome cannot establish this scope's current subtree.").Problem); return;
        }
        _account.Reach(frame.Selected);
        if (admitted == StorageVisitAdmission.Limit)
        {
            _stop = true;
            NoCall(frame, StorageEffect.NotProcessed, new(StorageReasonKind.FileCatBoundary, "observation-limit", "The bounded observation ledger is full; additional work was not admitted.", true)); return;
        }
        frame.Receipt = receipt;
        if (selected.Sources != 0 && _plan!.Roots[selected.Pin].Problem is { } blocked) { NoCall(frame, StorageEffect.NotProcessed, blocked); return; }
        StorageObjectCallResult result;
        try { result = StorageObjectCallRunner.Run(job, writer, _source, frame.Held); }
        catch (OperationCanceledException) { _account.Canceled = true; _stop = true; NoCall(frame, StorageEffect.NotProcessed, null); return; }
        catch (Exception error) when (Operational(error)) { NoCall(frame, StorageEffect.Failed, Problem(error, "observation-failed")); return; }
        frame.Directory = result.Result.Before.IsDirectory;
        _account.ObservationCounts |= result.Result.Problem?.Kind == StorageReasonKind.IdentityChanged;
        _account.Outcome(frame.Owners, frame.Held.Path, result.Result.Effect, result.Result.Problem, result.Result.NativeCallAttempted);
        _visited.Settle(receipt, result.Result.Effect); frame.Settled = true;
        bool success = result.Result.Effect is StorageEffect.Changed or StorageEffect.AlreadySatisfied;
        frame.Complete &= success;
        frame.Descend = success && frame.Directory == true && _request.Scope == StorageScope.ExistingDescendants;
        if (!success) _account.Incomplete(frame.Owners, RecursiveDirectory(frame));
        if (result.PersistenceProblem is { } persistence) _account.Reason(persistence);
        _account.Canceled |= result.Canceled; _account.PersistenceFault |= result.PersistenceFault;
        _account.ReviewPending |= result.PersistenceFault || result.Result.Effect == StorageEffect.Uncertain;
        _stop |= result.MustStop;
        Publish();
    }

    private bool RecursiveDirectory(Frame frame) => _request.Scope == StorageScope.ExistingDescendants && frame.Directory != false;
    private void NoCall(Frame frame, StorageEffect effect, StorageProblem? problem)
    {
        _account.Outcome(frame.Owners, frame.Held.Path, effect, problem, false);
        if (frame.Receipt >= 0) { _visited.Settle(frame.Receipt, effect); frame.Settled = true; }
        frame.Complete = false; frame.Descend = false;
        _account.Incomplete(frame.Owners, RecursiveDirectory(frame)); Publish();
    }
    private void ScopeProblem(Frame frame, StorageProblem problem)
    { frame.Complete = false; _account.Incomplete(frame.Owners, true); _account.Reason(problem); Publish(); }
    private void Pop()
    {
        var frame = _frames.Pop();
        bool retired = true;
        try { frame.Entries?.Dispose(); }
        catch (Exception error) when (Operational(error)) { CleanupFailed(error); }
        finally
        {
            try { frame.Held.Dispose(); }
            catch (Exception error) when (Operational(error)) { retired = false; CleanupFailed(error); }
            finally
            {
                if (retired && frame.Receipt >= 0 && frame.Settled) _visited.Close(frame.Receipt, frame.Complete);
                _account.Finish(frame.Selected, frame.Complete);
                if (_frames.TryPeek(out var parent)) parent.Complete &= frame.Complete;
            }
        }
        void CleanupFailed(Exception error)
        {
            _stop = true; frame.Complete = false; _account.ReviewPending = true;
            _account.Incomplete(frame.Owners, RecursiveDirectory(frame));
            _account.Reason(Problem(error, "owner-retirement-failed"));
        }
    }
    private void Drain()
    {
        while (_frames.Count > 0)
        {
            var frame = _frames.Peek(); frame.Complete = false;
            if (!frame.Started)
            {
                var identity = frame.Held.Identity;
                frame.Selected = _selected.TryGetValue(identity, out var selected) && !_account.Reached(selected.Sources) ? selected.Sources : 0;
                frame.Owners |= frame.Selected; _account.Reach(frame.Selected);
                var admitted = _visited.Admit(identity, frame.Owners, out int receipt);
                if (admitted == StorageVisitAdmission.New) frame.Receipt = receipt;
                else if (admitted is StorageVisitAdmission.LiveDuplicate or StorageVisitAdmission.ClosedDuplicate)
                { _account.ObservationCounts = true; _account.AmbiguousDuplicates++; }
                frame.Started = true; NoCall(frame, StorageEffect.NotProcessed, null);
            }
            _account.Incomplete(frame.Owners, RecursiveDirectory(frame));
            if (frame.Receipt >= 0 && !frame.Settled) NoCall(frame, StorageEffect.NotProcessed, null);
            Pop();
        }
    }
    private void KnownRoot(int pin, StorageProblem problem)
    {
        var lease = owner.Leases[pin]; ulong selected = _selected[lease.Observation.Identity].Sources;
        if (_account.Reached(selected)) return;
        ulong owners = _plan?.AncestorSources(pin) ?? selected;
        _account.Reach(selected);
        if (_visited.Admit(lease.Observation.Identity, owners, out int receipt) == StorageVisitAdmission.New)
        { _visited.Settle(receipt, StorageEffect.NotProcessed); _visited.Close(receipt, false); }
        _account.Outcome(owners, lease.Target.Path, StorageEffect.NotProcessed, problem, false);
        _account.Incomplete(owners, _request.Scope == StorageScope.ExistingDescendants && lease.Observation.IsDirectory);
        _account.Finish(selected, false);
    }
    private void Publish() => job.SetStorageResult(_account.Snapshot(writer.Prefix) with { CurrentPath = job.CurrentItem, ObservedUtc = DateTime.UtcNow });
    private static bool Operational(Exception error) => error is IOException or UnauthorizedAccessException or NotSupportedException;
    private static StorageInspectionException Changed(string message) => new(new(StorageReasonKind.IdentityChanged, "scope-changed", message, true));
    private static StorageProblem Problem(Exception error, string code) => error is StorageInspectionException inspection ? inspection.Problem :
        new(error is UnauthorizedAccessException ? StorageReasonKind.PermissionOrPolicy : StorageReasonKind.TransientFailure,
            code, error.Message.Length <= 2000 ? error.Message : error.Message[..2000], true);
}
