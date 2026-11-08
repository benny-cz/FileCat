using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed record AnalysisInventoryStageHeader(AnalysisInventoryFormat.Manifest Historical,
    AnalysisCoverageRecord Coverage, ImmutableDictionary<MeasurementState, ulong> IssueTotals)
{
    internal AnalysisOrigin Origin => AnalysisOrigin.ImportedInventory;
    internal AnalysisTaint Taint => AnalysisPrivacy.Imported(Historical.Taint);
}

/// <summary>
/// A schema/trailer-verified private staging area, NOT a validated inventory or live source.
/// Cross-record graph, coverage and accounting checks must still precede inventory publication.
/// </summary>
internal sealed partial class AnalysisInventoryStage : IAsyncDisposable
{
    private readonly AnalysisOwnedDatasetIo<Contents> _owned;
    private readonly DeviceIoScheduler _io;
    private readonly AnalysisIoDevices _devices;
    private readonly ulong[] _counts;
    internal Guid InventoryId { get; }
    internal long SourceBytes { get; }
    internal string SourceSha256 { get; }
    internal AnalysisTaint Taint { get; }
    internal AnalysisInventoryStage(Contents data, DeviceIoScheduler io, AnalysisIoDevices devices)
    {
        InventoryId = data.Header!.Historical.Id; SourceBytes = data.SourceBytes; Taint = data.Header.Taint;
        SourceSha256 = data.SourceSha256 ?? throw new InvalidDataException("The completed source digest is absent.");
        _counts = Enumerable.Range(0, 4).Select(data.Metadata!.Count).ToArray();
        _io = io; _devices = devices; _owned = new(data, io, devices);
    }
    internal ulong Count(AnalysisInventoryFormat.RecordKind kind) => _counts[Table(kind)];
    internal Task<AnalysisInventoryStageHeader> ReadHeaderAsync(CancellationToken cancellation = default) =>
        _owned.RunAsync((data, _) => data.Header!, cancellation);
    internal Task<AnalysisInventoryFormat.DecodedRecord> ReadAsync(AnalysisInventoryFormat.RecordKind kind, ulong row,
        CancellationToken cancellation = default) => _owned.UseAsync(data => ReadRecordAsync(data, kind, row, cancellation));
    private async Task<AnalysisInventoryFormat.DecodedRecord> ReadRecordAsync(Contents data, AnalysisInventoryFormat.RecordKind kind, ulong row,
        CancellationToken cancellation)
    {
        int table = Table(kind);
            await Reads.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var locator = await AnalysisIo.Run(_io, _devices, token => data.Locator(table, row, token), cancellation).ConfigureAwait(false);
                byte[] bytes = new byte[locator.Length]; int copied = 0;
                while (copied < bytes.Length)
                {
                    int at = copied;
                    copied += await AnalysisIo.Run(_io, _devices, token =>
                    {
                        long absolute = locator.Offset + at, block = absolute / BlockBytes;
                        int within = (int)(absolute % BlockBytes), length = (int)Math.Min(BlockBytes, data.RawBytes - block * BlockBytes);
                        byte[] payload = data.Raw!.Read(new(block * BlockStride, length), token);
                        int take = Math.Min(bytes.Length - at, payload.Length - within);
                        if (take <= 0) throw Invalid();
                        payload.AsSpan(within, take).CopyTo(bytes.AsSpan(at)); return take;
                    }, cancellation).ConfigureAwait(false);
                }
                return await Task.Run(() => AnalysisInventoryFormat.DecodeStagedRecord(kind, bytes, cancellation), cancellation).ConfigureAwait(false);
            }
            finally { Reads.Release(); }
    }
    internal Task<AnalysisInventoryPathMap> BuildPathMapAsync(CancellationToken cancellation = default) =>
        _owned.UseAsync(async data =>
        {
            using var source = data.PathIdentities(cancellation).GetEnumerator();
            return await AnalysisInventoryPathMap.BuildAsync(data.Workspace!, _io, _devices, source, cancellation).ConfigureAwait(false);
        });
    public ValueTask DisposeAsync() => _owned.DisposeAsync();
    private static readonly SemaphoreSlim Reads = new(2, 2);
    internal const int BlockBytes = 64 * 1024;
    internal static readonly long BlockStride = AnalysisScratchArena.RecordEnd(new(0, BlockBytes));
    internal static int Table(AnalysisInventoryFormat.RecordKind kind) => kind is >= AnalysisInventoryFormat.RecordKind.Object and <= AnalysisInventoryFormat.RecordKind.Issue
        ? (int)kind - 1 : throw new ArgumentOutOfRangeException(nameof(kind));
    internal static InvalidDataException Invalid() => new("Invalid private inventory staging record.");
    internal readonly record struct RecordLocator(long Offset, int Length, ulong Id, ulong ParentId)
    {
        internal byte[] Encode(AnalysisInventoryFormat.RecordKind kind)
        {
            byte[] bytes = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x31534946);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), (int)kind); BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), Offset);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), Length); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(20), Id);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(28), ParentId); return bytes;
        }
        internal static RecordLocator Decode(byte[] bytes, int table, long rawBytes)
        {
            if (bytes.Length != 36 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x31534946 || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != table + 1) throw Invalid();
            var result = new RecordLocator(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(28)));
            if (result.Offset < 0 || result.Length is < 1 or > AnalysisLimits.RecordBytes || result.Offset > rawBytes - result.Length ||
                table != 1 && (result.Id != 0 || result.ParentId != 0)) throw Invalid();
            return result;
        }
    }
    internal sealed class Contents : IAnalysisScratchOwned
    {
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisScratchCheckpoint? Raw;
        internal AnalysisScratchCompositeRevision? Metadata;
        internal AnalysisInventoryStageHeader? Header;
        internal long RawBytes, SourceBytes, Reservation;
        internal string? SourceSha256;
        internal RecordLocator Locator(int table, ulong row, CancellationToken cancellation) => RecordLocator.Decode(Metadata!.Read(table, row, cancellation), table, RawBytes);
        internal IEnumerable<AnalysisInventoryPathIdentity> PathIdentities(CancellationToken cancellation)
        {
            for (ulong row = 1, count = Metadata!.Count(1); row <= count; row++)
            { cancellation.ThrowIfCancellationRequested(); var value = Locator(1, row, cancellation); yield return new(value.Id, value.ParentId); }
        }
        public void Dispose()
        {
            try { Interlocked.Exchange(ref Metadata, null)?.Dispose(); }
            finally
            {
                try { Interlocked.Exchange(ref Raw, null)?.Dispose(); }
                finally
                {
                    try { Interlocked.Exchange(ref Workspace, null)?.Dispose(); }
                    finally { Header = null; AnalysisInventoryFormat.Release(Interlocked.Exchange(ref Reservation, 0)); }
                }
            }
        }
    }
}
