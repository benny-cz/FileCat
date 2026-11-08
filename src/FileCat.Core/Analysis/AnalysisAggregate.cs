using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Exact additive accounting on one immutable revision. Attribution precedes display filtering.</summary>
public static class AnalysisAggregate
{
    public static AnalysisMeasurement CompatibleAllocation(IEnumerable<StorageFact> observations)
    {
        var facts = SelectComponents(observations.ToArray());
        if (facts.Count == 0) return AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        UInt128 total = 0;
        foreach (var fact in facts)
        {
            if (!fact.Measurement.IsKnown) return fact.Measurement;
            total = checked(total + fact.Measurement.Value!.Value);
        }
        return AnalysisMeasurement.Known(total);
    }
    public static AnalysisProjection Project(AnalysisSnapshot snapshot, AnalysisMetric metric,
        Func<AnalysisNode, AnalysisObject?, bool>? filter = null, CancellationToken cancellation = default,
        Func<AnalysisNode, bool>? directoryFilter = null)
    {
        var nodes = snapshot.Nodes.ToDictionary(n => n.Id);
        var objects = snapshot.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var aliases = new Dictionary<string, List<AnalysisNode>>(StringComparer.Ordinal);
        var matching = ImmutableHashSet.CreateBuilder<ulong>();
        var context = ImmutableHashSet.CreateBuilder<ulong>();
        var charges = ImmutableArray.CreateBuilder<AnalysisCharge>();
        var unverifiedCharges = ImmutableArray.CreateBuilder<AnalysisCharge>();
        var missing = new Dictionary<MeasurementState, ulong>();
        var missingCharges = ImmutableArray.CreateBuilder<AnalysisMissingCharge>();
        var unmeasured = ImmutableDictionary.CreateBuilder<ulong, MeasurementState>();
        UInt128 known = 0, unverified = 0;
        ulong count = 0;
        int processed = 0;
        foreach (var node in snapshot.Nodes)
        {
            if ((processed++ & 4095) == 0) cancellation.ThrowIfCancellationRequested();
            if (node.Kind != AnalysisKind.File || !node.IncludedByPolicy) continue;
            AnalysisObject? obj = node.ObjectId is { } id ? objects.GetValueOrDefault(id) : null;
            // Always index aliases before applying filters, including nonmatching aliases.
            if (obj is not null)
            {
                string key = obj.VerifiedIdentity is { } identity ? "verified:" + identity : "node:" + node.Id;
                if (!aliases.TryGetValue(key, out var paths)) aliases.Add(key, paths = []);
                paths.Add(node);
            }
            if (filter is not null && !filter(node, obj)) continue;
            matching.Add(node.Id);
            count = checked(count + 1);
            if (metric == AnalysisMetric.Files) Add(node.ParentId, node.Name, 1, [node.Id], false, false);
            else if (metric == AnalysisMetric.FileSize)
            {
                var length = obj?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                if (length.IsKnown) Add(node.ParentId, node.Name, length.Value!.Value, [node.Id], false, false);
                else Missing(length, [node.Id]);
            }
            else if (obj is null) Missing(AnalysisMeasurement.Missing(MeasurementState.Unsupported), [node.Id]);
        }

        bool compatible = true;
        AnalysisReportBases? reportBases = null;
        if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
        {
            var byObject = snapshot.Facts.Where(f => f.ObjectId is not null).ToLookup(f => f.ObjectId!, StringComparer.Ordinal);
            // A cross-root total cannot conceal incompatible native component bases.
            var basesByRoot = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var paths in aliases.Values)
            {
                if ((processed++ & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                if (!paths.Any(p => matching.Contains(p.Id))) continue;
                foreach (var path in paths) if (!matching.Contains(path.Id)) context.Add(path.Id);
                var obj = objects[paths[0].ObjectId!];
                var members = paths.Select(p => objects[p.ObjectId!]).DistinctBy(o => o.Id).ToArray();
                if (members.Select(o => o.LogicalLength).Distinct().Skip(1).Any() ||
                    members.Select(o => o.VersionEvidence).Where(v => v is not null).Distinct(StringComparer.Ordinal).Skip(1).Any())
                { Missing(AnalysisMeasurement.Missing(MeasurementState.Changed), paths.Select(p => p.Id)); continue; }
                bool unknownIdentity = obj.VerifiedIdentity is null;
                ulong parent = CommonParent(paths, nodes);
                var ids = paths.Select(p => p.Id).ToImmutableArray();
                if (metric == AnalysisMetric.UniqueFileSize)
                {
                    if (obj.LogicalLength.IsKnown) Add(parent, paths.Count > 1 ? "Shared hard-linked files" : paths[0].Name,
                        obj.LogicalLength.Value!.Value, ids, paths.Count > 1, unknownIdentity);
                    else Missing(obj.LogicalLength, ids);
                    continue;
                }
                // Coherent aliases must agree; do not silently pick one of conflicting observations.
                var candidateFacts = members.SelectMany(o => byObject[o.Id]).ToArray();
                var facts = SelectComponents(candidateFacts);
                if (facts.Count == 0) Missing(AnalysisMeasurement.Missing(MeasurementState.Unsupported), ids);
                foreach (var fact in facts)
                {
                    foreach (var root in paths.Select(p => p.RootId).Distinct())
                    {
                        if (!basesByRoot.TryGetValue(root, out var b)) basesByRoot[root] = b = new(StringComparer.Ordinal);
                        b.Add(fact.BasisId);
                    }
                    if (fact.Measurement.IsKnown) Add(parent, paths.Count > 1 ? "Shared hard-linked files" : paths[0].Name,
                        fact.Measurement.Value!.Value, ids, paths.Count > 1, unknownIdentity);
                    else Missing(fact.Measurement, ids);
                }
            }
            if (metric == AnalysisMetric.OnDisk)
            {
                foreach (var directoryFacts in snapshot.Facts.Where(f => f.DirectoryId.HasValue).GroupBy(f => f.DirectoryId!.Value))
                foreach (var fact in SelectComponents(directoryFacts.ToArray()))
                {
                    // Directory self allocation is never attributed to a dominant file type.
                    if (!nodes.TryGetValue(fact.DirectoryId!.Value, out var directory) || !directory.IncludedByPolicy ||
                        (directoryFilter is not null ? !directoryFilter(directory) : filter is not null)) continue;
                    string root = nodes[fact.DirectoryId.Value].RootId;
                    if (!basesByRoot.TryGetValue(root, out var directoryBases)) basesByRoot[root] = directoryBases = new(StringComparer.Ordinal);
                    directoryBases.Add(fact.BasisId);
                    if (fact.Measurement.IsKnown) Add(fact.DirectoryId.Value, "Directory allocation", fact.Measurement.Value!.Value,
                        [], false, directory.ObservedIdentity is null, true);
                    else Missing(fact.Measurement, [fact.DirectoryId.Value], fact.DirectoryId.Value);
                }
                var sets = basesByRoot.Values.ToArray();
                compatible = sets.Length < 2 || sets.Skip(1).All(s => s.SetEquals(sets[0]));
                reportBases = AnalysisReportBases.Capture(basesByRoot.Values.SelectMany(values => values));
            }
        }
        if (!compatible) { known = 0; unverified = 0; charges.Clear(); }
        return new(snapshot.Identity.SessionId, snapshot.Identity.Epoch, snapshot.Identity.Revision, metric,
            charges.ToImmutable(), new(known, unverified, count, missing.ToImmutableDictionary(), compatible), matching.ToImmutable())
            { UnmeasuredStates = unmeasured.ToImmutable(), MissingCharges = missingCharges.ToImmutable(), UnverifiedCharges = unverifiedCharges.ToImmutable(), MeasurementBases = reportBases, AliasContextNodes = context.ToImmutable(), Taint = AnalysisPrivacy.Join(snapshot.Taint) };

        void Missing(AnalysisMeasurement measurement, IEnumerable<ulong> ids, ulong? directory = null)
        {
            var state = measurement.State == MeasurementState.Known ? MeasurementState.Unsupported : measurement.State;
            missing[state] = checked(missing.GetValueOrDefault(state) + 1);
            var members = ids.ToImmutableArray(); missingCharges.Add(new(state, directory is null ? members : [], directory));
            foreach (ulong id in members)
                if (!unmeasured.TryGetValue(id, out var old) || Rank(state) > Rank(old)) unmeasured[id] = state;
        }
        static int Rank(MeasurementState state) => state switch { MeasurementState.Changed => 5, MeasurementState.Failed => 4, MeasurementState.Pending => 3, MeasurementState.Excluded => 2, _ => 1 };
        void Add(ulong parent, string label, UInt128 value, ImmutableArray<ulong> ids, bool shared, bool uncertain, bool directory = false)
        {
            if (uncertain) unverified = checked(unverified + value); else known = checked(known + value);
            if (metric == AnalysisMetric.OnDisk) _ = checked(known + unverified);
            if (metric == AnalysisMetric.UniqueFileSize && uncertain)
            { unverifiedCharges.Add(new(parent, label, value, ids, shared, true, directory)); return; }
            charges.Add(new(parent, label, value, ids, shared, uncertain, directory));
        }
    }

    internal static ulong CommonParent(List<AnalysisNode> paths, Dictionary<ulong, AnalysisNode> nodes)
    {
        ulong parent = paths[0].ParentId;
        foreach (var path in paths.Skip(1))
        {
            var ancestors = new HashSet<ulong> { 0 };
            ulong current = path.ParentId;
            for (int depth = 0; current != 0; depth++)
            {
                if (depth > AnalysisLimits.Depth || !ancestors.Add(current) || !nodes.TryGetValue(current, out var n))
                    throw new InvalidDataException("Invalid analysis ancestry.");
                current = n.ParentId;
            }
            int steps = 0;
            while (!ancestors.Contains(parent))
            {
                if (++steps > AnalysisLimits.Depth || !nodes.TryGetValue(parent, out var n)) throw new InvalidDataException("Invalid analysis ancestry.");
                parent = n.ParentId;
            }
        }
        return parent;
    }

    internal static List<StorageFact> SelectComponents(StorageFact[] facts)
    {
        // Facts for an object must form an acyclic component inclusion graph. Duplicate observations agree or fail closed.
        var byComponent = new Dictionary<string, StorageFact>(StringComparer.Ordinal);
        foreach (var f in facts)
        {
            if (byComponent.TryGetValue(f.ComponentId, out var old))
            {
                if (old.BasisId != f.BasisId || old.Measurement != f.Measurement || old.Method != f.Method ||
                    !old.Includes.Order(StringComparer.Ordinal).SequenceEqual(f.Includes.Order(StringComparer.Ordinal)))
                    throw new InvalidDataException("Conflicting component observations.");
            }
            else byComponent.Add(f.ComponentId, f);
        }
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string component, int depth)
        {
            if (depth > AnalysisLimits.Components || !visiting.Add(component)) throw new InvalidDataException("Cyclic component coverage.");
            if (!visited.Contains(component) && byComponent.TryGetValue(component, out var f))
                foreach (var child in f.Includes) { covered.Add(child); Visit(child, depth + 1); }
            visiting.Remove(component); visited.Add(component);
        }
        foreach (var key in byComponent.Keys) Visit(key, 0);
        return byComponent.Values.Where(f => !covered.Contains(f.ComponentId)).ToList();
    }
}
