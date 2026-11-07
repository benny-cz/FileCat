using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    private enum EventKind : byte { Member, Charge, Reference }
    private sealed record Event(AnalysisGroupDefinition Definition, AnalysisGroupKind Kind, EventKind EventKind, ulong Path,
        AnalysisMeasurement Logical, AnalysisObjectCharge? Charge = null, bool SharedElsewhere = false);
    private sealed record Placement(byte[] GroupHash, ulong Path, ulong Event, UInt128 Known, UInt128 Unverified,
        ImmutableDictionary<MeasurementState, ulong> Missing, bool SharedElsewhere, bool Reference);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static byte[] EncodeEvent(Event item) => Encode(writer =>
    {
        writer.Write(0x32454741U); WriteDefinition(writer, item.Definition); writer.Write((byte)item.Kind); writer.Write((byte)item.EventKind);
        writer.Write(item.Path); WriteMeasurement(writer, item.Logical); writer.Write(item.SharedElsewhere);
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
        if (reader.ReadUInt32() != 0x32454741U) throw Invalid(); var definition = ReadDefinition(reader);
        var kind = ReadEnum<AnalysisGroupKind>(reader); var eventKind = ReadEnum<EventKind>(reader); ulong path = reader.ReadUInt64();
        var logical = ReadMeasurement(reader); bool sharedElsewhere = ReadBoolean(reader); AnalysisObjectCharge? charge = null;
        if (eventKind == EventKind.Charge)
        {
            ulong id = reader.ReadUInt64(), parent = reader.ReadUInt64(), representative = reader.ReadUInt64();
            string? obj = ReadNullable(reader), component = ReadNullable(reader), basis = ReadNullable(reader);
            var amount = ReadMeasurement(reader); ulong aliases = reader.ReadUInt64(); byte flags = reader.ReadByte();
            if (id == 0 || (flags & ~7) != 0) throw Invalid();
            charge = new(id, parent, representative, obj, component, basis, amount, aliases, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
        }
        else if (path == 0 || eventKind == EventKind.Member && kind != AnalysisGroupKind.Entries || eventKind == EventKind.Reference && kind == AnalysisGroupKind.Entries) throw Invalid();
        return new Event(definition, kind, eventKind, path, logical, charge, sharedElsewhere);
    });
    private static byte[] EncodePlacement(Placement placement) => Encode(writer =>
    {
        writer.Write(0x31504741U); writer.Write(placement.GroupHash); writer.Write(placement.Path); writer.Write(placement.Event);
        Write128(writer, placement.Known); Write128(writer, placement.Unverified); WriteMissing(writer, placement.Missing);
        writer.Write(placement.SharedElsewhere); writer.Write(placement.Reference);
    });
    private static Placement DecodePlacement(byte[] bytes) => Decode(bytes, reader =>
    {
        if (reader.ReadUInt32() != 0x31504741U) throw Invalid(); var hash = reader.ReadBytes(32);
        ulong path = reader.ReadUInt64(), item = reader.ReadUInt64(); UInt128 known = Read128(reader), unverified = Read128(reader); var missing = ReadMissing(reader);
        if (hash.Length != 32 || path == 0 || item == 0) throw Invalid();
        return new Placement(hash, path, item, known, unverified, missing, ReadBoolean(reader), ReadBoolean(reader));
    });
    private static byte[] EncodeSummary(AnalysisDatasetGroup item) => Encode(writer =>
    {
        writer.Write(0x32534741U); WriteDefinition(writer, item.Definition); writer.Write((byte)item.Kind);
        Write128(writer, item.Totals.Known); Write128(writer, item.Totals.IdentityUnverified); writer.Write(item.Totals.MatchingPaths);
        writer.Write(item.Totals.CompatibleBasis); WriteMissing(writer, item.Totals.Missing);
        Write128(writer, item.Logical.KnownBytes); writer.Write(item.Logical.Files); WriteMissing(writer, item.Logical.Missing);
        writer.Write(item.Members); writer.Write(item.Charges);
        Write128(writer, item.Shared.Known); Write128(writer, item.Shared.IdentityUnverified); WriteMissing(writer, item.Shared.Missing);
    });
    private static AnalysisDatasetGroup DecodeSummary(byte[] bytes) => Decode(bytes, reader =>
    {
        if (reader.ReadUInt32() != 0x32534741U) throw Invalid(); var definition = ReadDefinition(reader); var kind = ReadEnum<AnalysisGroupKind>(reader);
        UInt128 known = Read128(reader), unverified = Read128(reader); ulong files = reader.ReadUInt64(); bool compatible = ReadBoolean(reader); var missing = ReadMissing(reader);
        UInt128 logical = Read128(reader); ulong logicalFiles = reader.ReadUInt64(); var logicalMissing = ReadMissing(reader);
        ulong members = reader.ReadUInt64(), charges = reader.ReadUInt64(); UInt128 shared = Read128(reader), sharedUnverified = Read128(reader); var sharedMissing = ReadMissing(reader);
        if (shared > known || sharedUnverified > unverified || sharedMissing.Any(p => missing.GetValueOrDefault(p.Key) < p.Value)) throw Invalid();
        return new AnalysisDatasetGroup(definition, kind, new(known, unverified, files, missing, compatible), new(logical, logicalFiles, logicalMissing), members, charges)
            { Shared = new(shared, sharedUnverified, 0, sharedMissing, compatible) };
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
    private static bool ReadBoolean(BinaryReader reader) => reader.ReadByte() switch { 0 => false, 1 => true, _ => throw Invalid() };
    private static string? ReadNullable(BinaryReader reader) => ReadBoolean(reader) ? ReadText(reader, 4096) : null;
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
