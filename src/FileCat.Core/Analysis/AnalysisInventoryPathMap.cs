using System.Buffers.Binary;
using System.Diagnostics;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal readonly record struct AnalysisInventoryPathIdentity(ulong Id, ulong ParentId);
internal readonly record struct AnalysisInventoryPathMapping(ulong Row, ulong Id, ulong ParentId, ulong ParentRow);

/// <summary>
/// Exact original-inventory-ID to storage-row mapping. Rows retain file order; IDs need not be
/// consecutive or numerically ordered. This data-only import stage grants no filesystem authority.
/// </summary>
internal sealed class AnalysisInventoryPathMap : IAsyncDisposable
{
    private readonly AnalysisOwnedDatasetIo<Contents> _owned;
    internal ulong Count { get; }
    private AnalysisInventoryPathMap(Contents contents, DeviceIoScheduler io, AnalysisIoDevices device)
    { Count = contents.Rows!.Count; _owned = new(contents, io, device); }

    internal Task<ulong?> ResolveAsync(ulong originalId, CancellationToken cancellation = default)
    {
        if (originalId == 0) throw new ArgumentOutOfRangeException(nameof(originalId));
        return _owned.RunAsync((data, token) => data.Resolve(originalId, token), cancellation);
    }
    internal Task<AnalysisInventoryPathMapping> ReadAsync(ulong row, CancellationToken cancellation = default) =>
        _owned.RunAsync((data, token) =>
        {
            var identity = Decode(data.Rows!.Read(row, token));
            ulong parent = identity.ParentId == 0 ? 0 : data.Resolve(identity.ParentId, token) ?? throw Invalid();
            return new AnalysisInventoryPathMapping(row, identity.Id, identity.ParentId, parent);
        }, cancellation);
    public ValueTask DisposeAsync() => _owned.DisposeAsync();

