using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Bounded streaming graph checks; equality uses full captured keys rather than hash identity.</summary>
internal static class AnalysisIndexedGraphValidation
{
    internal static async Task<AnalysisIndexedGraphHeader> ValidateAsync(AnalysisIndexedGraphReader reader, DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation)
    {
        reader.Header.Validate();
        var coverage = new AnalysisIndexedCoverage(reader.Header);
        bool uniqueIdentities = true; byte[]? previousIdentity = null;
        ulong identityAt = 1, identityCount = reader.IndexCount(AnalysisIndexedGraphRevision.ObjectIdentities);
        while (identityAt <= identityCount)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (identityAt <= identityCount && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    var item = reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIdentities, identityAt++, token);
                    if (previousIdentity is not null)
                    {
                        int order = previousIdentity.AsSpan().SequenceCompareTo(item.Key);
                        if (order > 0) throw AnalysisIndexedGraph.Invalid();
                        if (order == 0) uniqueIdentities = false;
                    }
                    previousIdentity = item.Key; bytes += item.Key.Length + 12;
                }
                return true;
            }, cancellation).ConfigureAwait(false);
        foreach (int index in new[] { AnalysisIndexedGraphRevision.Children, AnalysisIndexedGraphRevision.ObjectIds, AnalysisIndexedGraphRevision.FactIds, AnalysisIndexedGraphRevision.PathIds })
        {
            ulong next = 1, count = reader.IndexCount(index); byte[]? previous = null;
            while (next <= count)
                await AnalysisIo.Run(io, device, token =>
                {
                    int records = 0, bytes = 0;
                    while (next <= count && records < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    {
                        token.ThrowIfCancellationRequested(); var item = reader.ReadIndex(index, next++, token);
                        if (previous is not null && previous.AsSpan().SequenceCompareTo(item.Key) >= 0) throw AnalysisIndexedGraph.Invalid();
                        previous = item.Key; records++; bytes += item.Key.Length + 12;
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
        }

        ulong path = 1, pathCount = reader.Count(AnalysisIndexedGraph.Paths); var roots = new bool[reader.Header.Roots.Length];
        // Sequential rows and their earlier parents have independent one-block caches. Alternating
        // through one cache would reload/authenticate both blocks for almost every observation.
        var parents = await AnalysisIo.Run(io, device, _ => reader.Retain(), cancellation).ConfigureAwait(false);
        try
        {
        while (path <= pathCount)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (path <= pathCount && records < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); ulong row = path++; var current = reader.ReadPathRowRecord(row, token); var node = current.Node;
                    if (reader.Header.DensePathIds && node.Id != row) throw AnalysisIndexedGraph.Invalid();
                    coverage.Path(node);
                    if (node.Times is { } times && (times.ObservedUtc < reader.Header.Identity.StartedUtc || times.ObservedUtc > reader.Header.Identity.ObservedUtc)) throw AnalysisIndexedGraph.Invalid();
                    if (node.ParentId == 0)
                    { if (roots[current.RootIndex]) throw AnalysisIndexedGraph.Invalid(); roots[current.RootIndex] = true; }
                    else
                    {
                        if (reader.FindPathRow(node.ParentId, token) is not { } parentRow || parentRow >= row) throw AnalysisIndexedGraph.Invalid();
                        var parent = parents.ReadPathRecord(node.ParentId, token);
                        if (parent.RootIndex != current.RootIndex || parent.Depth + 1 != current.Depth || parent.Node.Kind != AnalysisKind.Directory ||
                            !parent.Node.IncludedByPolicy || parent.Node.MetadataState != MeasurementState.Known) throw AnalysisIndexedGraph.Invalid();
                    }
                    records++; bytes += node.RawName.IsEmpty ? checked(node.Name.Length * 2 + 128) : node.RawName.Length + 128;
                }
                return true;
            }, cancellation).ConfigureAwait(false);
        }
        finally { await AnalysisIo.ReleaseScratchAsync(io, device, parents).ConfigureAwait(false); }

        // Merge the exact object-ID and alias indexes. A hostile long gap of unreferenced objects also
        // counts against the worker batch, so it cannot monopolize one admitted device delegate.
        ulong alias = 1, aliases = reader.IndexCount(AnalysisIndexedGraphRevision.Aliases), objectAt = 1;
        ulong objects = reader.IndexCount(AnalysisIndexedGraphRevision.ObjectIds);
        AnalysisSortEntry? objectEntry = null; AnalysisObject? currentObject = null;
        ulong objectFact = 1, objectFactCount = reader.IndexCount(AnalysisIndexedGraphRevision.FactOwners);
        AnalysisSortEntry? objectFactEntry = null; bool changedFacts = false;
        while (alias <= aliases)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (alias <= aliases && records < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested();
                    if (objectAt > objects) throw AnalysisIndexedGraph.Invalid();
                    var member = reader.ReadIndex(AnalysisIndexedGraphRevision.Aliases, alias, token);
                    objectEntry ??= reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, objectAt, token);
                    int order = objectEntry.Value.Key.AsSpan().SequenceCompareTo(member.Key);
                    if (order < 0)
                    {
                        if (currentObject is null)
                        {
                            var orphan = reader.ReadObject(objectEntry.Value.Value, token); coverage.Object(orphan, false);
                            bytes += (orphan.VerifiedIdentity?.Length ?? 0) * 2 + (orphan.VersionEvidence?.Length ?? 0) * 2;
                        }
                        bytes += objectEntry.Value.Key.Length + 1024; objectAt++; objectEntry = null; currentObject = null; records++; continue;
                    }
                    if (order > 0) throw AnalysisIndexedGraph.Invalid();
                    if (currentObject is null)
                    {
                        currentObject = reader.ReadObject(objectEntry.Value.Value, token); changedFacts = false; coverage.Object(currentObject, true);
                        records++; bytes += 1024 + (currentObject.VerifiedIdentity?.Length ?? 0) * 2 + (currentObject.VersionEvidence?.Length ?? 0) * 2; continue;
                    }
                    if (objectFact <= objectFactCount)
                    {
                        objectFactEntry ??= reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, objectFact, token);
                        var factEntry = objectFactEntry.Value;
                        int ownerOrder = factEntry.Key[0] == 0 ? factEntry.Key.AsSpan(1).SequenceCompareTo(objectEntry.Value.Key) : 1;
                        if (ownerOrder <= 0)
                        {
                            if (ownerOrder == 0)
                            {
                                var fact = reader.ReadFact(factEntry.Value, token); changedFacts |= fact.Measurement.State == MeasurementState.Changed;
                                bytes += 1024 + fact.Includes.Sum(s => s.Length * 3 + 4);
                            }
                            bytes += factEntry.Key.Length + 12; records++; objectFact++; objectFactEntry = null; continue;
                        }
                    }
                    var node = reader.ReadPathRow(member.Value, token);
                    if (node.ObjectId != currentObject.Id || node.ObservedIdentity is not null && currentObject.VerifiedIdentity != node.ObservedIdentity ||
                        node.MetadataState != MeasurementState.Known && (node.ObservedIdentity is not null || currentObject.VerifiedIdentity is not null || currentObject.LogicalLength.State != node.MetadataState))
                        throw AnalysisIndexedGraph.Invalid();
                    coverage.Alias(node, currentObject, changedFacts);
                    alias++; records++; bytes += member.Key.Length + node.Name.Length * 2 + node.RawName.Length + 128;
                }
                return true;
            }, cancellation).ConfigureAwait(false);

        if (currentObject is not null) objectAt++;
        while (objectAt <= objects)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (objectAt <= objects && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); var entry = reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, objectAt++, token);
                    var value = reader.ReadObject(entry.Value, token); coverage.Object(value, false);
                    bytes += 1024 + entry.Key.Length + (value.VerifiedIdentity?.Length ?? 0) * 2 + (value.VersionEvidence?.Length ?? 0) * 2;
                }
                return true;
            }, cancellation).ConfigureAwait(false);

        // Owner groups retain at most 256 bounded facts. Object owners are merged with object IDs;
        // directory owners resolve through their dense path row, avoiding a full owner dictionary.
        ulong factAt = 1, facts = reader.IndexCount(AnalysisIndexedGraphRevision.FactOwners); byte[]? owner = null;
        var components = new List<StorageFact>(); objectAt = 1; objectEntry = null;
        while (factAt <= facts)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (factAt <= facts && records < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); var entry = reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, factAt, token);
                    bool newOwner = owner is null || !owner.AsSpan().SequenceEqual(entry.Key);
                    if (newOwner && entry.Key[0] == 0)
                    {
                        if (objectAt > objects) throw AnalysisIndexedGraph.Invalid();
                        objectEntry ??= reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, objectAt, token);
                        int order = objectEntry.Value.Key.AsSpan().SequenceCompareTo(entry.Key.AsSpan(1));
                        if (order < 0) { bytes += objectEntry.Value.Key.Length + 12; objectAt++; objectEntry = null; records++; continue; }
                        if (order > 0) throw AnalysisIndexedGraph.Invalid();
                    }
                    var fact = reader.ReadFact(entry.Value, token);
                    AnalysisNode? directoryNode = null;
                    if (fact.DirectoryId is { } directoryId)
                    { if (reader.FindPathRow(directoryId, token) is null) throw AnalysisIndexedGraph.Invalid(); directoryNode = reader.ReadPath(directoryId, token); }
                    coverage.Fact(fact, directoryNode, fact.Measurement.State != MeasurementState.Changed || fact.ObjectId is null || reader.Aliases(fact.ObjectId, token).Count != 0);
                    if (fact.ObservedUtc < reader.Header.Identity.StartedUtc || fact.ObservedUtc > reader.Header.Identity.ObservedUtc) throw AnalysisIndexedGraph.Invalid();
                    if (newOwner)
                    {
                        ValidateComponents(components); components.Clear(); owner = entry.Key;
                        if (fact.DirectoryId is { } directory && reader.ReadPath(directory, token).Kind != AnalysisKind.Directory) throw AnalysisIndexedGraph.Invalid();
                    }
                    if (components.Count >= AnalysisLimits.Components) throw AnalysisIndexedGraph.Invalid();
                    components.Add(fact); factAt++; records++;
                    bytes += entry.Key.Length + fact.Includes.Sum(s => s.Length * 3 + 4) + 1024;
                }
                return true;
            }, cancellation).ConfigureAwait(false);
        // CPU-only component graph validation is bounded by the per-owner cap.
        ValidateComponents(components);

        ulong issueAt = 1, issues = reader.Count(AnalysisIndexedGraph.Issues);
        var sampledIssues = new Dictionary<MeasurementState, ulong>();
        var sampledBoundaries = new Dictionary<AnalysisBoundaryKind, ulong>();
        while (issueAt <= issues)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (issueAt <= issues && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); var issue = reader.ReadIssue(issueAt++, token);
                    sampledIssues[issue.State] = checked(sampledIssues.GetValueOrDefault(issue.State) + 1);
                    if (AnalysisCoverage.BoundaryKind(issue.Code) is { } boundary)
                        sampledBoundaries[boundary] = checked(sampledBoundaries.GetValueOrDefault(boundary) + 1);
                    bytes += issue.Code.Length * 3 + (issue.RootId?.Length ?? 0) * 3 + 32;
                    if (issue.NodeId is { } node)
                    {
                        if (reader.FindPathRow(node, token) is null) throw AnalysisIndexedGraph.Invalid();
                        var pathNode = reader.ReadPath(node, token);
                        if (issue.RootId is { } root && pathNode.RootId != root) throw AnalysisIndexedGraph.Invalid();
                        coverage.Issue(issue, pathNode.RootId);
                    }
                    else
                    {
                        if (issue.RootId is { } root && !reader.Header.Roots.Any(r => r.Id == root)) throw AnalysisIndexedGraph.Invalid();
                        coverage.Issue(issue);
                    }
                }
                return true;
            }, cancellation).ConfigureAwait(false);
        if (reader.Header.IssueTotals.Count != 0 && sampledIssues.Any(p => !reader.Header.IssueTotals.TryGetValue(p.Key, out ulong total) || total < p.Value) ||
            reader.Header.BoundaryTotals.Count != 0 && sampledBoundaries.Any(p => !reader.Header.BoundaryTotals.TryGetValue(p.Key, out ulong total) || total < p.Value))
            throw AnalysisIndexedGraph.Invalid();
        var issueTotals = reader.Header.IssueTotals.Count == 0 ? sampledIssues.ToImmutableDictionary() : reader.Header.IssueTotals;
        var boundaryTotals = reader.Header.BoundaryTotals.Count == 0 ? sampledBoundaries.ToImmutableDictionary() : reader.Header.BoundaryTotals;
        var freshness = coverage.Changed && reader.Header.Freshness == AnalysisFreshness.Observed ? AnalysisFreshness.PossiblyStale : reader.Header.Freshness;
        return reader.Header with
        {
            UniqueObjectIdentities = uniqueIdentities,
            IssueTotals = issueTotals, BoundaryTotals = boundaryTotals, Freshness = freshness,
            CoverageDetails = coverage.Complete(issueTotals, boundaryTotals, issues, freshness)
        };
    }
    private static void ValidateComponents(List<StorageFact> facts)
    { if (facts.Count != 0) AnalysisAggregate.SelectComponents(facts.ToArray()); }
}
