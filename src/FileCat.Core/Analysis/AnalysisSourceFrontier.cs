using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed class AnalysisFrontierDirectory(object owner, ulong record, AnalysisEntry entry, ulong path, int depth)
{
    internal object Owner { get; } = owner;
    internal ulong Record { get; } = record;
    internal AnalysisEntry Entry { get; } = entry;
    internal ulong Path { get; } = path;
    internal int Depth { get; } = depth;
}
internal sealed record AnalysisFrontierChild(AnalysisEntry Entry, ulong Path);
internal sealed class AnalysisFrontierScopeException(ulong path) : IOException("The live traversal witness exceeds its bounded payload.")
{ internal ulong Path { get; } = path; }

/// <summary>
/// A private queue for one currently held source, not an observation/inventory format. The root's
/// process-owned read witness is never serialized. Decoding records cannot create a source binding.
/// </summary>
internal sealed class AnalysisSourceFrontier : IAsyncDisposable
{
    private readonly AnalysisOwnedDatasetIo<State> _owned;
    private readonly DeviceIoScheduler _io; private readonly string _device;
    private AnalysisSourceFrontier(State state, DeviceIoScheduler io, string device) { _io = io; _device = device; _owned = new(state, io, device); }

    // Called only on the captured private workspace's admitted worker. The live backend remains held
    // by the session; neither a caller-supplied pathname nor inventory data enters this factory.
    internal static AnalysisSourceFrontier Create(AnalysisScratchWorkspace workspace, DeviceIoScheduler io, string device,
        AnalysisEntry root, RootBinding binding, ulong path)
    {
        if (root.Ancestry is not { BindingId: var id, Parent: null } witness || id == Guid.Empty ||
            !root.RelativeName.IsEmpty || !witness.RelativeName.IsEmpty || root.Kind != AnalysisKind.Directory ||
            root.MetadataState != MeasurementState.Known || !root.IncludedByPolicy || root.Cloud == true ||
            string.IsNullOrEmpty(root.Identity) || string.IsNullOrEmpty(root.Version) || witness.Identity != root.Identity || witness.Version != root.Version ||
            binding.Assurance != IdentityAssurance.Verified || path is < 1 or > AnalysisLimits.Observations)
            throw new AnalysisSourceChangedException();
        var arena = workspace.CreateArena();
        try { return new(new(arena, root, binding, path), io, device); }
        catch { arena.Dispose(); throw; }
    }
    internal Task<AnalysisFrontierDirectory?> PopAsync(CancellationToken cancellation) =>
        _owned.UseAsync(state => state.PopAsync(_io, _device, cancellation));
    internal Task PushAsync(AnalysisFrontierDirectory parent, ImmutableArray<AnalysisFrontierChild> children, CancellationToken cancellation)
    {
        if (children.IsDefault || children.Length is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(children));
        long bytes = 0;
        foreach (var child in children)
        {
            if (child?.Entry is not { } entry) throw new ArgumentException("A frontier child is missing.", nameof(children));
            bytes = checked(bytes + 512L + entry.RelativeName.Length + entry.RawName.Length + (entry.Identity?.Length ?? 0) * 2L + entry.Version.Length * 2L);
            if (bytes > AnalysisLimits.BatchBytes) throw new IOException("The queued traversal batch exceeds its payload bound.");
        }
        return _owned.RunAsync((state, token) => { state.Push(parent, children, token); return true; }, cancellation);
    }
    public ValueTask DisposeAsync() => _owned.DisposeAsync();

