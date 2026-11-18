using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal readonly record struct AnalysisNtfsObservation(UInt128 ObjectId, UInt128 ParentId, uint Attributes, ImmutableArray<byte> RawName)
{
    internal const int MaximumNameBytes = 510;

    internal AnalysisEntry ToEntry(DateTimeOffset observed)
    {
        const string encoding = "utf16le";
        bool reparse = (Attributes & (uint)FileAttributes.ReparsePoint) != 0;
        if (reparse)
            return AnalysisEntry.Unavailable([], RawName, encoding, AnalysisKind.Unknown, MeasurementState.Excluded,
                "ntfs-discovery-reparse-unclassified") with { ObservedUtc = observed };
        var kind = (Attributes & (uint)FileAttributes.Directory) != 0 ? AnalysisKind.Directory : AnalysisKind.File;
        var unsupported = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        return new([], AnalysisRawNames.Decode(encoding, RawName.AsSpan()), RawName, kind, null, unsupported, unsupported,
            unsupported, null, "", NameEncoding: encoding, ObservedUtc: observed,
            Compressed: (Attributes & (uint)FileAttributes.Compressed) != 0, Sparse: (Attributes & (uint)FileAttributes.SparseFile) != 0);
    }

    internal static bool ValidName(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumNameBytes || (bytes.Length & 1) != 0 ||
            bytes.Length == 2 && BinaryPrimitives.ReadUInt16LittleEndian(bytes) == '.' ||
            bytes.Length == 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x002E002E) return false;
        for (int index = 0; index < bytes.Length; index += 2)
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[index..]) is 0 or (ushort)'/' or (ushort)'\\' or (ushort)':')
                return false;
        return true;
    }
}

internal sealed record AnalysisNtfsChildren(ImmutableArray<AnalysisNtfsObservation> Observations, ulong? NextOffset, int AmbiguousNames);

/// <summary>Maps one approved NTFS volume's unordered discovery records in its already qualified private scratch workspace.</summary>
internal sealed class AnalysisNtfsDiscovery : IAsyncDisposable
{
    private const int IdBytes = 16, HeaderBytes = IdBytes * 2 + sizeof(uint);
    internal const int PageRecords = 256;
    private readonly AnalysisOwnedDatasetIo<Contents> _owned;
    private readonly DeviceIoScheduler _io;
    private readonly AnalysisIoDevices _device;
    private readonly UInt128 _rootId;

    internal AnalysisNtfsDiscovery(AnalysisScratchWorkspace workspace, DeviceIoScheduler io, AnalysisIoDevices device, UInt128 rootId)
    {
        if (rootId == 0) throw new ArgumentOutOfRangeException(nameof(rootId));
        var retained = workspace.Borrow();
        AnalysisScratchArena? arena = null;
        try
        {
            arena = retained.CreateArena();
            _owned = new(new Contents(retained, arena), io, device);
        }
        catch { try { arena?.Dispose(); } finally { retained.Dispose(); } throw; }
        _io = io; _device = device; _rootId = rootId;
    }

    internal Task AppendAsync(ImmutableArray<AnalysisNtfsObservation> observations, CancellationToken cancellation) =>
        _owned.RunAsync((data, token) =>
        {
            if (data.State != DiscoveryState.Receiving) throw new InvalidOperationException("The NTFS discovery input is closed.");
            try
            {
                if (observations.IsDefaultOrEmpty || observations.Length > PageRecords) throw Invalid();
                foreach (var observation in observations)
                {
                    if (observation.ObjectId == 0 || observation.ParentId == 0 || observation.ObjectId == _rootId ||
                        observation.ObjectId == observation.ParentId || !AnalysisNtfsObservation.ValidName(observation.RawName.AsSpan()))
                        throw Invalid();
                    data.Writer.Append(Encode(observation), token);
                }
                return true;
            }
            catch { data.State = DiscoveryState.Failed; throw; }
        }, cancellation);

    internal Task SealAsync(ulong expectedRecords, CancellationToken cancellation) => _owned.UseAsync(async data =>
    {
        var rows = await AnalysisIo.Run(_io, _device, token =>
        {
            if (data.State != DiscoveryState.Receiving) throw new InvalidOperationException("The NTFS discovery input is closed.");
            data.State = DiscoveryState.Failed;
            if (data.Writer.AcceptedCount != expectedRecords) throw Invalid();
            data.Writer.Flush(token);
            return data.Rows = data.Writer.CaptureCommitted();
        }, cancellation).ConfigureAwait(false);

        // A file ID has exactly one discovery record in this protocol. Multiple witnesses for the same ID
        // would let one directory appear under more than one parent. Refuse the catalog before publishing it.
        using (var ids = Keys(rows, byObjectId: true, cancellation).GetEnumerator())
        {
            var index = await AnalysisExternalSort.BuildSparseAsync(data.Workspace, _io, _device, ids, cancellation,
                duplicate: static (_, _, _) => throw Invalid()).ConfigureAwait(false);
            await AnalysisIo.ReleaseScratchAsync(_io, _device, index).ConfigureAwait(false);
        }
        using var names = Keys(rows, byObjectId: false, cancellation).GetEnumerator();
        var children = await AnalysisExternalSort.BuildSparseAsync(data.Workspace, _io, _device, names, cancellation).ConfigureAwait(false);
        try
        {
            await AnalysisIo.Run(_io, _device, _ =>
            {
                data.Children = children.Acquire();
                return true;
            }, cancellation).ConfigureAwait(false);
        }
        finally { await AnalysisIo.ReleaseScratchAsync(_io, _device, children).ConfigureAwait(false); }
        return await AnalysisIo.Run(_io, _device, _ =>
        {
            data.State = DiscoveryState.Ready;
            return true;
        }, cancellation).ConfigureAwait(false);
    });

