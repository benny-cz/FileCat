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
    private static long s_pendingBytes;
    private readonly AnalysisOwnedDatasetIo<Owner> _owned;
    private readonly DeviceIoScheduler _io; private readonly string _device;
    private AnalysisObservationCapture? _stopTarget;
    private AnalysisObservationWriter(Owner owner, DeviceIoScheduler io, string device)
    { _io = io; _device = device; _owned = new(owner, io, device); _stopTarget = owner.Capture; }
    public void RequestStop() => Volatile.Read(ref _stopTarget)?.RequestStop();

    // The factory is application-owned configuration, not a callback restored from an inventory.
    public static async Task<AnalysisObservationWriter> CreateAsync(DeviceIoScheduler io, string device, Func<AnalysisPrivateDirectory> createDirectory,
        SessionIdentity acquisition, ImmutableArray<RootBinding> roots, ScanPolicy policy, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(createDirectory);
        var header = new AnalysisIndexedGraphHeader(acquisition, roots, policy, AnalysisPhase.Scanning, NamespaceCoverage.Partial); header.Validate();
        var owner = await AnalysisIo.Run(io, device, _ =>
        {
            var workspace = new AnalysisScratchWorkspace(createDirectory());
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
        if (observations.IsDefault || observations.Length is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(observations));
        long bytes = 0;
        foreach (var item in observations)
        {
            if (item is null || item.Entry is not { } entry || entry.Name is null || item.RootId is null || item.Basis is null) throw new ArgumentException("A captured batch row is incomplete.", nameof(observations));
            bytes = checked(bytes + 512L + entry.Name.Length * 2L + entry.RawName.Length + (entry.Identity?.Length ?? 0) * 2L + (entry.Version?.Length ?? 0) * 2L + item.RootId.Length * 2L + item.Basis.Length * 2L);
            if (bytes > AnalysisLimits.BatchBytes) throw new ArgumentException("The captured batch exceeds its byte limit. Submit a smaller batch.", nameof(observations));
        }
        Reserve(bytes);
        try
        {
            cancellation.ThrowIfCancellationRequested(); var detached = ImmutableArray.CreateBuilder<AnalysisObservationItem>(observations.Length);
            // A queued store write must not retain a live backend's ancestry chain/relative locator.
            foreach (var item in observations) detached.Add(item with { Entry = item.Entry with { RelativeName = [], Ancestry = null } });
            var batch = detached.ToImmutable();
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
        finally { Interlocked.Add(ref s_pendingBytes, -bytes); }
    }
    private static void Reserve(long bytes)
    {
        while (true)
        {
            long observed = Volatile.Read(ref s_pendingBytes);
            if (bytes > AnalysisLimits.PendingBytes - observed) throw new IOException("The shared pending observation payload limit was reached.");
            if (Interlocked.CompareExchange(ref s_pendingBytes, observed + bytes, observed) == observed) return;
        }
    }
    public Task RecordIssueAsync(string code, ulong? node = null, MeasurementState state = MeasurementState.Failed, string? root = null, CancellationToken cancellation = default) =>
        _owned.RunAsync((owner, token) => { owner.Capture.Issue(code, node, state, root, token); owner.Capture.Publish(token); return true; }, cancellation);

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
