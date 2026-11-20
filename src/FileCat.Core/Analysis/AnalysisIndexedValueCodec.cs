using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

/// <summary>Bounded inert object/fact/issue records. Aliases are indexed separately; no supplied alias list is discarded.</summary>
internal static class AnalysisIndexedValueCodec
{
    private const uint ObjectFormat = 0x314f4e46, FactFormat = 0x31464e46, IssueFormat = 0x31494e46;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static byte[] EncodeObject(AnalysisObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ObservedAliases.IsDefault || !value.ObservedAliases.IsEmpty || value.ModifiedPrecisionTicks == 0) throw Invalid();
        return Encode(ObjectFormat, writer =>
        {
            Text(writer, value.Id, 512); Text(writer, value.VerifiedIdentity, 4096, optional: true);
            Measurement(writer, value.LogicalLength); Measurement(writer, value.LinkCount);
            OptionalDate(writer, value.ModifiedUtc); Text(writer, value.VersionEvidence, 4096, optional: true); writer.Write(value.ModifiedPrecisionTicks);
        });
    }
    internal static AnalysisObject DecodeObject(ReadOnlySpan<byte> bytes)
    {
        var reader = new Reader(bytes, ObjectFormat);
        string id = reader.Text(512)!; string? identity = reader.Text(4096, optional: true);
        var length = reader.Measurement(); var links = reader.Measurement(); var modified = reader.OptionalDate();
        string? version = reader.Text(4096, optional: true); ulong precision = reader.UInt64();
        if (precision == 0) throw Invalid(); reader.End(); return new(id, identity, length, links, modified, version, precision);
    }

    internal static byte[] EncodeFact(StorageFact value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if ((value.ObjectId is null) == (value.DirectoryId is null) || value.DirectoryId == 0 || value.Includes.IsDefault ||
            value.Includes.Length > AnalysisLimits.Components || value.Includes.Distinct(StringComparer.Ordinal).Count() != value.Includes.Length) throw Invalid();
        return Encode(FactFormat, writer =>
        {
            Text(writer, value.Id, 512); Text(writer, value.ObjectId, 512, optional: true);
            OptionalId(writer, value.DirectoryId); Text(writer, value.ComponentId, 256); Text(writer, value.BasisId, 256);
            Measurement(writer, value.Measurement); Text(writer, value.Method, 256); Date(writer, value.ObservedUtc);
            writer.Write(value.MayShareExtents); writer.Write(value.Includes.Length);
            foreach (string included in value.Includes) Text(writer, included, 256);
        });
    }
    internal static StorageFact DecodeFact(ReadOnlySpan<byte> bytes)
    {
        var reader = new Reader(bytes, FactFormat);
        string id = reader.Text(512)!; string? objectId = reader.Text(512, optional: true); ulong? directory = reader.OptionalId();
        if ((objectId is null) == (directory is null)) throw Invalid();
        string component = reader.Text(256)!, basis = reader.Text(256)!; var measurement = reader.Measurement();
        string method = reader.Text(256)!; var observed = reader.Date(); bool shared = reader.Boolean(); int count = reader.Int32();
        if (count is < 0 or > AnalysisLimits.Components) throw Invalid();
        var includes = ImmutableArray.CreateBuilder<string>(count); var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++) { string item = reader.Text(256)!; if (!unique.Add(item)) throw Invalid(); includes.Add(item); }
        reader.End(); return new(id, objectId, directory, component, basis, measurement, method, observed, includes.ToImmutable(), shared);
    }

    internal static byte[] EncodeIssue(AnalysisIssue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(value.State) || value.NodeId == 0) throw Invalid();
        return Encode(IssueFormat, writer =>
        {
            Text(writer, value.Code, 256); OptionalId(writer, value.NodeId); writer.Write((byte)value.State);
            Text(writer, value.RootId, 256, optional: true);
        });
    }
    internal static AnalysisIssue DecodeIssue(ReadOnlySpan<byte> bytes)
    {
        var reader = new Reader(bytes, IssueFormat); string code = reader.Text(256)!; ulong? node = reader.OptionalId();
        var state = (MeasurementState)reader.Byte(); if (!Enum.IsDefined(state)) throw Invalid();
        string? root = reader.Text(256, optional: true); reader.End(); return new(code, node, state) { RootId = root };
    }

    private static byte[] Encode(uint format, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
        writer.Write(format); write(writer); writer.Flush();
        if (stream.Length > AnalysisScratchVector.MaximumRecordBytes) throw Invalid(); return stream.ToArray();
    }
    private static void Text(BinaryWriter writer, string? value, int maximum, bool optional = false)
    {
        if (value is null) { if (!optional) throw Invalid(); writer.Write(-1); return; }
        if (value.Length is < 1 || value.Length > maximum || value.Contains('\0')) throw Invalid();
        byte[] bytes;
        try { if (Utf8.GetByteCount(value) > maximum) throw Invalid(); bytes = Utf8.GetBytes(value); }
        catch (EncoderFallbackException) { throw Invalid(); }
        writer.Write(bytes.Length); writer.Write(bytes);
    }
    private static void Measurement(BinaryWriter writer, AnalysisMeasurement value)
    {
        if (!Enum.IsDefined(value.State) || value.State == MeasurementState.Known != value.Value.HasValue) throw Invalid();
        writer.Write((byte)value.State);
        if (value.Value is { } number) { writer.Write((ulong)(number & ulong.MaxValue)); writer.Write((ulong)(number >> 64)); }
    }
    private static void OptionalId(BinaryWriter writer, ulong? value)
    { if (value == 0) throw Invalid(); writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value); }
    private static void Date(BinaryWriter writer, DateTimeOffset value)
    { writer.Write(value.Ticks); writer.Write(checked((short)value.Offset.TotalMinutes)); }
    private static void OptionalDate(BinaryWriter writer, DateTimeOffset? value)
    { writer.Write(value.HasValue); if (value.HasValue) Date(writer, value.Value); }
    private static InvalidDataException Invalid() => new("Invalid private analysis value record.");

    private ref struct Reader
    {
        private ReadOnlySpan<byte> _remaining;
        internal Reader(ReadOnlySpan<byte> bytes, uint format)
        {
            if (bytes.Length > AnalysisScratchVector.MaximumRecordBytes) throw Invalid();
            _remaining = bytes; if (UInt32() != format) throw Invalid();
        }
        private ReadOnlySpan<byte> Take(int count)
        { if (count < 0 || count > _remaining.Length) throw Invalid(); var result = _remaining[..count]; _remaining = _remaining[count..]; return result; }
        internal void End() { if (!_remaining.IsEmpty) throw Invalid(); }
        internal byte Byte() => Take(1)[0];
        internal bool Boolean() => Byte() switch { 0 => false, 1 => true, _ => throw Invalid() };
        internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        internal ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        internal ulong? OptionalId() { if (!Boolean()) return null; ulong value = UInt64(); if (value == 0) throw Invalid(); return value; }
        internal string? Text(int maximum, bool optional = false)
        {
            int length = Int32(); if (length == -1 && optional) return null;
            if (length < 1 || length > maximum) throw Invalid(); string value;
            try { value = Utf8.GetString(Take(length)); } catch (DecoderFallbackException) { throw Invalid(); }
            if (value.Contains('\0')) throw Invalid(); return value;
        }
        internal AnalysisMeasurement Measurement()
        {
            var state = (MeasurementState)Byte(); if (!Enum.IsDefined(state)) throw Invalid();
            if (state != MeasurementState.Known) return AnalysisMeasurement.Missing(state);
            ulong low = UInt64(), high = UInt64(); return AnalysisMeasurement.Known(((UInt128)high << 64) | low);
        }
        internal DateTimeOffset Date()
        {
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(Take(8)); short minutes = BinaryPrimitives.ReadInt16LittleEndian(Take(2));
            if (minutes is < -840 or > 840) throw Invalid();
            try { return new(ticks, TimeSpan.FromMinutes(minutes)); } catch (ArgumentException) { throw Invalid(); }
        }
        internal DateTimeOffset? OptionalDate() => Boolean() ? Date() : null;
    }
}
