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
            if (next == 0)
            {
                if (_lastPath[next] == item.Path) throw Invalid(); _lastPath[next] = item.Path;
                _files = checked(_files + 1); _memberCount = checked(_memberCount + 1);
                if (item.Logical.IsKnown) _logical = checked(_logical + item.Logical.Value!.Value);
                else Add(_logicalMissing, item.Logical.State);
            }
            else if (next == 2)
            {
                if (_lastPath[next] == item.Path) throw Invalid(); _lastPath[next] = item.Path;
                _memberCount = checked(_memberCount + 1);
            }
            else
            {
                var charge = item.Charge ?? throw Invalid();
                if (BinaryPrimitives.ReadUInt64BigEndian(entry.Key.AsSpan(40)) != charge.Id) throw Invalid();
                _chargeCount = checked(_chargeCount + 1);
                if (!charge.Measurement.IsKnown) Add(_missing, charge.Measurement.State);
                else if (charge.IdentityUnverified) _unverified = checked(_unverified + charge.Measurement.Value!.Value);
                else _known = checked(_known + charge.Measurement.Value!.Value);
            }
            return checked(bytes.Length * 3L + 1024);
        }
        private static void Add(Dictionary<MeasurementState, ulong> target, MeasurementState state, ulong count = 1)
        { if (state == MeasurementState.Known || !Enum.IsDefined(state)) throw Invalid(); target[state] = checked(target.GetValueOrDefault(state) + count); }
        private void FinishGroup(CancellationToken token)
        {
            if (_foldDefinition is null) return;
            var group = new AnalysisDatasetGroup(_foldDefinition, _foldKind, new(_known, _unverified, _files, _missing.ToImmutableDictionary(), Totals.CompatibleBasis),
                new(_logical, _files, _logicalMissing.ToImmutableDictionary()), _memberCount, _chargeCount);
            _summaryWriter!.Append(EncodeSummary(group), token);
            _allKnown = checked(_allKnown + _known); _allUnverified = checked(_allUnverified + _unverified); _allFiles = checked(_allFiles + _files); _allLogical = checked(_allLogical + _logical);
            foreach (var pair in _missing) Add(_allMissing, pair.Key, pair.Value);
            foreach (var pair in _logicalMissing) Add(_allLogicalMissing, pair.Key, pair.Value);
            _foldDefinition = null; _known = _unverified = _logical = 0; _files = _memberCount = _chargeCount = 0; _missing.Clear(); _logicalMissing.Clear();
        }
        internal void SealSummaries(CancellationToken token)
        {
            if (!Folded) throw Invalid(); _summaryWriter!.Flush(token); Summaries = _summaryWriter.CaptureCommitted(); _summaryWriter.Dispose(); _summaryWriter = null;
        }
        internal IEnumerable<AnalysisSortInput> SummaryEntries(int kind, CancellationToken token)
        {
            for (ulong id = 1; id <= Summaries!.Count; id++)
            {
                token.ThrowIfCancellationRequested(); var bytes = Summaries.Read(id, token); var item = DecodeSummary(bytes); byte[] hash = Hash(item.Definition.Key), key;
                if (kind == 0) key = hash;
                else
                {
                    UInt128 area = Metric == AnalysisMetric.OnDisk ? checked(item.Totals.Known + item.Totals.IdentityUnverified) : item.Totals.Known;
                    if (!item.Totals.CompatibleBasis) area = 0;
                    key = new byte[49]; key[0] = area == 0 && item.Totals.Missing.Count != 0 ? (byte)1 : (byte)0;
                    UInt128 rank = UInt128.MaxValue - area; BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(1), (ulong)(rank >> 64)); BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(9), (ulong)rank); hash.CopyTo(key, 17);
                }
                yield return new(new(key, id), checked(bytes.Length * 3 + 1024));
            }
        }
    }
}
