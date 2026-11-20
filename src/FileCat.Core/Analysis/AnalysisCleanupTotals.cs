using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace FileCat.Core.Analysis;

internal sealed record AnalysisCleanupTotals(SessionIdentity Identity, int Selected, BigInteger LogicalBytes, int MeasuredFiles,
    int UnknownSizes, int Folders, int Excluded, int MeasuredAllocationObjects, int IncompleteAllocationPaths,
    ImmutableArray<AnalysisCleanupAllocation> Allocation, bool AllocationLabelsExceeded);

internal sealed record AnalysisCleanupAllocation(string Basis, string Component, BigInteger Bytes);

/// <summary>Aggregates selected observations. Allocation is partitioned by component and basis and counts each verified object once.</summary>
internal sealed class AnalysisCleanupTotalsBuilder
{
    private readonly Dictionary<(string Basis, string Component), BigInteger> _allocation = [];
    private BigInteger _logicalBytes;
    private int _measured, _unknownSizes, _folders, _excluded, _allocationObjects, _incompleteAllocation, _labelBytes;
    private bool _labelLimit;

    internal bool CountPath(AnalysisNode path)
    {
        if (path.Kind == AnalysisKind.Directory) _folders++;
        if (!path.IncludedByPolicy)
        {
            _excluded++;
            return false;
        }
        if (path.Kind != AnalysisKind.File) return false;
        if (path.ObjectId is not null && path.MetadataState == MeasurementState.Known) return true;
        AddFile(default, false, 1);
        return false;
    }

    internal void AddFile(AnalysisMeasurement length, bool allocationComplete, int paths)
    {
        if (length.IsKnown)
        {
            _logicalBytes += (BigInteger)length.Value!.Value * paths;
            _measured += paths;
        }
        else _unknownSizes += paths;
        if (!allocationComplete) _incompleteAllocation += paths;
    }

    internal bool AddAllocation(List<StorageFact> observations)
    {
        if (observations.Sum(AnalysisDatasetLease.FactBytes) > AnalysisLimits.BatchBytes) return false;
        var facts = AnalysisAggregate.SelectComponents(observations.ToArray());
        if (facts.Any(fact => fact.Measurement.IsKnown)) _allocationObjects++;
        bool complete = facts.Count > 0;
        foreach (var fact in facts)
        {
            if (!fact.Measurement.IsKnown)
            {
                complete = false;
                continue;
            }
            if (_labelLimit) continue;
            var key = (fact.BasisId, fact.ComponentId);
            if (!_allocation.TryGetValue(key, out var prior))
            {
                long labelBytes = 2L * (fact.BasisId.Length + fact.ComponentId.Length) + 96;
                if (_allocation.Count == AnalysisLimits.Components || labelBytes > AnalysisLimits.BatchBytes - _labelBytes)
                {
                    _labelLimit = true;
                    _allocation.Clear();
                    continue;
                }
                _labelBytes += (int)labelBytes;
            }
            _allocation[key] = prior + (BigInteger)fact.Measurement.Value!.Value;
        }
        return complete;
    }

    internal AnalysisCleanupTotals Finish(SessionIdentity identity, int selected) => new(identity, selected, _logicalBytes, _measured,
        _unknownSizes, _folders, _excluded, _allocationObjects, _incompleteAllocation,
        [.. _allocation.Select(pair => new AnalysisCleanupAllocation(pair.Key.Basis, pair.Key.Component, pair.Value))
            .OrderBy(row => row.Basis, StringComparer.Ordinal).ThenBy(row => row.Component, StringComparer.Ordinal)], _labelLimit);

    internal static void Validate(ImmutableHashSet<ulong> paths)
    {
        if (paths.Count is < 1 or > AnalysisLimits.ReviewPaths || paths.Contains(0))
            throw new ArgumentException("Cleanup totals require a bounded set of real captured path IDs.", nameof(paths));
    }
}

