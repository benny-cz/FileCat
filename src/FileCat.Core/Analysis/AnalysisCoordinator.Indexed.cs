using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Application-owned native preparation; these factories are never decoded from inventory data.</summary>
public sealed record AnalysisPrivateRoot(string DeviceKey, Func<IAnalysisBackend> Prepare);
public sealed record AnalysisStorageOptions(string DeviceKey, Func<AnalysisPrivateDirectory> CreateDirectory, ImmutableArray<AnalysisPrivateRoot> ProtectedRoots, int PreviewPaths = 4096)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceKey) || DeviceKey.Length > 1024 || CreateDirectory is null || ProtectedRoots.IsDefaultOrEmpty || ProtectedRoots.Length > AnalysisLimits.Roots ||
            PreviewPaths is < 1 or > 65536 || ProtectedRoots.Any(p => p is null || p.Prepare is null || string.IsNullOrWhiteSpace(p.DeviceKey) || p.DeviceKey.Length > 1024))
            throw new ArgumentException("Secure analysis storage requires explicit bounded private-root preparation.");
    }
}

public sealed partial class AnalysisSession
{
    private readonly AnalysisStorageOptions? _storage;
    private readonly List<IAnalysisBackend> _protected = [];
    private readonly HashSet<string> _protectedIdentities = new(StringComparer.Ordinal);
    private readonly Queue<AnalysisIssue> _captureIssues = [];
    private readonly CancellationTokenSource _finalizeCancel = new();
    private AnalysisObservationWriter? _writer;
    private AnalysisDataset? _dataset;
    private Task _datasetClose = Task.CompletedTask;
    private bool _previewLimited, _indexing, _captureIssueOverflow;
    private int _capturedPaths;
    public Task DatasetCloseCompletion { get { lock (_sync) return _datasetClose; } }
    public AnalysisDatasetDescriptor? DatasetDescriptor { get { lock (_sync) return _dataset?.Descriptor; } }
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
        lock (_sync) _activeDevice = _storage.DeviceKey;
        _writer = await AnalysisObservationWriter.CreateAsync(_io, _storage.DeviceKey, _storage.CreateDirectory, _identity, _roots, _policy, _cancel.Token).ConfigureAwait(false);
        if (_cancel.IsCancellationRequested) { _writer.RequestStop(); _cancel.Token.ThrowIfCancellationRequested(); }
        foreach (var protection in _storage.ProtectedRoots)
        {
            lock (_sync) _activeDevice = protection.DeviceKey;
            var source = await AnalysisIo.Run(_io, protection.DeviceKey, _ => protection.Prepare(), _cancel.Token).ConfigureAwait(false);
            _protected.Add(source);
            if (source.Root.Assurance != IdentityAssurance.Verified || source.RootEntry.Kind != AnalysisKind.Directory || source.RootEntry.Identity is not { } identity ||
                _sources.Any(s => s.Root.Assurance != IdentityAssurance.Verified))
                throw new IOException("Private-state exclusion could not be qualified for these sources.");
            _protectedIdentities.Add(identity);
        }
        await FlushCaptureIssuesAsync().ConfigureAwait(false);
    }
    private bool IsProtected(AnalysisEntry entry) => entry.Identity is { } identity && _protectedIdentities.Contains(identity);
    private static AnalysisEntry ExcludedPrivate(AnalysisEntry entry) => entry with
    {
        Identity = null, Version = "", IncludedByPolicy = false, MetadataState = MeasurementState.Excluded,
        LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Excluded), Allocation = AnalysisMeasurement.Missing(MeasurementState.Excluded),
        Links = AnalysisMeasurement.Missing(MeasurementState.Excluded), IssueCode = "private-analysis-state-excluded"
    };
    private async Task<ulong?> AppendObservationAsync(AnalysisEntry entry, ulong parent, string root, string basis)
    {
        if (_writer is null) return _store.Append(entry, parent, root, basis, _policy.ComponentPreset == "detailed");
        return (await AppendCapturedBatchAsync([entry], parent, root, basis).ConfigureAwait(false))[0];
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
                var e = entries[next]; long cost = 512L + e.Name.Length * 2L + e.RawName.Length + (e.Identity?.Length ?? 0) * 2L + e.Version.Length * 2L + root.Length * 2L + basis.Length * 2L;
                if (cost > AnalysisLimits.BatchBytes) throw new IOException("One metadata observation exceeds the producer payload bound.");
                if (batch.Count != 0 && bytes + cost > AnalysisLimits.BatchBytes) break;
                bytes += cost; batch.Add(new(e, parent, root, basis, _policy.ComponentPreset == "detailed")); next++;
            }
            string? previous;
            lock (_sync) { previous = _activeDevice; _activeDevice = _storage!.DeviceKey; }
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
        if (_storage is null || _cancel.IsCancellationRequested) return;
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
            string? previous;
            lock (_sync) { previous = _activeDevice; _activeDevice = _storage!.DeviceKey; }
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
                _indexing = true; _activeDevice = _storage!.DeviceKey;
                phase = _phase is AnalysisPhase.Stopped or AnalysisPhase.Failed ? _phase : AnalysisPhase.Finished;
                coverage = _partial || phase != AnalysisPhase.Finished ? NamespaceCoverage.Partial : NamespaceCoverage.CompleteUnderPolicy;
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
        catch (Exception) { Issue("analysis-index-unavailable"); lock (_sync) _final = null; }
        finally
        {
            if (_writer is { } writer) { try { await writer.DisposeAsync().ConfigureAwait(false); } catch { lock (_sync) _sourceCloseFailures++; } }
            lock (_sync) { _writer = null; _indexing = false; _activeDevice = null; }
        }
    }
}
