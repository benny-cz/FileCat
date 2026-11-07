using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    private enum EventKind : byte { Member, Charge, Reference }
    private sealed record Event(AnalysisGroupDefinition Definition, AnalysisGroupKind Kind, EventKind EventKind, ulong Path,
        AnalysisMeasurement Logical, AnalysisObjectCharge? Charge = null);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static byte[] EncodeEvent(Event item) => Encode(writer =>
    {
        writer.Write(0x31454741U); WriteDefinition(writer, item.Definition); writer.Write((byte)item.Kind); writer.Write((byte)item.EventKind);
        writer.Write(item.Path); WriteMeasurement(writer, item.Logical);
        if (item.EventKind == EventKind.Charge)
        {
            var c = item.Charge ?? throw Invalid(); writer.Write(c.Id); writer.Write(c.ParentId); writer.Write(c.RepresentativePathId);
            WriteNullable(writer, c.ObjectId); WriteNullable(writer, c.ComponentId); WriteNullable(writer, c.BasisId);
            WriteMeasurement(writer, c.Measurement); writer.Write(c.ObservedAliases);
            writer.Write((byte)((c.Shared ? 1 : 0) | (c.IdentityUnverified ? 2 : 0) | (c.DirectorySelf ? 4 : 0)));
        }
    });
    private static Event DecodeEvent(byte[] bytes) => Decode(bytes, reader =>
    {
        if (reader.ReadUInt32() != 0x31454741U) throw Invalid(); var definition = ReadDefinition(reader);
        var kind = ReadEnum<AnalysisGroupKind>(reader); var eventKind = ReadEnum<EventKind>(reader); ulong path = reader.ReadUInt64();
        var logical = ReadMeasurement(reader); AnalysisObjectCharge? charge = null;
        if (eventKind == EventKind.Charge)
        {
            ulong id = reader.ReadUInt64(), parent = reader.ReadUInt64(), representative = reader.ReadUInt64();
            string? obj = ReadNullable(reader), component = ReadNullable(reader), basis = ReadNullable(reader);
            var amount = ReadMeasurement(reader); ulong aliases = reader.ReadUInt64(); byte flags = reader.ReadByte();
            if (id == 0 || (flags & ~7) != 0) throw Invalid();
            charge = new(id, parent, representative, obj, component, basis, amount, aliases, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
        }
        else if (path == 0 || eventKind == EventKind.Member && kind != AnalysisGroupKind.Entries || eventKind == EventKind.Reference && kind == AnalysisGroupKind.Entries) throw Invalid();
        return new Event(definition, kind, eventKind, path, logical, charge);
    });
    private static byte[] EncodeSummary(AnalysisDatasetGroup item) => Encode(writer =>
    {
        writer.Write(0x31534741U); WriteDefinition(writer, item.Definition); writer.Write((byte)item.Kind);
        Write128(writer, item.Totals.Known); Write128(writer, item.Totals.IdentityUnverified); writer.Write(item.Totals.MatchingPaths);
        writer.Write(item.Totals.CompatibleBasis); WriteMissing(writer, item.Totals.Missing);
        Write128(writer, item.Logical.KnownBytes); writer.Write(item.Logical.Files); WriteMissing(writer, item.Logical.Missing);
        writer.Write(item.Members); writer.Write(item.Charges);
    });
    private static AnalysisDatasetGroup DecodeSummary(byte[] bytes) => Decode(bytes, reader =>
    {
        if (reader.ReadUInt32() != 0x31534741U) throw Invalid(); var definition = ReadDefinition(reader); var kind = ReadEnum<AnalysisGroupKind>(reader);
        UInt128 known = Read128(reader), unverified = Read128(reader); ulong files = reader.ReadUInt64(); bool compatible = reader.ReadBoolean(); var missing = ReadMissing(reader);
        UInt128 logical = Read128(reader); ulong logicalFiles = reader.ReadUInt64(); var logicalMissing = ReadMissing(reader);
        return new AnalysisDatasetGroup(definition, kind, new(known, unverified, files, missing, compatible), new(logical, logicalFiles, logicalMissing), reader.ReadUInt64(), reader.ReadUInt64());
    });
    private static void WriteDefinition(BinaryWriter writer, AnalysisGroupDefinition definition)
    { WriteText(writer, definition.Value); WriteText(writer, definition.Label); WriteText(writer, definition.Family); }
    private static AnalysisGroupDefinition ReadDefinition(BinaryReader reader)
    {
        string value = ReadText(reader, AnalysisLimits.RawNameBytes + 1024), label = ReadText(reader, 4096), family = ReadText(reader, 64);
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(label) || string.IsNullOrEmpty(family)) throw Invalid();
        return AnalysisGroupingRules.Create(value, label, family);
    }
    private static void WriteMeasurement(BinaryWriter writer, AnalysisMeasurement amount)
    { writer.Write((byte)amount.State); Write128(writer, amount.Value ?? 0); }
    private static AnalysisMeasurement ReadMeasurement(BinaryReader reader)
    {
        var state = ReadEnum<MeasurementState>(reader); UInt128 value = Read128(reader);
        if (state != MeasurementState.Known && value != 0) throw Invalid();
        return state == MeasurementState.Known ? AnalysisMeasurement.Known(value) : AnalysisMeasurement.Missing(state);
    }
    private static void WriteMissing(BinaryWriter writer, ImmutableDictionary<MeasurementState, ulong> missing)
    { for (int i = 1; i <= 5; i++) writer.Write(missing.GetValueOrDefault((MeasurementState)i)); }
    private static ImmutableDictionary<MeasurementState, ulong> ReadMissing(BinaryReader reader)
    {
        var result = ImmutableDictionary.CreateBuilder<MeasurementState, ulong>();
        for (int i = 1; i <= 5; i++) { ulong count = reader.ReadUInt64(); if (count != 0) result.Add((MeasurementState)i, count); }
        return result.ToImmutable();
    }
    private static T ReadEnum<T>(BinaryReader reader) where T : struct, Enum
    { var value = (T)Enum.ToObject(typeof(T), reader.ReadByte()); return Enum.IsDefined(value) ? value : throw Invalid(); }
    private static void Write128(BinaryWriter writer, UInt128 value) { writer.Write((ulong)value); writer.Write((ulong)(value >> 64)); }
    private static UInt128 Read128(BinaryReader reader) => reader.ReadUInt64() | (UInt128)reader.ReadUInt64() << 64;
    private static void WriteNullable(BinaryWriter writer, string? value) { writer.Write(value is not null); if (value is not null) WriteText(writer, value); }
    private static string? ReadNullable(BinaryReader reader) => reader.ReadBoolean() ? ReadText(reader, 4096) : null;
    private static void WriteText(BinaryWriter writer, string value)
    {
        byte[] bytes = Utf8.GetBytes(value); if (bytes.Length > AnalysisLimits.RawNameBytes + 1024 || value.Contains('\0')) throw Invalid();
        writer.Write(bytes.Length); writer.Write(bytes);
    }
    private static string ReadText(BinaryReader reader, int maximum)
    {
        int length = reader.ReadInt32(); if (length < 0 || length > maximum || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
        string value = Utf8.GetString(reader.ReadBytes(length)); if (value.Contains('\0')) throw Invalid(); return value;
    }
    private static byte[] Encode(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, Utf8, true)) write(writer);
        if (stream.Length > AnalysisScratchVector.MaximumRecordBytes) throw new IOException("The captured grouping record exceeds its storage budget."); return stream.ToArray();
    }
    private static T Decode<T>(byte[] bytes, Func<BinaryReader, T> read)
    {
        try
        {
            if (bytes.Length > AnalysisScratchVector.MaximumRecordBytes) throw Invalid(); using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Utf8);
            T result = read(reader); if (stream.Position != stream.Length) throw Invalid(); return result;
        }
        catch (Exception error) when (error is EndOfStreamException or DecoderFallbackException or ArgumentException or OverflowException) { throw Invalid(); }
    }
}
