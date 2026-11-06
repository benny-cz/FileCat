using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Bounded streaming graph checks; equality uses full captured keys rather than hash identity.</summary>
internal static class AnalysisIndexedGraphValidation
{
    internal static async Task ValidateAsync(AnalysisIndexedGraphReader reader, DeviceIoScheduler io, string device, CancellationToken cancellation)
    {
        reader.Header.Validate();
        foreach (int index in new[] { AnalysisIndexedGraphRevision.Children, AnalysisIndexedGraphRevision.ObjectIds, AnalysisIndexedGraphRevision.FactIds })
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
        while (path <= pathCount)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (path <= pathCount && records < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); var current = reader.ReadPathRecord(path++, token); var node = current.Node;
                    if (node.Times is { } times && (times.ObservedUtc < reader.Header.Identity.StartedUtc || times.ObservedUtc > reader.Header.Identity.ObservedUtc)) throw AnalysisIndexedGraph.Invalid();
                    if (node.ParentId == 0)
                    { if (roots[current.RootIndex]) throw AnalysisIndexedGraph.Invalid(); roots[current.RootIndex] = true; }
                    else
                    {
                        var parent = reader.ReadPathRecord(node.ParentId, token);
                        if (parent.RootIndex != current.RootIndex || parent.Depth + 1 != current.Depth || parent.Node.Kind != AnalysisKind.Directory ||
                            !parent.Node.IncludedByPolicy || parent.Node.MetadataState != MeasurementState.Known) throw AnalysisIndexedGraph.Invalid();
                    }
                    records++; bytes += node.RawName.IsEmpty ? checked(node.Name.Length * 2 + 128) : node.RawName.Length + 128;
                }
                return true;
            }, cancellation).ConfigureAwait(false);

        // Merge the exact object-ID and alias indexes. A hostile long gap of unreferenced objects also
        // counts against the worker batch, so it cannot monopolize one admitted device delegate.
        ulong alias = 1, aliases = reader.IndexCount(AnalysisIndexedGraphRevision.Aliases), objectAt = 1;
        ulong objects = reader.IndexCount(AnalysisIndexedGraphRevision.ObjectIds);
        AnalysisSortEntry? objectEntry = null; AnalysisObject? currentObject = null;
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
                    if (order < 0) { bytes += objectEntry.Value.Key.Length + 12; objectAt++; objectEntry = null; currentObject = null; records++; continue; }
                    if (order > 0) throw AnalysisIndexedGraph.Invalid();
                    currentObject ??= reader.ReadObject(objectEntry.Value.Value, token);
                    var node = reader.ReadPath(member.Value, token);
                    if (node.ObjectId != currentObject.Id || node.ObservedIdentity is not null && currentObject.VerifiedIdentity != node.ObservedIdentity ||
                        node.MetadataState != MeasurementState.Known && (node.ObservedIdentity is not null || currentObject.VerifiedIdentity is not null || currentObject.LogicalLength.State != node.MetadataState))
                        throw AnalysisIndexedGraph.Invalid();
                    alias++; records++; bytes += member.Key.Length + node.Name.Length * 2 + node.RawName.Length + 128;
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
        while (issueAt <= issues)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (issueAt <= issues && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); var issue = reader.ReadIssue(issueAt++, token);
                    bytes += issue.Code.Length * 3 + (issue.RootId?.Length ?? 0) * 3 + 32;
                    if (issue.NodeId is { } node)
                    {
                        if (node > pathCount) throw AnalysisIndexedGraph.Invalid();
                        if (issue.RootId is { } root && reader.ReadPath(node, token).RootId != root) throw AnalysisIndexedGraph.Invalid();
                    }
                    else if (issue.RootId is { } root && !reader.Header.Roots.Any(r => r.Id == root)) throw AnalysisIndexedGraph.Invalid();
                }
                return true;
            }, cancellation).ConfigureAwait(false);
    }
    private static void ValidateComponents(List<StorageFact> facts)
    { if (facts.Count != 0) AnalysisAggregate.SelectComponents(facts.ToArray()); }
}
