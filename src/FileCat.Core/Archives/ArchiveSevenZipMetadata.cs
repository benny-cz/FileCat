using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

/// <summary>Public coder semantics. AES properties are [work factor, salt width, IV width], never raw crypto material.</summary>
public sealed class ArchiveSevenZipCoder
{
    private readonly byte[] properties;
    public ulong Method { get; }
    public int Inputs { get; }
    public ReadOnlySpan<byte> Properties => properties;
    public ArchiveSevenZipCoder(ulong method, ReadOnlySpan<byte> publicProperties, int inputs = 1)
    {
        if (inputs != (method == 0x0303011b ? 4 : 1) || publicProperties.Length > 64) throw ArchiveSevenZipMetadata.Invalid();
        var p = publicProperties;
        bool valid = method switch
        {
            0 or 0x040108 or 0x040202 or 0x0303011b or 0x020302 or 0x020304 or
                0x03030103 or 0x03030205 or 0x03030401 or 0x03030501 or 0x03030701 or 0x03030805 => p.IsEmpty,
            3 => p.Length == 1,
            10 or 11 => p.IsEmpty || p.Length == 4 && (BinaryPrimitives.ReadUInt32LittleEndian(p) & (method == 10 ? 3u : 1u)) == 0,
            0x21 => p.Length == 1 && p[0] <= 28,
            0x030101 => p.Length == 5 && p[0] < 225 && BinaryPrimitives.ReadUInt32LittleEndian(p[1..]) <= 64 << 20,
            0x030401 => p.Length == 5 && p[0] is >= 2 and <= 64 && BinaryPrimitives.ReadUInt32LittleEndian(p[1..]) is >= 2048 and <= 32 << 20,
            0x04f71101 => p.Length == 5 && p[0] == 1 && p[1] == 5 && p[3] == 0 && p[4] == 0,
            0x06f10701 => p.Length == 3 && (p[0] <= 24 || p[0] == 63) && p[1] <= 16 && p[2] <= 16,
            _ => false,
        };
        if (!valid) throw ArchiveSevenZipMetadata.Invalid();
        Method = method; Inputs = inputs; properties = p.ToArray();
    }
}

public sealed record ArchiveSevenZipBinding(int Input, int Output);

