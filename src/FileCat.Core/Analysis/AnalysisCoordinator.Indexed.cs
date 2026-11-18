using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Application-owned native preparation; these factories are never decoded from inventory data.</summary>
public sealed record AnalysisPrivateRoot(string DeviceKey, Func<IAnalysisBackend> Prepare)
{
    // A held ancestor of an unavailable boundary is not itself declared private. Only native
    // sources proven disjoint in both directions may be read while that boundary is unavailable.
    public bool RequiresDisjointSource { get; init; }
}
public sealed record AnalysisStorageOptions(string DeviceKey, Func<AnalysisPrivateDirectory> CreateDirectory, ImmutableArray<AnalysisPrivateRoot> ProtectedRoots, int PreviewPaths = 4096)
{
    // Trusted application configuration, never reconstructed from an inventory's profile correlation ID.
    public Func<AnalysisProfileQuota>? CreateProfileQuota { get; init; }
    public string? ProfileDeviceKey { get; init; }
    public bool AllowMemoryFallback { get; init; }
    public bool MemoryOnly { get; init; }
    internal AnalysisIoDevices Devices => new(DeviceKey, ProfileDeviceKey);
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceKey) || DeviceKey.Length > 1024 || CreateDirectory is null || ProtectedRoots.IsDefaultOrEmpty || ProtectedRoots.Length > AnalysisLimits.Roots ||
            PreviewPaths is < 1 or > 65536 || MemoryOnly && !AllowMemoryFallback || ProtectedRoots.Any(p => p is null || p.Prepare is null || string.IsNullOrWhiteSpace(p.DeviceKey) || p.DeviceKey.Length > 1024))
            throw new ArgumentException("Secure analysis storage requires explicit bounded private-root preparation.");
        _ = Devices;
    }
}

