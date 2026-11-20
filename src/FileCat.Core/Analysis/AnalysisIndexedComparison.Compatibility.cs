using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    private sealed class SourceInfo(AnalysisDatasetDescriptor descriptor, AnalysisIndexedGraphReader graph, AnalysisDatasetDescriptor peer)
    {
        internal readonly byte[] Encodings = new byte[descriptor.Roots.Length];
        internal bool GoodNodes = true, HasFiles, FileBasesAcceptedByPeer = true;
        internal ulong FileFacts;
        internal AnalysisSortedIndexReader? Bases;
        internal bool Complete => descriptor.Phase == AnalysisPhase.Finished && descriptor.Coverage == NamespaceCoverage.CompleteUnderPolicy &&
            descriptor.Freshness == AnalysisFreshness.Observed && GoodNodes &&
            descriptor.IssueTotals.All(pair => pair.Key is not (MeasurementState.Failed or MeasurementState.Changed or MeasurementState.Pending) || pair.Value == 0) &&
            descriptor.CoverageDetails is { EnumerationKnown: true, StaleScopesComplete: true, HasUnlocatedStaleScopes: false, Observations: var c } &&
            c.DenominatorFinal && c.EnumerationFailed == 0 && c.EnumerationPending == 0 && c.FailedIssues == 0 && c.PendingIssues == 0 && c.StaleScopeNodes.IsEmpty && c.StaleRootIds.IsEmpty;
        internal IEnumerable<AnalysisSortInput> Entries(CancellationToken token, bool includeBases)
        {
            for (ulong row = 1; row <= descriptor.Paths; row++)
            {
                token.ThrowIfCancellationRequested(); var p = graph.ReadPathRowRecord(row, token); var node = p.Node;
                Encodings[p.RootIndex] |= node.NameEncoding switch { "utf8" => (byte)1, "utf16le" => (byte)2, "unix-bytes" => (byte)4, _ => throw AnalysisIndexedGraph.Invalid() };
                if (node.IncludedByPolicy)
                {
                    HasFiles |= node.Kind is AnalysisKind.File or AnalysisKind.Unknown;
                    GoodNodes &= node.Kind != AnalysisKind.Unknown && node.MetadataState is not (MeasurementState.Failed or MeasurementState.Changed or MeasurementState.Pending);
                }
                yield return new(null, checked((int)(1024L + node.Name.Length * 2L + node.RawName.Length)));
            }
            if (!includeBases) yield break;
            for (ulong row = 1; row <= descriptor.Facts; row++)
            {
                token.ThrowIfCancellationRequested(); var fact = graph.ReadFact(row, token);
                if (fact.ObjectId is not null) { FileFacts++; FileBasesAcceptedByPeer &= peer.Policy.BasisIds.Contains(fact.BasisId, StringComparer.Ordinal); }
                yield return new(new(BasisHash(fact), row), FactCost(fact));
            }
        }
        private ulong _lastBasisRow;
        private StorageFact? _lastBasis;
        internal bool Duplicate(AnalysisSortEntry previous, AnalysisSortEntry current, CancellationToken token)
        {
            if (_lastBasisRow != previous.Value) { _lastBasis = graph.ReadFact(previous.Value, token); _lastBasisRow = previous.Value; }
            return SameBasis(_lastBasis!, graph.ReadFact(current.Value, token));
        }
        internal AnalysisIndexedGraphReader Graph => graph;
    }
    private static readonly UTF8Encoding BasisUtf8 = new(false, true);
    private static byte[] BasisHash(StorageFact fact)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Field(string value)
        {
            byte[] bytes = BasisUtf8.GetBytes(value); Span<byte> size = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(size, bytes.Length); hash.AppendData(size); hash.AppendData(bytes);
        }
        Field(fact.ComponentId); Field(fact.BasisId);
        foreach (string included in fact.Includes.Order(StringComparer.Ordinal)) Field(included);
        return [fact.DirectoryId.HasValue ? (byte)1 : (byte)0, .. hash.GetHashAndReset()];
    }
    private static bool SameBasis(StorageFact a, StorageFact b) => a.DirectoryId.HasValue == b.DirectoryId.HasValue &&
        a.ComponentId == b.ComponentId && a.BasisId == b.BasisId && a.Includes.Order(StringComparer.Ordinal).SequenceEqual(b.Includes.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    private static int FactCost(StorageFact f) => checked((int)(1024L + (f.ComponentId.Length + f.BasisId.Length + f.Method.Length) * 2L + f.Includes.Sum(x => x.Length * 2L + 32)));

    private sealed class BasisSubset(SourceInfo source, SourceInfo target)
    {
        private ulong _at = 1, _candidate;
        private AnalysisIndexRange _range;
        private StorageFact? _fact;
        private byte _kind;
        internal bool Finished;
        internal bool Files = true, Directories = true;
        internal int Step(CancellationToken token)
        {
            if (_fact is null)
            {
                if (_at > source.Bases!.Count) { Finished = true; return 1; }
                var entry = source.Bases.Read(_at++, token); _kind = entry.Key[0];
                if (_kind == 0 && !Files || _kind == 1 && !Directories) return 64;
                _fact = source.Graph.ReadFact(entry.Value, token); _range = target.Bases!.FindRange(entry.Key, token); _candidate = 0;
                return FactCost(_fact);
            }
            if (_candidate == _range.Count)
            { if (_kind == 0) Files = false; else Directories = false; _fact = null; return 1; }
            var other = target.Graph.ReadFact(target.Bases!.Read(_range.First + _candidate++, token).Value, token);
            if (SameBasis(_fact, other)) _fact = null;
            return FactCost(other);
        }
    }
    private static async Task<AnalysisComparisonCompatibility> CompatibilityAsync(SourceInfo previous, SourceInfo current,
        AnalysisDatasetDescriptor a, AnalysisDatasetDescriptor b, AnalysisMetric metric, AnalysisTotals at, AnalysisTotals bt,
        ImmutableDictionary<string, string> map, bool structural, DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        bool basis = true;
        if (metric == AnalysisMetric.OnDisk)
        {
            var ab = new BasisSubset(previous, current); var ba = new BasisSubset(current, previous);
            while (!ab.Finished || !ba.Finished)
                await AnalysisIo.Run(io, devices, token =>
                {
                    int records = 0, bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while ((!ab.Finished || !ba.Finished) && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += !ab.Finished ? ab.Step(token) : ba.Step(token); if (clock.ElapsedMilliseconds >= 20) break; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            bool files = ab.Files && ba.Files || previous.FileFacts == 0 && !previous.HasFiles && a.Policy.BasisIds.Length > 0 && current.FileBasesAcceptedByPeer ||
                current.FileFacts == 0 && !current.HasFiles && b.Policy.BasisIds.Length > 0 && previous.FileBasesAcceptedByPeer;
            basis = at.CompatibleBasis && bt.CompatibleBasis && files && ab.Directories && ba.Directories;
        }
        bool roots = map.Count == a.Roots.Count(r => r.Disposition == AnalysisRootDisposition.Active) && map.Count == b.Roots.Count(r => r.Disposition == AnalysisRootDisposition.Active);
        static int Ordinal(ImmutableArray<RootBinding> roots, string id)
        { for (int n = 0; n < roots.Length; n++) if (roots[n].Id == id) return n; throw AnalysisIndexedGraph.Invalid(); }
        bool names = map.All(pair => previous.Encodings[Ordinal(a.Roots, pair.Key)] == current.Encodings[Ordinal(b.Roots, pair.Value)]);
        bool identity = !structural && a.Identity.HostProfileIdentity is { HasHostContext: true } host && host == b.Identity.HostProfileIdentity &&
            map.All(pair => AnalysisComparison.SameCapturedBinding(a.Roots.Single(r => r.Id == pair.Key), b.Roots.Single(r => r.Id == pair.Value)));
        bool policy = a.Policy.Fingerprint == b.Policy.Fingerprint;
        var reasons = ImmutableArray.CreateBuilder<string>();
        if (!policy) reasons.Add("Scan policies differ. Values are shown side by side; affected differences are unknown.");
        if (!basis) reasons.Add("Allocation components or bases differ. No combined allocation difference is inferred.");
        if (!roots) reasons.Add("Only some captured roots match. Unmapped observations and the total difference are unknown.");
        if (!names) reasons.Add("Raw name representations differ between these roots. Missing entries and differences are unknown.");
        if (structural) reasons.Add("Explicit structural comparison. Matching paths do not establish object identity or continuity.");
        else if (!identity) reasons.Add("Path-based comparison. Captured host/profile and source-binding continuity are not established.");
        if (!previous.Complete) reasons.Add("Earlier coverage is incomplete or stale. Missing earlier entries are previously unknown, not additions.");
        if (!current.Complete) reasons.Add("Current coverage is incomplete or stale. Missing current entries are unknown, not removals.");
        if (a.Identity.ObservedUtc > b.Identity.ObservedUtc) reasons.Add("The chosen previous inventory was observed later. Differences follow the selected Previous/Current order.");
        reasons.Add("All values are metadata observations over an interval. Unchanged metadata does not prove unchanged content.");
        return new(policy, basis, roots && names, identity, previous.Complete, current.Complete, reasons.ToImmutable());
    }
}
