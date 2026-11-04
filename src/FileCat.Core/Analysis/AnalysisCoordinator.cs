using System.Collections.Concurrent;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisProgress(int ObservedPaths, ulong Issues, AnalysisPhase Phase);

public sealed class AnalysisCoordinator(DeviceIoScheduler io, string? profileIdentity = null)
{
    private readonly string _profileIdentity = profileIdentity ?? "ephemeral:" + Guid.NewGuid().ToString("N");
    // Ownership of prepared sources transfers to the session, including blocked native requests.
    public AnalysisSession Start(IReadOnlyList<IAnalysisBackend> sources, IProgress<AnalysisProgress>? progress = null)
    {
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (sources.Count is < 1 or > AnalysisLimits.Roots) throw new ArgumentException("Select between 1 and 32 roots.", nameof(sources));
        return new(io, sources.ToArray(), progress, _profileIdentity);
    }
}

public sealed class AnalysisSession : IDisposable
{
    private static readonly SemaphoreSlim GlobalScans = new(AnalysisLimits.ActiveScans);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly AnalysisStore _store = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly DeviceIoScheduler _io;
    private readonly IAnalysisBackend[] _sources;
    private readonly ImmutableArray<RootBinding> _roots;
    private readonly ScanPolicy _policy;
    private readonly SessionIdentity _identity;
    private readonly IProgress<AnalysisProgress>? _progress;
    private AnalysisPhase _phase = AnalysisPhase.Scanning;
    private bool _partial, _disposed, _workerFinished;
    private bool _released;
    private int _snapshotLeases;
    private long _revision;
    private TaskCompletionSource? _resume;
    private AnalysisSnapshot? _final;
    private string? _activeDevice;
    private AnalysisFreshness _freshness;
    public Task Completion { get; }
    internal AnalysisSession(DeviceIoScheduler io, IAnalysisBackend[] sources, IProgress<AnalysisProgress>? progress, string profileIdentity)
    {
        _io = io; _sources = sources; _progress = progress;
        _roots = sources.Select((s, i) => s.Root with { Id = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToImmutableArray();
        var now = DateTimeOffset.UtcNow;
        var firstObservation = sources.Select(s => s.RootEntry.ObservedUtc ?? now).Append(now).Min();
        _identity = new(Guid.NewGuid(), 1, 1, profileIdentity, firstObservation, now);
        _policy = new() { BasisIds = sources.Select(s => s.Basis).Distinct().ToImmutableArray() };
        _io.HealthChanged += OnDeviceHealth;
        Completion = Task.Run(ProduceAsync);
    }
    public AnalysisSnapshot Snapshot()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _final ?? Capture();
        }
    }
    private AnalysisSnapshot Capture() => _store.Snapshot(_identity with { Revision = checked(++_revision), ObservedUtc = DateTimeOffset.UtcNow }, _roots, _policy,
        _phase, _partial || _phase != AnalysisPhase.Finished ? NamespaceCoverage.Partial : NamespaceCoverage.CompleteUnderPolicy) with { Freshness = _freshness };
    public AnalysisSnapshotLease AcquireSnapshot()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_snapshotLeases >= 8) throw new InvalidOperationException("This analysis already has eight active snapshot consumers.");
            var snapshot = _final ?? Capture(); _snapshotLeases++;
            return new(snapshot, () =>
            {
                lock (_sync)
                {
                    _snapshotLeases--;
                    if (_disposed && _workerFinished && _snapshotLeases == 0) Release();
                }
            });
        }
    }
    public void Pause()
    {
        lock (_sync)
        {
            if (_disposed || _workerFinished || _phase != AnalysisPhase.Scanning) return;
            _resume = new(TaskCreationOptions.RunContinuationsAsynchronously); _phase = AnalysisPhase.Pausing;
        }
        Report();
    }
    public void Resume()
    {
        TaskCompletionSource? resume;
        lock (_sync)
        {
            if (_disposed || _workerFinished || _phase is not (AnalysisPhase.Paused or AnalysisPhase.Pausing)) return;
            resume = _resume; _resume = null; _phase = AnalysisPhase.Scanning;
        }
        resume?.TrySetResult(); Report();
    }
    private async Task PauseBoundaryAsync()
    {
        Task? wait;
        lock (_sync)
        {
            wait = _resume?.Task;
            if (wait is not null && !_disposed && _phase == AnalysisPhase.Pausing) _phase = AnalysisPhase.Paused;
        }
        if (wait is not null) { Report(); await wait.WaitAsync(_cancel.Token).ConfigureAwait(false); }
    }
    public void Stop()
        => Stop(null);
    private void Stop(string? issue)
    {
        lock (_sync)
        {
            if (_disposed || _workerFinished || _phase == AnalysisPhase.Stopped) return;
            if (issue is not null) { _store.Issue(issue); _freshness = AnalysisFreshness.SourceUnavailable; }
            _cancel.Cancel(); _store.Seal(); _partial = true; _phase = AnalysisPhase.Stopped; _final = Capture();
        }
        Report();
    }
    private void OnDeviceHealth(string key, DeviceHealth health)
    {
        if (health != DeviceHealth.NotResponding) return;
        lock (_sync)
        {
            if (_disposed || _workerFinished || !string.Equals(_activeDevice, key, StringComparison.OrdinalIgnoreCase)) return;
        }
        Stop("device-not-responding");
    }
    private async Task ProduceAsync()
    {
        bool admitted = false;
        try
        {
            await GlobalScans.WaitAsync(_cancel.Token).ConfigureAwait(false); admitted = true;
            for (int i = 0; i < _sources.Length; i++)
            {
                _cancel.Token.ThrowIfCancellationRequested();
                var source = _sources[i]; var root = _roots[i];
                if (_roots.Take(i).Any(r => Contains(r.CanonicalLocator, root.CanonicalLocator))) continue;
                string device = source.IoDeviceKey;
                lock (_sync) _activeDevice = device;
                if (_io.GetHealth(device) == DeviceHealth.NotResponding) { Issue("device-not-responding"); continue; }
                var gate = Devices.GetOrAdd(device, _ => new(1));
                await gate.WaitAsync(_cancel.Token).ConfigureAwait(false);
                try { await WalkAsync(source, root, device).ConfigureAwait(false); }
                finally { gate.Release(); }
                if (_store.Limited) break;
            }
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Issue("analysis-source-unavailable"); }
        finally
        {
            _io.HealthChanged -= OnDeviceHealth;
            foreach (var source in _sources) source.Dispose();
            if (admitted) GlobalScans.Release();
            lock (_sync)
            {
                _store.Seal();
                if (_phase != AnalysisPhase.Stopped) _phase = AnalysisPhase.Finished;
                _workerFinished = true;
                if (!_disposed) _final ??= Capture(); else if (_snapshotLeases == 0) Release();
            }
            Report();
        }
    }
    private async Task WalkAsync(IAnalysisBackend source, RootBinding root, string device)
    {
        var rootId = _store.Append(source.RootEntry, 0, root.Id, source.Basis);
        if (!rootId.HasValue) { Issue("memory-or-observation-limit"); return; }
        if (source.RootEntry.Kind != AnalysisKind.Directory) return;
        if (source.RootEntry.Cloud == true) { Issue("cloud-directory-not-enumerated", rootId, MeasurementState.Excluded); return; }
        var pending = new Stack<(AnalysisEntry Entry, ulong Id, int Depth, long Bytes)>();
        pending.Push((source.RootEntry, rootId.Value, 0, 0));
        long pendingBytes = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (pending.Count > 0 && !_store.Limited)
        {
            _cancel.Token.ThrowIfCancellationRequested();
            var (entry, id, depth, cost) = pending.Pop(); pendingBytes -= cost;
            if (depth >= AnalysisLimits.Depth) { Issue("depth-limit", id); continue; }
            using var enumeration = source.Children(entry, _cancel.Token).GetEnumerator();
            bool finished = false;
            AnalysisEntry? carry = null;
            while (!finished)
            {
                _cancel.Token.ThrowIfCancellationRequested();
                await PauseBoundaryAsync().ConfigureAwait(false);
                Batch batch;
                try
                {
                    // Never cancel the scheduler task early: it owns the enumerator and its native handles until
                    // the call returns. Stop freezes observations immediately while retaining device admission.
                    batch = await _io.Run(device, IoPriority.Background, _ => ReadBatch(source, entry, enumeration, carry), CancellationToken.None).ConfigureAwait(false);
                }
                catch (AnalysisSourceChangedException) { Issue("source-changed-or-boundary", id); break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Issue("enumeration-unavailable", id); break; }
                _cancel.Token.ThrowIfCancellationRequested();
                finished = batch.Finished;
                carry = batch.Carry;
                foreach (var child in batch.Entries)
                {
                    var childId = _store.Append(child, id, root.Id, source.Basis);
                    if (childId is null) { Issue("memory-or-observation-limit", id); return; }
                    if (child.Kind != AnalysisKind.Directory) continue;
                    if (child.Cloud == true) { Issue("cloud-directory-not-enumerated", childId, MeasurementState.Excluded); continue; }
                    long bytes = 2048L + child.RelativeName.Length * 2L + child.RawName.Length * 4L;
                    if (pendingBytes + bytes > AnalysisLimits.PendingBytes / 2) { Issue("pending-payload-limit", childId); continue; }
                    pending.Push((child, childId.Value, depth + 1, bytes)); pendingBytes += bytes;
                }
                if (batch.Limited) { Issue("producer-record-limit", id); break; }
                if (batch.Error is { } error) { Issue(error, id); break; }
                if (clock.ElapsedMilliseconds >= 250) { Report(); clock.Restart(); }
            }
        }
    }
    private sealed record Batch(List<AnalysisEntry> Entries, bool Finished, bool Limited, AnalysisEntry? Carry = null, string? Error = null);
    private static Batch ReadBatch(IAnalysisBackend source, AnalysisEntry directory, IEnumerator<AnalysisEntry> iterator, AnalysisEntry? carry)
    {
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        source.ValidateReadScope(directory);
        var entries = new List<AnalysisEntry>(); long bytes = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (entries.Count < AnalysisLimits.BatchRecords)
        {
            if (carry is null)
            {
                try { if (!iterator.MoveNext()) return Finish(new(entries, true, false)); }
                catch (AnalysisSourceChangedException) { return Finish(new(entries, true, false, Error: "source-changed-or-boundary")); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Finish(new(entries, true, false, Error: "enumeration-unavailable")); }
            }
            var entry = carry ?? iterator.Current; carry = null;
            long cost = checked(1024L + entry.RawName.Length * 4L + entry.RelativeName.Length * 2L);
            if (cost > AnalysisLimits.BatchBytes) return Finish(new(entries, true, true));
            if (bytes + cost > AnalysisLimits.BatchBytes) return Finish(new(entries, false, false, entry));
            bytes += cost;
            entries.Add(entry);
            if (timer.ElapsedMilliseconds >= 20) break;
        }
        return Finish(new(entries, false, false));
        Batch Finish(Batch batch) { source.ValidateReadScope(directory); return batch; }
    }
    private void Issue(string code, ulong? node = null, MeasurementState state = MeasurementState.Failed)
    {
        lock (_sync)
        {
            if (state != MeasurementState.Excluded) _partial = true;
            if (code == "source-changed-or-boundary" && _freshness == AnalysisFreshness.Observed) _freshness = AnalysisFreshness.PossiblyStale;
            _store.Issue(code, node, state);
        }
    }
    private void Report()
    {
        AnalysisProgress p;
        lock (_sync) { if (_disposed) return; p = new(_store.Count, _store.IssueCount, _phase); }
        _progress?.Report(p);
    }
    private static bool Contains(string parent, string child)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return parent.Equals(child, comparison) || child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }
    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _final = null;
            if (_workerFinished && _snapshotLeases == 0) Release();
        }
    }
    private void Release()
    {
        if (_released) return;
        _released = true; _store.Dispose(); _cancel.Dispose();
    }
}

public sealed class AnalysisSnapshotLease : IDisposable
{
    private AnalysisSnapshot? _snapshot;
    private Action? _release;
    internal AnalysisSnapshotLease(AnalysisSnapshot snapshot, Action release) { _snapshot = snapshot; _release = release; }
    public AnalysisSnapshot Snapshot => _snapshot ?? throw new ObjectDisposedException(nameof(AnalysisSnapshotLease));
    public void Dispose() { _snapshot = null; Interlocked.Exchange(ref _release, null)?.Invoke(); }
}
