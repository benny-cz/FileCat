using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisProgress(int ObservedPaths, ulong Issues, AnalysisPhase Phase)
{
    public Guid SessionId { get; init; }
    public long Epoch { get; init; }
    // Runtime resource lifecycle, separate from an already frozen metadata revision.
    public ulong SourceCloseFailures { get; init; }
    public int PreviewPaths { get; init; }
    public bool PreviewLimited { get; init; }
    public bool DatasetReady { get; init; }
    public bool MemoryFallback { get; init; }
    internal uint? NtfsDiscoveryRecords { get; init; }
    internal bool WaitingForNtfsAuthorization { get; init; }
    internal bool NtfsAuthorizationDeclined { get; init; }
}

public sealed partial class AnalysisCoordinator(DeviceIoScheduler io, string? profileIdentity = null)
{
    private readonly string _profileIdentity = profileIdentity ?? "ephemeral:" + Guid.NewGuid().ToString("N");
    // Ownership of prepared sources transfers to the session, including blocked native requests.
    public AnalysisSession Start(IReadOnlyList<IAnalysisBackend> sources, IProgress<AnalysisProgress>? progress = null,
        AnalysisComponentPreset components = AnalysisComponentPreset.Standard, AnalysisStorageOptions? storage = null,
        ImmutableArray<AnalysisFolderExclusion> exclusions = default)
    {
        RequireAdmission();
        ArgumentNullException.ThrowIfNull(sources); var prepared = sources.ToArray();
        ValidateStart(prepared, components, storage);
        return new(io, prepared, progress, _profileIdentity, components, storage, exclusions: exclusions);
    }
}

