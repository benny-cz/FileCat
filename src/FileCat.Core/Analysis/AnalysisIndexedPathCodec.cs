using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

internal readonly record struct AnalysisIndexedPathRecord(AnalysisNode Node, int RootIndex, int Depth);

/// <summary>
/// Private path records contain observations, never traversal tickets, live binding IDs or approvals.
/// Object facts, aliases and issues belong in separate indexes; a base path cannot silently drop supplied issues.
/// Exact names are stored once as reversible native bytes. Root strings live only in the bounded revision header.
/// </summary>
internal static class AnalysisIndexedPathCodec
{
    private const uint Format = 0x31504e46; // FNP1
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static byte[] Encode(AnalysisNode node, int rootIndex, int depth, ImmutableArray<RootBinding> roots)
    {
        if (node is null || node.RawName.IsDefault || node.IssueCodes.IsDefault || !node.IssueCodes.IsEmpty ||
            node.Name is not { Length: > 0 and <= AnalysisLimits.RawNameBytes } || node.Name.Contains('\0')) throw Invalid();
        byte[] raw = node.RawName.IsEmpty ? AnalysisRawNames.Encode(node.NameEncoding, node.Name) : node.RawName.ToArray();
        if (raw.Length > AnalysisLimits.RawNameBytes || AnalysisRawNames.Decode(node.NameEncoding, raw) != node.Name) throw Invalid();
        Validate(node, rootIndex, depth, roots, raw.Length);
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Utf8, leaveOpen: true);
        writer.Write(Format); writer.Write(node.Id); writer.Write(node.ParentId); writer.Write((byte)rootIndex); writer.Write((ushort)depth);
        writer.Write((byte)node.Kind); writer.Write((byte)node.MetadataState);
        writer.Write(Flag(node.Sparse)); writer.Write(Flag(node.Compressed)); writer.Write(Flag(node.Cloud)); writer.Write(node.IncludedByPolicy);
        writer.Write(node.NameEncoding switch { "utf8" => (byte)1, "utf16le" => (byte)2, "unix-bytes" => (byte)3, _ => throw Invalid() });
        writer.Write(node.RawName.IsEmpty); writer.Write(raw.Length); writer.Write(raw);
        Text(writer, node.ObjectId, 512); Text(writer, node.ObservedIdentity, 4096);
        writer.Write(node.Times is not null);
        if (node.Times is { } times)
        {
            Date(writer, times.ObservedUtc); OptionalDate(writer, times.ModifiedUtc); writer.Write(times.ModifiedPrecisionTicks);
            OptionalDate(writer, times.CreatedUtc); if (times.CreatedUtc.HasValue) writer.Write(times.CreatedPrecisionTicks!.Value);
            OptionalDate(writer, times.ChangedUtc); if (times.ChangedUtc.HasValue) writer.Write(times.ChangedPrecisionTicks!.Value);
        }
        writer.Flush();
        if (bytes.Length > AnalysisScratchVector.MaximumRecordBytes) throw Invalid();
        return bytes.ToArray();
    }

    internal static AnalysisIndexedPathRecord Decode(ReadOnlySpan<byte> bytes, ulong expectedId, ImmutableArray<RootBinding> roots)
    {
        if (bytes.Length > AnalysisScratchVector.MaximumRecordBytes) throw Invalid();
        var reader = new Reader(bytes);
        if (reader.UInt32() != Format) throw Invalid();
        ulong id = reader.UInt64(), parent = reader.UInt64(); int root = reader.Byte(), depth = reader.UInt16();
        if (id != expectedId || roots.IsDefault || root >= roots.Length) throw Invalid();
        var kind = (AnalysisKind)reader.Byte(); var metadata = (MeasurementState)reader.Byte();
        bool? sparse = NullableFlag(reader.Byte()), compressed = NullableFlag(reader.Byte()), cloud = NullableFlag(reader.Byte());
        bool included = reader.Boolean();
        string encoding = reader.Byte() switch { 1 => "utf8", 2 => "utf16le", 3 => "unix-bytes", _ => throw Invalid() };
        bool originallyEmpty = reader.Boolean(); int length = reader.Int32();
        if (length is < 1 or > AnalysisLimits.RawNameBytes) throw Invalid();
        var raw = reader.Take(length); string name = AnalysisRawNames.Decode(encoding, raw);
        if (originallyEmpty && !AnalysisRawNames.Encode(encoding, name).AsSpan().SequenceEqual(raw)) throw Invalid();
        string? objectId = reader.Text(512), identity = reader.Text(4096);
        AnalysisObservationTimes? times = null;
        if (reader.Boolean())
        {
            var observed = reader.Date(); var modified = reader.OptionalDate(); ulong precision = reader.UInt64();
            var created = reader.OptionalDate(); ulong? createdPrecision = created.HasValue ? reader.UInt64() : null;
            var changed = reader.OptionalDate(); ulong? changedPrecision = changed.HasValue ? reader.UInt64() : null;
            times = new(observed, modified, precision, created, createdPrecision, changed, changedPrecision);
        }
        if (!reader.Empty) throw Invalid();
        var node = new AnalysisNode(id, roots[root].Id, parent, name, kind, objectId, sparse, compressed, cloud)
        {
            NameEncoding = encoding, RawName = originallyEmpty ? [] : raw.ToArray().ToImmutableArray(),
            MetadataState = metadata, IncludedByPolicy = included, ObservedIdentity = identity, Times = times
        };
        Validate(node, root, depth, roots, length);
        return new(node, root, depth);
    }

    private static void Validate(AnalysisNode node, int root, int depth, ImmutableArray<RootBinding> roots, int rawBytes)
    {
        if (roots.IsDefault || roots.Length is < 1 or > AnalysisLimits.Roots || root < 0 || root >= roots.Length ||
            node.RootId != roots[root].Id || node.Id is < 1 or > AnalysisLimits.Observations || node.ParentId >= node.Id ||
            depth is < 0 or > AnalysisLimits.Depth || (node.ParentId == 0) != (depth == 0) || !Enum.IsDefined(node.Kind) || !Enum.IsDefined(node.MetadataState) ||
            !node.IncludedByPolicy != (node.MetadataState == MeasurementState.Excluded) ||
            node.Name is not { Length: > 0 and <= AnalysisLimits.RawNameBytes } || node.Name.Contains('\0') ||
            (node.Kind == AnalysisKind.File) != (node.ObjectId is not null) || node.MetadataState != MeasurementState.Known && node.ObservedIdentity is not null)
            throw Invalid();
        if (node.ParentId == 0
            ? roots[root].Disposition != AnalysisRootDisposition.Active || roots[root].Kind != node.Kind
            : !AnalysisNames.IsObservedChild(node.Name, roots[root].NameRules == AnalysisNameRules.Windows, rawBytes)) throw Invalid();
        if (node.Times is { } times && (times.ModifiedPrecisionTicks == 0 || times.CreatedUtc.HasValue != times.CreatedPrecisionTicks.HasValue ||
            times.ChangedUtc.HasValue != times.ChangedPrecisionTicks.HasValue || times.CreatedPrecisionTicks == 0 || times.ChangedPrecisionTicks == 0)) throw Invalid();
    }

    private static byte Flag(bool? value) => value is null ? (byte)0 : value.Value ? (byte)2 : (byte)1;
    private static bool? NullableFlag(byte value) => value switch { 0 => null, 1 => false, 2 => true, _ => throw Invalid() };
    private static void Text(BinaryWriter writer, string? value, int maximum)
    {
        if (value is null) { writer.Write(-1); return; }
        if (value.Length is < 1 || value.Length > maximum || value.Contains('\0')) throw Invalid();
        byte[] text;
        try { if (Utf8.GetByteCount(value) > maximum) throw Invalid(); text = Utf8.GetBytes(value); }
        catch (EncoderFallbackException) { throw Invalid(); }
        writer.Write(text.Length); writer.Write(text);
    }
    private static void Date(BinaryWriter writer, DateTimeOffset value)
    { writer.Write(value.Ticks); writer.Write(checked((short)value.Offset.TotalMinutes)); }
    private static void OptionalDate(BinaryWriter writer, DateTimeOffset? value)
    { writer.Write(value.HasValue); if (value.HasValue) Date(writer, value.Value); }
    private static InvalidDataException Invalid() => new("Invalid private analysis path record.");

    private ref struct Reader(ReadOnlySpan<byte> remaining)
    {
        private ReadOnlySpan<byte> _remaining = remaining;
        internal bool Empty => _remaining.IsEmpty;
        internal ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || length > _remaining.Length) throw Invalid();
            var result = _remaining[..length]; _remaining = _remaining[length..]; return result;
        }
        internal byte Byte() => Take(1)[0];
        internal bool Boolean() => Byte() switch { 0 => false, 1 => true, _ => throw Invalid() };
        internal ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        internal ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        internal string? Text(int maximum)
        {
            int length = Int32(); if (length == -1) return null;
            if (length < 1 || length > maximum) throw Invalid();
            string value;
            try { value = Utf8.GetString(Take(length)); }
            catch (DecoderFallbackException) { throw Invalid(); }
            if (value.Contains('\0')) throw Invalid(); return value;
        }
        internal DateTimeOffset Date()
        {
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(Take(8)); short minutes = BinaryPrimitives.ReadInt16LittleEndian(Take(2));
            if (minutes is < -840 or > 840) throw Invalid();
            try { return new(ticks, TimeSpan.FromMinutes(minutes)); }
            catch (ArgumentException) { throw Invalid(); }
        }
        internal DateTimeOffset? OptionalDate() => Boolean() ? Date() : null;
    }
}