    private sealed class State : IAnalysisScratchOwned
    {
        private readonly AnalysisScratchVector _rows;
        private readonly AnalysisEntry _root;
        private readonly ulong _rootPath;
        private readonly object _owner = new();
        private readonly bool _windows;
        private readonly long _rootBytes;
        private ulong _head, _lastPath;
        private bool _rootPending = true;
        internal State(AnalysisScratchArena arena, AnalysisEntry root, RootBinding binding, ulong path)
        {
            _rows = new(arena); _root = root; _rootPath = _lastPath = path;
            _windows = binding.NameRules == AnalysisNameRules.Windows;
            _rootBytes = _windows ? binding.CanonicalLocator.Length * 2L : Encoding.UTF8.GetByteCount(binding.CanonicalLocator);
        }
        internal void Push(AnalysisFrontierDirectory parent, ImmutableArray<AnalysisFrontierChild> children, CancellationToken token)
        {
            Validate(parent, token); ulong head = _head, last = _lastPath;
            // Check every proof before writing any row. These witnesses came from the native backend
            // while enumerating this exact popped parent, not from the captured observation tables.
            foreach (var child in children)
            {
                var entry = child.Entry; var proof = entry.Ancestry;
                if (proof is null || proof.BindingId != _root.Ancestry!.BindingId || !ReferenceEquals(proof.Parent, parent.Entry.Ancestry) ||
                    proof.Identity != entry.Identity || proof.Version != entry.Version || !proof.RelativeName.AsSpan().SequenceEqual(entry.RelativeName.AsSpan()) ||
                    string.IsNullOrEmpty(entry.Identity) || string.IsNullOrEmpty(entry.Version) || child.Path <= last || child.Path > AnalysisLimits.Observations ||
                    entry.Kind != AnalysisKind.Directory || entry.MetadataState != MeasurementState.Known || !entry.IncludedByPolicy || entry.Cloud == true || parent.Depth >= AnalysisLimits.Depth)
                    throw new AnalysisSourceChangedException();
                byte[] raw = Raw(entry); byte[] expected = Join(parent.Entry.RelativeName.AsSpan(), raw, _windows);
                if (!entry.RelativeName.AsSpan().SequenceEqual(expected) || _rootBytes + expected.Length > AnalysisLimits.LocatorBytes)
                    throw new AnalysisSourceChangedException();
                last = child.Path;
            }
            foreach (var child in children)
            {
                token.ThrowIfCancellationRequested(); var entry = child.Entry;
                head = _rows.Append(Encode(new(parent.Record, head, child.Path, parent.Depth + 1, entry.NameEncoding, Raw(entry), entry.Identity!, entry.Version)), token);
            }
            _rows.Flush(token); _head = head; _lastPath = last;
        }
        private void Validate(AnalysisFrontierDirectory directory, CancellationToken token)
        {
            if (!ReferenceEquals(directory.Owner, _owner)) throw new InvalidOperationException("The directory belongs to another live traversal.");
            if (directory.Record == 0)
            { if (directory.Path != _rootPath || directory.Depth != 0 || !ReferenceEquals(directory.Entry, _root)) throw new AnalysisSourceChangedException(); return; }
            var row = Read(directory.Record, token); var entry = directory.Entry;
            if (row.Path != directory.Path || row.Depth != directory.Depth || row.Identity != entry.Identity || row.Version != entry.Version ||
                entry.Ancestry is not { } proof || proof.BindingId != _root.Ancestry!.BindingId || proof.Identity != row.Identity || proof.Version != row.Version ||
                !proof.RelativeName.AsSpan().SequenceEqual(entry.RelativeName.AsSpan()) || !row.Raw.AsSpan().SequenceEqual(Raw(entry)))
                throw new AnalysisSourceChangedException();
        }
        internal async Task<AnalysisFrontierDirectory?> PopAsync(DeviceIoScheduler io, string device, CancellationToken cancellation)
        {
            var first = await AnalysisIo.Run(io, device, token =>
            {
                if (_rootPending) { _rootPending = false; return (Root: true, Row: 0UL, Value: (Row?)null); }
                if (_head == 0) return (Root: false, Row: 0UL, Value: (Row?)null);
                ulong id = _head; var row = Read(id, token); _head = row.Next; return (Root: false, Row: id, Value: (Row?)row);
            }, cancellation).ConfigureAwait(false);
            if (first.Root) return new(_owner, 0, _root, _rootPath, 0);
            if (first.Value is not { } leaf) return null;
            var lineage = new List<Row> { leaf }; ulong parent = leaf.Parent; long bytes = Cost(leaf);
            while (parent != 0)
                await AnalysisIo.Run(io, device, token =>
                {
                    long batchBytes = 0; int records = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (parent != 0 && records++ < AnalysisLimits.BatchRecords && batchBytes < AnalysisLimits.BatchBytes)
                    {
                        token.ThrowIfCancellationRequested(); var row = Read(parent, token); var child = lineage[^1];
                        if (row.Depth != child.Depth - 1 || row.Path >= child.Path || lineage.Count >= AnalysisLimits.Depth) throw Invalid();
                        bytes = checked(bytes + Cost(row)); if (bytes > AnalysisLimits.PendingBytes / 4) throw new AnalysisFrontierScopeException(leaf.Path);
                        lineage.Add(row); parent = row.Parent; batchBytes += Cost(row);
                        if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            if (lineage[^1].Depth != 1 || lineage[^1].Path <= _rootPath) throw Invalid();
            lineage.Reverse(); long relativeLength = 0, prefixBytes = 0;
            foreach (var row in lineage)
            {
                relativeLength = checked(relativeLength + row.Raw.Length + (relativeLength == 0 ? 0 : _windows ? 2 : 1));
                prefixBytes = checked(prefixBytes + relativeLength);
                if (_rootBytes + relativeLength > AnalysisLimits.LocatorBytes || bytes + prefixBytes > AnalysisLimits.PendingBytes / 2)
                    throw new AnalysisFrontierScopeException(leaf.Path);
            }
            // Only CPU/owned bytes here. The root witness is the runtime capability; record decoding
            // alone never supplies its binding ID. Every later native read still revalidates the chain.
            cancellation.ThrowIfCancellationRequested(); var ancestor = _root.Ancestry!; ImmutableArray<byte> relative = [];
            foreach (var row in lineage)
            {
                cancellation.ThrowIfCancellationRequested(); relative = Join(relative.AsSpan(), row.Raw, _windows).ToImmutableArray();
                ancestor = new(ancestor.BindingId, relative, row.Identity, row.Version, ancestor);
            }
            var missing = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
            var entry = new AnalysisEntry(relative, AnalysisRawNames.Decode(leaf.Encoding, leaf.Raw), leaf.Raw.ToImmutableArray(), AnalysisKind.Directory,
                leaf.Identity, missing, missing, missing, null, leaf.Version, NameEncoding: leaf.Encoding) { Ancestry = ancestor };
            return new(_owner, first.Row, entry, leaf.Path, leaf.Depth);
        }
        private Row Read(ulong id, CancellationToken token)
        {
            var row = Decode(_rows.ReadCurrent(id, token));
            if (row.Parent >= id || row.Next >= id || row.Depth is < 1 or > AnalysisLimits.Depth || row.Path is < 1 or > AnalysisLimits.Observations ||
                (row.Depth == 1) != (row.Parent == 0) || _windows != (row.Encoding == "utf16le")) throw Invalid();
            return row;
        }
        public void Dispose() => _rows.Dispose();
    }
    private sealed record Row(ulong Parent, ulong Next, ulong Path, int Depth, string Encoding, byte[] Raw, string Identity, string Version);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static byte[] Raw(AnalysisEntry entry)
    {
        byte[] raw = entry.RawName.IsEmpty ? AnalysisRawNames.Encode(entry.NameEncoding, entry.Name) : entry.RawName.ToArray();
        if (AnalysisRawNames.Decode(entry.NameEncoding, raw) != entry.Name || !AnalysisNames.IsObservedChild(entry.Name, entry.NameEncoding == "utf16le", raw.Length)) throw Invalid();
        return raw;
    }
    private static byte[] Join(ReadOnlySpan<byte> parent, ReadOnlySpan<byte> name, bool windows)
    {
        int separator = parent.IsEmpty ? 0 : windows ? 2 : 1;
        if (parent.Length + separator + name.Length > AnalysisLimits.LocatorBytes) throw Invalid();
        var result = new byte[parent.Length + separator + name.Length]; parent.CopyTo(result);
        if (separator != 0) result[parent.Length] = windows ? (byte)'\\' : (byte)'/'; name.CopyTo(result.AsSpan(parent.Length + separator)); return result;
    }
    private static byte[] Encode(Row row)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory, Utf8, true);
        writer.Write(0x31465341); writer.Write(row.Parent); writer.Write(row.Next); writer.Write(row.Path); writer.Write(row.Depth);
        writer.Write(row.Encoding switch { "utf16le" => (byte)1, "unix-bytes" => (byte)2, "utf8" => (byte)3, _ => throw Invalid() });
        writer.Write(row.Raw.Length); writer.Write(row.Raw); Text(writer, row.Identity); Text(writer, row.Version); writer.Flush(); return memory.ToArray();
    }
    private static Row Decode(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes, false); using var reader = new BinaryReader(memory, Utf8, true);
        try
        {
            if (reader.ReadInt32() != 0x31465341) throw Invalid(); ulong parent = reader.ReadUInt64(), next = reader.ReadUInt64(), path = reader.ReadUInt64(); int depth = reader.ReadInt32();
            string encoding = reader.ReadByte() switch { 1 => "utf16le", 2 => "unix-bytes", 3 => "utf8", _ => throw Invalid() };
            int count = reader.ReadInt32(); if (count is < 1 or > AnalysisLimits.RawNameBytes) throw Invalid(); byte[] raw = reader.ReadBytes(count); if (raw.Length != count) throw Invalid();
            string identity = Text(reader), version = Text(reader); if (memory.Position != memory.Length) throw Invalid();
            if (!AnalysisNames.IsObservedChild(AnalysisRawNames.Decode(encoding, raw), encoding == "utf16le", raw.Length)) throw Invalid();
            return new(parent, next, path, depth, encoding, raw, identity, version);
        }
        catch (Exception error) when (error is EndOfStreamException or DecoderFallbackException or EncoderFallbackException) { throw new InvalidDataException("Invalid live traversal record.", error); }
    }
    private static void Text(BinaryWriter writer, string value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('\0') || Utf8.GetByteCount(value) > 4096) throw Invalid();
        byte[] bytes = Utf8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
    }
    private static string Text(BinaryReader reader)
    {
        int count = reader.ReadInt32(); if (count is < 1 or > 4096) throw Invalid(); byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) throw Invalid();
        string value = Utf8.GetString(bytes); if (value.Contains('\0')) throw Invalid(); return value;
    }
    private static long Cost(Row row) => 512L + row.Raw.Length + row.Identity.Length * 2L + row.Version.Length * 2L;
    private static InvalidDataException Invalid() => new("Invalid live traversal record.");
}
