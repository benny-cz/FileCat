using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    // Suspended saves keep their buffers. Per-turn I/O admission alone cannot bound them.
    private static readonly SemaphoreSlim DatasetSaves = new(2, 2);
    internal static async Task<AnalysisInventoryReceipt> WriteDatasetAsync(AnalysisIndexedGraphReader reader, DeviceIoScheduler io,
        AnalysisIoDevices devices, Func<AnalysisPrivateDirectory> createDirectory, string name, Guid inventoryId, Action published, CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        object gate = new(); bool finished = false;
        void HealthChanged(string key, DeviceHealth health)
        {
            if (health != DeviceHealth.NotResponding || !devices.Contains(key)) return;
            lock (gate) { if (!finished) stop.Cancel(); }
        }
        io.HealthChanged += HealthChanged;
        try { return await WriteDatasetActualAsync(reader, io, devices, createDirectory, name, inventoryId, published, stop.Token).ConfigureAwait(false); }
        finally { io.HealthChanged -= HealthChanged; lock (gate) finished = true; }
    }
    private static async Task<AnalysisInventoryReceipt> WriteDatasetActualAsync(AnalysisIndexedGraphReader reader, DeviceIoScheduler io,
        AnalysisIoDevices devices, Func<AnalysisPrivateDirectory> createDirectory, string name, Guid inventoryId, Action published, CancellationToken cancellation)
    {
        await DatasetSaves.WaitAsync(cancellation).ConfigureAwait(false);
        AnalysisPrivateInventoryStore.DatasetWrite? output = null;
        try
        {
            using var writer = new DatasetWriter(reader, inventoryId);
            output = await AnalysisIo.Run(io, devices, token =>
            {
                token.ThrowIfCancellationRequested(); var directory = createDirectory();
                return AnalysisPrivateInventoryStore.BeginDatasetWrite(directory, name, token);
            }, cancellation).ConfigureAwait(false);
            while (!writer.Done)
                await AnalysisIo.Run(io, devices, token =>
                {
                    int bytes = 0, records = 0; var clock = Stopwatch.StartNew();
                    while (!writer.Done && bytes < AnalysisLimits.BatchBytes && records < AnalysisLimits.BatchRecords)
                    {
                        token.ThrowIfCancellationRequested();
                        if (writer.HasPending) bytes += writer.Drain(output.Stream, AnalysisLimits.BatchBytes - bytes, token);
                        else { writer.Prepare(token); records++; }
                        if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    output.FlushBatch(); return true;
                }, cancellation).ConfigureAwait(false);
            var receipt = await AnalysisIo.Run(io, devices, _ => output.Commit(inventoryId), cancellation).ConfigureAwait(false);
            published(); return receipt;
        }
        finally
        {
            // The same paired admission and actual release worker own staging and directory close,
            // including cancellation, scheduler shutdown and a published-but-uncertain result.
            try { if (output is not null) await AnalysisIo.ReleaseScratchAsync(io, devices, output).ConfigureAwait(false); }
            finally { DatasetSaves.Release(); }
        }
    }

    private sealed class DatasetWriter : IDisposable
    {
        private readonly AnalysisIndexedGraphReader _reader;
        private readonly Guid _inventoryId;
        private readonly Dictionary<string, string> _roots;
        private readonly ulong[] _counts = new ulong[Kinds.Length];
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private RecordBuffer? _pending;
        private int _offset, _stage;
        private ulong _row = 1, _records;
        private long _bytes;
        internal bool HasPending => _pending is not null;
        internal bool Done => _stage == 7 && !HasPending;
        internal DatasetWriter(AnalysisIndexedGraphReader reader, Guid inventoryId)
        {
            _reader = reader; _inventoryId = inventoryId;
            _roots = reader.Header.Roots.Select((r, i) => (r.Id, Wire: Decimal((ulong)i + 1))).ToDictionary(p => p.Id, p => p.Wire, StringComparer.Ordinal);
        }
        private string ObjectId(string id, CancellationToken cancellation)
        {
            var range = _reader.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: cancellation);
            if (range.Count != 1) throw Invalid("The captured object reference is ambiguous.");
            return Decimal(_reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, cancellation).Value);
        }
        internal void Prepare(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (HasPending) throw new InvalidOperationException("Drain the current inventory record before reading another.");
            var header = _reader.Header;
            while (_stage is >= 1 and <= 4 && _row > _reader.Count(_stage switch { 1 => AnalysisIndexedGraph.Objects, 2 => AnalysisIndexedGraph.Paths, 3 => AnalysisIndexedGraph.Facts, _ => AnalysisIndexedGraph.Issues }))
            { _stage++; _row = 1; }
            switch (_stage)
            {
                case 0:
                    var snapshot = new AnalysisSnapshot(header.Identity, header.Roots, [], [], [], [], header.Policy, header.Phase, header.Coverage, header.Freshness, header.Origin, header.Taint);
                    CheckText(header.Identity.HostProfile, header.Identity.TimeZoneId);
                    CheckText(header.Policy.AnalysisMode, header.Policy.ComponentPreset, header.Policy.MountPolicy, header.Policy.PackagePolicy, header.Policy.CloudPolicy);
                    CheckText(header.Policy.BasisIds.ToArray()); CheckText(header.Policy.Exclusions.ToArray()); CheckText(header.Policy.Limits.Keys.ToArray());
                    foreach (var root in header.Roots) CheckText(root.Id, root.Scheme, root.SourceIdentity, root.TraversalBoundary, root.ConnectionIdentityReference, root.CoveredByRootId, root.ObservedObjectIdentity);
                    Record(0, w => WriteManifest(w, snapshot, _inventoryId, id => _roots[id])); _stage = 1; break;
                case 1:
                    var obj = _reader.ReadObject(_row, cancellation); CheckText(obj.Id, obj.VerifiedIdentity, obj.VersionEvidence);
                    string objectId = Decimal(_row++); Record(1, w => WriteObject(w, obj, objectId)); break;
                case 2:
                    var path = _reader.ReadPathRow(_row++, cancellation); CheckText(path.RootId, path.ObjectId, path.ObservedIdentity, path.NameEncoding);
                    Record(2, w => WritePath(w, path, id => _roots[id], id => ObjectId(id, cancellation))); break;
                case 3:
                    var fact = _reader.ReadFact(_row, cancellation); CheckText(fact.Id, fact.ObjectId, fact.ComponentId, fact.BasisId, fact.Method); CheckText(fact.Includes.ToArray());
                    ulong factId = _row++; Record(3, w => WriteFact(w, fact, factId, id => ObjectId(id, cancellation))); break;
                case 4:
                    var issue = _reader.ReadIssue(_row++, cancellation); CheckText(issue.Code, issue.RootId);
                    Record(4, w => WriteIssue(w, issue, id => _roots[id])); break;
                case 5:
                    var full = header.CoverageDetails ?? throw Invalid("Full indexed coverage is unavailable.");
                    var coverage = full.Observations with { Qualification = new(full.EnumerationKnown, full.EligibleDirectories, full.StaleScopesComplete, full.HasUnlocatedStaleScopes) };
                    Record(5, w => WriteCoverage(w, coverage, header.IssueTotals, id => _roots[id])); _stage = 6; break;
                case 6:
                    string digest = Convert.ToHexStringLower(_hash.GetHashAndReset());
                    Emit(w =>
                    {
                        w.WriteString("kind", "trailer"); w.WriteStartObject("counts");
                        for (int i = 0; i < Kinds.Length; i++) Number(w, Kinds[i], _counts[i]);
                        w.WriteEndObject(); w.WriteString("sha256", digest);
                    }, false); _stage = 7; break;
                default: throw new InvalidOperationException("The inventory writer is complete.");
            }
        }
        private void Record(int kind, Action<Utf8JsonWriter> write)
        { _counts[kind]++; Emit(w => { w.WriteString("kind", Kinds[kind]); write(w); }, true); }
        private void Emit(Action<Utf8JsonWriter> write, bool hashed)
        {
            var buffer = new RecordBuffer();
            try
            {
                using (var json = new Utf8JsonWriter(buffer)) { json.WriteStartObject(); write(json); json.WriteEndObject(); }
                var line = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)); ValidateJson(line);
                if (++_records > AnalysisLimits.InventoryRecords || line.Length + 1L > AnalysisLimits.InventoryBytes - _bytes) throw Invalid("Inventory size limit reached.");
                _bytes += line.Length + 1L;
                if (hashed) { _hash.AppendData(line); _hash.AppendData(Lf); }
                _pending = buffer; _offset = 0;
            }
            catch { buffer.Dispose(); throw; }
        }
        internal int Drain(Stream destination, int maximum, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); var pending = _pending ?? throw new InvalidOperationException();
            int length = checked((int)pending.Length);
            if (_offset < length)
            {
                int count = Math.Min(Math.Min(64 * 1024, maximum), length - _offset);
                destination.Write(pending.GetBuffer().AsSpan(_offset, count)); _offset += count; return count;
            }
            destination.Write(Lf); pending.Dispose(); _pending = null; return 1;
        }
        public void Dispose() { _pending?.Dispose(); _pending = null; _hash.Dispose(); }
    }
}
