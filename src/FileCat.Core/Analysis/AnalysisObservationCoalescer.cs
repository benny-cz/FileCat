using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Converts one coherent metadata tape into the query graph; it never enumerates or binds sources.</summary>
internal static class AnalysisObservationCoalescer
{
    // Consumes the retained raw publication on success or failure. The capture owner and workspace
    // remain caller-owned, including a retry after transient indexing failure.
    internal static async Task<AnalysisIndexedGraphRevision> BuildAsync(AnalysisIndexedGraphHeader header, AnalysisScratchCompositeRevision raw,
        AnalysisScratchWorkspace workspace, DeviceIoScheduler io, string device, CancellationToken cancellation)
    {
        var owned = new Ownership(raw);
        AnalysisIndexedGraphRevision? result = null;
        try
        {
            owned.Graph = await AnalysisIo.Run(io, device, _ =>
            {
                var arena = workspace.CreateArena();
                try { return new AnalysisIndexedGraph(arena, header); }
                catch { arena.Dispose(); throw; }
            }, cancellation).ConfigureAwait(false);
            var issues = header.IssueTotals.ToDictionary(); var boundaries = header.BoundaryTotals.ToDictionary();
            ulong staleEvents = header.StaleEvidence?.Events ?? 0;
            var staleRoots = (header.StaleEvidence?.RootIds ?? []).ToHashSet(StringComparer.Ordinal);
            ulong sampled = 0; bool changed = header.IssueTotals.GetValueOrDefault(MeasurementState.Changed) != 0;
            void Issue(string code, ulong node, MeasurementState state, CancellationToken token)
            {
                issues[state] = checked(issues.GetValueOrDefault(state) + 1);
                if (AnalysisCoverage.BoundaryKind(code) is { } boundary) boundaries[boundary] = checked(boundaries.GetValueOrDefault(boundary) + 1);
                if (AnalysisCoverage.IsStaleIssue(code, state))
                {
                    staleEvents = checked(staleEvents + 1);
                    staleRoots.Add(AnalysisIndexedPathCodec.Decode(raw.Read(AnalysisObservationCapture.Paths, node, token), node, header.Roots).Node.RootId);
                }
                if (sampled < AnalysisLimits.Issues) { owned.Graph.AppendIssue(new(code, node, state), token); sampled++; }
            }
            await CopyAsync(AnalysisObservationCapture.Issues, (bytes, token) => { owned.Graph.AppendIssue(AnalysisIndexedValueCodec.DecodeIssue(bytes), token); sampled++; }, raw, io, device, cancellation).ConfigureAwait(false);

            IEnumerable<AnalysisSortInput> Identities()
            {
                for (ulong row = 1, count = raw.Count(AnalysisObservationCapture.Files); row <= count; row++)
                {
                    cancellation.ThrowIfCancellationRequested(); byte[] bytes = raw.Read(AnalysisObservationCapture.Files, row, cancellation);
                    yield return new(new(AnalysisCapturedFileCodec.IdentityKey(AnalysisCapturedFileCodec.Decode(bytes)), row), bytes.Length);
                }
            }
            using (var source = Identities().GetEnumerator())
                owned.Identities = await AnalysisExternalSort.BuildSparseAsync(workspace, io, device, source, cancellation).ConfigureAwait(false);
            owned.IdentityReader = await AnalysisIo.Run(io, device, _ => owned.Identities.Acquire(), cancellation).ConfigureAwait(false);

            IEnumerable<AnalysisSortInput> Mappings()
            {
                byte[]? key = null; AnalysisCapturedFile? first = null; ulong lastPath = 0;
                bool logicalChanged = false, allocationChanged = false, linksChanged = false;
                void Finish()
                {
                    if (first is null) return;
                    string canonical = AnalysisObservationCapture.Id('o', first.PathId);
                    var obj = first.Object with { Id = canonical,
                        LogicalLength = logicalChanged ? AnalysisMeasurement.Missing(MeasurementState.Changed) : first.Object.LogicalLength,
                        LinkCount = linksChanged ? AnalysisMeasurement.Missing(MeasurementState.Changed) : first.Object.LinkCount };
                    var fact = first.Fact with { Id = "f" + canonical, ObjectId = canonical,
                        Measurement = logicalChanged || allocationChanged ? AnalysisMeasurement.Missing(MeasurementState.Changed) : first.Fact.Measurement };
                    owned.Graph.AppendObject(obj, cancellation); owned.Graph.AppendFact(fact, cancellation);
                    if (logicalChanged) Issue("changed-during-observation", first.PathId, MeasurementState.Changed, cancellation);
                    else if (allocationChanged) Issue("allocation-changed-during-observation", first.PathId, MeasurementState.Changed, cancellation);
                    if (linksChanged) Issue("link-count-changed-during-observation", first.PathId, MeasurementState.Changed, cancellation);
                    changed |= logicalChanged || allocationChanged || linksChanged;
                    changed |= obj.LogicalLength.State == MeasurementState.Changed || obj.LinkCount.State == MeasurementState.Changed || fact.Measurement.State == MeasurementState.Changed;
                }
                for (ulong row = 1; row <= owned.IdentityReader.Count; row++)
                {
                    cancellation.ThrowIfCancellationRequested(); var entry = owned.IdentityReader.Read(row, cancellation);
                    byte[] bytes = raw.Read(AnalysisObservationCapture.Files, entry.Value, cancellation); var current = AnalysisCapturedFileCodec.Decode(bytes);
                    if (!entry.Key.AsSpan().SequenceEqual(AnalysisCapturedFileCodec.IdentityKey(current))) throw AnalysisIndexedGraph.Invalid();
                    if (key is null || !key.AsSpan().SequenceEqual(entry.Key))
                    {
                        Finish(); key = entry.Key; first = current; lastPath = 0;
                        logicalChanged = allocationChanged = linksChanged = false;
                    }
                    if (current.PathId <= lastPath || first is null || current.Object.VerifiedIdentity != first.Object.VerifiedIdentity ||
                        current.Fact.BasisId != first.Fact.BasisId || current.Fact.ComponentId != first.Fact.ComponentId || current.Fact.Method != first.Fact.Method)
                        throw AnalysisIndexedGraph.Invalid();
                    lastPath = current.PathId;
                    logicalChanged |= current.Object.VersionEvidence != first.Object.VersionEvidence || current.Object.LogicalLength != first.Object.LogicalLength ||
                        current.Object.ModifiedUtc != first.Object.ModifiedUtc || current.Object.ModifiedPrecisionTicks != first.Object.ModifiedPrecisionTicks;
                    allocationChanged |= current.Fact.Measurement != first.Fact.Measurement;
                    linksChanged |= current.Object.LinkCount != first.Object.LinkCount;
                    byte[] path = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(path, current.PathId);
                    yield return new(new(path, first.PathId), bytes.Length + entry.Key.Length);
                }
                Finish();
            }
            using (var source = Mappings().GetEnumerator())
                owned.Mapping = await AnalysisExternalSort.BuildSparseAsync(workspace, io, device, source, cancellation).ConfigureAwait(false);
            // Full identity strings and sort runs are not retained by the immutable alias map.
            await AnalysisIo.ReleaseScratchAsync(io, device, owned.IdentityReader).ConfigureAwait(false); owned.IdentityReader = null;
            await AnalysisIo.ReleaseScratchAsync(io, device, owned.Identities).ConfigureAwait(false); owned.Identities = null;
            owned.MapReader = await AnalysisIo.Run(io, device, _ => owned.Mapping.Acquire(), cancellation).ConfigureAwait(false);

            ulong map = 1;
            await CopyAsync(AnalysisObservationCapture.Paths, (bytes, token) =>
            {
                // Dense path IDs are embedded in the authenticated record, not inferred from an object ID.
                if (bytes.Length < 12) throw AnalysisIndexedGraph.Invalid(); ulong id = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4));
                var node = AnalysisIndexedPathCodec.Decode(bytes, id, header.Roots).Node;
                if (node.Kind == AnalysisKind.File)
                {
                    if (map > owned.MapReader.Count) throw AnalysisIndexedGraph.Invalid();
                    var entry = owned.MapReader.Read(map++, token);
                    if (entry.Key.Length != 8 || BinaryPrimitives.ReadUInt64BigEndian(entry.Key) != id || entry.Value > id || node.ObjectId != AnalysisObservationCapture.Id('p', id))
                        throw AnalysisIndexedGraph.Invalid();
                    node = node with { ObjectId = AnalysisObservationCapture.Id('o', entry.Value) };
                }
                owned.Graph.AppendPath(node, token);
            }, raw, io, device, cancellation).ConfigureAwait(false);
            if (map != owned.MapReader.Count + 1) throw AnalysisIndexedGraph.Invalid();
            await AnalysisIo.ReleaseScratchAsync(io, device, owned.MapReader).ConfigureAwait(false); owned.MapReader = null;
            await AnalysisIo.ReleaseScratchAsync(io, device, owned.Mapping).ConfigureAwait(false); owned.Mapping = null;
            await CopyAsync(AnalysisObservationCapture.Directories, (bytes, token) =>
            {
                var fact = AnalysisIndexedValueCodec.DecodeFact(bytes); changed |= fact.Measurement.State == MeasurementState.Changed; owned.Graph.AppendFact(fact, token);
            }, raw, io, device, cancellation).ConfigureAwait(false);
            var final = header with { Freshness = changed && header.Freshness == AnalysisFreshness.Observed ? AnalysisFreshness.PossiblyStale : header.Freshness,
                IssueTotals = issues.ToImmutableDictionary(), BoundaryTotals = boundaries.ToImmutableDictionary(),
                StaleEvidence = new(staleEvents, staleRoots.Order(StringComparer.Ordinal).ToImmutableArray(), header.StaleEvidence?.Unlocated ?? false) };
            result = await owned.Graph.SealAsync(workspace, io, device, final, cancellation).ConfigureAwait(false); return result;
        }
        finally
        {
            try { await AnalysisIo.ReleaseScratchAsync(io, device, owned).ConfigureAwait(false); }
            catch
            {
                if (result is not null) await AnalysisIo.ReleaseScratchAsync(io, device, result).ConfigureAwait(false);
                throw;
            }
        }
    }

    private static async Task CopyAsync(int table, Action<byte[], CancellationToken> consume, AnalysisScratchCompositeRevision raw,
        DeviceIoScheduler io, string device, CancellationToken cancellation)
    {
        ulong next = 1, count = raw.Count(table);
        while (next <= count)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0, bytes = 0;
                while (next <= count && records < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested(); byte[] item = raw.Read(table, next++, token); consume(item, token); records++; bytes += item.Length;
                }
                return true;
            }, cancellation).ConfigureAwait(false);
    }
    private sealed class Ownership(AnalysisScratchCompositeRevision raw) : IAnalysisScratchOwned
    {
        internal AnalysisIndexedGraph? Graph;
        internal AnalysisSortedIndex? Identities, Mapping;
        internal AnalysisSortedIndexReader? IdentityReader, MapReader;
        public void Dispose()
        {
            try { IdentityReader?.Dispose(); MapReader?.Dispose(); Identities?.Dispose(); Mapping?.Dispose(); }
            finally { try { Graph?.Dispose(); } finally { raw.Dispose(); } }
        }
    }
}
