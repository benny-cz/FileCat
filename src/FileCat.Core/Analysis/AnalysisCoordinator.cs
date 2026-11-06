using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisProgress(int ObservedPaths, ulong Issues, AnalysisPhase Phase)
{
    // Runtime resource lifecycle, separate from an already frozen metadata revision.
    public ulong SourceCloseFailures { get; init; }
}

public sealed class AnalysisCoordinator(DeviceIoScheduler io, string? profileIdentity = null)
{
    private readonly string _profileIdentity = profileIdentity ?? "ephemeral:" + Guid.NewGuid().ToString("N");
    // Ownership of prepared sources transfers to the session, including blocked native requests.
    public AnalysisSession Start(IReadOnlyList<IAnalysisBackend> sources, IProgress<AnalysisProgress>? progress = null,
        AnalysisComponentPreset components = AnalysisComponentPreset.Standard)
    {
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (sources.Count is < 1 or > AnalysisLimits.Roots) throw new ArgumentException("Select between 1 and 32 roots.", nameof(sources));
        if (!Enum.IsDefined(components)) throw new ArgumentOutOfRangeException(nameof(components));
        return new(io, sources.ToArray(), progress, _profileIdentity, components);
    }
}

public sealed class AnalysisSession : IDisposable
{
    private static readonly SemaphoreSlim GlobalScans = new(AnalysisLimits.ActiveScans);
    private readonly object _sync = new();
    private readonly AnalysisStore _store = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly DeviceIoScheduler _io;
    private readonly IAnalysisBackend[] _sources;
    private ImmutableArray<RootBinding> _roots;
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
    private ulong _enumerationRequested, _enumerationCompleted, _enumerationFailed;
    private ulong _sourceCloseFailures;
    public ulong SourceCloseFailures { get { lock (_sync) return _sourceCloseFailures; } }
    public Task Completion { get; }
    internal AnalysisSession(DeviceIoScheduler io, IAnalysisBackend[] sources, IProgress<AnalysisProgress>? progress, string profileIdentity,
        AnalysisComponentPreset components)
    {
        _io = io; _sources = sources; _progress = progress;
        _roots = sources.Select((s, i) => s.Root with { Id = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Kind = s.RootEntry.Kind,
            ObservedObjectIdentity = s.RootEntry.Identity }).ToImmutableArray();
        var now = DateTimeOffset.UtcNow;
        var firstObservation = sources.Select(s => s.RootEntry.ObservedUtc ?? now).Append(now).Min();
        _identity = new(Guid.NewGuid(), 1, 1, profileIdentity, firstObservation, now);
        _policy = new() { BasisIds = sources.Select(s => s.Basis).Distinct().ToImmutableArray(), PackagePolicy = "contents-included",
            ComponentPreset = components == AnalysisComponentPreset.DetailedAllocation ? "detailed" : "standard", Limits = AnalysisLimits.EffectivePolicy };
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
    private AnalysisSnapshot Capture()
    {
        var snapshot = _store.Snapshot(_identity with { Revision = checked(++_revision), ObservedUtc = DateTimeOffset.UtcNow }, _roots, _policy,
            _phase, _partial || _phase != AnalysisPhase.Finished ? NamespaceCoverage.Partial : NamespaceCoverage.CompleteUnderPolicy) with { Freshness = _freshness };
        if (snapshot.Freshness == AnalysisFreshness.Observed && snapshot.Facts.Any(f => f.Measurement.State == MeasurementState.Changed))
            snapshot = snapshot with { Freshness = AnalysisFreshness.PossiblyStale };
        return snapshot with { CoverageDetails = AnalysisCoverage.Create(snapshot, _enumerationRequested, _enumerationCompleted, _enumerationFailed) };
    }
    public AnalysisSnapshotLease AcquireSnapshot()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return RetainSnapshot(_final ?? Capture());
        }
    }
    private AnalysisSnapshotLease RetainSnapshot(AnalysisSnapshot snapshot)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_released, this);
            if (_snapshotLeases >= AnalysisLimits.SnapshotConsumers) throw new InvalidOperationException("This analysis already has eight active snapshot consumers.");
            _snapshotLeases++;
            return new(snapshot, () =>
            {
                lock (_sync)
                {
                    _snapshotLeases--;
                    if (_disposed && _workerFinished && _snapshotLeases == 0) Release();
                }
            }, () => RetainSnapshot(snapshot));
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
            if (_io.IsStopped) throw new OperationCanceledException("Analysis I/O has stopped.");
            await NormalizeRootsAsync().ConfigureAwait(false);
            for (int i = 0; i < _sources.Length; i++)
            {
                _cancel.Token.ThrowIfCancellationRequested();
                var source = _sources[i]; var root = _roots[i];
                if (root.Disposition != AnalysisRootDisposition.Active) continue;
                string device = source.IoDeviceKey;
                lock (_sync) _activeDevice = device;
                if (_io.GetHealth(device) == DeviceHealth.NotResponding) { Issue("device-not-responding"); continue; }
                using var turn = await AnalysisScanDevices.AcquireAsync(device, _cancel.Token).ConfigureAwait(false);
                await WalkAsync(source, root, device).ConfigureAwait(false);
                if (_store.Limited) break;
            }
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (OperationCanceledException) when (_io.IsStopped) { Stop("analysis-io-stopped"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Issue("analysis-source-unavailable"); }
        catch (Exception)
        {
            Issue("analysis-producer-failed");
            lock (_sync) if (_phase != AnalysisPhase.Stopped) _phase = AnalysisPhase.Failed;
        }
        finally
        {
            try
            {
                for (int i = 0; i < _sources.Length; i++)
                {
                    try
                    {
                        string device = _sources[i].IoDeviceKey;
                        lock (_sync) _activeDevice = device;
                        if (_io.IsStopped) Stop("analysis-io-stopped");
                        else if (_io.GetHealth(device) == DeviceHealth.NotResponding) Stop("device-not-responding");
                        await AnalysisIo.ReleaseAsync(_io, _sources[i]).ConfigureAwait(false);
                    }
                    // A close is attempted once. Retrying an uncertain native close could operate on a reused
                    // descriptor. Preserve the failure without abandoning the other independently owned roots.
                    catch (Exception)
                    {
                        lock (_sync) _sourceCloseFailures++;
                        Issue("analysis-source-close-failed", rootId: _roots[i].Id);
                        Report();
                    }
                }
            }
            finally
            {
                lock (_sync) _activeDevice = null;
                _io.HealthChanged -= OnDeviceHealth;
                if (admitted) GlobalScans.Release();
                lock (_sync)
                {
                    _store.Seal();
                    if (_phase is not (AnalysisPhase.Stopped or AnalysisPhase.Failed)) _phase = AnalysisPhase.Finished;
                    _workerFinished = true;
                    if (!_disposed) _final ??= Capture(); else if (_snapshotLeases == 0) Release();
                }
                Report();
            }
        }
    }
    private async Task NormalizeRootsAsync()
    {
        var roots = _roots.ToArray(); var active = new List<int>();
        // Parents are considered before children regardless of selection order. IDs keep their captured binding.
        foreach (int child in Enumerable.Range(0, roots.Length).OrderBy(i => roots[i].CanonicalLocator.Length).ThenBy(i => i))
        {
            _cancel.Token.ThrowIfCancellationRequested(); bool uncertain = false; int? coveredBy = null;
            foreach (int parent in active.Where(p => AnalysisRootPaths.Potential(roots[p], roots[child])))
            {
                await PauseBoundaryAsync().ConfigureAwait(false);
                string device = _sources[parent].IoDeviceKey;
                lock (_sync) _activeDevice = device;
                if (_io.GetHealth(device) == DeviceHealth.NotResponding) { uncertain = true; continue; }
                using var turn = await AnalysisScanDevices.AcquireAsync(device, _cancel.Token).ConfigureAwait(false);
                try
                {
                    await PauseBoundaryAsync().ConfigureAwait(false);
                    var relation = await AnalysisIo.Run(_io, device, _ =>
                    {
                        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
                        return _sources[parent].ContainsRoot(_sources[child]);
                    }, _cancel.Token).ConfigureAwait(false);
                    _cancel.Token.ThrowIfCancellationRequested();
                    if (relation == AnalysisRootContainment.Covered) { coveredBy = parent; break; }
                    uncertain |= relation == AnalysisRootContainment.Unavailable;
                }
                catch (AnalysisSourceChangedException) { uncertain = true; Issue("source-changed-or-boundary", rootId: roots[child].Id); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { uncertain = true; }
            }
            if (coveredBy is { } p)
            {
                AnalysisRootPaths.TryRelative(roots[p], roots[child], roots[p].NameRules == AnalysisNameRules.Windows || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, out string relative);
                if (roots[p].NameRules == AnalysisNameRules.Windows) relative = relative.Replace('\\', '/');
                roots[child] = roots[child] with { Disposition = AnalysisRootDisposition.Covered, CoveredByRootId = roots[p].Id, CoveredRelativePath = relative };
            }
            else if (uncertain)
            {
                roots[child] = roots[child] with { Disposition = AnalysisRootDisposition.ExcludedUnverifiedOverlap };
                Issue("root-overlap-unverified", rootId: roots[child].Id);
            }
            else active.Add(child);
        }
        lock (_sync) _roots = roots.ToImmutableArray();
    }
    private async Task WalkAsync(IAnalysisBackend source, RootBinding root, string device)
    {
        var rootId = _store.Append(source.RootEntry, 0, root.Id, source.Basis, _policy.ComponentPreset == "detailed");
        if (!rootId.HasValue) { Issue("memory-or-observation-limit"); return; }
        if (source.RootEntry.Kind != AnalysisKind.Directory) return;
        if (source.RootEntry.Cloud == true) { Issue("cloud-directory-not-enumerated", rootId, MeasurementState.Excluded); return; }
        var pending = new Stack<(AnalysisEntry Entry, ulong Id, int Depth, long Bytes)>();
        pending.Push((source.RootEntry, rootId.Value, 0, 0));
        lock (_sync) _enumerationRequested++;
        long pendingBytes = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (pending.Count > 0 && !_store.Limited)
        {
            _cancel.Token.ThrowIfCancellationRequested();
            var (entry, id, depth, cost) = pending.Pop(); pendingBytes -= cost;
            if (depth >= AnalysisLimits.Depth) { Issue("depth-limit", id); lock (_sync) _enumerationFailed++; continue; }
            var enumeration = await AnalysisIo.Run(_io, device, _ => source.Children(entry, _cancel.Token).GetEnumerator(), _cancel.Token).ConfigureAwait(false);
            await using var cursorOwner = AnalysisIo.OwnCursor(_io, device, enumeration);
            bool finished = false;
            bool succeeded = false;
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
                    batch = await AnalysisIo.Run(_io, device, _ => ReadBatch(source, entry, enumeration, carry), _cancel.Token).ConfigureAwait(false);
                }
                catch (AnalysisSourceChangedException) { Issue("source-changed-or-boundary", id); break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Issue("enumeration-unavailable", id); break; }
                _cancel.Token.ThrowIfCancellationRequested();
                finished = batch.Finished;
                carry = batch.Carry;
                foreach (var child in batch.Entries)
                {
                    var childId = _store.Append(child, id, root.Id, source.Basis, _policy.ComponentPreset == "detailed");
                    if (childId is null) { Issue("memory-or-observation-limit", id); return; }
                    if (child.IssueCode is { } issueCode) Issue(issueCode, childId, child.MetadataState);
                    if (!child.IncludedByPolicy || child.MetadataState != MeasurementState.Known) continue;
                    if (child.Kind is AnalysisKind.Link or AnalysisKind.Special)
                        Issue(child.Kind == AnalysisKind.Link ? "navigation-link-not-followed" : "special-entry-not-traversed", childId, MeasurementState.Excluded);
                    if (child.Kind != AnalysisKind.Directory) continue;
                    if (child.Cloud == true) { Issue("cloud-directory-not-enumerated", childId, MeasurementState.Excluded); continue; }
                    long bytes = 2048L + child.RelativeName.Length * 2L + child.RawName.Length * 4L;
                    if (pendingBytes + bytes > AnalysisLimits.PendingBytes / 2) { Issue("pending-payload-limit", childId); continue; }
                    pending.Push((child, childId.Value, depth + 1, bytes)); pendingBytes += bytes;
                    lock (_sync) _enumerationRequested++;
                }
                if (batch.Limited) { Issue("producer-record-limit", id); break; }
                if (batch.Error is { } error) { Issue(error, id); break; }
                if (finished) succeeded = true;
                if (clock.ElapsedMilliseconds >= 250) { Report(); clock.Restart(); }
            }
            lock (_sync) { if (succeeded) _enumerationCompleted++; else _enumerationFailed++; }
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
    private void Issue(string code, ulong? node = null, MeasurementState state = MeasurementState.Failed, string? rootId = null)
    {
        lock (_sync)
        {
            if (state != MeasurementState.Excluded) _partial = true;
            if ((state == MeasurementState.Changed || code == "source-changed-or-boundary") && _freshness == AnalysisFreshness.Observed) _freshness = AnalysisFreshness.PossiblyStale;
            _store.Issue(code, node, state, rootId);
        }
    }
    private void Report()
    {
        AnalysisProgress p;
        lock (_sync) { if (_disposed) return; p = new(_store.Count, _store.IssueCount, _phase) { SourceCloseFailures = _sourceCloseFailures }; }
        // Observers do not own traversal or handle lifetimes. A released/broken UI subscription cannot
        // skip independent closes, strand a scan reservation or turn valid observations into a producer failure.
        try { _progress?.Report(p); } catch (Exception) { }
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
    private Func<AnalysisSnapshotLease>? _retain;
    internal AnalysisSnapshotLease(AnalysisSnapshot snapshot, Action release, Func<AnalysisSnapshotLease> retain) { _snapshot = snapshot; _release = release; _retain = retain; }
    public AnalysisSnapshot Snapshot => _snapshot ?? throw new ObjectDisposedException(nameof(AnalysisSnapshotLease));
    public AnalysisSnapshotLease Retain()
    {
        _ = Snapshot;
        return (Volatile.Read(ref _retain) ?? throw new ObjectDisposedException(nameof(AnalysisSnapshotLease)))();
    }
    public void Dispose() { _snapshot = null; Interlocked.Exchange(ref _retain, null); Interlocked.Exchange(ref _release, null)?.Invoke(); }
}
