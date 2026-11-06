using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisObjectCharge(ulong Id, ulong ParentId, ulong RepresentativePathId, string? ObjectId,
    string? ComponentId, string? BasisId, AnalysisMeasurement Measurement, ulong ObservedAliases,
    bool Shared, bool IdentityUnverified, bool DirectorySelf);

public sealed partial class AnalysisDatasetLease
{
    /// <summary>Indexes unfiltered Unique file size/On disk charges. Attribution uses the entire captured alias set.</summary>
    public Task<AnalysisObjectChargeIndex> BuildObjectChargesAsync(AnalysisMetric metric, CancellationToken cancellation = default)
    {
        if (metric is not (AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)) throw new ArgumentOutOfRangeException(nameof(metric));
        return _owned.UseAsync(reader => AnalysisObjectChargeIndex.BuildAsync(reader, Descriptor, metric, _io, _device, cancellation));
    }
}

/// <summary>Immutable accounting evidence, without source bindings or operation authority.</summary>
public sealed partial class AnalysisObjectChargeIndex : IAsyncDisposable
{
    private static readonly SemaphoreSlim Builds = new(AnalysisLimits.ActiveScans, AnalysisLimits.ActiveScans);
    private readonly AnalysisOwnedDatasetIo<State> _owned;
    private readonly DeviceIoScheduler _io;
    private readonly string _device;
    public AnalysisDatasetDescriptor Descriptor { get; }
    public AnalysisMetric Metric { get; }
    public AnalysisTotals Totals { get; }
    private AnalysisObjectChargeIndex(State state, AnalysisDatasetDescriptor descriptor, DeviceIoScheduler io, string device)
    { Descriptor = descriptor; Metric = state.Metric; Totals = state.Totals(); _owned = new(state, io, device); _io = io; _device = device; }

    /// <summary>Folds charges into real directory rows and explicit shared/directory-self buckets.</summary>
    public Task<AnalysisDatasetHierarchy> BuildHierarchyAsync(CancellationToken cancellation = default) =>
        _owned.UseAsync(state => AnalysisDatasetHierarchy.BuildAsync(state.Graph, Descriptor, Metric, state.PlacementEntries(cancellation),
            Totals.CompatibleBasis, _io, _device, cancellation, state.Query));