/// <summary>Immutable non-solid coder graph and integrity-field presence, excluding packed extents, digests and keys.</summary>
public sealed class ArchiveSevenZipGraph
{
    public IReadOnlyList<ArchiveSevenZipCoder> Coders { get; }
    public IReadOnlyList<ArchiveSevenZipBinding> Bindings { get; }
    public IReadOnlyList<int> PackedInputs { get; }
    public IReadOnlyList<bool> PackedCrcPresent { get; }
    public int FinalOutput { get; }
    public bool FolderCrcPresent { get; }
    public bool MemberCrcPresent { get; }
    public bool Encrypted => Coders.Any(coder => coder.Method == 0x06f10701);
    public ArchiveSevenZipGraph(IReadOnlyList<ArchiveSevenZipCoder> coders, IReadOnlyList<ArchiveSevenZipBinding> bindings,
        IReadOnlyList<int> packedInputs, IReadOnlyList<bool> packedCrcPresent, int finalOutput,
        bool folderCrcPresent, bool memberCrcPresent)
    {
        ArgumentNullException.ThrowIfNull(coders); ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(packedInputs); ArgumentNullException.ThrowIfNull(packedCrcPresent);
        if (coders.Count is < 1 or > 32 || bindings.Count != coders.Count - 1 || packedInputs.Count is < 1 or > 128 ||
            packedInputs.Count != packedCrcPresent.Count || finalOutput < 0 || finalOutput >= coders.Count) throw ArchiveSevenZipMetadata.Invalid();
        var c = coders.ToArray(); var b = bindings.ToArray(); int[] packs = packedInputs.ToArray();
        if (c.Any(value => value is null) || b.Any(value => value is null)) throw ArchiveSevenZipMetadata.Invalid();
        int inputs = c.Sum(value => value.Inputs); if (inputs > 128 || packs.Length != inputs - b.Length) throw ArchiveSevenZipMetadata.Invalid();
        var bound = Enumerable.Repeat(-1, inputs).ToArray(); var used = new bool[c.Length]; var packed = new bool[inputs];
        foreach (var binding in b)
        {
            if (binding.Input < 0 || binding.Input >= inputs || binding.Output < 0 || binding.Output >= c.Length ||
                bound[binding.Input] >= 0 || used[binding.Output]) throw ArchiveSevenZipMetadata.Invalid();
            bound[binding.Input] = binding.Output; used[binding.Output] = true;
        }
        if (used[finalOutput] || used.Count(value => !value) != 1) throw ArchiveSevenZipMetadata.Invalid();
        foreach (int input in packs)
        { if (input < 0 || input >= inputs || bound[input] >= 0 || packed[input]) throw ArchiveSevenZipMetadata.Invalid(); packed[input] = true; }
        if (Enumerable.Range(0, inputs).Any(i => bound[i] < 0 && !packed[i])) throw ArchiveSevenZipMetadata.Invalid();
        var state = new byte[c.Length]; Visit(finalOutput); if (state.Any(value => value != 2)) throw ArchiveSevenZipMetadata.Invalid();
        Coders = new ReadOnlyCollection<ArchiveSevenZipCoder>(c); Bindings = new ReadOnlyCollection<ArchiveSevenZipBinding>(b);
        PackedInputs = new ReadOnlyCollection<int>(packs); PackedCrcPresent = new ReadOnlyCollection<bool>(packedCrcPresent.ToArray());
        FinalOutput = finalOutput; FolderCrcPresent = folderCrcPresent; MemberCrcPresent = memberCrcPresent;
        void Visit(int index)
        {
            if (state[index] == 1) throw ArchiveSevenZipMetadata.Invalid(); if (state[index] == 2) return; state[index] = 1;
            int start = 0; for (int i = 0; i < index; i++) start += c[i].Inputs;
            for (int i = start; i < start + c[index].Inputs; i++) if (bound[i] >= 0) Visit(bound[i]); state[index] = 2;
        }
    }
    internal void Write(BinaryWriter writer)
    {
        writer.Write(Coders.Count);
        foreach (var coder in Coders) { writer.Write(coder.Method); writer.Write(coder.Inputs); writer.Write(coder.Properties.Length); writer.Write(coder.Properties); }
        foreach (var binding in Bindings) { writer.Write(binding.Input); writer.Write(binding.Output); }
        writer.Write(PackedInputs.Count); foreach (int input in PackedInputs) writer.Write(input);
        foreach (bool defined in PackedCrcPresent) writer.Write(defined);
        writer.Write(FinalOutput); writer.Write(FolderCrcPresent); writer.Write(MemberCrcPresent);
    }
    internal static ArchiveSevenZipGraph Read(ref SevenZipMetadataPacket reader)
    {
        int count = reader.Count(32); if (count == 0) throw ArchiveSevenZipMetadata.Invalid();
        var coders = new ArchiveSevenZipCoder[count];
        for (int i = 0; i < count; i++) { ulong method = reader.ULong(); int inputs = reader.Int(); coders[i] = new(method, reader.Blob(64), inputs); }
        var bindings = new ArchiveSevenZipBinding[count - 1]; for (int i = 0; i < bindings.Length; i++) bindings[i] = new(reader.Int(), reader.Int());
        int n = reader.Count(128); var packed = new int[n]; for (int i = 0; i < n; i++) packed[i] = reader.Int();
        var checks = new bool[n]; for (int i = 0; i < n; i++) checks[i] = reader.Flag();
        return new(coders, bindings, packed, checks, reader.Int(), reader.Flag(), reader.Flag());
    }
}

