using System.Collections.Immutable;
using System.Text.Json;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

public sealed record StorageReviewApproval(StorageAction Action, StorageScope Scope, Guid ReviewRevision);
public sealed record StorageTransformRequest(StorageAction Action, StorageScope Scope, Guid ReviewRevision)
{
    public ImmutableArray<StorageReviewedRoot> Roots { get; init; } = [];
}
public sealed record StorageReviewedRoot(int SourceIndex, int PinIndex, StorageObservation Observation);

/// <summary>Immutable review facts survive disposal; only the opaque native lifetime owner is consumed.</summary>
public sealed class ReviewedStorageRoots : IDisposable
{
    public const int MetadataByteLimit = 2 * 1024 * 1024;
    private readonly object _lock = new();
    private readonly IStorageTransforms _adapter;
    private StorageReviewOwner? _owner;
    private readonly StorageAction? _inspectedAction;

    internal ReviewedStorageRoots(IStorageTransforms adapter, StorageAction? action, ImmutableArray<StorageTarget> targets,
        ImmutableArray<StorageReviewedRoot> roots, StorageReviewOwner owner)
    {
        _adapter = adapter; _inspectedAction = action; Targets = targets; Roots = roots; _owner = owner;
        Sources = targets.Select(t => t.Source).ToImmutableArray();
    }

    public Guid Revision { get; } = Guid.NewGuid();
    public ImmutableArray<StorageTarget> Targets { get; }
    public ImmutableArray<ItemRef> Sources { get; }
    public ImmutableArray<StorageReviewedRoot> Roots { get; }
    public bool OwnsPins { get { lock (_lock) return _owner is not null; } }

    /// <summary>Scheduled callers must retain this synchronous delegate's ownership until its actual return.</summary>
    internal static ReviewedStorageRoots Prepare(IStorageTransforms adapter, IReadOnlyList<ItemRef> sources,
        StorageAction? action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (action is { } requested) RequireAction(adapter, requested);
        var targets = StoragePaths.Capture(sources); // complete lexical admission precedes any native call
        RequireMetadataBound(targets);
        using var native = StorageReviewResources.EnterNative(ct);
        var allowance = StorageReviewResources.ReservePins(targets.Length);
        var leases = new List<IStorageReviewLease>();
        try
        {
            var roots = ImmutableArray.CreateBuilder<StorageReviewedRoot>(targets.Length);
            for (int i = 0; i < targets.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var inspection = StorageReviewResources.EnterRootInspection(targets[i]);
                var lease = adapter.InspectRoot(targets[i], action, ct) ?? throw new InvalidOperationException("The storage adapter returned no lifetime pin.");
                bool adopted = false;
                try
                {
                    RequireLease(lease, targets[i]);
                    int pin = leases.FindIndex(l => l.Observation.Identity == lease.Observation.Identity);
                    if (pin < 0) { pin = leases.Count; leases.Add(lease); adopted = true; }
                    roots.Add(new(i, pin, lease.Observation));
                }
                finally { if (!adopted) DisposeLease(lease); }
            }
            var facts = roots.MoveToImmutable();
            RequireMetadataBound(new { Targets = targets, Roots = facts });
            if (leases.Any(l => !l.IsAlive)) throw new InvalidOperationException("A storage lifetime pin retired during inspection.");
            ct.ThrowIfCancellationRequested();
            return new(adapter, action, targets, facts, new(leases.ToImmutableArray(), allowance));
        }
        catch
        {
            foreach (var lease in leases) DisposeLease(lease);
            allowance.Dispose();
            throw;
        }
    }

    public StorageReviewApproval Approve(StorageAction action, StorageScope scope)
    {
        var approval = new StorageReviewApproval(action, scope, Revision);
        lock (_lock) ValidateLocked(_adapter, approval);
        return approval;
    }

    internal void Validate(IStorageTransforms adapter, StorageReviewApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        lock (_lock) ValidateLocked(adapter, approval);
    }

    internal StorageReviewOwner Transfer(IStorageTransforms adapter, StorageReviewApproval approval)
    {
        lock (_lock)
        {
            ValidateLocked(adapter, approval);
            var owner = _owner!;
            _owner = null;
            return owner;
        }
    }

    private void ValidateLocked(IStorageTransforms adapter, StorageReviewApproval approval)
    {
        RequireAction(adapter, approval.Action);
        if (!Enum.IsDefined(approval.Scope)) throw new ArgumentOutOfRangeException(nameof(approval), "Choose an explicit storage scope.");
        if (!ReferenceEquals(_adapter, adapter) || approval.ReviewRevision != Revision || _inspectedAction != approval.Action)
            throw new InvalidOperationException("The action or review changed; inspect this exact selection again.");
        if (_owner is null || !_owner.IsAlive) throw new InvalidOperationException("The reviewed lifetime pins were released or already consumed.");
        if (Roots.Any(r => r.Observation.Ineligible is not null)) throw new InvalidOperationException("Every selected root must be eligible before submission.");
        var captured = StoragePaths.Capture(Sources);
        if (!captured.SequenceEqual(Targets)) throw new InvalidOperationException("The captured storage sources no longer match the review.");
    }