    // The caller owns source. It is trusted import code reading captured input/staging on these
    // device keys, never code or path/device instructions reconstructed from inventory records.
    internal static async Task<AnalysisInventoryPathMap> BuildAsync(AnalysisScratchWorkspace workspace, DeviceIoScheduler io,
        AnalysisIoDevices device, IEnumerator<AnalysisInventoryPathIdentity> source, CancellationToken cancellation = default,
        int runMemoryBytes = AnalysisExternalSort.RunMemoryBytes)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(device); ArgumentNullException.ThrowIfNull(source);
        if (runMemoryBytes is < 1024 or > AnalysisExternalSort.RunMemoryBytes) throw new ArgumentOutOfRangeException(nameof(runMemoryBytes));
        // Cover suspended input and validation as well as the sort's own admitted lifetime.
        await Builds.WaitAsync(cancellation).ConfigureAwait(false);
        var data = new Contents(); bool transferred = false;
        try
        {
            await AnalysisIo.Run(io, device, _ =>
            {
                data.Arena = workspace.CreateArena(); data.Writer = new(data.Arena, ownsArena: false); return true;
            }, cancellation).ConfigureAwait(false);
            bool finished = false;
            while (!finished)
                await AnalysisIo.Run(io, device, token =>
                {
                    int records = 0; var clock = Stopwatch.StartNew();
                    while (records++ < AnalysisLimits.BatchRecords)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!source.MoveNext()) { finished = true; break; }
                        var item = source.Current;
                        if (item.Id == 0 || item.Id == item.ParentId) throw Invalid();
                        data.Writer!.Append(Encode(item), token);
                        if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            data.Rows = await AnalysisIo.Run(io, device, token =>
            {
                data.Writer!.Flush(token); return data.Writer.CaptureCommitted();
            }, cancellation).ConfigureAwait(false);
            using (var entries = Entries(data.Rows, cancellation).GetEnumerator())
                data.Index = await AnalysisExternalSort.BuildAsync(workspace, io, device, entries, cancellation, runMemoryBytes).ConfigureAwait(false);
            await AnalysisIo.Run(io, device, _ =>
            {
                data.Sequential = data.Index.Acquire(); data.Lookup = data.Index.Acquire(); return true;
            }, cancellation).ConfigureAwait(false);
            ulong position = 1; ulong? previous = null;
            while (position <= data.Rows.Count)
                await AnalysisIo.Run(io, device, token =>
                {
                    int records = 0; var clock = Stopwatch.StartNew();
                    while (position <= data.Rows.Count && records++ < AnalysisLimits.BatchRecords)
                    {
                        token.ThrowIfCancellationRequested(); var entry = data.Sequential!.Read(position++, token);
                        if (entry.Key.Length != 8) throw Invalid();
                        ulong id = BinaryPrimitives.ReadUInt64BigEndian(entry.Key);
                        if (previous is { } prior && id <= prior) throw Invalid();
                        previous = id;
                        var identity = Decode(data.Rows.Read(entry.Value, token));
                        if (identity.Id != id) throw Invalid();
                        if (identity.ParentId != 0 && (data.Resolve(identity.ParentId, token) is not { } parent || parent >= entry.Value)) throw Invalid();
                        if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            // Retained immutable rows/readers own their native arenas after construction closes.
            await AnalysisIo.Run(io, device, _ =>
            {
                data.Sequential!.Dispose(); data.Sequential = null;
                data.Index.Dispose(); data.Index = null;
                data.Writer!.Dispose(); data.Writer = null;
                data.Arena!.Dispose(); data.Arena = null; return true;
            }, cancellation).ConfigureAwait(false);
            var result = new AnalysisInventoryPathMap(data, io, device); transferred = true; return result;
        }
        finally
        {
            try { if (!transferred) await AnalysisIo.ReleaseScratchAsync(io, device, data).ConfigureAwait(false); }
            finally { Builds.Release(); }
        }
    }

    private static readonly SemaphoreSlim Builds = new(AnalysisLimits.ActiveScans, AnalysisLimits.ActiveScans);
    private static byte[] Encode(AnalysisInventoryPathIdentity identity)
    {
        byte[] bytes = new byte[16]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, identity.Id);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), identity.ParentId); return bytes;
    }
    private static AnalysisInventoryPathIdentity Decode(byte[] bytes)
    {
        if (bytes.Length != 16) throw Invalid();
        return new(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)));
    }
    private static byte[] Key(ulong id)
    { byte[] key = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(key, id); return key; }
    private static IEnumerable<AnalysisSortEntry> Entries(AnalysisScratchVectorCheckpoint rows, CancellationToken cancellation)
    {
        for (ulong row = 1; row <= rows.Count; row++)
        { cancellation.ThrowIfCancellationRequested(); yield return new(Key(Decode(rows.Read(row, cancellation)).Id), row); }
    }
    private static InvalidDataException Invalid() => new("Invalid inventory path identity or parent order.");
    private sealed class Contents : IAnalysisScratchOwned
    {
        internal AnalysisScratchArena? Arena;
        internal AnalysisScratchVector? Writer;
        internal AnalysisScratchVectorCheckpoint? Rows;
        internal AnalysisSortedIndex? Index;
        internal AnalysisSortedIndexReader? Sequential, Lookup;
        internal ulong? Resolve(ulong id, CancellationToken cancellation)
        {
            var range = Lookup!.FindRange(Key(id), cancellation);
            if (range.Count == 0) return null;
            if (range.Count != 1) throw Invalid();
            return Lookup.Read(range.First, cancellation).Value;
        }
        public void Dispose()
        {
            try { Sequential?.Dispose(); }
            finally
            {
                try { Lookup?.Dispose(); }
                finally
                {
                    try { Index?.Dispose(); }
                    finally { try { Rows?.Dispose(); } finally { try { Writer?.Dispose(); } finally { Arena?.Dispose(); } } }
                }
            }
        }
    }
}