/// <summary>Exact optional native attributes and unsigned FILETIME values. Random crypto material is never serialized.</summary>
public sealed class ArchiveSevenZipMetadata
{
    public ArchiveMemberType Type { get; }
    public uint? Attributes { get; }
    public ulong? CreatedFileTime { get; }
    public ulong? AccessedFileTime { get; }
    public ulong? ModifiedFileTime { get; }
    public ArchiveSevenZipGraph? Graph { get; }
    public ArchiveSevenZipMetadata(ArchiveMemberType type, uint? attributes = null, ulong? createdFileTime = null,
        ulong? accessedFileTime = null, ulong? modifiedFileTime = null, ArchiveSevenZipGraph? graph = null)
    {
        if (!Enum.IsDefined(type) || type == ArchiveMemberType.Directory && graph is not null) throw Invalid();
        if (attributes is { } value && (((value & 0x10) != 0) != (type == ArchiveMemberType.Directory) || (value & 0x400) != 0 ||
            (value & 0x8000) != 0 && (value >> 16 & 0xf000) is var kind && kind != 0 && kind != (type == ArchiveMemberType.Directory ? 0x4000u : 0x8000u))) throw Invalid();
        Type = type; Attributes = attributes; CreatedFileTime = createdFileTime; AccessedFileTime = accessedFileTime; ModifiedFileTime = modifiedFileTime; Graph = graph;
    }
    public byte[] Encode()
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write((ushort)1); writer.Write((int)Type); writer.Write(Attributes is not null); if (Attributes is { } attrs) writer.Write(attrs);
        Time(CreatedFileTime); Time(AccessedFileTime); Time(ModifiedFileTime); writer.Write(Graph is not null); Graph?.Write(writer);
        writer.Flush(); return output.ToArray();
        void Time(ulong? time) { writer.Write(time is not null); if (time is { } value) writer.Write(value); }
    }
    public static ArchiveSevenZipMetadata Decode(ReadOnlySpan<byte> data)
    {
        var reader = new SevenZipMetadataPacket(data); if (reader.Short() != 1) throw Invalid();
        var type = (ArchiveMemberType)reader.Int(); uint? attrs = reader.Flag() ? reader.UInt() : null;
        ulong? created = reader.Time(), accessed = reader.Time(), modified = reader.Time();
        var graph = reader.Flag() ? ArchiveSevenZipGraph.Read(ref reader) : null; reader.End(); return new(type, attrs, created, accessed, modified, graph);
    }
    internal static ArchiveWriteException Invalid() => new(ArchiveWriteReason.UnsupportedFeature, "The complete 7z semantic metadata or coder graph is invalid or unsupported.");
}

internal ref struct SevenZipMetadataPacket
{
    private ReadOnlySpan<byte> remaining;
    internal SevenZipMetadataPacket(ReadOnlySpan<byte> data)
    { if (data.Length > ArchiveWritePolicy.MaxEntryMetadataBytes) throw ArchiveWriteBudget.Limit(); remaining = data; }
    private ReadOnlySpan<byte> Take(int count)
    { if (count < 0 || count > remaining.Length) throw ArchiveSevenZipMetadata.Invalid(); var value = remaining[..count]; remaining = remaining[count..]; return value; }
    internal ushort Short() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    internal byte Byte() => Take(1)[0];
    internal int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    internal uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    internal ulong ULong() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    internal long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    internal bool Flag() => Take(1)[0] switch { 0 => false, 1 => true, _ => throw ArchiveSevenZipMetadata.Invalid() };
    internal ulong? Time() => Flag() ? ULong() : null;
    internal int Count(int maximum) { int count = Int(); if (count < 0 || count > maximum) throw ArchiveSevenZipMetadata.Invalid(); return count; }
    internal ReadOnlySpan<byte> Blob(int maximum) => Take(Count(maximum));
    internal void End() { if (!remaining.IsEmpty) throw ArchiveSevenZipMetadata.Invalid(); }
}
