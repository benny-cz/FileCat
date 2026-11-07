using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace FileCat.Core.Archives;

/// <summary>One canonical public-options encoding for every hash, request, sidecar and journal consumer.</summary>
internal static class ArchiveWriteProfileEncoding
{
    internal const int LegacyBytes = 16, MaximumBytes = 52;
    private static readonly string[] Fields = ["format", "version", "level", "dictionary", "codec", "filter", "modelOrder",
        "modelMemory", "deltaDistance", "header", "encryption", "countPower", "algorithm"];
    internal static int Length(ArchiveWriteProfile profile) => Length(profile.Version);
    private static int Length(int version) => version switch { 1 => LegacyBytes, 2 => MaximumBytes, _ => throw ArchiveEncryptionOptions.Invalid() };
    internal static int Write(ArchiveWriteProfile profile, Span<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(profile); int length = Length(profile); bytes = bytes[..length]; bytes.Clear();
        Put(bytes, 0, (int)profile.Format); Put(bytes, 1, profile.Version); Put(bytes, 2, profile.CompressionLevel); Put(bytes, 3, profile.DictionaryBytes);
        if (profile.Version == 2)
        {
            if (profile.SevenZip is { } seven)
            { Put(bytes, 4, (int)seven.Codec); Put(bytes, 5, (int)seven.Filter); Put(bytes, 6, seven.ModelOrder); Put(bytes, 7, seven.ModelMemoryBytes);
                Put(bytes, 8, seven.DeltaDistance); Put(bytes, 9, (int)seven.HeaderCompression); }
            Put(bytes, 10, (int)profile.Encryption.Mode); Put(bytes, 11, profile.Encryption.CountPower); Put(bytes, 12, profile.Rar5?.AlgorithmVersion ?? 0);
        }
        return length;
    }
    internal static void Write(BinaryWriter writer, ArchiveWriteProfile profile)
    { Span<byte> encoded = stackalloc byte[MaximumBytes]; int length = Write(profile, encoded); writer.Write(encoded[..length]); }
    internal static ArchiveWriteProfile Read(ref ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < LegacyBytes) throw ArchiveEncryptionOptions.Invalid();
        int length = Length(BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]));
        if (bytes.Length < length) throw ArchiveEncryptionOptions.Invalid();
        var profile = Decode(bytes[..length]); bytes = bytes[length..]; return profile;
    }
    internal static ArchiveWriteProfile Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < LegacyBytes || bytes.Length != Length(BinaryPrimitives.ReadInt32LittleEndian(bytes[4..])))
            throw ArchiveEncryptionOptions.Invalid();
        var format = (ArchiveWriteFormat)Value(bytes, 0); int version = Value(bytes, 1), level = Value(bytes, 2), dictionary = Value(bytes, 3);
        if (version == 1) return new(format, version, level, dictionary);
        var encryption = new ArchiveEncryptionOptions((ArchiveEncryptionMode)Value(bytes, 10), Value(bytes, 11));
        return format switch
        {
            ArchiveWriteFormat.SevenZip when Value(bytes, 12) == 0 => new(format, 2, level, dictionary,
                new((ArchiveSevenZipCodec)Value(bytes, 4), (ArchiveSevenZipFilter)Value(bytes, 5), Value(bytes, 6), Value(bytes, 7), Value(bytes, 8),
                    (ArchiveSevenZipHeaderCompression)Value(bytes, 9)), encryption: encryption),
            ArchiveWriteFormat.Rar5 when Value(bytes, 4) == 0 && Value(bytes, 5) == 0 && Value(bytes, 6) == 0 && Value(bytes, 7) == 0 && Value(bytes, 8) == 0 && Value(bytes, 9) == 0 =>
                new(format, 2, level, dictionary, rar5: new(Value(bytes, 12)), encryption: encryption),
            _ => throw ArchiveEncryptionOptions.Invalid(),
        };
    }
    internal static JsonObject ToJson(ArchiveWriteProfile profile)
    {
        Span<byte> bytes = stackalloc byte[MaximumBytes]; int length = Write(profile, bytes); var result = new JsonObject();
        for (int index = 0; index < length / 4; index++) result[Fields[index]] = BinaryPrimitives.ReadInt32LittleEndian(bytes[(index * 4)..]);
        return result;
    }
    internal static ArchiveWriteProfile FromJson(JsonObject profile)
    {
        if (profile["version"] is not JsonValue version || !version.TryGetValue<int>(out int v)) throw new ArgumentException("Invalid archive profile version.");
        int length = Length(v);
        if (profile.Count != length / 4) throw new ArgumentException("Incomplete or unknown archive profile fields.");
        Span<byte> bytes = stackalloc byte[MaximumBytes];
        for (int index = 0; index < length / 4; index++)
        {
            if (profile[Fields[index]] is not JsonValue value || !value.TryGetValue<int>(out int number)) throw new ArgumentException("Invalid archive profile option.");
            BinaryPrimitives.WriteInt32LittleEndian(bytes[(index * 4)..], number);
        }
        return Decode(bytes[..length]);
    }
    private static void Put(Span<byte> bytes, int index, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[(index * 4)..], value);
    private static int Value(ReadOnlySpan<byte> bytes, int index) => BinaryPrimitives.ReadInt32LittleEndian(bytes[(index * 4)..]);

}
