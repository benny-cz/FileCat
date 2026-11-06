using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    internal enum RecordKind { Manifest, Object, Path, Fact, Issue, Coverage, Trailer }
    internal readonly record struct DecodedRecord(RecordKind Kind, Manifest? Manifest = null, AnalysisObject? Object = null,
        AnalysisNode? Path = null, StorageFact? Fact = null, AnalysisIssue? Issue = null, AnalysisCoverageRecord? Coverage = null,
        ImmutableDictionary<MeasurementState, ulong>? IssueTotals = null);

    /// <summary>
    /// One framed record at a time, with no stream I/O or inventory-sized collections. The caller
    /// owns framing/buffers and must confirm actual EOF via Complete before publishing any data.
    /// This validates the container, not cross-record graph/accounting relationships or authority.
    /// </summary>
    internal sealed class RecordDecoder : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly ulong[] _counts = new ulong[Kinds.Length];
        private readonly Action<int>? _admitPayload;
        private ulong _records;
        private long _bytes;
        private int _stage;
        private bool _manifest, _coverage, _trailer, _failed, _disposed;
        internal bool IsComplete { get; private set; }
        // Trusted retained-data accounting runs before typed payload allocation. A streaming caller
        // instead reserves its bounded decode/staging job before constructing this decoder.
        internal RecordDecoder(Action<int>? admitPayload = null) { _admitPayload = admitPayload; }
        private void Guard()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_failed || IsComplete) throw new InvalidOperationException("The inventory decoder is no longer accepting records.");
        }
        // line excludes exactly one LF supplied by the caller's strict bounded framing layer.
        // Returned models own their values and never retain the caller's reusable line buffer.
        internal DecodedRecord Read(ReadOnlyMemory<byte> line, CancellationToken cancellation = default)
        {
            Guard();
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (_trailer) throw Invalid("Data follows the inventory trailer.");
                if (line.Length > AnalysisLimits.RecordBytes || ++_records > AnalysisLimits.InventoryRecords ||
                    line.Length + 1L > AnalysisLimits.InventoryBytes - _bytes) throw Invalid("Inventory size limit reached.");
                _bytes += line.Length + 1L; ValidateJson(line.Span);
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = AnalysisLimits.JsonDepth });
                var r = document.RootElement; string kind = String(r.GetProperty("kind"), 32);
                if (kind == "trailer")
                {
                    Fields(r, "kind", "counts", "sha256");
                    if (!_manifest || !_coverage) throw Invalid("Incomplete inventory.");
                    var declared = r.GetProperty("counts"); Fields(declared, Kinds);
                    for (int i = 0; i < Kinds.Length; i++) if (Id(declared.GetProperty(Kinds[i])) != _counts[i]) throw Invalid("Inventory record counts do not match.");
                    if (String(r.GetProperty("sha256"), 64) != Convert.ToHexStringLower(_hash.GetHashAndReset())) throw Invalid("Inventory checksum does not match.");
                    _trailer = true; return new(RecordKind.Trailer);
                }
                int index = System.Array.IndexOf(Kinds, kind);
                if (index < 0 || index < _stage || !_manifest && index != 0 || index == 0 && _manifest || index == 5 && _coverage)
                    throw Invalid("Unknown or out-of-order inventory record.");
                _stage = index; _counts[index]++;
                if (_counts[index] > (ulong)(index switch { 1 or 2 => AnalysisLimits.Observations, 4 => AnalysisLimits.Issues, _ => AnalysisLimits.InventoryRecords }))
                    throw Invalid("Inventory kind limit reached.");
                _admitPayload?.Invoke(line.Length);
                _hash.AppendData(line.Span); _hash.AppendData(Lf);
                switch ((RecordKind)index)
                {
                    case RecordKind.Manifest: var manifest = ReadManifest(r); _manifest = true; return new(RecordKind.Manifest, Manifest: manifest);
                    case RecordKind.Object: return new(RecordKind.Object, Object: ReadObject(r));
                    case RecordKind.Path: return new(RecordKind.Path, Path: ReadNode(r));
                    case RecordKind.Fact: return new(RecordKind.Fact, Fact: ReadFact(r));
                    case RecordKind.Issue: return new(RecordKind.Issue, Issue: ReadIssue(r));
                    case RecordKind.Coverage:
                        var (coverage, totals) = ReadCoverage(r); _coverage = true;
                        return new(RecordKind.Coverage, Coverage: coverage, IssueTotals: totals);
                    default: throw Invalid();
                }
            }
            catch (Exception error)
            {
                _failed = true;
                if (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException or ArgumentException)
                    throw new InvalidDataException("Invalid analysis inventory encoding or schema.", error);
                throw;
            }
        }
        internal void Complete()
        {
            if (IsComplete && !_disposed) return;
            Guard();
            if (!_trailer) { _failed = true; throw Invalid("Inventory ended without its mandatory trailer."); }
            IsComplete = true;
        }
        public void Dispose() { if (_disposed) return; _disposed = true; _hash.Dispose(); }
    }
}