public sealed partial class AnalysisSession
{
    private readonly AnalysisStorageOptions? _storage;
    private readonly List<IAnalysisBackend> _protected = [];
    private readonly HashSet<string> _protectedIdentities = new(StringComparer.Ordinal);
    private readonly HashSet<IAnalysisBackend> _protectedSources = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IAnalysisBackend> _unavailableSources = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<AnalysisIssue> _captureIssues = [];
    private readonly CancellationTokenSource _finalizeCancel = new();
    private AnalysisObservationWriter? _writer;
    private AnalysisDataset? _dataset;
    private Task _datasetClose = Task.CompletedTask;
    private bool _previewLimited, _indexing, _captureIssueOverflow, _memoryFallback;
    private int _capturedPaths;
    private AnalysisScratchLimit? _scratchLimit;
    public Task DatasetCloseCompletion { get { lock (_sync) return _datasetClose; } }
    public AnalysisDatasetDescriptor? DatasetDescriptor { get { lock (_sync) return _dataset?.Descriptor; } }
    public bool UsesMemoryFallback { get { lock (_sync) return _memoryFallback; } }
    internal AnalysisScratchLimit? ScratchLimitReached { get { lock (_sync) return _scratchLimit; } }
    /// <summary>Current scratch reservations, or null while the session has no private index.</summary>
    public long? ScratchReservedBytes { get { lock (_sync) return _writer?.ScratchReservations.ReservedBytes ?? _dataset?.ScratchReservedBytes; } }
    public Task<AnalysisDatasetLease?> AcquireDatasetAsync(CancellationToken cancellation = default)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _dataset is { } dataset ? Acquire(dataset) : Task.FromResult<AnalysisDatasetLease?>(null);
        }
        async Task<AnalysisDatasetLease?> Acquire(AnalysisDataset dataset) => await dataset.AcquireAsync(cancellation).ConfigureAwait(false);
    }
    private async Task PrepareStorageAsync()
    {
        if (_storage is null) return;
        Exception? protectionFailure = null;
        try
        {
            foreach (var protection in _storage.ProtectedRoots)
            {
                lock (_sync) _activeDevice = protection.DeviceKey;
                var source = await AnalysisIo.Run(_io, protection.DeviceKey, _ => protection.Prepare(), _cancel.Token).ConfigureAwait(false);
                _protected.Add(source);
                if (source.Root.Assurance != IdentityAssurance.Verified || source.RootEntry.Kind != AnalysisKind.Directory || source.RootEntry.Identity is not { Length: > 0 } identity ||
                    _sources.Any(s => s.Root.Assurance != IdentityAssurance.Verified))
                    throw new IOException("Private-state exclusion could not be qualified for these sources.");
                if (!protection.RequiresDisjointSource) _protectedIdentities.Add(identity);
                foreach (var candidate in _sources)
                {
                    var containment = candidate.RootEntry.Identity == identity ? AnalysisRootContainment.Covered :
                        await AnalysisIo.Run(_io, new(protection.DeviceKey, candidate.IoDeviceKey), _ => source.ContainsRoot(candidate), _cancel.Token).ConfigureAwait(false);
                    if (protection.RequiresDisjointSource)
                    {
                        var reverse = containment == AnalysisRootContainment.Disjoint
                            ? await AnalysisIo.Run(_io, new(protection.DeviceKey, candidate.IoDeviceKey), _ => candidate.ContainsRoot(source), _cancel.Token).ConfigureAwait(false)
                            : AnalysisRootContainment.Unavailable;
                        if (containment != AnalysisRootContainment.Disjoint || reverse != AnalysisRootContainment.Disjoint) _unavailableSources.Add(candidate);
                        continue;
                    }
                    if (containment == AnalysisRootContainment.Unavailable) throw new IOException("Private-state containment could not be qualified for these sources.");
                    if (containment == AnalysisRootContainment.Covered) _protectedSources.Add(candidate);
                }
            }
        }
        catch (Exception error) when (!_cancel.IsCancellationRequested && error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        { protectionFailure = error; }
        // Qualify membership before scratch creation changes a private directory's version. Even an
        // explicitly selected private file root contributes only excluded evidence, never its metadata.
        if (protectionFailure is not null) throw new IOException("Private-state exclusion could not be qualified for these sources.", protectionFailure);
        if (_unavailableSources.Count != 0)
        {
            lock (_sync) _roots = _roots.Select((root, i) => _unavailableSources.Contains(_sources[i])
                ? root with { Disposition = AnalysisRootDisposition.UnavailablePrivateBoundary, CoveredByRootId = null, CoveredRelativePath = null,
                    ObservedObjectIdentity = null, SourceIdentity = null, Assurance = IdentityAssurance.Unknown } : root).ToImmutableArray();
            foreach (var root in _roots.Where(r => r.Disposition == AnalysisRootDisposition.UnavailablePrivateBoundary))
                Issue("private-analysis-boundary-unavailable", state: MeasurementState.Unsupported, rootId: root.Id);
            // A selected safe child can still run when its previously covering root is unavailable.
            // Recompute coverage among the remaining held sources so nested selections cannot double count.
            await NormalizeRootsAsync().ConfigureAwait(false);
        }
        lock (_sync) _roots = _roots.Select((root, i) => _protectedSources.Contains(_sources[i]) && root.Disposition != AnalysisRootDisposition.UnavailablePrivateBoundary
            ? root with { ObservedObjectIdentity = null, SourceIdentity = null, Assurance = IdentityAssurance.Unknown,
                Disposition = root.Disposition == AnalysisRootDisposition.Covered ? AnalysisRootDisposition.ExcludedPrivateState : root.Disposition,
                CoveredByRootId = null, CoveredRelativePath = null } : root).ToImmutableArray();
        foreach (var root in _roots.Where(r => r.Disposition == AnalysisRootDisposition.ExcludedPrivateState))
            Issue("private-analysis-state-excluded", state: MeasurementState.Excluded, rootId: root.Id);
        lock (_sync) _activeDevice = _storage.Devices;
        if (_storage.MemoryOnly) { await SwitchToMemoryAsync().ConfigureAwait(false); return; }
        try
        {
            _writer = await AnalysisObservationWriter.CreateAsync(_io, _storage.Devices, _storage.CreateDirectory, _identity, _roots, _policy,
                SourceTaint, _cancel.Token, _storage.CreateProfileQuota).ConfigureAwait(false);
            if (_cancel.IsCancellationRequested) { _writer.RequestStop(); _cancel.Token.ThrowIfCancellationRequested(); }
            await PublishEnumerationAsync().ConfigureAwait(false);
            await FlushCaptureIssuesAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (CanUseMemory(error)) { await SwitchToMemoryAsync().ConfigureAwait(false); }
    }
    private bool CanUseMemory(Exception error) => _ntfs is null && _storage?.AllowMemoryFallback == true && !_cancel.IsCancellationRequested &&
        _capturedPaths == 0 && !_memoryFallback && (error is IOException or UnauthorizedAccessException or NotSupportedException) &&
        _storage.Devices.Keys.All(key => _io.GetHealth(key) == DeviceHealth.Responsive) && AnalysisAdmission.Refusal() is null;
    private async Task SwitchToMemoryAsync()
    {
        // No native work is abandoned and no second disk or store is opened. Failure to close the
        // unpublished scratch owner refuses fallback; private-root capabilities stay held for traversal.
        if (_writer is { } writer) { await writer.DisposeAsync().ConfigureAwait(false); _writer = null; }
        _cancel.Token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _store.UseBoundedMemory(); _memoryFallback = true; _captureIssues.Clear(); _captureIssueOverflow = false;
        }
        Report();
    }
    private Task PublishEnumerationAsync()
    {
        if (_writer is null || _ntfs is not null) return Task.CompletedTask;
        AnalysisEnumerationProgress progress;
        lock (_sync) progress = new(_enumerationRequested, _enumerationCompleted, _enumerationFailed);
        return OnStorageDeviceAsync(async () => { await _writer.PublishEnumerationAsync(progress, _cancel.Token).ConfigureAwait(false); return true; });
    }
    private bool IsProtected(AnalysisEntry entry) => entry.Identity is { } identity && _protectedIdentities.Contains(identity);
    internal bool IsPrivateAnalysisEntry(AnalysisEntry entry) => IsProtected(entry);
    private async Task<T> OnStorageDeviceAsync<T>(Func<Task<T>> work)
    {
        AnalysisIoDevices? previous;
        lock (_sync) { previous = _activeDevice; _activeDevice = _storage!.Devices; }
        try { return await work().ConfigureAwait(false); }
        finally { lock (_sync) _activeDevice = previous; }
    }
    private static AnalysisEntry ExcludedPrivate(AnalysisEntry entry) => entry with
    {
        Identity = null, Version = "", IncludedByPolicy = false, MetadataState = MeasurementState.Excluded,
        ModifiedUtc = null, CreatedUtc = null, CreatedPrecisionTicks = null, ChangedUtc = null, ChangedPrecisionTicks = null,
        Sparse = null, Compressed = null, Cloud = null,
        LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Excluded), Allocation = AnalysisMeasurement.Missing(MeasurementState.Excluded),
        Links = AnalysisMeasurement.Missing(MeasurementState.Excluded), IssueCode = "private-analysis-state-excluded", ExtraAllocation = null
    };
    private async Task<ulong?> AppendObservationAsync(AnalysisEntry entry, ulong parent, string root, string basis)
    {
        if (_writer is null) return _store.Append(entry, parent, root, basis, _policy.ComponentPreset == "detailed");
        try { return (await AppendCapturedBatchAsync([entry], parent, root, basis).ConfigureAwait(false))[0]; }
        catch (Exception error) when (CanUseMemory(error))
        {
            await OnStorageDeviceAsync(async () => { await SwitchToMemoryAsync().ConfigureAwait(false); return true; }).ConfigureAwait(false);
            return _store.Append(entry, parent, root, basis, _policy.ComponentPreset == "detailed");
        }
    }
    private async Task<ImmutableArray<ulong>> AppendCapturedBatchAsync(IReadOnlyList<AnalysisEntry> entries, ulong parent, string root, string basis)
    {
        if (entries.Count == 0) return [];
        var result = ImmutableArray.CreateBuilder<ulong>(entries.Count); int next = 0;
        while (next < entries.Count)
        {
            var batch = ImmutableArray.CreateBuilder<AnalysisObservationItem>(); long bytes = 0;
            while (next < entries.Count && batch.Count < AnalysisLimits.BatchRecords)
            {
                var e = entries[next]; long cost = AnalysisObservationWriter.RecordBytes(e, root, basis);
                if (cost > AnalysisLimits.BatchBytes) throw new IOException("One metadata observation exceeds the producer payload bound.");
                if (batch.Count != 0 && bytes + cost > AnalysisLimits.BatchBytes) break;
                bytes += cost; batch.Add(new(e, parent, root, basis, _policy.ComponentPreset == "detailed")); next++;
            }
            AnalysisIoDevices? previous;
            lock (_sync) { previous = _activeDevice; _activeDevice = _storage!.Devices; }
            ImmutableArray<ulong> ids;
            try { ids = await _writer!.AppendBatchAsync(batch.ToImmutable(), _cancel.Token).ConfigureAwait(false); }
            finally { lock (_sync) _activeDevice = previous; }
            lock (_sync)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    _capturedPaths = checked((int)ids[i]);
                    if (!_previewLimited)
                    {
                        var item = batch[i]; var preview = _store.Append(item.Entry, item.Parent, root, basis, item.DirectoryAllocation);
                        if (preview is null) _previewLimited = true;
                        else if (preview != ids[i]) throw new InvalidDataException("Preview and captured path identities diverged.");
                    }
                }
            }
            result.AddRange(ids);
        }
        return result.ToImmutable();
    }
    private void QueueCaptureIssue(string code, ulong? node, MeasurementState state, string? root)
    {
        if (_storage is null || _memoryFallback || _cancel.IsCancellationRequested) return;
        if (_captureIssues.Count >= AnalysisLimits.BatchRecords * 2)
        { _captureIssueOverflow = true; _partial = true; _writer?.RequestStop(); return; }
        _captureIssues.Enqueue(new(code, node, state) { RootId = root });
    }
    private async Task FlushCaptureIssuesAsync()
    {
        if (_writer is null || _cancel.IsCancellationRequested || _captureIssueOverflow) return;
        while (true)
        {
            AnalysisIssue issue;
            lock (_sync) { if (_captureIssues.Count == 0) return; issue = _captureIssues.Dequeue(); }
            AnalysisIoDevices? previous;
            lock (_sync) { previous = _activeDevice; _activeDevice = _storage!.Devices; }
            try { await _writer.RecordIssueAsync(issue.Code, issue.NodeId, issue.State, issue.RootId, _cancel.Token).ConfigureAwait(false); }
            finally { lock (_sync) _activeDevice = previous; }
        }
    }
    private async Task FinishStorageAsync()
    {
        try
        {
            foreach (var source in _protected)
            { try { await AnalysisIo.ReleaseAsync(_io, source).ConfigureAwait(false); } catch { lock (_sync) _sourceCloseFailures++; } }
            _protected.Clear();
            if (_writer is null) return;
            try { await FlushCaptureIssuesAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
            SessionIdentity identity; AnalysisPhase phase; NamespaceCoverage coverage; AnalysisFreshness freshness;
            lock (_sync)
            {
                if (_disposed) return;
                _indexing = true; _activeDevice = _storage!.Devices;
                phase = _phase is AnalysisPhase.Stopped or AnalysisPhase.Failed ? _phase : AnalysisPhase.Finished;
                coverage = _partial || phase != AnalysisPhase.Finished || _ntfs is not null ? NamespaceCoverage.Partial : NamespaceCoverage.CompleteUnderPolicy;
                freshness = _freshness;
                // Source acquisition is finished. Freeze its final preview before reserving the
                // dataset revision, so UI polling cannot publish newer preview IDs during indexing.
                _phase = phase; _final ??= Capture();
                identity = _identity with { Revision = checked(++_revision), ObservedUtc = DateTimeOffset.UtcNow };
            }
            var dataset = await _writer.CompleteAsync(identity, phase, coverage, _finalizeCancel.Token, freshness).ConfigureAwait(false);
            bool keep;
            lock (_sync) { keep = !_disposed && !_finalizeCancel.IsCancellationRequested; if (keep) _dataset = dataset; }
            if (!keep) await dataset.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_finalizeCancel.IsCancellationRequested) { }
        catch (AnalysisScratchLimitException error) { RecordScratchLimit(error); lock (_sync) _final = null; }
        catch (Exception) { Issue("analysis-index-unavailable"); lock (_sync) _final = null; }
        finally
        {
            if (_writer is { } writer) { try { await writer.DisposeAsync().ConfigureAwait(false); } catch { lock (_sync) _sourceCloseFailures++; } }
            lock (_sync) { _writer = null; _indexing = false; _activeDevice = null; }
        }
    }

    private void RecordScratchLimit(AnalysisScratchLimitException error)
    {
        lock (_sync) _scratchLimit ??= error.Limit;
        Issue(error.Limit == AnalysisScratchLimit.Budget ? "analysis-scratch-budget-reached" : "analysis-scratch-reserve-reached");
    }
}
