using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisObservationItem(AnalysisEntry Entry, ulong Parent, string RootId, string Basis, bool DirectoryAllocation = false);

/// <summary>
/// Production capture/read pipeline over an explicitly supplied private directory factory. Captured
/// entries are observations, not read witnesses. This writer never enumerates or reconnects a source.
/// </summary>
public sealed class AnalysisObservationWriter : IAsyncDisposable
{
    private readonly AnalysisOwnedDatasetIo<Owner> _owned;
    private readonly DeviceIoScheduler _io; private readonly AnalysisIoDevices _device;
    private AnalysisObservationCapture? _stopTarget;
    private AnalysisObservationWriter(Owner owner, DeviceIoScheduler io, AnalysisIoDevices device)
    { _io = io; _device = device; _owned = new(owner, io, device); _stopTarget = owner.Capture; }
    public void RequestStop() => Volatile.Read(ref _stopTarget)?.RequestStop();
    internal void RequestStop(string? issueCode) => Volatile.Read(ref _stopTarget)?.RequestStop(issueCode);
    internal Task PublishEnumerationAsync(AnalysisEnumerationProgress progress, CancellationToken cancellation) =>
        _owned.RunAsync((owner, token) => { owner.Capture.Enumeration(progress, token); owner.Capture.Publish(token); return true; }, cancellation);

    // The factory is application-owned configuration, not a callback restored from an inventory.
    public static async Task<AnalysisObservationWriter> CreateAsync(DeviceIoScheduler io, AnalysisIoDevices device, Func<AnalysisPrivateDirectory> createDirectory,
        SessionIdentity acquisition, ImmutableArray<RootBinding> roots, ScanPolicy policy, CancellationToken cancellation = default, Func<AnalysisProfileQuota>? createProfileQuota = null)
    {
        ArgumentNullException.ThrowIfNull(createDirectory);
        var header = new AnalysisIndexedGraphHeader(acquisition, roots, policy, AnalysisPhase.Scanning, NamespaceCoverage.Partial); header.Validate();
        var owner = await AnalysisIo.Run(io, device, _ =>
        {
            AnalysisProfileQuota? quota = null; AnalysisPrivateDirectory? directory = null; AnalysisScratchWorkspace workspace;
            try
            {
                quota = createProfileQuota?.Invoke(); directory = createDirectory();
                workspace = new(directory, AnalysisLimits.ScratchBytes, AnalysisScratchBudget.Shared, quota);
                quota?.Dispose(); // Workspace/reader ownership independently retains its capacity owner.
            }
            catch { try { directory?.Dispose(); } finally { quota?.Dispose(); } throw; }
            try
            {
                var arena = workspace.CreateArena();
                try { return new Owner(workspace, new(arena, header)); }
                catch { arena.Dispose(); throw; }
            }
            catch { workspace.Dispose(); throw; }
        }, cancellation).ConfigureAwait(false);
        try { return new(owner, io, device); }
        catch { await AnalysisIo.ReleaseScratchAsync(io, device, owner).ConfigureAwait(false); throw; }
    }

    public async Task<ImmutableArray<ulong>> AppendBatchAsync(ImmutableArray<AnalysisObservationItem> observations, CancellationToken cancellation = default)
    {
        long bytes = PayloadBytes(observations);
        cancellation.ThrowIfCancellationRequested();
        using (AnalysisPendingPayload.Writes.Acquire(bytes))
        {
            var batch = Detach(observations);
            // Clear the async method's input field before awaiting. Detaching only the lambda
            // payload would still leave original read witnesses reachable through that field.
            observations = default;
            return await _owned.RunAsync((owner, token) =>
            {
                try
                {
                    var ids = ImmutableArray.CreateBuilder<ulong>(batch.Length);
                    foreach (var item in batch) ids.Add(owner.Capture.Append(item.Entry, item.Parent, item.RootId, item.Basis, item.DirectoryAllocation, token));
                    owner.Capture.Publish(token); return ids.ToImmutable();
                }
                catch { owner.Capture.StopAccepting(); throw; }
            }, cancellation).ConfigureAwait(false);
        }
    }
    internal static long PayloadBytes(ImmutableArray<AnalysisObservationItem> observations)
    {
        if (observations.IsDefault || observations.Length is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(observations));
        long bytes = 0;
        foreach (var item in observations)
        {
            if (item is null || item.Entry is not { } entry || entry.Name is null || entry.NameEncoding is null || entry.Version is null || item.RootId is null || item.Basis is null) throw new ArgumentException("A captured batch row is incomplete.", nameof(observations));
            bytes = checked(bytes + 512L + entry.Name.Length * 2L + entry.RawName.Length + (entry.Identity?.Length ?? 0) * 2L + (entry.Version?.Length ?? 0) * 2L +
                entry.NameEncoding.Length * 2L + (entry.IssueCode?.Length ?? 0) * 2L + item.RootId.Length * 2L + item.Basis.Length * 2L);
            if (bytes > AnalysisLimits.BatchBytes) throw new ArgumentException("The captured batch exceeds its byte limit. Submit a smaller batch.", nameof(observations));
        }
        return bytes;
    }
    private static ImmutableArray<AnalysisObservationItem> Detach(ImmutableArray<AnalysisObservationItem> observations)
    {
        var detached = ImmutableArray.CreateBuilder<AnalysisObservationItem>(observations.Length);
        foreach (var item in observations) detached.Add(item with { Entry = item.Entry with { RelativeName = [], Ancestry = null } });
        return detached.ToImmutable();
    }
    public Task RecordIssueAsync(string code, ulong? node = null, MeasurementState state = MeasurementState.Failed, string? root = null, CancellationToken cancellation = default) =>
        _owned.RunAsync((owner, token) => { owner.Capture.Issue(code, node, state, root, token); owner.Capture.Publish(token); return true; }, cancellation);

    internal Task<AnalysisSourceFrontier> CreateSourceFrontierAsync(AnalysisEntry root, RootBinding binding, ulong path, CancellationToken cancellation) =>
        _owned.RunAsync((owner, _) => AnalysisSourceFrontier.Create(owner.Workspace, _io, _device, root, binding, path), cancellation);

    public async Task<AnalysisDataset> CompleteAsync(SessionIdentity identity, AnalysisPhase phase, NamespaceCoverage coverage, CancellationToken cancellation = default, AnalysisFreshness freshness = AnalysisFreshness.Observed)
    {
        var dataset = await _owned.UseAsync(async owner =>
        {
            var revision = await owner.Capture.CompleteAsync(owner.Workspace, _io, _device, identity, phase, coverage, cancellation, freshness).ConfigureAwait(false);
            try { return new AnalysisDataset(revision, _io, _device); }
            catch { await AnalysisIo.ReleaseScratchAsync(_io, _device, revision).ConfigureAwait(false); throw; }
        }).ConfigureAwait(false);
        try { await DisposeAsync().ConfigureAwait(false); return dataset; }
        catch { await dataset.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public ValueTask DisposeAsync() { Interlocked.Exchange(ref _stopTarget, null); return _owned.DisposeAsync(); }
    private sealed class Owner(AnalysisScratchWorkspace workspace, AnalysisObservationCapture capture) : IAnalysisScratchOwned
    {
        internal readonly AnalysisScratchWorkspace Workspace = workspace;
        internal readonly AnalysisObservationCapture Capture = capture;
        public void Dispose() { try { Capture.Dispose(); } finally { Workspace.Dispose(); } }
    }
}
