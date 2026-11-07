namespace FileCat.Core.Analysis;

public sealed partial class AnalysisObjectChargeIndex
{
    internal sealed partial class State
    {
        internal AnalysisIndexedFilter? Query;
        private ulong _groupFirst, _matchObject, _matchObjRow, _matchAlias, _matchFact;
        private AnalysisIndexRange _matchAliases, _matchFacts;
        private AnalysisObject? _matchObj;
        private AnalysisNode? _matchNode;
        private AnalysisMeasurement _queryAllocation;
        private readonly Dictionary<string, StorageFact> _queryFacts = new(StringComparer.Ordinal);
        private long _queryFactBytes;
        private bool _eligible, _contextPass, _emitForPath;
        private void BeginMatching()
        {
            _eligible = _contextPass = _emitForPath = false; _matchObj = null; _matchNode = null; _queryFacts.Clear(); _queryFactBytes = 0;
            if (_directoryEmission)
            {
                if (!Query!.NamespaceOnly) { FinishQueryGroup(); return; }
                _matchNode = _directory; Query.Begin(_directory!); _stage = Stage.MatchAncestors; return;
            }
            _matchObject = _groupFirst; _stage = Stage.MatchObjects;
        }
        private long MatchStep(CancellationToken token)
        {
            var query = Query ?? throw Invalid();
            switch (_stage)
            {
                case Stage.MatchObjects:
                    if (_matchObject == _object)
                    {
                        if (_contextPass) BeginEmission();
                        else if (!_eligible || Metric is AnalysisMetric.Files or AnalysisMetric.FileSize || _first!.VerifiedIdentity is null) FinishQueryGroup();
                        else if (_paths > 1) { _contextPass = true; _matchObject = _groupFirst; }
                        else BeginEmission();
                        return 1;
                    }
                    var entry = _graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIdentities, _matchObject++, token);
                    _matchObjRow = entry.Value; _matchObj = _graph.ReadObject(entry.Value, token); _matchAliases = _graph.Aliases(_matchObj.Id, token); _matchAlias = 0;
                    _queryFacts.Clear(); _queryFactBytes = 0; _queryAllocation = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                    if (!_contextPass && query.NeedsAllocation)
                    {
                        _matchFacts = _graph.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner(_matchObj.Id, null), cancellation: token);
                        _matchFact = 0; _stage = Stage.MatchFacts;
                    }
                    else _stage = Stage.MatchAliases;
                    return 1024L + (_matchObj.VerifiedIdentity?.Length ?? 0) * 2L + (_matchObj.VersionEvidence?.Length ?? 0) * 2L;
                case Stage.MatchFacts:
                    if (_matchFact == _matchFacts.Count)
                    { _queryAllocation = AnalysisAggregate.CompatibleAllocation(_queryFacts.Values); _stage = Stage.MatchAliases; return 1; }
                    var fact = _graph.ReadFact(_graph.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, _matchFacts.First + _matchFact++, token).Value, token);
                    long bytes = FactBytes(fact);
                    if (_queryFacts.TryGetValue(fact.ComponentId, out var old))
                    {
                        if (old.BasisId != fact.BasisId || old.Measurement != fact.Measurement || old.Method != fact.Method ||
                            !old.Includes.Order(StringComparer.Ordinal).SequenceEqual(fact.Includes.Order(StringComparer.Ordinal))) throw Invalid();
                        return bytes;
                    }
                    if (_queryFacts.Count >= AnalysisLimits.Components || bytes > AnalysisLimits.PendingBytes / 4 - _queryFactBytes) throw new IOException("The filter allocation buffer limit was reached.");
                    _queryFacts.Add(fact.ComponentId, fact); _queryFactBytes += bytes; return bytes;
                case Stage.MatchAliases:
                    if (_matchAlias == _matchAliases.Count) { _stage = Stage.MatchObjects; return 1; }
                    _matchNode = _graph.ReadPathRow(_graph.ReadIndex(AnalysisIndexedGraphRevision.Aliases, _matchAliases.First + _matchAlias++, token).Value, token);
                    if (_matchNode is not { Kind: AnalysisKind.File, IncludedByPolicy: true }) return PathBytes(_matchNode);
                    if (_contextPass) { query.Context(_matchNode.Id, token); return PathBytes(_matchNode); }
                    query.Begin(_matchNode); _stage = Stage.MatchAncestors; return PathBytes(_matchNode);
                case Stage.MatchAncestors:
                    if (query.Walking) return query.Walk(_graph, token);
                    _stage = Stage.MatchEvaluate; return 1;
                case Stage.MatchEvaluate:
                    var truth = query.Evaluate(_matchObj, _queryAllocation, ActiveState(), _graph.Header.Freshness);
                    if (_directoryEmission)
                    {
                        if (truth == AnalysisTruth.True) BeginEmission(); else FinishQueryGroup(); return query.EvaluationBytes;
                    }
                    query.Record(_matchNode!.Id, truth, token);
                    if (!query.Matches(_matchNode.Id, token)) { _stage = Stage.MatchAliases; return query.EvaluationBytes; }
                    _count = checked(_count + 1); _eligible = true;
                    _referenceLogical.Add(_matchObj!.LogicalLength);
                    if (Metric is AnalysisMetric.FileSize or AnalysisMetric.Files || _first!.VerifiedIdentity is null)
                    { _emitForPath = true; BeginEmission(); }
                    else _stage = Stage.MatchAliases;
                    return query.EvaluationBytes;
                default: throw Invalid();
            }
        }
        private MeasurementState ActiveState()
        {
            if (Metric == AnalysisMetric.Files) return MeasurementState.Known;
            if (Metric == AnalysisMetric.FileSize) return _matchObj!.LogicalLength.State;
            if (_override is { } missing) return missing.State;
            if (Metric == AnalysisMetric.UniqueFileSize)
                return !_first!.LogicalLength.IsKnown ? _first.LogicalLength.State : _first.VerifiedIdentity is null ? MeasurementState.Unsupported : MeasurementState.Known;
            var state = MeasurementState.Known;
            foreach (var component in _selected) if (Rank(component.Fact.Measurement.State) > Rank(state)) state = component.Fact.Measurement.State;
            return state;
        }
        private static int Rank(MeasurementState state) => state switch
        { MeasurementState.Changed => 5, MeasurementState.Failed => 4, MeasurementState.Pending => 3, MeasurementState.Excluded => 2, MeasurementState.Unsupported => 1, _ => 0 };
        private void FinishFilteredEmission()
        {
            if (_emitForPath) { _emitForPath = false; _stage = Stage.MatchAliases; }
            else FinishQueryGroup();
        }
        private void FinishQueryGroup()
        {
            bool directory = _directoryEmission; Reset(); _queryFacts.Clear(); _queryFactBytes = 0; _matchNode = null; _matchObj = null;
            _stage = directory ? _pendingDirectory is null ? Stage.Directories : Stage.DirectoryOwner : Stage.Objects;
        }
    }
}