    internal static void RequireAction(IStorageTransforms adapter, StorageAction action)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action), "Choose an explicit storage action.");
        if (action is StorageAction.EncryptEfs or StorageAction.DecryptEfs)
            throw new NotSupportedException("EFS execution requires its qualified identity, key and output protocol.");
        if (adapter.Unavailable(action) is { } reason) throw new NotSupportedException(reason.Message);
    }

    internal static void RequireLease(IStorageReviewLease lease, StorageTarget target)
    {
        var observation = lease.Observation;
        if (!lease.IsAlive || lease.Target != target || observation is null || observation.Identity is null ||
            string.IsNullOrEmpty(observation.Identity.ObjectId) || observation.Identity.ObjectId.Length > 256 ||
            observation.Identity.VolumeBinding is { Length: > 128 } ||
            observation.IsDirectory != (target.Source.Kind == EntryKind.Directory) ||
            !Enum.IsDefined(observation.Compression) || !Enum.IsDefined(observation.Efs) ||
            observation.Ineligible is { } problem && (!Enum.IsDefined(problem.Kind) || problem.Code is null || problem.Code.Length > 128 ||
                problem.Message is null || problem.Message.Length > 2000))
            throw new InvalidOperationException("The storage adapter did not return a bound, valid observation.");
    }

    internal static void RequireMetadataBound<T>(T value)
    {
        using var count = new MetadataCounter();
        JsonSerializer.Serialize(count, value);
    }

    private sealed class MetadataCounter : Stream
    {
        private long _bytes;
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > MetadataByteLimit - _bytes) throw new ArgumentException("The captured storage review exceeds the 2 MiB metadata limit.");
            _bytes += buffer.Length;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _bytes;
        public override long Position { get => _bytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal static void DisposeLease(IStorageReviewLease lease)
    {
        try { lease.Dispose(); }
        catch (Exception error) { AppLog.Warn("Storage lifetime pin cleanup failed", error); }
    }

    public void Dispose()
    {
        StorageReviewOwner? owner;
        lock (_lock) { owner = _owner; _owner = null; }
        owner?.Dispose();
    }
}

internal sealed class StorageReviewOwner(ImmutableArray<IStorageReviewLease> leases, IDisposable allowance) : IDisposable
{
    private int _disposed;
    public bool IsAlive => Volatile.Read(ref _disposed) == 0 && leases.All(l => l.IsAlive);
    internal ImmutableArray<IStorageReviewLease> Leases => IsAlive ? leases : throw new ObjectDisposedException(nameof(StorageReviewOwner));
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { foreach (var lease in leases) ReviewedStorageRoots.DisposeLease(lease); }
        finally { allowance.Dispose(); }
    }
}

/// <summary>Fixed process allowances. A started synchronous owner releases only after native work retires.</summary>
internal static class StorageReviewResources
{
    private static readonly object PinLock = new();
    private static readonly SemaphoreSlim Native = new(1, 1);
    private static Job? _nativeJob;
    internal static Job? NativeBlocker => Volatile.Read(ref _nativeJob);
    private static int _pins;
    [ThreadStatic] private static StorageTarget? _inspecting;
    internal const int PinLimit = 256, NativeSlots = 188;
    internal static IDisposable ReservePins(int count)
    {
        lock (PinLock)
        {
            if (count is < 1 or > 64 || count > PinLimit - _pins) throw new InvalidOperationException("The storage review pin allowance is in use; close another review or wait for its job.");
            _pins += count;
        }
        return new Once(() => { lock (PinLock) _pins -= count; });
    }
    internal static IDisposable EnterNative(CancellationToken ct, Job? owner = null)
    {
        if (StorageNativeAllowance.Current is not null) throw new InvalidOperationException("Borrow the existing storage native allowance; do not reacquire it.");
        Native.Wait(ct);
        Volatile.Write(ref _nativeJob, owner);
        using var reserved = new StorageNativeReservation(ReleaseNative);
        return reserved.Enter();
    }
    /// <summary>Reserve before entering the device queue, so no device worker waits for global native capacity.</summary>
    internal static async Task<StorageNativeReservation> ReserveNativeAsync(CancellationToken ct)
    {
        if (StorageNativeAllowance.Current is not null) throw new InvalidOperationException("Release the current native allowance before another reservation.");
        await Native.WaitAsync(ct).ConfigureAwait(false);
        return new StorageNativeReservation(ReleaseNative);
    }
    private static void ReleaseNative()
    {
        Volatile.Write(ref _nativeJob, null);
        Native.Release();
    }
    internal static IDisposable EnterRootInspection(StorageTarget target)
    {
        _ = StorageNativeAllowance.RequireCurrent();
        if (_inspecting is not null) throw new InvalidOperationException("A root inspection is already active.");
        _inspecting = target;
        return new Once(() => _inspecting = null);
    }
    internal static void RequireRootInspection(StorageTarget target)
    {
        _ = StorageNativeAllowance.RequireCurrent();
        if (!ReferenceEquals(_inspecting, target)) throw new InvalidOperationException("Root inspection requires its reserved review pin allowance.");
    }
    private sealed class Once(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

/// <summary>A queued reservation transfers its release to the actual synchronous native owner exactly once.</summary>
internal sealed class StorageNativeReservation(Action release) : IDisposable
{
    private readonly object _gate = new();
    private Action? _release = release;
    internal IDisposable Enter()
    {
        lock (_gate)
        {
            if (StorageNativeAllowance.Current is not null) throw new InvalidOperationException("A native allowance is already bound to this thread.");
            var owned = _release ?? throw new InvalidOperationException("This native reservation was disposed or consumed.");
            var allowance = new StorageNativeAllowance(owned); _release = null; return allowance;
        }
    }
    public void Dispose()
    {
        Action? release;
        lock (_gate) { release = _release; _release = null; }
        release?.Invoke();
    }
}
