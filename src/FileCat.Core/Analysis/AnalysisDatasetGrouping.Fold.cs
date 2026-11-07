using System.Buffers.Binary;
using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    private sealed partial class State
    {
        private readonly ulong[] _positions = new ulong[3];
        private readonly AnalysisSortEntry?[] _heads = new AnalysisSortEntry?[3];
        private AnalysisGroupDefinition? _foldDefinition;
        private AnalysisGroupKind _foldKind;
        private UInt128 _known, _unverified, _logical, _allKnown, _allUnverified, _allLogical;
        private ulong _files, _memberCount, _chargeCount, _allFiles;
        private readonly Dictionary<MeasurementState, ulong> _missing = [], _logicalMissing = [], _allMissing = [], _allLogicalMissing = [];
        private readonly ulong[] _lastPath = new ulong[3];
        private ulong _placementPath, _placementEvent;
        private UInt128 _placementKnown, _placementUnverified;
        private bool _placementShared, _placementReference;
        private readonly Dictionary<MeasurementState, ulong> _placementMissing = [];
        private UInt128 _sharedKnown, _sharedUnverified;
        private readonly Dictionary<MeasurementState, ulong> _sharedMissing = [];
        internal bool Folded { get; private set; }
        internal AnalysisLogicalTotals LogicalTotals => new(_allLogical, _allFiles, _allLogicalMissing.ToImmutableDictionary());
        internal long FoldStep(CancellationToken token)
        {
            AnalysisSortedIndexReader[] indexes = [MemberIndex!, ChargeIndex!, ReferenceIndex!];
            for (int n = 0; n < 3; n++)
                if (_heads[n] is null && _positions[n] < indexes[n].Count)
                { _heads[n] = indexes[n].Read(++_positions[n], token); return 128; }
            int next = -1;
            for (int n = 0; n < 3; n++)
                if (_heads[n] is { } candidate && (next == -1 || candidate.Key.AsSpan().SequenceCompareTo(_heads[next]!.Value.Key) < 0)) next = n;
            if (next == -1)
            {
                FinishGroup(token);
                if (_allKnown != Totals.Known || _allUnverified != Totals.IdentityUnverified || _allFiles != Totals.MatchingPaths ||
                    _allMissing.Count != Totals.Missing.Count || _allMissing.Any(pair => Totals.Missing.GetValueOrDefault(pair.Key) != pair.Value)) throw Invalid();
                Folded = true; return 1;
            }
            var entry = _heads[next]!.Value; _heads[next] = null;
            var records = next == 0 ? Members! : next == 1 ? Charges! : References!;
            byte[] bytes = records.Read(entry.Value, token); var item = DecodeEvent(bytes);
            if (entry.Key.Length != (next == 1 ? 48 : 40) || !entry.Key.AsSpan(0, 32).SequenceEqual(Hash(item.Definition.Key)) ||
                BinaryPrimitives.ReadUInt64BigEndian(entry.Key.AsSpan(32, 8)) != item.Path || item.EventKind != (next == 0 ? EventKind.Member : next == 1 ? EventKind.Charge : EventKind.Reference)) throw Invalid();
            if (_foldDefinition is not null && _foldDefinition.Key != item.Definition.Key) FinishGroup(token);
            if (_foldDefinition is null) { _foldDefinition = item.Definition; _foldKind = item.Kind; Array.Clear(_lastPath); }
            else if (_foldDefinition != item.Definition || _foldKind != item.Kind) throw Invalid();
            if (_placementPath != item.Path) { FinishPlacement(token); _placementPath = item.Path; }
            if (next == 0)
            {
                if (_lastPath[next] == item.Path) throw Invalid(); _lastPath[next] = item.Path;
                _files = checked(_files + 1); _memberCount = checked(_memberCount + 1);
                if (item.Logical.IsKnown) _logical = checked(_logical + item.Logical.Value!.Value);
                else Add(_logicalMissing, item.Logical.State);
                _placementEvent = entry.Value; _placementReference = false; _placementShared = item.SharedElsewhere;
            }
            else if (next == 2)
            {
                if (_lastPath[next] == item.Path) throw Invalid(); _lastPath[next] = item.Path;
                _memberCount = checked(_memberCount + 1);
                _placementEvent = entry.Value; _placementReference = true; _placementShared = item.Kind == AnalysisGroupKind.SharedAcrossGroups;
            }
            else
            {
                var charge = item.Charge ?? throw Invalid();
                if (BinaryPrimitives.ReadUInt64BigEndian(entry.Key.AsSpan(40)) != charge.Id) throw Invalid();
                _chargeCount = checked(_chargeCount + 1);
                if (!charge.Measurement.IsKnown) Add(_missing, charge.Measurement.State);
                else if (charge.IdentityUnverified) _unverified = checked(_unverified + charge.Measurement.Value!.Value);
                else _known = checked(_known + charge.Measurement.Value!.Value);
                if (item.Path != 0)
                {
                    if (!charge.Measurement.IsKnown) Add(_placementMissing, charge.Measurement.State);
                    else if (charge.IdentityUnverified) _placementUnverified = checked(_placementUnverified + charge.Measurement.Value!.Value);
                    else _placementKnown = checked(_placementKnown + charge.Measurement.Value!.Value);
                }
                else
                {
                    if (!charge.Measurement.IsKnown) Add(_sharedMissing, charge.Measurement.State);
                    else if (charge.IdentityUnverified) _sharedUnverified = checked(_sharedUnverified + charge.Measurement.Value!.Value);
                    else _sharedKnown = checked(_sharedKnown + charge.Measurement.Value!.Value);
                }
            }
            return checked(bytes.Length * 3L + 1024);
        }
        private static void Add(Dictionary<MeasurementState, ulong> target, MeasurementState state, ulong count = 1)
        { if (state == MeasurementState.Known || !Enum.IsDefined(state)) throw Invalid(); target[state] = checked(target.GetValueOrDefault(state) + count); }
        private void FinishGroup(CancellationToken token)
        {
            if (_foldDefinition is null) return;
            FinishPlacement(token);
            var group = new AnalysisDatasetGroup(_foldDefinition, _foldKind, new(_known, _unverified, _files, _missing.ToImmutableDictionary(), Totals.CompatibleBasis),
                new(_logical, _files, _logicalMissing.ToImmutableDictionary()), _memberCount, _chargeCount)
                { Shared = new(_sharedKnown, _sharedUnverified, 0, _sharedMissing.ToImmutableDictionary(), Totals.CompatibleBasis) };
            _summaryWriter!.Append(EncodeSummary(group), token);
            _allKnown = checked(_allKnown + _known); _allUnverified = checked(_allUnverified + _unverified); _allFiles = checked(_allFiles + _files); _allLogical = checked(_allLogical + _logical);
            foreach (var pair in _missing) Add(_allMissing, pair.Key, pair.Value);
            foreach (var pair in _logicalMissing) Add(_allLogicalMissing, pair.Key, pair.Value);
            _foldDefinition = null; _known = _unverified = _logical = 0; _files = _memberCount = _chargeCount = 0; _missing.Clear(); _logicalMissing.Clear();
            _sharedKnown = _sharedUnverified = 0; _sharedMissing.Clear();
        }
        private void FinishPlacement(CancellationToken token)
        {
            if (_placementPath != 0)
            {
                if (_placementEvent == 0 || _foldDefinition is null) throw Invalid();
                var record = new Placement(Hash(_foldDefinition.Key), _placementPath, _placementEvent, _placementKnown, _placementUnverified,
                    _placementMissing.ToImmutableDictionary(), _placementShared, _placementReference);
                (_placementReference ? _referencePlacementWriter : _memberPlacementWriter)!.Append(EncodePlacement(record), token);
            }
            _placementPath = _placementEvent = 0; _placementKnown = _placementUnverified = 0; _placementMissing.Clear(); _placementShared = _placementReference = false;
        }
        internal void SealSummaries(CancellationToken token)
        {
            if (!Folded) throw Invalid(); _summaryWriter!.Flush(token); Summaries = _summaryWriter.CaptureCommitted(); _summaryWriter.Dispose(); _summaryWriter = null;
            _memberPlacementWriter!.Flush(token); MemberPlacements = _memberPlacementWriter.CaptureCommitted(); _memberPlacementWriter.Dispose(); _memberPlacementWriter = null;
            _referencePlacementWriter!.Flush(token); ReferencePlacements = _referencePlacementWriter.CaptureCommitted(); _referencePlacementWriter.Dispose(); _referencePlacementWriter = null;
        }
        internal IEnumerable<AnalysisSortInput> SummaryEntries(int kind, CancellationToken token)
        {
            for (ulong id = 1; id <= Summaries!.Count; id++)
            {
                token.ThrowIfCancellationRequested(); var bytes = Summaries.Read(id, token); var item = DecodeSummary(bytes); byte[] hash = Hash(item.Definition.Key), key;
                if (kind == 0) key = hash;
                else
                {
                    foreach (var field in Enum.GetValues<AnalysisTableSort>())
                    {
                        UInt128 area = !item.Totals.CompatibleBasis ? 0 : Metric == AnalysisMetric.OnDisk ? checked(item.Totals.Known + item.Totals.IdentityUnverified) : item.Totals.Known;
                        UInt128 value = field == AnalysisTableSort.FileSize ? item.Logical.KnownBytes : field == AnalysisTableSort.FilePaths ? item.Totals.MatchingPaths : area;
                        bool missing = field == AnalysisTableSort.FileSize ? item.Logical.UnknownFiles != 0 && value == 0 : field == AnalysisTableSort.ActiveMetric ?
                            (!item.Totals.CompatibleBasis || value == 0 && (item.Totals.Missing.Count != 0 || Metric == AnalysisMetric.UniqueFileSize && item.Totals.IdentityUnverified != 0)) :
                            field == AnalysisTableSort.MeasurementState && item.Totals.Missing.Count != 0;
                        yield return new(new(OrderKey([(byte)field], hash, item.Definition.Label, id, field, value, missing, item.Totals.Missing.Count == 0 ? MeasurementState.Known : item.Totals.Missing.Keys.Max()), id), checked(bytes.Length * 3 + 1024));
                    }
                    continue;
                }
                yield return new(new(key, id), checked(bytes.Length * 3 + 1024));
            }
        }
    }
}