public sealed partial class AnalysisSession : IDisposable
{
    private static readonly SemaphoreSlim GlobalScans = new(AnalysisLimits.ActiveScans);
    private readonly object _sync = new();
    private readonly AnalysisStore _store;
    private readonly CancellationTokenSource _cancel = new();
    private readonly DeviceIoScheduler _io;
    private readonly IAnalysisBackend[] _sources;
    private ImmutableArray<RootBinding> _roots;
    private readonly ScanPolicy _policy;
    private readonly AnalysisFolderExclusions _folderExclusions;
    internal ImmutableArray<AnalysisFolderExclusion> FolderExclusions { get; }
    private readonly SessionIdentity _identity;
    private readonly IProgress<AnalysisProgress>? _progress;
    private AnalysisPhase _phase = AnalysisPhase.Scanning;
    private bool _partial, _disposed, _workerFinished;
    private bool _released;
    private int _snapshotLeases;
    private long _revision;
    private TaskCompletionSource? _resume;
    private AnalysisSnapshot? _final;
    private AnalysisIoDevices? _activeDevice;
    private AnalysisFreshness _freshness;
    private ulong _enumerationRequested, _enumerationCompleted, _enumerationFailed;
    private ulong _sourceCloseFailures;
    public ulong SourceCloseFailures { get { lock (_sync) return _sourceCloseFailures; } }
    public Task Completion { get; }
    internal AnalysisSession(DeviceIoScheduler io, IAnalysisBackend[] sources, IProgress<AnalysisProgress>? progress, string profileIdentity,
        AnalysisComponentPreset components, AnalysisStorageOptions? storage = null, SessionIdentity? nextEpoch = null,
        IAnalysisNtfsScan? ntfs = null, ImmutableArray<AnalysisFolderExclusion> exclusions = default)
    {
        FolderExclusions = exclusions.IsDefault ? [] : exclusions;
        _folderExclusions = new(sources, FolderExclusions);
        _storage = storage; _io = io; _sources = sources; _progress = progress; _ntfs = ntfs;
        _ntfsDiscoveryPending = ntfs is not null;
        AnalysisStore? created = null; bool subscribed = false;
        try
        {
            _store = created = new(storage is not null, storage?.PreviewPaths ?? AnalysisLimits.Observations);
            _roots = sources.Select((s, i) => s.Root with { Id = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Kind = s.RootEntry.Kind,
                ObservedObjectIdentity = s.RootEntry.Identity }).ToImmutableArray();
            _selectedRoots = _roots;
            var now = DateTimeOffset.UtcNow;
            var firstObservation = sources.Select(s => s.RootEntry.ObservedUtc ?? now).Append(now).Min();
            _identity = nextEpoch is null ? new(Guid.NewGuid(), 1, 1, profileIdentity, firstObservation, now) :
                nextEpoch with { StartedUtc = firstObservation, ObservedUtc = now };
            _revision = _identity.Revision - 1;
            if (nextEpoch is not null) _phase = AnalysisPhase.Refreshing;
            var bases = sources.Select(source => source.Basis);
            if (components == AnalysisComponentPreset.DetailedAllocation)
                bases = bases.Concat(sources.OfType<IAnalysisDetailedBackend>().Select(source => source.ExtraAllocationBasis).OfType<string>());
            _policy = new() { BasisIds = bases.Distinct(StringComparer.Ordinal).ToImmutableArray(), PackagePolicy = "contents-included",
                ComponentPreset = components == AnalysisComponentPreset.DetailedAllocation ? "detailed" : "standard", Limits = AnalysisLimits.EffectivePolicy,
                Exclusions = storage is null ? _folderExclusions.PolicyRules :
                    _folderExclusions.PolicyRules.Add("filecat-private-analysis-state") };
            _io.HealthChanged += OnDeviceHealth;
            subscribed = true;
            Completion = Task.Run(ProduceAsync);
        }
        catch
        {
            if (subscribed) _io.HealthChanged -= OnDeviceHealth;
            try { created?.Dispose(); }
            finally { _cancel.Dispose(); _finalizeCancel.Dispose(); }
            throw;
        }
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
            _phase, _partial || _previewLimited || _phase != AnalysisPhase.Finished || _ntfs is not null ? NamespaceCoverage.Partial : NamespaceCoverage.CompleteUnderPolicy)
            with { Freshness = _freshness, Taint = SourceTaint };
        if (snapshot.Freshness == AnalysisFreshness.Observed && snapshot.Facts.Any(f => f.Measurement.State == MeasurementState.Changed))
            snapshot = snapshot with { Freshness = AnalysisFreshness.PossiblyStale };
        ulong requested = _previewLimited ? Math.Min(_enumerationRequested, (ulong)snapshot.Nodes.Count(n => n.Kind == AnalysisKind.Directory)) : _enumerationRequested;
        ulong completed = Math.Min(_enumerationCompleted, requested), failed = Math.Min(_enumerationFailed, requested - completed);
        var coverage = AnalysisCoverage.Create(snapshot, requested, completed, failed);
        if (_ntfs is not null)
        {
            ulong eligible = (ulong)snapshot.Nodes.Count(n => n.Kind == AnalysisKind.Directory && n.IncludedByPolicy && n.MetadataState == MeasurementState.Known && n.Cloud != true);
            coverage = coverage with { Qualification = new(false, eligible, false, snapshot.Freshness != AnalysisFreshness.Observed),
                StaleScopeNodes = coverage.StaleScopeNodes.Order().ToImmutableArray(), StaleRootIds = coverage.StaleRootIds.Order(StringComparer.Ordinal).ToImmutableArray() };
        }
        return snapshot with { CoverageDetails = coverage };
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
            if (_disposed || _workerFinished || !CanPause || _phase is not (AnalysisPhase.Scanning or AnalysisPhase.Refreshing)) return;
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
            resume = _resume; _resume = null;
            if (!_ntfsDiscoveryPending) _phase = _identity.Epoch > 1 ? AnalysisPhase.Refreshing : AnalysisPhase.Scanning;
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
    private void Stop(string? issue, bool ntfsAuthorizationDeclined = false)
    {
        lock (_sync)
        {
            if (_disposed || _workerFinished || _phase == AnalysisPhase.Stopped) return;
            _ntfsAuthorizationDeclined = ntfsAuthorizationDeclined;
            if (issue is not null) { _store.Issue(issue); _freshness = AnalysisFreshness.SourceUnavailable; }
            _writer?.RequestStop(issue); if (_indexing) _finalizeCancel.Cancel();
            _cancel.Cancel(); _store.Seal(); _partial = true; _phase = AnalysisPhase.Stopped; _final = Capture();
        }
        Report();
    }
    private void OnDeviceHealth(string key, DeviceHealth health)
    {
        if (health != DeviceHealth.NotResponding) return;
        lock (_sync)
        {
            if (_disposed || _workerFinished || _activeDevice?.Contains(key) != true) return;
        }
        Stop("device-not-responding");
    }
    private async Task ProduceAsync()
    {
        bool admitted = false; IDisposable? payloadAdmission = null;
        try
        {
            await GlobalScans.WaitAsync(_cancel.Token).ConfigureAwait(false); admitted = true;
            payloadAdmission = AnalysisPendingPayload.Working.Acquire(AnalysisPendingPayload.WorkingPerScan);
            if (_io.IsStopped) throw new OperationCanceledException("Analysis I/O has stopped.");
            await NormalizeRootsAsync().ConfigureAwait(false);
            await PrepareStorageAsync().ConfigureAwait(false);
            for (int i = 0; i < _sources.Length; i++)
            {
                _cancel.Token.ThrowIfCancellationRequested();
                var source = _sources[i]; var root = _roots[i];
                if (root.Disposition != AnalysisRootDisposition.Active)
                {
                    if (_ntfs is not null) FailNtfsDiscovery(MeasurementState.Unsupported);
                    continue;
                }
                string device = source.IoDeviceKey;
                lock (_sync) _activeDevice = device;
                if (_io.GetHealth(device) == DeviceHealth.NotResponding) { Issue("device-not-responding"); continue; }
                using var turn = await AnalysisScanDevices.AcquireAsync(device, _cancel.Token).ConfigureAwait(false);
                if (_ntfs is { } ntfs) await WalkNtfsAsync(ntfs, root).ConfigureAwait(false);
                else
                {
                    if (source is IAnalysisVolumeBackend capacity && !_protectedSources.Contains(source))
                        await CaptureVolumeCapacityAsync(capacity, root.Id, device).ConfigureAwait(false);
                    await WalkAsync(source, root, device, i).ConfigureAwait(false);
                }
                if (_store.Limited && _writer is null) break;
            }
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (OperationCanceledException) when (_io.IsStopped) { Stop("analysis-io-stopped"); }
        catch (Exception ex) when (_ntfs is not null && ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or TimeoutException)
        { FailNtfsDiscovery(ex is AnalysisSourceChangedException ? MeasurementState.Changed : MeasurementState.Unsupported); }
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
                try { await FinishStorageAsync().ConfigureAwait(false); }
                finally
                {
                    lock (_sync) _activeDevice = null;
                    _io.HealthChanged -= OnDeviceHealth;
                    payloadAdmission?.Dispose();
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
    }
    private async Task NormalizeRootsAsync()
    {
        var roots = _roots.Select(root => root.Disposition == AnalysisRootDisposition.UnavailablePrivateBoundary ? root :
            root with { Disposition = AnalysisRootDisposition.Active, CoveredByRootId = null, CoveredRelativePath = null }).ToArray(); var active = new List<int>();
        // Parents are considered before children regardless of selection order. IDs keep their captured binding.
        foreach (int child in Enumerable.Range(0, roots.Length).Where(i => roots[i].Disposition != AnalysisRootDisposition.UnavailablePrivateBoundary)
            .OrderBy(i => roots[i].CanonicalLocator.Length).ThenBy(i => i))
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
    private async Task WalkAsync(IAnalysisBackend source, RootBinding root, string device, int rootIndex)
    {
        bool protectedRoot = _protectedSources.Contains(source) || IsProtected(source.RootEntry);
        var rootEntry = protectedRoot ? ExcludedPrivate(source.RootEntry) : source.RootEntry;
        var detailed = _policy.ComponentPreset == "detailed" && source is IAnalysisDetailedBackend { ExtraAllocationBasis: not null } provider
            ? provider : null;
        if (!protectedRoot && detailed is not null && rootEntry.Kind == AnalysisKind.File)
            rootEntry = await AnalysisIo.Run(_io, device, detailed.ReadDetailedRoot, _cancel.Token).ConfigureAwait(false);
        var rootId = await AppendObservationAsync(rootEntry, 0, root.Id, source.Basis).ConfigureAwait(false);
        if (!rootId.HasValue) { Issue("memory-or-observation-limit"); return; }
        // Publish the first observation before a child enumeration can block. The setup-time
        // preview may already have consumed the initial dirty notification with an empty store.
        if (rootId.Value == 1) Report();
        if (protectedRoot) { Issue("private-analysis-state-excluded", rootId, MeasurementState.Excluded); return; }
        if (rootEntry.ExtraAllocation?.Measurement.State == MeasurementState.Changed)
            Issue("extra-data-changed-during-observation", rootId, MeasurementState.Changed);
        if (source.RootEntry.Kind != AnalysisKind.Directory) return;
        if (!source.RootEntry.IncludedByPolicy || source.RootEntry.MetadataState != MeasurementState.Known)
        { Issue(source.RootEntry.IssueCode ?? "root-enumeration-unavailable", rootId, source.RootEntry.MetadataState); return; }
        if (source.RootEntry.Cloud == true) { Issue("cloud-directory-not-enumerated", rootId, MeasurementState.Excluded); return; }
        var exclusions = _folderExclusions.EncodePaths(_roots, rootIndex);
        var pending = new Stack<(AnalysisEntry Entry, ulong Id, int Depth, IDisposable? Payload)>();
        AnalysisSourceFrontier? frontier = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (_writer is not null && source.RootEntry.Ancestry is not null)
                frontier = await OnStorageDeviceAsync(() => _writer.CreateSourceFrontierAsync(source.RootEntry, root, rootId.Value, _cancel.Token)).ConfigureAwait(false);
            else pending.Push((source.RootEntry, rootId.Value, 0, null));
            lock (_sync) _enumerationRequested++;
            await PublishEnumerationAsync().ConfigureAwait(false);
            while (!_store.Limited || _writer is not null)
            {
                _cancel.Token.ThrowIfCancellationRequested();
                AnalysisEntry entry; ulong id; int depth; AnalysisFrontierDirectory? ticket = null;
                if (frontier is not null)
                {
                    try { ticket = await OnStorageDeviceAsync(() => frontier.PopAsync(_cancel.Token)).ConfigureAwait(false); }
                    catch (AnalysisFrontierScopeException error) { Issue("frontier-read-witness-limit", error.Path); lock (_sync) _enumerationFailed++; await PublishEnumerationAsync().ConfigureAwait(false); continue; }
                    if (ticket is null) break;
                    (entry, id, depth) = (ticket.Entry, ticket.Path, ticket.Depth);
                }
                else
                {
                    if (!pending.TryPop(out var next)) break;
                    (entry, id, depth) = (next.Entry, next.Id, next.Depth); next.Payload?.Dispose();
                }
                if (depth >= AnalysisLimits.Depth) { Issue("depth-limit", id); lock (_sync) _enumerationFailed++; await PublishEnumerationAsync().ConfigureAwait(false); continue; }
                if (AnalysisPendingPayload.EntryBytes(entry, ancestry: true) > AnalysisPendingPayload.WitnessBytes)
                { Issue("frontier-read-witness-limit", id); lock (_sync) _enumerationFailed++; await PublishEnumerationAsync().ConfigureAwait(false); continue; }
                var enumeration = await AnalysisIo.Run(_io, device, _ =>
                    (detailed is null ? source.Children(entry, _cancel.Token) :
                    detailed.ReadDetailedChildren(entry, observed => !IsProtected(observed), _cancel.Token)).GetEnumerator(), _cancel.Token).ConfigureAwait(false);
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
                    for (int i = 0; i < batch.Entries.Count; i++)
                    {
                        var child = batch.Entries[i];
                        if (IsProtected(child)) batch.Entries[i] = ExcludedPrivate(child);
                        else if (exclusions.Length != 0) batch.Entries[i] = AnalysisFolderExclusions.Apply(child, exclusions);
                    }
                    var storedIds = _writer is null ? default : await AppendCapturedBatchAsync(batch.Entries, id, root.Id, source.Basis).ConfigureAwait(false);
                    var frontierChildren = frontier is null ? null : ImmutableArray.CreateBuilder<AnalysisFrontierChild>();
                    for (int childAt = 0; childAt < batch.Entries.Count; childAt++)
                    {
                        var child = batch.Entries[childAt];
                        ulong? childId = storedIds.IsDefault ? _store.Append(child, id, root.Id, source.Basis, _policy.ComponentPreset == "detailed") : storedIds[childAt];
                        if (childId is null) { Issue("memory-or-observation-limit", id); return; }
                        if (child.ExtraAllocation?.Measurement.State == MeasurementState.Changed)
                            Issue("extra-data-changed-during-observation", childId, MeasurementState.Changed);
                        if (child.IssueCode is { } issueCode) Issue(issueCode, childId, child.MetadataState);
                        if (!child.IncludedByPolicy || child.MetadataState != MeasurementState.Known) continue;
                        if (child.Kind is AnalysisKind.Link or AnalysisKind.Special)
                            Issue(child.Kind == AnalysisKind.Link ? "navigation-link-not-followed" : "special-entry-not-traversed", childId, MeasurementState.Excluded);
                        if (child.Kind != AnalysisKind.Directory) continue;
                        if (child.Cloud == true) { Issue("cloud-directory-not-enumerated", childId, MeasurementState.Excluded); continue; }
                        if (frontierChildren is not null) { frontierChildren.Add(new(child, childId.Value)); continue; }
                        lock (_sync) _enumerationRequested++;
                        long bytes = AnalysisPendingPayload.EntryBytes(child, ancestry: true);
                        if (!AnalysisPendingPayload.Frontier.TryAcquire(bytes, out var payload))
                        { Issue("pending-payload-limit", childId); lock (_sync) _enumerationFailed++; continue; }
                        try { pending.Push((child, childId.Value, depth + 1, payload)); }
                        catch { payload!.Dispose(); throw; }
                    }
                    if (frontierChildren is { Count: > 0 })
                    {
                        var children = frontierChildren.ToImmutable();
                        await OnStorageDeviceAsync(async () => { await frontier!.PushAsync(ticket!, children, _cancel.Token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
                        lock (_sync) _enumerationRequested = checked(_enumerationRequested + (ulong)children.Length);
                    }
                    await PublishEnumerationAsync().ConfigureAwait(false);
                    await FlushCaptureIssuesAsync().ConfigureAwait(false);
                    if (batch.Limited) { Issue("producer-record-limit", id); break; }
                    if (batch.Error is { } error) { Issue(error, id); break; }
                    if (finished) succeeded = true;
                    if (clock.ElapsedMilliseconds >= 250) { Report(); clock.Restart(); }
                }
                lock (_sync) { if (succeeded) _enumerationCompleted++; else _enumerationFailed++; }
                await PublishEnumerationAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            while (pending.TryPop(out var queued)) queued.Payload?.Dispose();
            if (frontier is not null)
                await OnStorageDeviceAsync(async () => { await frontier.DisposeAsync().ConfigureAwait(false); return true; }).ConfigureAwait(false);
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
            long cost = AnalysisPendingPayload.EntryBytes(entry);
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
            _store.Issue(code, node is { } id && id > (ulong)_store.Count ? null : node, state, rootId);
            QueueCaptureIssue(code, node, state, rootId);
        }
    }
    private void Report()
    {
        AnalysisProgress p;
        lock (_sync) { if (_disposed) return; p = new(_storage is null || _memoryFallback ? _store.Count : _capturedPaths, _store.IssueCount, _phase) { SourceCloseFailures = _sourceCloseFailures,
            PreviewPaths = _store.Count, PreviewLimited = _previewLimited, DatasetReady = _dataset is not null, MemoryFallback = _memoryFallback,
            SessionId = _identity.SessionId, Epoch = _identity.Epoch, NtfsDiscoveryRecords = _ntfsDiscoveryPending ? _ntfsDiscoveryRecords : null,
            WaitingForNtfsAuthorization = _ntfsDiscoveryPending && _ntfsDiscoveryRecords is null,
            NtfsAuthorizationDeclined = _ntfsAuthorizationDeclined }; }
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
            _disposed = true; _finalizeCancel.Cancel(); _final = null;
            if (_workerFinished && _snapshotLeases == 0) Release();
        }
    }
    private void Release()
    {
        if (_released) return;
        _released = true; _store.Dispose(); _cancel.Dispose(); _finalizeCancel.Dispose();
        if (_dataset is { } dataset) { _dataset = null; _datasetClose = dataset.DisposeAsync().AsTask(); }
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
