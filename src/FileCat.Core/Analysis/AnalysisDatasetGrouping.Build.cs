using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    private static readonly SemaphoreSlim Builds = new(AnalysisLimits.ActiveScans, AnalysisLimits.ActiveScans);
    internal static async Task<AnalysisDatasetGrouping> BuildAsync(AnalysisObjectChargeIndex.State source, AnalysisDatasetDescriptor descriptor,
        AnalysisMetric metric, AnalysisTotals totals, AnalysisGroupingDimension dimension, DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation)
    {
        await Builds.WaitAsync(cancellation).ConfigureAwait(false); State? state = null;
        try
        {
            if (source.Graph.Header.Identity != descriptor.Identity) throw Invalid();
            descriptor = descriptor with { Taint = AnalysisPrivacy.Join(descriptor.Taint, source.Graph.Header.Taint) };
            state = await AnalysisIo.Run(io, device, _ => State.Create(source.Graph, metric, totals, dimension), cancellation).ConfigureAwait(false);
            while (!state.Emitted)
                await AnalysisIo.Run(io, device, token => Batch(() => state.EmitStep(source, token), () => state.Emitted, token), cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, device, token => { state.SealEvents(token); return true; }, cancellation).ConfigureAwait(false);
            for (int kind = 0; kind < 3; kind++)
            {
                using var entries = state.EventEntries(kind, cancellation).GetEnumerator();
                var index = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, entries, cancellation).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { state.SetEventIndex(kind, index.Acquire()); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
            }
            while (!state.Folded)
                await AnalysisIo.Run(io, device, token => Batch(() => state.FoldStep(token), () => state.Folded, token), cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, device, token => { state.SealSummaries(token); return true; }, cancellation).ConfigureAwait(false);
            for (int kind = 0; kind < 2; kind++)
            {
                using var entries = state.SummaryEntries(kind, cancellation).GetEnumerator();
                var index = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, entries, cancellation, maximumRecords: kind == 0 ? AnalysisLimits.GroupRows : AnalysisLimits.GroupOrderRows).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { if (kind == 0) state.Lookup = index.Acquire(); else state.Order = index.Acquire(); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
            }
            for (int kind = 0; kind < 2; kind++)
            {
                using var entries = state.PlacementEntries(kind != 0, cancellation).GetEnumerator();
                var index = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, entries, cancellation, maximumRecords: AnalysisLimits.GroupOrderRows).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { if (kind == 0) state.MemberOrder = index.Acquire(); else state.ReferenceOrder = index.Acquire(); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
            }
            await AnalysisIo.Run(io, device, _ => { state.Workspace!.Dispose(); state.Workspace = null; return true; }, cancellation).ConfigureAwait(false);
            var result = new AnalysisDatasetGrouping(state, descriptor, io, device); state = null; return result;
        }
        finally
        { try { if (state is not null) await AnalysisIo.ReleaseScratchAsync(io, device, state).ConfigureAwait(false); } finally { Builds.Release(); } }
    }
    private static bool Batch(Func<long> step, Func<bool> finished, CancellationToken token)
    {
        int records = 0; long bytes = 0; var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!finished() && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
        { token.ThrowIfCancellationRequested(); bytes += step(); if (timer.ElapsedMilliseconds >= 20) break; }
        return true;
    }
    private enum BuildStage { Paths, Member, Charges, SinglePath, SingleClassify, AliasObjects, AliasPaths, ClassifyAlias, DirectoryReference, EmitCharge, Done }
    private sealed partial class State : IAnalysisScratchOwned
    {
        internal readonly AnalysisIndexedGraphReader Graph;
        internal AnalysisScratchWorkspace? Workspace;
        internal readonly AnalysisMetric Metric;
        internal readonly AnalysisGroupingDimension Dimension;
        internal readonly AnalysisTotals Totals;
        internal AnalysisScratchVectorCheckpoint? Members, Charges, References, Summaries, MemberPlacements, ReferencePlacements;
        internal AnalysisSortedIndexReader? MemberIndex, ChargeIndex, ReferenceIndex, Lookup, Order, MemberOrder, ReferenceOrder;
        private AnalysisScratchVector? _memberWriter, _chargeWriter, _referenceWriter, _summaryWriter, _memberPlacementWriter, _referencePlacementWriter;
        private BuildStage _stage;
        private ulong _path = 1, _charge = 1, _objectAt, _aliasAt, _seenAliases;
        private AnalysisIndexRange _objects, _aliases;
        private AnalysisNode? _pendingPath;
        private AnalysisObject? _aliasObject;
        private AnalysisObjectCharge? _current;
        private AnalysisGroupDefinition? _definition, _first, _cachedDefinition;
        private AnalysisGroupKind _kind, _cachedKind;
        private bool _cross, _referencesPass, _cachedShared, _cachedDirectory;
        private string? _cachedObject;
        private ulong _cachedPath;
        internal bool Emitted => _stage == BuildStage.Done;
        private State(AnalysisIndexedGraphReader graph, AnalysisScratchWorkspace workspace, AnalysisMetric metric, AnalysisTotals totals, AnalysisGroupingDimension dimension)
        { Graph = graph; Workspace = workspace; Metric = metric; Totals = totals; Dimension = dimension; }
        internal static State Create(AnalysisIndexedGraphReader source, AnalysisMetric metric, AnalysisTotals totals, AnalysisGroupingDimension dimension)
        {
            AnalysisIndexedGraphReader? graph = null; AnalysisScratchWorkspace? workspace = null; State? state = null;
            try
            {
                graph = source.Retain(); workspace = graph.BorrowWorkspace(); state = new(graph, workspace, metric, totals, dimension); graph = null; workspace = null;
                state._memberWriter = CreateVector(state.Workspace!); state._chargeWriter = CreateVector(state.Workspace!);
                state._referenceWriter = CreateVector(state.Workspace!); state._summaryWriter = CreateVector(state.Workspace!, AnalysisLimits.GroupRows);
                state._memberPlacementWriter = CreateVector(state.Workspace!); state._referencePlacementWriter = CreateVector(state.Workspace!); return state;
            }
            catch
            {
                Exception? error = null; Close(state, ref error); Close(workspace, ref error); Close(graph, ref error);
                if (error is not null) ExceptionDispatchInfo.Capture(error).Throw(); throw;
            }
        }
        internal AnalysisObject? Object(string? id, CancellationToken token)
        {
            if (id is null) return null; var range = Graph.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
            if (range.Count != 1) throw Invalid(); return Graph.ReadObject(Graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token);
        }
        private AnalysisGroupDefinition Classify(AnalysisNode node, AnalysisObject? obj) => AnalysisGroupingRules.Classify(Dimension, node, obj, Graph.Header.Identity, Graph.Header.Roots);
        private static AnalysisScratchVector CreateVector(AnalysisScratchWorkspace workspace, int records = AnalysisLimits.Observations)
        {
            var arena = workspace.CreateArena();
            try { return new(arena, records); }
            catch (Exception creation)
            { try { arena.Dispose(); } catch (Exception close) { throw new AggregateException(creation, close); } throw; }
        }
        internal long EmitStep(AnalysisObjectChargeIndex.State source, CancellationToken token)
        {
            switch (_stage)
            {
                case BuildStage.Paths:
                    if (_path > Graph.Count(AnalysisIndexedGraph.Paths)) { _stage = BuildStage.Charges; return 1; }
                    _pendingPath = Graph.ReadPathRow(_path++, token);
                    long pathBytes = AnalysisIndexedFilter.Cost(_pendingPath);
                    if (_pendingPath.Kind == AnalysisKind.File && _pendingPath.IncludedByPolicy && (source.Query is null || source.Query.Matches(_pendingPath.Id, token))) _stage = BuildStage.Member;
                    else _pendingPath = null;
                    return pathBytes;
                case BuildStage.Member:
                    var memberObject = Object(_pendingPath!.ObjectId, token); var memberDefinition = Classify(_pendingPath, memberObject);
                    var member = new Event(memberDefinition, AnalysisGroupKind.Entries, EventKind.Member, _pendingPath.Id,
                        memberObject?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported),
                        SharedElsewhere: source.Query?.IsContext(_pendingPath.Id, token) == true);
                    var memberBytes = EncodeEvent(member); _memberWriter!.Append(memberBytes, token); _pendingPath = null; _stage = BuildStage.Paths;
                    return 1024L + memberBytes.Length * 3L;
                case BuildStage.Charges:
                    if (_charge > source.Count) { _stage = BuildStage.Done; return 1; }
                    _current = source.Read(_charge++, token, out long chargeBytes);
                    if (!Totals.CompatibleBasis && _current.Measurement.IsKnown) return chargeBytes;
                    ulong path = _current.DirectorySelf ? _current.ParentId : _current.Shared ? 0 : _current.RepresentativePathId;
                    if (_cachedDefinition is not null && _cachedObject == _current.ObjectId && _cachedShared == _current.Shared && _cachedDirectory == _current.DirectorySelf && _cachedPath == path)
                    { _definition = _cachedDefinition; _kind = _cachedKind; _stage = BuildStage.EmitCharge; return chargeBytes; }
                    if (_current.DirectorySelf)
                    { _definition = AnalysisGroupingRules.Directory; _kind = AnalysisGroupKind.DirectoryAllocation; _stage = BuildStage.DirectoryReference; }
                    else if (!_current.Shared) _stage = BuildStage.SinglePath;
                    else
                    {
                        var firstObject = Object(_current.ObjectId, token) ?? throw Invalid();
                        _objects = Graph.Find(AnalysisIndexedGraphRevision.ObjectIdentities, AnalysisGraphKeys.ObjectIdentity(firstObject), cancellation: token);
                        _objectAt = _aliasAt = _seenAliases = 0; _first = null; _cross = _referencesPass = false; _aliasObject = null; _stage = BuildStage.AliasObjects;
                    }
                    return chargeBytes;
                case BuildStage.SinglePath:
                    _pendingPath = Graph.ReadPath(_current!.RepresentativePathId, token); _stage = BuildStage.SingleClassify; return AnalysisIndexedFilter.Cost(_pendingPath);
                case BuildStage.SingleClassify:
                    var observed = Dimension == AnalysisGroupingDimension.ModifiedAge && _pendingPath!.Times is null ? Object(_pendingPath.ObjectId, token) : null;
                    _definition = Classify(_pendingPath!, observed); _kind = AnalysisGroupKind.Entries; _pendingPath = null; _stage = BuildStage.EmitCharge;
                    return 1024L + _definition.Value.Length * 2L;
                case BuildStage.AliasObjects:
                    if (_objectAt == _objects.Count)
                    {
                        if (!_referencesPass)
                        {
                            if (_seenAliases != _current!.ObservedAliases || _first is null) throw Invalid();
                            _definition = _cross ? AnalysisGroupingRules.Shared : _first; _kind = _cross ? AnalysisGroupKind.SharedAcrossGroups : AnalysisGroupKind.Entries;
                            if (_cross) { _objectAt = _aliasAt = 0; _referencesPass = true; _aliasObject = null; return 1; }
                        }
                        _aliasObject = null; _stage = BuildStage.EmitCharge; return 1;
                    }
                    _aliasObject = Graph.ReadObject(Graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIdentities, _objects.First + _objectAt++, token).Value, token);
                    _aliases = Graph.Aliases(_aliasObject.Id, token); _aliasAt = 0; _stage = BuildStage.AliasPaths;
                    return 1024L + _aliasObject.Id.Length * 2L + (_aliasObject.VersionEvidence?.Length ?? 0) * 2L;
                case BuildStage.AliasPaths:
                    if (_aliasAt == _aliases.Count) { _stage = BuildStage.AliasObjects; return 1; }
                    _pendingPath = Graph.ReadPathRow(Graph.ReadIndex(AnalysisIndexedGraphRevision.Aliases, _aliases.First + _aliasAt++, token).Value, token);
                    long bytes = AnalysisIndexedFilter.Cost(_pendingPath);
                    if (_pendingPath.Kind != AnalysisKind.File || !_pendingPath.IncludedByPolicy) { _pendingPath = null; return bytes; }
                    if (_referencesPass)
                    {
                        if (source.Query is null || source.Query.Matches(_pendingPath.Id, token))
                            _referenceWriter!.Append(EncodeEvent(new(_definition!, _kind, EventKind.Reference, _pendingPath.Id,
                                _aliasObject?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported))), token);
                        _pendingPath = null;
                    }
                    else _stage = BuildStage.ClassifyAlias;
                    return bytes;
                case BuildStage.ClassifyAlias:
                    var classified = Classify(_pendingPath!, _aliasObject); _seenAliases++;
                    if (_first is null) _first = classified; else if (_first.Value != classified.Value) _cross = true;
                    _pendingPath = null; _stage = BuildStage.AliasPaths; return 1024L + classified.Value.Length * 2L;
                case BuildStage.DirectoryReference:
                    Graph.RequireParent(_current!.ParentId, token);
                    _referenceWriter!.Append(EncodeEvent(new(_definition!, _kind, EventKind.Reference, _current.ParentId, AnalysisMeasurement.Missing(MeasurementState.Unsupported))), token);
                    _stage = BuildStage.EmitCharge; return 1024;
                case BuildStage.EmitCharge:
                    ulong owner = _current!.DirectorySelf ? _current.ParentId : _current.Shared ? 0 : _current.RepresentativePathId;
                    var encoded = EncodeEvent(new(_definition!, _kind, EventKind.Charge, owner, AnalysisMeasurement.Known(0), _current)); _chargeWriter!.Append(encoded, token);
                    _cachedObject = _current.ObjectId; _cachedPath = owner; _cachedShared = _current.Shared; _cachedDirectory = _current.DirectorySelf;
                    _cachedDefinition = _definition; _cachedKind = _kind; _current = null; _definition = null; _stage = BuildStage.Charges;
                    return 1024L + encoded.Length * 3L;
                default: throw Invalid();
            }
        }
        internal void SealEvents(CancellationToken token)
        {
            if (!Emitted) throw Invalid();
            _memberWriter!.Flush(token); Members = _memberWriter.CaptureCommitted(); _memberWriter.Dispose(); _memberWriter = null;
            _chargeWriter!.Flush(token); Charges = _chargeWriter.CaptureCommitted(); _chargeWriter.Dispose(); _chargeWriter = null;
            _referenceWriter!.Flush(token); References = _referenceWriter.CaptureCommitted(); _referenceWriter.Dispose(); _referenceWriter = null;
            _cachedDefinition = _definition = _first = null; _cachedObject = null; _aliasObject = null; _pendingPath = null; _current = null;
        }
        internal IEnumerable<AnalysisSortInput> EventEntries(int kind, CancellationToken token)
        {
            var records = kind == 0 ? Members! : kind == 1 ? Charges! : References!;
            for (ulong id = 1; id <= records.Count; id++)
            {
                token.ThrowIfCancellationRequested(); var data = records.Read(id, token); var item = DecodeEvent(data);
                yield return new(new(Join(Hash(item.Definition.Key), Number(item.Path), kind == 1 ? Number(item.Charge!.Id) : []), id), checked(data.Length * 3 + 1024));
            }
        }
        internal void SetEventIndex(int kind, AnalysisSortedIndexReader index)
        { if (kind == 0) MemberIndex = index; else if (kind == 1) ChargeIndex = index; else ReferenceIndex = index; }
        internal AnalysisDatasetGroup Group(string key, CancellationToken token)
        {
            var range = Lookup!.FindRange(Hash(key), token); if (range.Count != 1) throw new KeyNotFoundException("This captured group is absent.");
            var result = DecodeSummary(Summaries!.Read(Lookup.Read(range.First, token).Value, token)); if (result.Definition.Key != key) throw Invalid(); return result;
        }
        public void Dispose()
        {
            Exception? error = null;
            foreach (var item in new IDisposable?[] { ReferenceOrder, MemberOrder, Order, Lookup, ReferenceIndex, ChargeIndex, MemberIndex,
                ReferencePlacements, MemberPlacements, Summaries, References, Charges, Members,
                _referencePlacementWriter, _memberPlacementWriter, _summaryWriter, _referenceWriter, _chargeWriter, _memberWriter, Workspace, Graph }) Close(item, ref error);
            if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        }
        private static void Close(IDisposable? item, ref Exception? error)
        { try { item?.Dispose(); } catch (Exception failure) { error = error is null ? failure : new AggregateException(error, failure); } }
    }
}
