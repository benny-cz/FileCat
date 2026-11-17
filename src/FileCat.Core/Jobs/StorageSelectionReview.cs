using System.Collections.Immutable;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Jobs;

public sealed record StorageSelectionRoot(int SourceIndex, StorageObservation? Observation, StorageProblem? Problem,
    bool Pinned, bool Stale);
public sealed record StorageSelectionState(long Revision, StorageAction? Action, StorageScope Scope, bool Inspecting,
    bool NeedsInspection, bool CanApprove, int Checked, ImmutableArray<StorageSelectionRoot> Roots, StorageProblem? Problem);

/// <summary>One immutable selection and its adopted lifetime pins. Revisions never change membership or replace a pin.</summary>
public sealed class StorageSelectionReview : IDisposable
{
    private readonly object _gate = new();
    private readonly StorageObjectIdentity?[] _bound;
    private readonly IStorageTransforms _adapter;
    private readonly IStorageReviewLease?[] _pins;
    private readonly StorageObservation?[] _facts;
    private readonly StorageProblem?[] _errors;
    private readonly bool[] _stale;
    private IDisposable? _pinAllowance;
    private long _revision;
    private StorageAction? _action;
    private StorageScope _scope;
    private bool _busy, _disposed, _transferred, _needsInspection = true;
    private int _checked;
    private StorageProblem? _problem;
    public ImmutableArray<StorageTarget> Targets { get; }
    public ImmutableArray<ItemRef> Sources { get; }
    internal StorageSelectionReview(IStorageTransforms adapter, IReadOnlyList<ItemRef> sources)
    {
        _adapter = adapter; Targets = StoragePaths.Capture(sources);
        ReviewedStorageRoots.RequireMetadataBound(Targets);
        Sources = Targets.Select(t => t.Source).ToImmutableArray();
        _pins = new IStorageReviewLease?[Targets.Length]; _facts = new StorageObservation?[Targets.Length];
        _bound = new StorageObjectIdentity?[Targets.Length];
        _errors = new StorageProblem?[Targets.Length]; _stale = new bool[Targets.Length];
        _pinAllowance = StorageReviewResources.ReservePins(Targets.Length);
    }
    public StorageSelectionState State { get { lock (_gate) return Snapshot(); } }

    /// <summary>Immediately invalidates approval. Cached action eligibility and scope changes require no native I/O.</summary>
    public long Request(StorageAction? action, StorageScope scope, bool retry = false)
    {
        if (action is { } requested) ReviewedStorageRoots.RequireAction(_adapter, requested);
        ReviewedStorageRoots.RequireScope(_adapter, action, scope);
        lock (_gate)
        {
            Check(); var previousAction = _action; bool previousReady = !_needsInspection;
            _revision++; _action = action; _scope = scope;
            _needsInspection = true; _problem = null;
            if (_busy || retry || _pins.Any(p => p?.IsAlive != true) || _errors.Any(e => e is not null) || _stale.Any(s => s)) return _revision;
            if (previousReady && previousAction == action) { _needsInspection = false; CheckMetadata(); return _revision; }
            var updated = new StorageObservation?[Targets.Length];
            for (int i = 0; i < Targets.Length; i++)
            {
                var observed = _adapter.EvaluatePinnedRoot(_pins[i]!, action);
                if (observed is null) return _revision;
                RequireObservation(observed, _pins[i]!, _bound[i]!, Targets[i]); updated[i] = observed;
            }
            Array.Copy(updated, _facts, updated.Length); _needsInspection = false; CheckMetadata(); return _revision;
        }
    }

