using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisDatasetSummary(SessionIdentity Identity, AnalysisMetric Metric, AnalysisTotals Totals, AnalysisTaint Taint);

public sealed partial class AnalysisDatasetLease
{
    /// <summary>Exact unfiltered totals over the whole captured dataset, without a path or alias array.</summary>
    public Task<AnalysisDatasetSummary> SummarizeAsync(AnalysisMetric metric, CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        return _owned.UseAsync(reader => AnalysisDatasetSummarizer.RunAsync(reader, metric, _io, _device, cancellation));
    }
}

/// <summary>One bounded state machine. Every native read occurs inside a captured Background turn.</summary>
internal sealed class AnalysisDatasetSummarizer
{
    // Component buffers live across I/O turns. Limit actual buffer owners globally, rather than
    // assuming the eight per-turn I/O slots also bound suspended compound operations.
    private static readonly SemaphoreSlim Summaries = new(AnalysisLimits.ActiveScans, AnalysisLimits.ActiveScans);
    private enum Stage { Paths, Objects, Aliases, Facts, Directories, DirectoryOwner, Done }
    private readonly AnalysisIndexedGraphReader _reader;
    private readonly AnalysisMetric _metric;
    private Stage _stage;
    private ulong _path = 1, _object = 1, _alias, _fact, _directoryAt;
    private AnalysisIndexRange _aliases, _facts, _directories;
    private AnalysisObject? _current, _first;
    private byte[]? _identity;
    private string? _version;
    private ulong _currentPaths, _groupPaths;
    private uint _currentRoots, _groupRoots;
    private bool _changed, _conflictingComponents;
    private readonly Dictionary<string, StorageFact> _components = new(StringComparer.Ordinal);
    private AnalysisNode? _directory;
    private StorageFact? _pendingDirectoryFact;
    private readonly Dictionary<MeasurementState, ulong> _missing = [];
    private readonly HashSet<string>[] _bases;
    private int _basisEntries;
    private UInt128 _known, _unverified;
    private ulong _count;

    private AnalysisDatasetSummarizer(AnalysisIndexedGraphReader reader, AnalysisMetric metric)
    { _reader = reader; _metric = metric; _bases = reader.Header.Roots.Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToArray(); }