    internal Task<AnalysisNtfsChildren> ReadChildrenAsync(UInt128 parentId, ulong offset, CancellationToken cancellation) =>
        _owned.RunAsync((data, token) =>
        {
            if (data.State != DiscoveryState.Ready) throw new InvalidOperationException("The NTFS discovery index is unavailable.");
            if (parentId == 0) throw new ArgumentOutOfRangeException(nameof(parentId));
            Span<byte> parentKey = stackalloc byte[IdBytes];
            BinaryPrimitives.WriteUInt128BigEndian(parentKey, parentId);
            var index = data.Children!;
            var range = index.PrefixRange(parentKey, token);
            if (offset > range.Count) throw new ArgumentOutOfRangeException(nameof(offset));
            ulong first = range.First + offset, end = range.First + range.Count;
            ulong next = Math.Min(end, first + PageRecords);
            var records = ImmutableArray.CreateBuilder<AnalysisNtfsObservation>();
            int ambiguous = 0;
            byte[]? previous = offset == 0 ? null : index.Read(first - 1, token).Key;
            AnalysisSortEntry? current = first < end ? index.Read(first, token) : null;
            for (ulong position = first; position < next; position++)
            {
                var item = current!.Value;
                current = position + 1 < end ? index.Read(position + 1, token) : null;
                if (item.Key.AsSpan().SequenceEqual(previous) ||
                    current is { } following && item.Key.AsSpan().SequenceEqual(following.Key)) ambiguous++;
                else records.Add(Decode(data.Rows!.Read(item.Value, token)));
                previous = item.Key;
            }
            return new AnalysisNtfsChildren(records.ToImmutable(), next < end ? next - range.First : null, ambiguous);
        }, cancellation);

    private static IEnumerable<AnalysisSortInput> Keys(AnalysisScratchVectorCheckpoint rows, bool byObjectId, CancellationToken cancellation)
    {
        for (ulong id = 1; id <= rows.Count; id++)
        {
            byte[] record = rows.Read(id, cancellation);
            byte[] key;
            if (byObjectId) key = record.AsSpan(0, IdBytes).ToArray();
            else
            {
                key = new byte[IdBytes + record.Length - HeaderBytes];
                record.AsSpan(IdBytes, IdBytes).CopyTo(key);
                record.AsSpan(HeaderBytes).CopyTo(key.AsSpan(IdBytes));
            }
            yield return new(new(key, id), record.Length);
        }
    }

    private static byte[] Encode(AnalysisNtfsObservation entry)
    {
        var row = new byte[HeaderBytes + entry.RawName.Length];
        BinaryPrimitives.WriteUInt128BigEndian(row, entry.ObjectId);
        BinaryPrimitives.WriteUInt128BigEndian(row.AsSpan(IdBytes), entry.ParentId);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(IdBytes * 2), entry.Attributes);
        entry.RawName.AsSpan().CopyTo(row.AsSpan(HeaderBytes));
        return row;
    }

    private static AnalysisNtfsObservation Decode(byte[] row) => new(BinaryPrimitives.ReadUInt128BigEndian(row),
        BinaryPrimitives.ReadUInt128BigEndian(row.AsSpan(IdBytes)), BinaryPrimitives.ReadUInt32LittleEndian(row.AsSpan(IdBytes * 2)),
        ImmutableCollectionsMarshal.AsImmutableArray(row[HeaderBytes..]));

    private static InvalidDataException Invalid() => new("The NTFS discovery records were inconsistent.");

    public ValueTask DisposeAsync() => _owned.DisposeAsync();

    private enum DiscoveryState { Receiving, Failed, Ready }

    private sealed class Contents(AnalysisScratchWorkspace workspace, AnalysisScratchArena arena) : IAnalysisScratchOwned
    {
        internal readonly AnalysisScratchWorkspace Workspace = workspace;
        internal readonly AnalysisScratchVector Writer = new(arena, ownsArena: false);
        internal AnalysisScratchVectorCheckpoint? Rows;
        internal AnalysisSortedIndexReader? Children;
        internal DiscoveryState State;

        public void Dispose()
        {
            try { Children?.Dispose(); }
            finally
            {
                try { Rows?.Dispose(); }
                finally
                {
                    try { Writer.Dispose(); }
                    finally { try { arena.Dispose(); } finally { Workspace.Dispose(); } }
                }
            }
        }
    }
}
