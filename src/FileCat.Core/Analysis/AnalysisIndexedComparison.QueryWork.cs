using System.Buffers.Binary;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    // Each yield is one captured metadata/ancestor/fact operation. The producer batches these
    // independently of output rows; long alias groups cannot hide inside one sort MoveNext.
    private sealed class QueryWork(State source, AnalysisIndexedFilter? before, AnalysisIndexedFilter? now,
        AnalysisComparisonView view, QueryState output)
    {
        private AnalysisTruth _sideTruth;
        private byte[]? _pathKey;
        private readonly Dictionary<string, StorageFact> _facts = new(StringComparer.Ordinal);
        internal IEnumerable<AnalysisSortInput> Steps(CancellationToken token)
        {
            for (ulong id = 1; id <= source.Rows!.Count; id++)
            {
                token.ThrowIfCancellationRequested(); var row = source.ReadRow(id, token, out long bytes);
                yield return new(null, checked((int)bytes));
                if (!AnalysisComparisonProjection.InCohort(row.Change, view.Cohort)) continue;
                AnalysisTruth a = AnalysisTruth.Unknown, b = AnalysisTruth.Unknown;
                bool old = view.QuerySide is AnalysisComparisonSide.Previous or AnalysisComparisonSide.Either || view.QuerySide == AnalysisComparisonSide.CurrentOrPrevious && row.Current is null;
                bool current = view.QuerySide is AnalysisComparisonSide.Current or AnalysisComparisonSide.Either || view.QuerySide == AnalysisComparisonSide.CurrentOrPrevious && row.Current is not null;
                if (old && row.Previous is { } previous)
                {
                    foreach (var step in Evaluate(row, previous, source.Previous, before!, token)) yield return step;
                    a = _sideTruth;
                }
                if (current && row.Current is { } value)
                {
                    foreach (var step in Evaluate(row, value, source.Current, now!, token)) yield return step;
                    b = _sideTruth;
                }
                var truth = view.QuerySide switch
                {
                    AnalysisComparisonSide.Current => b, AnalysisComparisonSide.Previous => a,
                    AnalysisComparisonSide.Either => AnalysisFilter.Or(row.Previous is null ? AnalysisTruth.False : a, row.Current is null ? AnalysisTruth.False : b),
                    _ => row.Current is null ? a : b
                };
                if (truth == AnalysisTruth.True) output.Definite++; else if (truth == AnalysisTruth.Unknown) output.Unknown++;
                if (truth != (view.ShowUnknown ? AnalysisTruth.Unknown : AnalysisTruth.True)) continue;
                byte[] key;
                if (view.Order == AnalysisComparisonOrder.Path)
                {
                    foreach (var step in Path(row, token)) yield return step;
                    key = _pathKey!; _pathKey = null;
                }
                else key = NumericKey(row, view.Order);
                yield return new(new(key, id), checked(key.Length + 64));
            }
        }
        private IEnumerable<AnalysisSortInput> Evaluate(AnalysisIndexedComparisonRow row, AnalysisComparisonAccountingEntity entity,
            AnalysisComparisonEntities.State side, AnalysisIndexedFilter filter, CancellationToken token)
        {
            _sideTruth = AnalysisTruth.False;
            for (ulong member = 0; member < entity.ObservedPaths; member++)
            {
                token.ThrowIfCancellationRequested(); var node = side.Graph.ReadPathRow(side.MemberPathRow(entity.Id, member, token), token);
                yield return new(null, checked((int)AnalysisIndexedFilter.Cost(node)));
                AnalysisObject? obj = null;
                if (node.ObjectId is { } objectId)
                {
                    var range = side.Graph.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(objectId), cancellation: token);
                    if (range.Count != 1) throw AnalysisIndexedGraph.Invalid();
                    obj = side.Graph.ReadObject(side.Graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token);
                    yield return new(null, checked(1024 + obj.Id.Length * 2 + (obj.VerifiedIdentity?.Length ?? 0) * 2 + (obj.VersionEvidence?.Length ?? 0) * 2));
                }
                var allocation = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                if (filter.NeedsAllocation && (node.Kind == AnalysisKind.Directory || obj is not null))
                {
                    _facts.Clear(); long factBytes = 0;
                    var range = side.Graph.Find(AnalysisIndexedGraphRevision.FactOwners,
                        AnalysisGraphKeys.FactOwner(node.Kind == AnalysisKind.Directory ? null : obj!.Id, node.Kind == AnalysisKind.Directory ? node.Id : null), cancellation: token);
                    for (ulong at = 0; at < range.Count; at++)
                    {
                        token.ThrowIfCancellationRequested(); var fact = side.Graph.ReadFact(side.Graph.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, range.First + at, token).Value, token);
                        int cost = FactCost(fact);
                        if (_facts.TryGetValue(fact.ComponentId, out var prior))
                        {
                            if (prior.BasisId != fact.BasisId || prior.Measurement != fact.Measurement || prior.Method != fact.Method ||
                                !prior.Includes.Order(StringComparer.Ordinal).SequenceEqual(fact.Includes.Order(StringComparer.Ordinal))) throw AnalysisIndexedGraph.Invalid();
                        }
                        else
                        {
                            if (_facts.Count >= AnalysisLimits.Components || cost > AnalysisLimits.PendingBytes / 4 - factBytes)
                                throw new IOException("The comparison allocation-predicate buffer limit was reached. Keep the applied view and narrow the query.");
                            _facts.Add(fact.ComponentId, fact); factBytes += cost;
                        }
                        yield return new(null, cost);
                    }
                    allocation = AnalysisAggregate.CompatibleAllocation(_facts.Values); _facts.Clear();
                }
                filter.Begin(node);
                while (filter.Walking) { token.ThrowIfCancellationRequested(); yield return new(null, checked((int)filter.Walk(side.Graph, token))); }
                var active = view.Metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk && entity.Value.IsKnown &&
                    (node.Kind == AnalysisKind.Directory ? node.ObservedIdentity is null : obj?.VerifiedIdentity is null) ? MeasurementState.Unsupported : entity.Value.State;
                var metadata = obj ?? (node.Times is { } times ? new AnalysisObject("recorded-metadata", null, AnalysisMeasurement.Missing(MeasurementState.Unsupported),
                    AnalysisMeasurement.Missing(MeasurementState.Unsupported), times.ModifiedUtc, ModifiedPrecisionTicks: times.ModifiedPrecisionTicks) : null);
                _sideTruth = AnalysisFilter.Or(_sideTruth, filter.EvaluateComparison(metadata, allocation, active, entity.Fresh, Change(row.Change), row.Delta));
                yield return new(null, checked((int)filter.EvaluationBytes));
                if (_sideTruth == AnalysisTruth.True) yield break;
            }
        }
        private IEnumerable<AnalysisSortInput> Path(AnalysisIndexedComparisonRow row, CancellationToken token)
        {
            var side = row.Current is null ? source.Previous : source.Current; var entity = row.Current ?? row.Previous!;
            var node = side.Graph.ReadPathRow(side.MemberPathRow(entity.Id, 0, token), token); int depth = 0, characters = 0;
            string root = node.RootId; var parts = new List<string>();
            while (true)
            {
                token.ThrowIfCancellationRequested(); yield return new(null, checked((int)AnalysisIndexedFilter.Cost(node)));
                if (node.RootId != root || ++depth > AnalysisLimits.Depth + 1) throw AnalysisIndexedGraph.Invalid();
                if (node.ParentId == 0) break;
                string name = AnalysisNames.DisplayName(node, int.MaxValue); characters = checked(characters + name.Length + 1);
                if (characters > (AnalysisExternalSort.MaximumKeyBytes - 10) / 2) throw PathLimit();
                parts.Add(name); node = side.Graph.ReadPath(node.ParentId, token);
            }
            var binding = side.Graph.Header.Roots.Single(r => r.Id == root);
            string separator = binding.NameRules == AnalysisNameRules.Windows ? "\\" : "/";
            string prefix = AnalysisNames.Display(binding.CanonicalLocator, int.MaxValue);
            parts.Reverse(); string path = prefix + (parts.Count == 0 || prefix.EndsWith(separator, StringComparison.Ordinal) ? "" : separator) + string.Join(separator, parts);
            if (path.Length > (AnalysisExternalSort.MaximumKeyBytes - 10) / 2 || path.Contains('\0')) throw PathLimit();
            byte[] key = new byte[path.Length * 2 + 10];
            for (int at = 0; at < path.Length; at++) BinaryPrimitives.WriteUInt16BigEndian(key.AsSpan(at * 2), path[at]);
            BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(key.Length - 8), row.Id); _pathKey = key;
        }
        private static IOException PathLimit() => new("A displayed comparison path exceeds the bounded exact ordering key. Keep the applied view or choose another order; paths were not truncated for ranking.");
        private static byte[] NumericKey(AnalysisIndexedComparisonRow row, AnalysisComparisonOrder order)
        {
            byte[] key;
            if (order == AnalysisComparisonOrder.RelativeChange)
            {
                key = new byte[17]; double? value = row.RelativePercent; key[0] = value.HasValue ? (byte)0 : (byte)1;
                BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(1), ulong.MaxValue - BitConverter.DoubleToUInt64Bits(Math.Abs(value ?? 0)));
            }
            else
            {
                key = new byte[25];
                bool current = order == AnalysisComparisonOrder.CurrentAmount;
                key[0] = current && row.Current?.Value.IsKnown != true ? (byte)1 : (byte)0;
                UInt128 amount = current ? row.Current?.Value.Value ?? 0 : row.Delta?.Magnitude ?? 0;
                BinaryPrimitives.WriteUInt128BigEndian(key.AsSpan(1), UInt128.MaxValue - amount);
            }
            BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(key.Length - 8), row.Id); return key;
        }
        private static string Change(AnalysisChange change) => change switch
        {
            AnalysisChange.MetadataChanged => "metadata-changed", AnalysisChange.NoObservedMetadataChange => "unchanged",
            AnalysisChange.PreviouslyUnknown => "previously-unknown", _ => change.ToString().ToLowerInvariant()
        };
    }
}