    /// <summary>Reserve outside the queue and retain ownership through actual return, even after Stop or close.</summary>
    internal async Task InspectAsync(DeviceIoScheduler io, string device, long revision, CancellationToken ct,
        Action<StorageSelectionState>? progress = null)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, io.StoppingToken);
        try
        {
            using var reservation = await StorageReviewResources.ReserveNativeAsync(stop.Token).ConfigureAwait(false);
            await io.RunOwned(device, IoPriority.Interactive, token =>
            {
                using var native = reservation.Enter(); InspectOwned(revision, token, progress); return true;
            }, stop.Token).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate) if (!_disposed && !_transferred && _revision == revision) { _revision++; _needsInspection = true; }
            throw;
        }
    }

    /// <summary>Run only inside a pre-reserved native allowance on an owned device delegate. No capacity wait occurs here.</summary>
    internal void InspectOwned(long revision, CancellationToken ct, Action<StorageSelectionState>? progress = null)
    {
        _ = StorageNativeAllowance.RequireCurrent(); StorageAction? action;
        lock (_gate)
        {
            Check(); if (revision != _revision) return;
            if (_busy) throw new InvalidOperationException("This selection is already being inspected.");
            _busy = true; _needsInspection = true; _checked = 0; action = _action;
        }
        try
        {
            if (progress is not null)
                try { progress(State); } catch (Exception ex) { AppLog.Warn("Storage selection observer failed", ex); }
            for (int i = 0; i < Targets.Length; i++)
            {
                ct.ThrowIfCancellationRequested(); IStorageReviewLease? pin;
                lock (_gate)
                {
                    if (_disposed || revision != _revision) return;
                    if (_stale[i]) { _checked = i + 1; continue; }
                    pin = _pins[i];
                }
                StorageObservation? observed = null; StorageProblem? error = null; IStorageReviewLease? acquired = null;
                try
                {
                    if (pin is not null && !pin.IsAlive)
                        throw new StorageInspectionException(new(StorageReasonKind.IdentityChanged, "pin-retired", "The original lifetime pin retired. Close this review and select the item again.", true));
                    using var inspecting = StorageReviewResources.EnterRootInspection(pin?.Target ?? Targets[i]);
                    if (pin is null)
                    {
                        acquired = _adapter.InspectRoot(Targets[i], action, ct)
                            ?? throw new InvalidOperationException("The adapter returned no lifetime pin.");
                        ReviewedStorageRoots.RequireLease(acquired, Targets[i]); observed = acquired.Observation;
                    }
                    else
                    {
                        observed = _adapter.InspectPinnedRoot(pin, action, ct); RequireObservation(observed, pin, _bound[i]!, Targets[i]);
                    }
                    ct.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (!_disposed && revision == _revision && !ct.IsCancellationRequested && acquired is not null)
                        { _pins[i] = acquired; _bound[i] = acquired.Observation.Identity; acquired = null; }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                { error = Describe(ex); observed = null; }
                finally { if (acquired is not null) ReviewedStorageRoots.DisposeLease(acquired); }
                StorageSelectionState? update = null;
                lock (_gate)
                {
                    // Lost binding is sticky even if the action changed while its native query was running.
                    if (_pins[i] is not null && (error?.Kind == StorageReasonKind.IdentityChanged || observed?.Ineligible?.Kind == StorageReasonKind.IdentityChanged))
                    { _stale[i] = true; _errors[i] = error ?? observed!.Ineligible; }
                    if (!_disposed && revision == _revision)
                    {
                        _facts[i] = observed ?? _facts[i]; _errors[i] = error ?? (_stale[i] ? _errors[i] : null);
                        _checked = i + 1; CheckMetadata(); update = Snapshot();
                    }
                }
                if (update is not null && progress is not null)
                    try { progress(update); } catch (Exception ex) { AppLog.Warn("Storage selection observer failed", ex); }
                if (_problem is not null) return;
            }
            ct.ThrowIfCancellationRequested();
            lock (_gate) if (!_disposed && revision == _revision) _needsInspection = false;
        }
        finally
        {
            StorageReviewOwner? cleanup = null;
            lock (_gate)
            {
                _busy = false;
                if (_disposed) cleanup = TakeOwner();
            }
            cleanup?.Dispose();
        }
    }

    /// <summary>One-shot handoff after the displayed revision is approved. Immutable facts remain readable.</summary>
    public ReviewedStorageRoots Freeze(long revision)
    {
        lock (_gate)
        {
            Check(); var state = Snapshot();
            if (revision != _revision || !state.CanApprove) throw new InvalidOperationException("This review changed or is not eligible. Inspect its current revision before approval.");
            var roots = _facts.Select((fact, i) => new StorageReviewedRoot(i, i, fact!)).ToImmutableArray();
            ReviewedStorageRoots.RequireMetadataBound(new { Targets, Roots = roots });
            var owner = TakeOwner()!;
            var reviewed = new ReviewedStorageRoots(_adapter, _action, Targets, roots, owner);
            _transferred = true; return reviewed;
        }
    }
    private StorageReviewOwner? TakeOwner()
    {
        if (_pinAllowance is null) return null;
        var pins = _pins.Where(p => p is not null).Cast<IStorageReviewLease>().ToImmutableArray();
        var allowance = _pinAllowance; _pinAllowance = null; Array.Clear(_pins);
        return new(pins, allowance);
    }
    private StorageSelectionState Snapshot()
    {
        var roots = _facts.Select((fact, i) => new StorageSelectionRoot(i, fact, _errors[i], _pins[i]?.IsAlive == true, _stale[i])).ToImmutableArray();
        bool approve = !_disposed && !_transferred && !_busy && !_needsInspection && _problem is null && _action is not null &&
            roots.All(r => r.Pinned && !r.Stale && r.Problem is null &&
                r.Observation is { Ineligible: null, Compression: StorageObservationState.Enabled or StorageObservationState.Disabled } &&
                (_action switch
                {
                    StorageAction.Compress => r.Observation.Efs == StorageObservationState.Disabled,
                    StorageAction.EncryptEfs or StorageAction.DecryptEfs => r.Observation.Efs is StorageObservationState.Enabled or StorageObservationState.Disabled,
                    _ => true,
                }));
        return new(_revision, _action, _scope, _busy, _needsInspection, approve, _checked, roots, _problem);
    }
    private void CheckMetadata()
    {
        try { ReviewedStorageRoots.RequireMetadataBound(new { Targets, Roots = _facts, Errors = _errors }); }
        catch (ArgumentException)
        { _problem = new(StorageReasonKind.FileCatBoundary, "review-metadata-limit", "The complete selection review exceeds its 2 MiB metadata bound. Close this review and select fewer items.", false); _needsInspection = false; }
    }
    private static void RequireObservation(StorageObservation observation, IStorageReviewLease pin, StorageObjectIdentity expected, StorageTarget target)
    {
        if (!pin.IsAlive || observation.Identity != expected || observation.IsDirectory != (target.Source.Kind == EntryKind.Directory))
            throw new StorageInspectionException(new(StorageReasonKind.IdentityChanged, "review-stale", "The original lifetime binding changed. Close this review and select the item again.", true));
        if (
            !Enum.IsDefined(observation.Compression) || !Enum.IsDefined(observation.Efs) ||
            observation.Ineligible is { } reason && (!Enum.IsDefined(reason.Kind) || reason.Code is null || reason.Code.Length > 128 || reason.Message is null || reason.Message.Length > 2000))
            throw new InvalidOperationException("Pinned-root observation lost its original binding or valid shape.");
    }
    private static StorageProblem Describe(Exception ex)
    {
        if (ex is StorageInspectionException inspection)
        {
            var reason = inspection.Problem;
            return new(Enum.IsDefined(reason.Kind) ? reason.Kind : StorageReasonKind.Unknown,
                reason.Code is { Length: <= 128 } ? reason.Code : reason.Code is { } code ? code[..128] : "inspection-invalid-reason",
                reason.Message is { Length: <= 2000 } ? reason.Message : reason.Message is { } detail ? detail[..2000] : "Inspection reason unavailable.", reason.IsError);
        }
        string message = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        return new(ex is UnauthorizedAccessException ? StorageReasonKind.PermissionOrPolicy : ex is NotSupportedException
            ? StorageReasonKind.FileCatBoundary : StorageReasonKind.TransientFailure, "inspection-" + ex.GetType().Name, message, ex is not NotSupportedException);
    }
    private void Check() { ObjectDisposedException.ThrowIf(_disposed, this); if (_transferred) throw new InvalidOperationException("This selection's pins were already transferred."); }
    public void Dispose()
    {
        StorageReviewOwner? cleanup;
        lock (_gate) { _disposed = true; cleanup = _busy ? null : TakeOwner(); }
        cleanup?.Dispose();
    }
}