    internal static async Task<AnalysisObjectChargeIndex> BuildAsync(AnalysisIndexedGraphReader source, AnalysisDatasetDescriptor descriptor,
        AnalysisMetric metric, DeviceIoScheduler io, string device, CancellationToken cancellation, AnalysisIndexedFilter? query = null)
    {
        await Builds.WaitAsync(cancellation).ConfigureAwait(false); State? state = null;
        try
        {
            state = await AnalysisIo.Run(io, device, _ => State.Create(source, metric, query), cancellation).ConfigureAwait(false);
            while (!state.Finished)
                await AnalysisIo.Run(io, device, token =>
                {
                    int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!state.Finished && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += state.Step(token); if (clock.ElapsedMilliseconds >= 20) break; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, device, token => { state.Seal(token); return true; }, cancellation).ConfigureAwait(false);
            using (var entries = state.Entries(cancellation).GetEnumerator())
            {
                var index = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, entries, cancellation).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { state.Index = index.Acquire(); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
            }
            await AnalysisIo.Run(io, device, _ => { state.Workspace!.Dispose(); state.Workspace = null; return true; }, cancellation).ConfigureAwait(false);
            var result = new AnalysisObjectChargeIndex(state, descriptor, io, device); state = null; return result;
        }
        finally
        {
            try { if (state is not null) await AnalysisIo.ReleaseScratchAsync(io, device, state).ConfigureAwait(false); }
            finally { Builds.Release(); }
        }
    }

    /// <summary>Bounded evidence rows at their original parent, ranked by known amount. Incompatible bases have no combined page.</summary>
    public Task<AnalysisDatasetPage<AnalysisObjectCharge>> ReadChargesAsync(ulong parent, ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) =>
        {
            if (parent > Descriptor.Paths || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(parent));
            if (!Totals.CompatibleBasis)
            {
                if (offset != 0) throw new ArgumentOutOfRangeException(nameof(offset));
                return new AnalysisDatasetPage<AnalysisObjectCharge>(0, 0, []);
            }
            var range = state.Index!.PrefixRange(Key(parent), token);
            if (offset > range.Count) throw new ArgumentOutOfRangeException(nameof(offset));
            var rows = ImmutableArray.CreateBuilder<AnalysisObjectCharge>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var row = state.Read(state.Index.Read(range.First + offset + (ulong)rows.Count, token).Value, token, out long cost);
                if (cost > AnalysisLimits.BatchBytes) throw Invalid(); if (cost > AnalysisLimits.BatchBytes - bytes) break;
                rows.Add(row); bytes += cost;
            }
            return new AnalysisDatasetPage<AnalysisObjectCharge>(offset, range.Count, rows.ToImmutable());
        }, cancellation);

    // Identity-unverified unique sizes are evidence, never asserted unique chart area.
    public bool ContributesChartArea(AnalysisObjectCharge charge) => Totals.CompatibleBasis && charge.Measurement.IsKnown &&
        !(Metric == AnalysisMetric.UniqueFileSize && charge.IdentityUnverified);

    /// <summary>Streams evidence without constructing a charge array for the captured dataset.</summary>
    public Task<AnalysisDatasetPage<AnalysisObjectCharge>> ReadAllChargesAsync(ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) =>
        {
            ulong total = Totals.CompatibleBasis ? state.Count : 0;
            if (offset > total || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(offset));
            var rows = ImmutableArray.CreateBuilder<AnalysisObjectCharge>(); long bytes = 0;
            while (offset + (ulong)rows.Count < total && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var row = state.Read(offset + (ulong)rows.Count + 1, token, out long cost);
                if (cost > AnalysisLimits.BatchBytes) throw Invalid(); if (cost > AnalysisLimits.BatchBytes - bytes) break;
                rows.Add(row); bytes += cost;
            }
            return new AnalysisDatasetPage<AnalysisObjectCharge>(offset, total, rows.ToImmutable());
        }, cancellation);
    public ValueTask DisposeAsync() => _owned.DisposeAsync();

    private enum Stage { Objects, Aliases, AliasAncestors, CommonAncestor, Facts, Emit, UnknownAliases, Directories, DirectoryOwner, MatchObjects, MatchFacts, MatchAliases, MatchAncestors, MatchEvaluate, Done }
    private sealed record Component(StorageFact Fact, ulong Row);
    private sealed record Row(ulong Id, ulong Parent, ulong Path, ulong Object, ulong Fact, ulong Aliases, AnalysisMeasurement Measurement, byte Flags);
    private sealed partial class State : IAnalysisScratchOwned
    {
        private readonly AnalysisIndexedGraphReader _graph;
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisSortedIndexReader? Index;
        internal readonly AnalysisMetric Metric;
        private AnalysisScratchVector? _writer;
        private AnalysisScratchVectorCheckpoint? _rows;
        private Stage _stage;
        private ulong _object = 1, _objectRow, _representativeObject, _currentPaths, _alias, _fact, _directoryAt, _count, _paths, _parent, _representative;
        private AnalysisIndexRange _aliases, _facts, _directories;
        private AnalysisObject? _current, _first;
        private AnalysisNode? _directory;
        private Component? _pendingDirectory;
        private byte[]? _identity;
        private string? _version;
        private bool _changed, _conflictingComponents, _directoryEmission;
        private uint _roots;
        private readonly Dictionary<string, Component> _components = new(StringComparer.Ordinal);
        private readonly HashSet<string>[] _bases;
        private long _componentBytes, _basisBytes;
        private readonly HashSet<ulong> _ancestors = [];
        private ulong _walk;
        private int _steps, _emit;
        private Component[] _selected = [];
        private AnalysisMeasurement? _override;
        private readonly Dictionary<MeasurementState, ulong> _missing = [];
        private UInt128 _known, _unverified;
        internal bool Finished => _stage == Stage.Done;
        internal ulong Count => _rows!.Count;
        internal AnalysisIndexedGraphReader Graph => _graph;
        private State(AnalysisIndexedGraphReader graph, AnalysisScratchWorkspace workspace, AnalysisScratchVector writer, AnalysisMetric metric)
        { _graph = graph; Workspace = workspace; _writer = writer; Metric = metric; _bases = graph.Header.Roots.Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToArray(); }
        internal static State Create(AnalysisIndexedGraphReader source, AnalysisMetric metric, AnalysisIndexedFilter? query)
        {
            var graph = source.Retain(); AnalysisScratchWorkspace? workspace = null; AnalysisScratchArena? arena = null; AnalysisScratchVector? writer = null;
            try
            {
                workspace = graph.BorrowWorkspace(); arena = workspace.CreateArena(); writer = new(arena); arena = null;
                return new(graph, workspace, writer, metric) { Query = query };
            }
            catch { try { writer?.Dispose(); arena?.Dispose(); } finally { try { workspace?.Dispose(); } finally { graph.Dispose(); } } throw; }
        }
        internal long Step(CancellationToken token)
        {
            switch (_stage)
            {
                case Stage.Objects:
                    if (_object > _graph.IndexCount(AnalysisIndexedGraphRevision.ObjectIdentities))
                    { if (_first is not null) PrepareEmission(); else StartDirectories(token); return 1; }
                    var entry = _graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIdentities, _object, token);
                    if (_identity is not null && !_identity.AsSpan().SequenceEqual(entry.Key))
                    { if (_first is not null) { PrepareEmission(); return entry.Key.Length; } Reset(); }
                    if (_identity is null) _groupFirst = _object;
                    _identity = entry.Key; _object++; _current = _graph.ReadObject(entry.Value, token);
                    _objectRow = entry.Value; _currentPaths = 0; _aliases = _graph.Aliases(_current.Id, token); _alias = 0; _stage = Stage.Aliases;
                    return 1024L + entry.Key.Length + (_current.VersionEvidence?.Length ?? 0) * 2L;
                case Stage.Aliases:
                    if (_alias == _aliases.Count)
                    {
                        if (Metric != AnalysisMetric.OnDisk || _currentPaths == 0) { _stage = Stage.Objects; return 1; }
                        _facts = _graph.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner(_current!.Id, null), cancellation: token);
                        _fact = 0; _stage = Stage.Facts; return 1;
                    }
                    var node = _graph.ReadPathRecord(_graph.ReadIndex(AnalysisIndexedGraphRevision.Aliases, _aliases.First + _alias++, token).Value, token);
                    if (node.Node is not { Kind: AnalysisKind.File, IncludedByPolicy: true }) return PathBytes(node.Node);
                    if (_first is null) { _first = _current; _representative = node.Node.Id; _representativeObject = _objectRow; }
                    _changed |= _first!.LogicalLength != _current!.LogicalLength;
                    if (_current.VersionEvidence is { } version) { if (_version is null) _version = version; else _changed |= _version != version; }
                    if (Query is null) _count = checked(_count + 1);
                    _paths = checked(_paths + 1); _currentPaths++; _roots |= 1U << node.RootIndex;
                    if (_paths == 1) _parent = node.Node.ParentId;
                    else if (_parent != 0 && _parent != node.Node.ParentId && _first.VerifiedIdentity is not null)
                    { _ancestors.Clear(); _ancestors.Add(0); _walk = node.Node.ParentId; _steps = 0; _stage = Stage.AliasAncestors; }
                    return PathBytes(node.Node);
                case Stage.AliasAncestors:
                    if (_walk == 0) { _steps = 0; _stage = Stage.CommonAncestor; return 1; }
                    if (++_steps > AnalysisLimits.Depth + 1 || !_ancestors.Add(_walk)) throw Invalid();
                    var ancestor = _graph.ReadPath(_walk, token); _walk = ancestor.ParentId; return PathBytes(ancestor);
                case Stage.CommonAncestor:
                    if (_ancestors.Contains(_parent)) { _stage = Stage.Aliases; return 1; }
                    if (++_steps > AnalysisLimits.Depth + 1) throw Invalid();
                    var common = _graph.ReadPath(_parent, token); _parent = common.ParentId; return PathBytes(common);
                case Stage.Facts:
                    if (_fact == _facts.Count) { _stage = Stage.Objects; return 1; }
                    ulong row = _graph.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, _facts.First + _fact++, token).Value;
                    var fact = _graph.ReadFact(row, token); AddComponent(new(fact, row)); return FactBytes(fact);
                case Stage.Emit:
                    if (_emit == EmissionCount)
                    {
                        if (Query is not null) { FinishFilteredEmission(); return 1; }
                        if (_directoryEmission) { Reset(); _stage = _pendingDirectory is null ? Stage.Directories : Stage.DirectoryOwner; }
                        else if (_first!.VerifiedIdentity is null) _stage = Stage.UnknownAliases;
                        else { Reset(); _stage = Stage.Objects; }
                        return 1;
                    }
                    Emit(token); return 128;
                case Stage.UnknownAliases:
                    if (_alias == _aliases.Count) { Reset(); _stage = Stage.Objects; return 1; }
                    var unknown = _graph.ReadPath(_graph.ReadIndex(AnalysisIndexedGraphRevision.Aliases, _aliases.First + _alias++, token).Value, token);
                    if (unknown is { Kind: AnalysisKind.File, IncludedByPolicy: true })
                    { _representative = unknown.Id; _parent = unknown.ParentId; _emit = 0; _stage = Stage.Emit; }
                    return PathBytes(unknown);
                case Stage.Directories:
                    if (_directoryAt == _directories.Count)
                    { if (_directory is not null) PrepareEmission(); else _stage = Stage.Done; return 1; }
                    ulong directoryRow = _graph.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, _directories.First + _directoryAt++, token).Value;
                    var directoryFact = _graph.ReadFact(directoryRow, token);
                    if (_directory?.Id != directoryFact.DirectoryId)
                    {
                        _pendingDirectory = new(directoryFact, directoryRow);
                        if (_directory is not null) PrepareEmission(); else _stage = Stage.DirectoryOwner;
                    }
                    else if (_directory!.IncludedByPolicy) AddComponent(new(directoryFact, directoryRow));
                    return FactBytes(directoryFact);
                case Stage.DirectoryOwner:
                    var pending = _pendingDirectory ?? throw Invalid(); _pendingDirectory = null;
                    _directory = _graph.ReadPath(pending.Fact.DirectoryId!.Value, token);
                    if (_directory.IncludedByPolicy)
                    {
                        _parent = _representative = _directory.Id;
                        int root = -1; for (int n = 0; n < _graph.Header.Roots.Length; n++) if (_graph.Header.Roots[n].Id == _directory.RootId) { root = n; break; }
                        if (root < 0) throw Invalid(); _roots = 1U << root; AddComponent(pending);
                    }
                    _stage = Stage.Directories; return PathBytes(_directory);
                case Stage.MatchObjects: case Stage.MatchFacts: case Stage.MatchAliases: case Stage.MatchAncestors: case Stage.MatchEvaluate:
                    return MatchStep(token);
                default: throw Invalid();
            }
        }
        private void StartDirectories(CancellationToken token)
        {
            Reset();
            if (Metric != AnalysisMetric.OnDisk) { _stage = Stage.Done; return; }
            _directories = _graph.Find(AnalysisIndexedGraphRevision.FactOwners, [1], prefix: true, cancellation: token);
            _stage = Stage.Directories;
        }
        private void AddComponent(Component component)
        {
            var fact = component.Fact;
            if (_components.TryGetValue(fact.ComponentId, out var old))
            {
                _conflictingComponents |= old.Fact.BasisId != fact.BasisId || old.Fact.Measurement != fact.Measurement || old.Fact.Method != fact.Method ||
                    !old.Fact.Includes.Order(StringComparer.Ordinal).SequenceEqual(fact.Includes.Order(StringComparer.Ordinal));
                return;
            }
            long cost = FactBytes(fact);
            if (_components.Count >= AnalysisLimits.Components || cost > AnalysisLimits.PendingBytes / 4 - _componentBytes)
                throw new IOException("The analysis charge component buffer limit was reached.");
            _components.Add(fact.ComponentId, component); _componentBytes += cost;
        }
        private int EmissionCount => _override is not null || Metric != AnalysisMetric.OnDisk ? 1 : _selected.Length;
        private void PrepareEmission()
        {
            _directoryEmission = _directory is not null;
            if (_directoryEmission && !_directory!.IncludedByPolicy) { Reset(); _stage = _pendingDirectory is null ? Stage.Directories : Stage.DirectoryOwner; return; }
            _override = _changed && Metric is (AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk) ? AnalysisMeasurement.Missing(MeasurementState.Changed) : null;
            if (Metric == AnalysisMetric.OnDisk && _override is null)
            {
                if (_conflictingComponents) throw new InvalidDataException("Conflicting component observations.");
                _selected = AnalysisAggregate.SelectComponents(_components.Values.Select(c => c.Fact).ToArray()).Select(f => _components[f.ComponentId]).ToArray();
                if (_selected.Length == 0) _override = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
            }
            if (Query is not null) { BeginMatching(); return; }
            BeginEmission();
        }
        private void BeginEmission()
        {
            if (Metric == AnalysisMetric.OnDisk && _override is null)
                foreach (var component in _selected)
                    for (int n = 0; n < _bases.Length; n++) if ((_roots & (1U << n)) != 0 && !_bases[n].Contains(component.Fact.BasisId))
                    {
                        long cost = 64L + component.Fact.BasisId.Length * 2L;
                        if (cost > AnalysisLimits.PendingBytes / 4 - _basisBytes) throw new IOException("The analysis charge measurement-basis buffer limit was reached.");
                        _bases[n].Add(component.Fact.BasisId); _basisBytes += cost;
                    }
            _emit = 0;
            if (Query is null && !_directoryEmission && _first!.VerifiedIdentity is null) { _alias = 0; _stage = Stage.UnknownAliases; }
            else _stage = Stage.Emit;
        }
        private void Emit(CancellationToken token)
        {
            bool logical = Metric is AnalysisMetric.FileSize or AnalysisMetric.Files;
            bool uncertain = !logical && (_directoryEmission ? _directory!.ObservedIdentity is null : _first!.VerifiedIdentity is null);
            var measurement = logical ? Metric == AnalysisMetric.Files ? AnalysisMeasurement.Known(1) : _matchObj!.LogicalLength :
                _override ?? (Metric == AnalysisMetric.UniqueFileSize ? _first!.LogicalLength : _selected[_emit].Fact.Measurement);
            ulong fact = _override is null && Metric == AnalysisMetric.OnDisk ? _selected[_emit].Row : 0;
            byte flags = (byte)((!_emitForPath && !_directoryEmission && !uncertain && _paths > 1 ? 1 : 0) | (uncertain ? 2 : 0) | (_directoryEmission ? 4 : 0));
            _writer!.Append(Encode(new(_writer.AcceptedCount + 1, _emitForPath ? _matchNode!.ParentId : _parent, _emitForPath ? _matchNode!.Id : _representative,
                _directoryEmission ? 0 : _emitForPath ? _matchObjRow : _representativeObject, fact,
                _directoryEmission ? 0 : _emitForPath || uncertain ? 1 : _paths, measurement, flags)), token); _emit++;
            if (measurement.IsKnown)
            {
                if (uncertain) _unverified = checked(_unverified + measurement.Value!.Value); else _known = checked(_known + measurement.Value!.Value);
                _ = checked(_known + _unverified);
            }
            else _missing[measurement.State] = checked(_missing.GetValueOrDefault(measurement.State) + 1);
        }
        private void Reset()
        {
            _identity = null; _first = _current = null; _version = null; _directory = null; _roots = 0; _paths = _parent = _representative = 0;
            _changed = _conflictingComponents = _directoryEmission = false; _components.Clear(); _componentBytes = 0; _selected = []; _override = null; _ancestors.Clear();
            // A directory fact read ahead remains owned until its separate DirectoryOwner turn.
            if (_pendingDirectory is not null) _stage = Stage.DirectoryOwner;
        }
        internal AnalysisTotals Totals()
        {
            var populated = _bases.Where(b => b.Count != 0).ToArray();
            bool compatible = Metric != AnalysisMetric.OnDisk || populated.Length < 2 || populated.Skip(1).All(b => b.SetEquals(populated[0]));
            return new(compatible ? _known : 0, compatible ? _unverified : 0, _count, _missing.ToImmutableDictionary(), compatible);
        }
        internal void Seal(CancellationToken token)
        { if (!Finished) throw Invalid(); _writer!.Flush(token); _rows = _writer.CaptureCommitted(); _writer.Dispose(); _writer = null; }
        internal IEnumerable<AnalysisSortInput> Entries(CancellationToken token)
        {
            for (ulong row = 1; row <= _rows!.Count; row++)
            {
                token.ThrowIfCancellationRequested(); var value = Decode(_rows.Read(row, token)); var key = new byte[33]; Key(value.Parent).CopyTo(key, 0);
                key[8] = value.Measurement.IsKnown ? (byte)0 : (byte)1;
                Write128(key.AsSpan(9), UInt128.MaxValue - (value.Measurement.Value ?? 0)); Key(value.Id).CopyTo(key, 25);
                yield return new(new(key, row), 128);
            }
        }
        internal IEnumerable<AnalysisSortInput> PlacementEntries(CancellationToken token)
        {
            for (ulong row = 1; row <= _rows!.Count; row++)
            {
                token.ThrowIfCancellationRequested(); var value = Decode(_rows.Read(row, token));
                ulong placement = (value.Flags & 5) != 0 ? value.Parent : value.Path;
                var key = new byte[50]; Key(placement).CopyTo(key, 0); Key(value.Id).CopyTo(key, 8);
                Key(value.Path).CopyTo(key, 16); Key(value.Aliases).CopyTo(key, 24); Write128(key.AsSpan(32), value.Measurement.Value ?? 0);
                key[48] = (byte)value.Measurement.State; key[49] = value.Flags;
                yield return new(new(key, value.Id), 128);
            }
        }
        internal AnalysisObjectCharge Read(ulong row, CancellationToken token, out long cost)
        {
            var value = Decode(_rows!.Read(row, token)); var obj = value.Object == 0 ? null : _graph.ReadObject(value.Object, token);
            var fact = value.Fact == 0 ? null : _graph.ReadFact(value.Fact, token);
            cost = 512L + (obj is null ? 0 : 512L + obj.Id.Length * 2L + (obj.VerifiedIdentity?.Length ?? 0) * 2L + (obj.VersionEvidence?.Length ?? 0) * 2L) +
                (fact is null ? 0 : FactBytes(fact));
            return new(value.Id, value.Parent, value.Path, obj?.Id, fact?.ComponentId, fact?.BasisId, value.Measurement, value.Aliases,
                (value.Flags & 1) != 0, (value.Flags & 2) != 0, (value.Flags & 4) != 0);
        }
        public void Dispose()
        {
            try { Index?.Dispose(); _writer?.Dispose(); _rows?.Dispose(); }
            finally { try { Workspace?.Dispose(); } finally { _graph.Dispose(); } }
        }
    }
    private static byte[] Key(ulong value) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); return bytes; }
    private static byte[] Encode(Row row)
    {
        var bytes = new byte[72]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x31434f41);
        ulong[] fields = [row.Id, row.Parent, row.Path, row.Object, row.Fact, row.Aliases];
        for (int n = 0; n < fields.Length; n++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4 + n * 8), fields[n]);
        Write128(bytes.AsSpan(52), row.Measurement.Value ?? 0); bytes[68] = (byte)row.Measurement.State; bytes[69] = row.Flags; return bytes;
    }
    private static Row Decode(byte[] bytes)
    {
        if (bytes.Length != 72 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x31434f41 || bytes[68] > 5 || bytes[69] > 7 || bytes[70] != 0 || bytes[71] != 0) throw Invalid();
        ulong[] fields = new ulong[6]; for (int n = 0; n < fields.Length; n++) fields[n] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4 + n * 8));
        UInt128 value = Read128(bytes.AsSpan(52)); var state = (MeasurementState)bytes[68];
        if (fields[0] == 0 || fields[1] > AnalysisLimits.Observations || fields[2] is < 1 or > AnalysisLimits.Observations || fields[3] > AnalysisLimits.Observations || fields[5] > AnalysisLimits.Observations || state != MeasurementState.Known && value != 0) throw Invalid();
        bool directory = (bytes[69] & 4) != 0, shared = (bytes[69] & 1) != 0, uncertain = (bytes[69] & 2) != 0;
        if (directory ? fields[3] != 0 || fields[5] != 0 || shared || fields[1] != fields[2] : fields[3] == 0 || fields[5] == 0 || shared != (fields[5] > 1) || uncertain && fields[5] != 1) throw Invalid();
        return new(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], state == MeasurementState.Known ? AnalysisMeasurement.Known(value) : AnalysisMeasurement.Missing(state), bytes[69]);
    }
    private static long PathBytes(AnalysisNode node) => 1024L + node.Name.Length * 2L + node.RawName.Length;
    private static long FactBytes(StorageFact fact) => 1024L + fact.Id.Length * 2L + (fact.ObjectId?.Length ?? 0) * 2L +
        fact.ComponentId.Length * 2L + fact.BasisId.Length * 2L + fact.Method.Length * 2L + fact.Includes.Sum(s => 32L + s.Length * 2L);
    private static void Write128(Span<byte> bytes, UInt128 value)
    { BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)(value >> 64)); BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], (ulong)value); }
    private static UInt128 Read128(ReadOnlySpan<byte> bytes) => ((UInt128)BinaryPrimitives.ReadUInt64BigEndian(bytes) << 64) | BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
    private static InvalidDataException Invalid() => new("Invalid object charge structure.");
}