    internal static async Task<AnalysisDatasetSummary> RunAsync(AnalysisIndexedGraphReader reader, AnalysisMetric metric,
        DeviceIoScheduler io, string device, CancellationToken cancellation)
    {
        await Summaries.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await ComputeAsync(reader, metric, io, device, cancellation).ConfigureAwait(false); }
        finally { Summaries.Release(); }
    }
    private static async Task<AnalysisDatasetSummary> ComputeAsync(AnalysisIndexedGraphReader reader, AnalysisMetric metric,
        DeviceIoScheduler io, string device, CancellationToken cancellation)
    {
        var state = new AnalysisDatasetSummarizer(reader, metric);
        while (state._stage != Stage.Done)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                while (state._stage != Stage.Done && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); bytes += state.Step(token);
                    if (clock.ElapsedMilliseconds >= 20) break;
                }
                return true;
            }, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        var populated = state._bases.Where(b => b.Count != 0).ToArray();
        bool compatible = metric != AnalysisMetric.OnDisk || populated.Length < 2 || populated.Skip(1).All(b => b.SetEquals(populated[0]));
        return new(reader.Header.Identity, metric,
            new(compatible ? state._known : 0, compatible ? state._unverified : 0, state._count, state._missing.ToImmutableDictionary(), compatible), reader.Header.Taint);
    }

    private long Step(CancellationToken token)
    {
        switch (_stage)
        {
            case Stage.Paths:
                if (_path > _reader.Count(AnalysisIndexedGraph.Paths))
                { _stage = _metric is AnalysisMetric.Files or AnalysisMetric.FileSize ? Stage.Done : Stage.Objects; return 1; }
                var path = _reader.ReadPath(_path++, token);
                if (path.Kind == AnalysisKind.File && path.IncludedByPolicy)
                {
                    _count = checked(_count + 1);
                    if (_metric == AnalysisMetric.Files) Add(1, 1, false);
                    else if (_metric == AnalysisMetric.FileSize)
                    {
                        var obj = path.ObjectId is { } id ? Object(id, token) : null;
                        Measure(obj?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported), 1, false);
                    }
                    else if (path.ObjectId is null) Missing(MeasurementState.Unsupported, 1);
                }
                return 1024L + path.Name.Length * 2L + path.RawName.Length;
            case Stage.Objects:
                if (_object > _reader.IndexCount(AnalysisIndexedGraphRevision.ObjectIdentities))
                {
                    FinishGroup();
                    if (_metric != AnalysisMetric.OnDisk) _stage = Stage.Done;
                    else { _directories = _reader.Find(AnalysisIndexedGraphRevision.FactOwners, [1], prefix: true, cancellation: token); _directoryAt = 0; _stage = Stage.Directories; }
                    return 1;
                }
                var entry = _reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIdentities, _object++, token);
                if (_identity is null || !_identity.AsSpan().SequenceEqual(entry.Key)) { FinishGroup(); _identity = entry.Key; }
                _current = _reader.ReadObject(entry.Value, token); _currentPaths = 0; _currentRoots = 0;
                _aliases = _reader.Aliases(_current.Id, token); _alias = 0; _stage = Stage.Aliases;
                return 1024L + entry.Key.Length + (_current.VersionEvidence?.Length ?? 0) * 2L;
            case Stage.Aliases:
                if (_alias < _aliases.Count)
                {
                    var alias = _reader.ReadIndex(AnalysisIndexedGraphRevision.Aliases, _aliases.First + _alias++, token);
                    var member = _reader.ReadPathRecord(alias.Value, token);
                    if (member.Node.Kind == AnalysisKind.File && member.Node.IncludedByPolicy)
                    { _currentPaths = checked(_currentPaths + 1); _currentRoots |= 1U << member.RootIndex; }
                    return 1024L + member.Node.Name.Length * 2L + member.Node.RawName.Length;
                }
                if (_currentPaths == 0) { _stage = Stage.Objects; return 1; }
                if (_first is null) _first = _current;
                else _changed |= _first.LogicalLength != _current!.LogicalLength;
                if (_current!.VersionEvidence is { } version)
                { if (_version is null) _version = version; else _changed |= _version != version; }
                _groupPaths = checked(_groupPaths + _currentPaths); _groupRoots |= _currentRoots;
                if (_metric != AnalysisMetric.OnDisk) { _stage = Stage.Objects; return 1; }
                _facts = _reader.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner(_current!.Id, null), cancellation: token);
                _fact = 0; _stage = Stage.Facts; return 1;
            case Stage.Facts:
                if (_fact == _facts.Count) { _stage = Stage.Objects; return 1; }
                var fact = _reader.ReadFact(_reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, _facts.First + _fact++, token).Value, token);
                Component(fact); return FactBytes(fact);
            case Stage.Directories:
                if (_directoryAt == _directories.Count) { FinishDirectory(); _stage = Stage.Done; return 1; }
                var directoryFact = _reader.ReadFact(_reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, _directories.First + _directoryAt++, token).Value, token);
                long cost = FactBytes(directoryFact);
                if (_directory?.Id != directoryFact.DirectoryId)
                {
                    FinishDirectory(); _pendingDirectoryFact = directoryFact; _stage = Stage.DirectoryOwner; return cost;
                }
                if (_directory!.IncludedByPolicy) Component(directoryFact);
                return cost;
            case Stage.DirectoryOwner:
                var pending = _pendingDirectoryFact ?? throw AnalysisIndexedGraph.Invalid();
                _directory = _reader.ReadPath(pending.DirectoryId!.Value, token);
                if (_directory.IncludedByPolicy) Component(pending);
                _pendingDirectoryFact = null; _stage = Stage.Directories;
                return 1024L + _directory.Name.Length * 2L + _directory.RawName.Length;
            default: throw new InvalidOperationException("Unknown analysis summary stage.");
        }
    }

    private AnalysisObject? Object(string id, CancellationToken token)
    {
        var range = _reader.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
        return range.Count == 0 ? null : range.Count == 1 ? _reader.ReadObject(_reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token) : throw AnalysisIndexedGraph.Invalid();
    }
    private void Component(StorageFact fact)
    {
        if (_components.TryGetValue(fact.ComponentId, out var old))
            _conflictingComponents |= old.BasisId != fact.BasisId || old.Measurement != fact.Measurement || old.Method != fact.Method ||
                !old.Includes.Order(StringComparer.Ordinal).SequenceEqual(fact.Includes.Order(StringComparer.Ordinal));
        else
        {
            if (_components.Count >= AnalysisLimits.Components) throw new IOException("The analysis summary component limit was reached.");
            _components.Add(fact.ComponentId, fact);
        }
    }
    private void FinishGroup()
    {
        if (_first is not null)
        {
            bool uncertain = _first.VerifiedIdentity is null; ulong factor = uncertain ? _groupPaths : 1;
            if (_changed) Missing(MeasurementState.Changed, factor);
            else if (_metric == AnalysisMetric.UniqueFileSize) Measure(_first.LogicalLength, factor, uncertain);
            else Components(factor, uncertain, _groupRoots);
        }
        _first = null; _version = null; _components.Clear(); _groupPaths = 0; _groupRoots = 0; _changed = _conflictingComponents = false;
    }
    private void FinishDirectory()
    {
        if (_directory is { IncludedByPolicy: true } directory)
        {
            int root = -1;
            for (int n = 0; n < _reader.Header.Roots.Length; n++) if (_reader.Header.Roots[n].Id == directory.RootId) { root = n; break; }
            if (root < 0) throw AnalysisIndexedGraph.Invalid();
            Components(1, directory.ObservedIdentity is null, 1U << root);
        }
        _directory = null; _components.Clear(); _conflictingComponents = false;
    }
    private void Components(ulong factor, bool uncertain, uint roots)
    {
        if (_conflictingComponents) throw new InvalidDataException("Conflicting component observations.");
        var components = AnalysisAggregate.SelectComponents(_components.Values.ToArray());
        if (components.Count == 0) Missing(MeasurementState.Unsupported, factor);
        foreach (var fact in components)
        {
            for (int n = 0; n < _bases.Length; n++)
                if ((roots & (1U << n)) != 0 && !_bases[n].Contains(fact.BasisId))
                {
                    if (_basisEntries >= AnalysisLimits.Roots * AnalysisLimits.Components) throw new IOException("The analysis summary measurement-basis limit was reached.");
                    _bases[n].Add(fact.BasisId); _basisEntries++;
                }
            Measure(fact.Measurement, factor, uncertain);
        }
    }
    private void Measure(AnalysisMeasurement measurement, ulong factor, bool uncertain)
    { if (measurement.IsKnown) Add(measurement.Value!.Value, factor, uncertain); else Missing(measurement.State, factor); }
    private void Missing(MeasurementState state, ulong count) => _missing[state] = checked(_missing.GetValueOrDefault(state) + count);
    private void Add(UInt128 value, ulong factor, bool uncertain)
    {
        UInt128 total = checked(value * factor);
        if (uncertain) _unverified = checked(_unverified + total); else _known = checked(_known + total);
        _ = checked(_known + _unverified);
    }
    private static long FactBytes(StorageFact fact) => 1024L + fact.Includes.Sum(s => 32L + s.Length * 2L);
}