internal static class AnalysisMemoryCleanupTotals
{
    internal static AnalysisCleanupTotals Read(AnalysisSnapshot snapshot, ImmutableHashSet<ulong> paths, CancellationToken token)
    {
        AnalysisCleanupTotalsBuilder.Validate(paths);
        var totals = new AnalysisCleanupTotalsBuilder();
        var selected = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes)
        {
            token.ThrowIfCancellationRequested();
            if (paths.Contains(node.Id) && totals.CountPath(node))
                selected[node.ObjectId!] = selected.GetValueOrDefault(node.ObjectId!) + 1;
        }
        var objects = new Dictionary<string, AnalysisObject>(StringComparer.Ordinal);
        var identities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var obj in snapshot.Objects)
        {
            token.ThrowIfCancellationRequested();
            if (!selected.ContainsKey(obj.Id)) continue;
            objects.Add(obj.Id, obj);
            if (obj.VerifiedIdentity is { } identity) identities.TryAdd(identity, 0);
        }
        foreach (var obj in snapshot.Objects)
        {
            token.ThrowIfCancellationRequested();
            if (obj.VerifiedIdentity is { } identity && identities.TryGetValue(identity, out int count))
                identities[identity] = count + 1;
        }
        var facts = new Dictionary<string, List<StorageFact>>(StringComparer.Ordinal);
        foreach (var fact in snapshot.Facts)
        {
            token.ThrowIfCancellationRequested();
            if (fact.ObjectId is not { } id || !objects.TryGetValue(id, out var obj) ||
                obj.VerifiedIdentity is not { } identity || identities[identity] != 1) continue;
            if (!facts.TryGetValue(id, out var components)) facts.Add(id, components = []);
            if (components.Count <= AnalysisLimits.Components) components.Add(fact);
        }
        foreach (var (id, count) in selected)
        {
            token.ThrowIfCancellationRequested();
            var length = objects.TryGetValue(id, out var obj) ? obj.LogicalLength : default;
            bool complete = facts.TryGetValue(id, out var components) && components.Count <= AnalysisLimits.Components && totals.AddAllocation(components);
            totals.AddFile(length, complete, count);
        }
        return totals.Finish(snapshot.Identity, paths.Count);
    }
}

public sealed partial class AnalysisDatasetLease
{
    internal Task<AnalysisCleanupTotals> ReadCleanupTotalsAsync(ImmutableHashSet<ulong> paths, CancellationToken cancellation)
    {
        AnalysisCleanupTotalsBuilder.Validate(paths);
        return _owned.UseAsync(async reader =>
        {
            var totals = new AnalysisCleanupTotalsBuilder();
            var objects = new Dictionary<ulong, (AnalysisMeasurement Length, bool Complete)>();
            using IEnumerator<ulong> members = paths.GetEnumerator();
            bool remaining = true;
            while (remaining)
                remaining = await AnalysisIo.Run(_io, _device, token =>
                {
                    long bytes = 0;
                    var clock = Stopwatch.StartNew();
                    for (int count = 0; count < AnalysisLimits.BatchRecords; count++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!members.MoveNext()) return false;
                        ulong id = members.Current;
                        if (reader.FindPathRow(id, token) is { } pathRow)
                        {
                            var path = reader.ReadPathRow(pathRow, token);
                            if (path.Id != id) throw AnalysisIndexedGraph.Invalid();
                            bytes += AnalysisIndexedFilter.Cost(path);
                            if (totals.CountPath(path)) AddFile(path.ObjectId!, token, ref bytes);
                        }
                        if (bytes >= AnalysisLimits.BatchBytes || clock.ElapsedMilliseconds >= 20) break;
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return totals.Finish(Descriptor.Identity, paths.Count);

            void AddFile(string id, CancellationToken token, ref long bytes)
            {
                var matches = reader.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
                if (matches.Count != 1) throw AnalysisIndexedGraph.Invalid();
                ulong row = reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, matches.First, token).Value;
                if (!objects.TryGetValue(row, out var result))
                {
                    var obj = reader.ReadObject(row, token);
                    bytes += 256L + 2L * ((obj.VerifiedIdentity?.Length ?? 0) + (obj.VersionEvidence?.Length ?? 0));
                    bool unique = obj.VerifiedIdentity is not null && (reader.Header.UniqueObjectIdentities ||
                        reader.Find(AnalysisIndexedGraphRevision.ObjectIdentities, AnalysisGraphKeys.ObjectIdentity(obj), cancellation: token).Count == 1);
                    result = (obj.LogicalLength, unique && ReadAllocation(id, token, ref bytes));
                    objects.Add(row, result);
                }
                totals.AddFile(result.Length, result.Complete, 1);
            }

            bool ReadAllocation(string id, CancellationToken token, ref long bytes)
            {
                var matches = reader.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner(id, null), cancellation: token);
                if (matches.Count is 0 or > AnalysisLimits.Components) return false;
                var facts = new List<StorageFact>((int)matches.Count);
                long cost = 0;
                for (ulong index = 0; index < matches.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var fact = reader.ReadFact(reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, matches.First + index, token).Value, token);
                    long next = FactBytes(fact);
                    bytes += next;
                    cost += next;
                    if (cost > AnalysisLimits.BatchBytes) return false;
                    facts.Add(fact);
                }
                return totals.AddAllocation(facts);
            }
        });
    }
}
