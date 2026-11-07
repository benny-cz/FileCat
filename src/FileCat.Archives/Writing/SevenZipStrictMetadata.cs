using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record SevenZipRawPack(long Offset, long Length, uint? Crc32);
internal sealed record SevenZipRawFolder(IReadOnlyList<SevenZipCoder> Coders, IReadOnlyList<SevenZipBinding> Bindings,
    IReadOnlyList<int> PackedInputs, IReadOnlyList<long> UnpackedSizes, int FinalOutput, uint? Crc32, int FirstPack)
{
    internal long Length => UnpackedSizes[FinalOutput];
    internal bool Encrypted => Coders.Any(coder => SevenZipStrictMetadata.Method(coder) == 0x06f10701);
}
internal sealed record SevenZipRawStreams(IReadOnlyList<SevenZipRawPack> Packed, IReadOnlyList<SevenZipRawFolder> Folders,
    IReadOnlyList<uint?> MemberCrc32, bool SubstreamsPresent, bool SubstreamCountsPresent, bool SubstreamSizesPresent, bool SubstreamCrcPresent)
{
    internal static SevenZipRawStreams Empty { get; } = new([], [], [], false, false, false, false);
}
internal sealed record SevenZipRawMember(string Name, ArchiveMemberType Type, int Occurrence, int? Folder,
    long Length, uint? Crc32, uint? Attributes, ulong? CreatedFileTime, ulong? AccessedFileTime, ulong? ModifiedFileTime);
internal sealed record SevenZipRawFiles(IReadOnlyList<SevenZipRawMember> Members, uint PropertyMask, long DummyBytes);

/// <summary>
/// Exact non-solid metadata observations, independent from tolerant browsing and its DateTime projections.
/// No source opening, decoding, preservation assessment, publication or deletion authority.
/// </summary>
internal static class SevenZipStrictMetadata
{
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);
    internal const int MaximumCoders = 32;
    internal const int MaximumPackedStreams = 4 * ArchiveWritePolicy.MaxMembers;

    internal static (SevenZipRawStreams Streams, SevenZipRawFiles Files, bool MainStreamsPresent) Header(
        ReadOnlySpan<byte> bytes, ArchiveWriteBudget budget, CancellationToken ct)
    {
        var reader = new SevenZipStrictHeaderReader(bytes); reader.Require(1);
        byte tag = reader.Tag(); bool main = tag == 4;
        var streams = main ? Streams(ref reader, budget, ct) : SevenZipRawStreams.Empty;
        if (main) tag = reader.Tag();
        if (tag is 2 or 3) throw Unsupported("7z archive properties or external metadata require a preservation implementation.");
        if (tag != 5) throw Invalid("The 7z header is missing its complete file records.");
        var files = Files(ref reader, streams, budget, ct);
        reader.Require(0); reader.End();
        return (streams, files, main);
    }

    internal static SevenZipRawStreams Streams(ref SevenZipStrictHeaderReader reader, ArchiveWriteBudget budget, CancellationToken ct)
    {
        byte first = reader.Tag();
        if (first == 0) return SevenZipRawStreams.Empty;
        if (first != 6) throw Invalid("The 7z streams are missing their packed extents.");
        long position = reader.Size(); int packs = reader.Count(MaximumPackedStreams);
        budget.AddMetadata(0, checked(64L * packs)); reader.Require(9);
        long[] sizes = new long[packs];
        for (int index = 0; index < packs; index++) { ct.ThrowIfCancellationRequested(); sizes[index] = reader.Size(); }
        uint?[] checks = new uint?[packs]; byte tag = reader.Tag();
        if (tag == 10) { checks = Digests(ref reader, packs); tag = reader.Tag(); }
        if (tag != 0) throw Unsupported("Unknown or repeated 7z packed-stream properties cannot be preserved.");
        var packed = new SevenZipRawPack[packs]; long offset = position;
        for (int index = 0; index < packs; index++)
        {
            if (offset > ArchiveWritePolicy.MaxPhysicalBytes - 32 || sizes[index] > ArchiveWritePolicy.MaxPhysicalBytes - 32 - offset) throw Limit();
            packed[index] = new(32 + offset, sizes[index], checks[index]); offset += sizes[index];
        }
        reader.Require(7); reader.Require(11); int count = reader.Count(ArchiveWritePolicy.MaxMembers);
        budget.AddMetadata(0, checked(count * 256L));
        if (reader.Byte() != 0) throw Unsupported("External 7z folder descriptions cannot be preserved.");
        var folders = new SevenZipRawFolder[count]; int firstPack = 0;
        for (int index = 0; index < count; index++)
        {
            ct.ThrowIfCancellationRequested(); folders[index] = Folder(ref reader, budget, firstPack);
            if (folders[index].PackedInputs.Count > packs - firstPack) throw Invalid("The 7z folders consume more packed streams than declared.");
            firstPack += folders[index].PackedInputs.Count;
        }
        if (firstPack != packs) throw Invalid("The 7z packed streams are not all owned by folders.");
        reader.Require(12);
        for (int index = 0; index < count; index++)
        {
            int outputs = folders[index].Coders.Sum(coder => coder.Outputs);
            var lengths = new long[outputs];
            for (int output = 0; output < outputs; output++) lengths[output] = reader.Size();
            folders[index] = folders[index] with { UnpackedSizes = lengths };
        }
        tag = reader.Tag();
        if (tag == 10)
        {
            var crcs = Digests(ref reader, count);
            for (int index = 0; index < count; index++) folders[index] = folders[index] with { Crc32 = crcs[index] };
            tag = reader.Tag();
        }
        if (tag != 0) throw Unsupported("Unknown or repeated 7z unpack properties cannot be preserved.");
        tag = reader.Tag(); bool substreams = tag == 8, counts = false, lengthsPresent = false, crcsPresent = false;
        uint?[] memberCrc = folders.Select(folder => folder.Crc32).ToArray();
        if (substreams)
        {
            tag = reader.Tag();
            if (tag == 13)
            {
                counts = true;
                for (int index = 0; index < count; index++)
                    if (reader.Number() != 1) throw Unsupported("Solid or unused 7z compression folders need a separately qualified write profile.");
                tag = reader.Tag();
            }
            if (tag == 9) { lengthsPresent = true; tag = reader.Tag(); } // A single substream inherits its folder size.
            if (tag == 10)
            {
                crcsPresent = true; var extra = Digests(ref reader, folders.Count(folder => folder.Crc32 is null)); int next = 0;
                for (int index = 0; index < count; index++) if (folders[index].Crc32 is null) memberCrc[index] = extra[next++];
                tag = reader.Tag();
            }
            if (tag != 0) throw Unsupported("Unknown or repeated 7z substream properties cannot be preserved.");
            tag = reader.Tag();
        }
        if (tag != 0) throw Unsupported("Unknown or repeated 7z stream records cannot be preserved.");
        return new(packed, folders, memberCrc, substreams, counts, lengthsPresent, crcsPresent);
    }

    private static SevenZipRawFolder Folder(ref SevenZipStrictHeaderReader reader, ArchiveWriteBudget budget, int firstPack)
    {
        int count = reader.Count(MaximumCoders); if (count == 0) throw Invalid("A 7z compression folder has no coders.");
        budget.AddMetadata(0, checked(count * 512L));
        var coders = new SevenZipCoder[count]; int inputs = 0, outputs = 0;
        for (int index = 0; index < count; index++)
        {
            byte flags = reader.Byte(); int idLength = flags & 15;
            if ((flags & 0xc0) != 0) throw Unsupported("Alternative or reserved 7z coder flags cannot be preserved.");
            if (idLength is < 1 or > 8) throw Unsupported("This 7z coder identifier width is not understood.");
            byte[] method = reader.Bytes(idLength).ToArray();
            int ins = (flags & 16) == 0 ? 1 : reader.Count(128), outs = (flags & 16) == 0 ? 1 : reader.Count(32);
            if (ins == 0 || outs == 0) throw Invalid("A 7z coder has a zero stream count.");
            int properties = (flags & 32) == 0 ? 0 : reader.Count(64);
            coders[index] = new(method, reader.Bytes(properties).ToArray(), ins, outs);
            ValidateCoder(coders[index]); inputs += ins; outputs += outs;
        }
        int bindCount = outputs - 1, packCount = inputs - bindCount;
        if (packCount is < 1 or > 128) throw Invalid("The 7z compression graph has invalid stream counts.");
        var bindings = new SevenZipBinding[bindCount]; var boundInput = new bool[inputs]; var boundOutput = new bool[outputs];
        for (int index = 0; index < bindCount; index++)
        {
            int input = Index(ref reader, inputs), output = Index(ref reader, outputs);
            if (boundInput[input] || boundOutput[output]) throw Invalid("The 7z compression graph repeats a binding.");
            boundInput[input] = boundOutput[output] = true; bindings[index] = new(input, output);
        }
        var packed = new int[packCount];
        if (packCount == 1) packed[0] = Array.FindIndex(boundInput, value => !value);
        else
        {
            var used = new bool[inputs];
            for (int index = 0; index < packCount; index++)
            {
                int input = Index(ref reader, inputs);
                if (boundInput[input] || used[input]) throw Invalid("The 7z compression graph repeats or binds a packed input.");
                used[input] = true; packed[index] = input;
            }
        }
        int final = Array.FindIndex(boundOutput, value => !value);
        var state = new byte[count];
        Visit(final);
        if (state.Any(value => value != 2)) throw Invalid("The 7z compression graph has disconnected coders.");
        return new(coders, bindings, packed, [], final, null, firstPack);

        void Visit(int coder)
        {
            if (state[coder] == 1) throw Invalid("The 7z compression graph contains a cycle.");
            if (state[coder] == 2) return;
            state[coder] = 1; int start = 0;
            for (int index = 0; index < coder; index++) start += coders[index].Inputs;
            for (int input = start; input < start + coders[coder].Inputs; input++)
            {
                var binding = bindings.FirstOrDefault(value => value.Input == input);
                if (binding is not null) Visit(binding.Output); // All admitted coders have exactly one output.
                else if (!packed.Contains(input)) throw Invalid("A 7z coder input has no source.");
            }
            state[coder] = 2;
        }
    }

    private static SevenZipRawFiles Files(ref SevenZipStrictHeaderReader reader, SevenZipRawStreams streams,
        ArchiveWriteBudget budget, CancellationToken ct)
    {
        int count = reader.Count(ArchiveWritePolicy.MaxMembers); budget.AddMetadata(0, checked(count * 256L));
        bool[] empty = new bool[count]; bool[]? emptyFiles = null; string[]? names = null;
        ulong?[] created = new ulong?[count], accessed = new ulong?[count], modified = new ulong?[count];
        uint?[] attributes = new uint?[count]; uint mask = 0; long dummyBytes = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested(); byte tag = reader.Tag(); if (tag == 0) break;
            if (tag > 25 || tag is < 14 or 22 or 23 or 24) throw Unsupported("A 7z file property needs a preservation implementation.");
            uint bit = 1u << tag;
            if ((mask & bit) != 0) throw Invalid("The 7z file information repeats a property.");
            mask |= bit; long length = reader.Size();
            if (length > reader.Remaining) throw Invalid("A 7z file property exceeds its containing header.");
            var property = new SevenZipStrictHeaderReader(reader.Bytes((int)length));
            switch (tag)
            {
                case 14: empty = property.Bits(count); break;
                case 15:
                    if ((mask & (1u << 14)) == 0) throw Invalid("The 7z empty-file vector precedes empty-stream information.");
                    emptyFiles = property.Bits(empty.Count(value => value)); break;
                case 16:
                    if ((mask & (1u << 14)) == 0) throw Invalid("The 7z anti-item vector precedes empty-stream information.");
                    if (property.Bits(empty.Count(value => value)).Any(value => value)) throw Unsupported("7z anti-items cannot be preserved by this profile.");
                    break;
                case 17:
                    if (property.Byte() != 0) throw Unsupported("External 7z names cannot be preserved.");
                    names = Names(ref property, count, budget); break;
                case 18: Times(ref property, created); break;
                case 19: Times(ref property, accessed); break;
                case 20: Times(ref property, modified); break;
                case 21:
                    var defined = property.Bits(count, optional: true);
                    if (property.Byte() != 0) throw Unsupported("External 7z attributes cannot be preserved.");
                    for (int index = 0; index < count; index++) if (defined[index]) attributes[index] = property.UInt32();
                    break;
                case 25:
                    dummyBytes = length;
                    while (property.Remaining != 0) if (property.Byte() != 0) throw Unsupported("Nonzero 7z padding requires a preservation implementation.");
                    break;
                default: throw Unsupported("This 7z file property is not understood.");
            }
            property.End();
        }
        if (names is null && count != 0) throw Unsupported("A nameless 7z file catalog cannot be rewritten by this profile.");
        int folders = count - empty.Count(value => value);
        if (folders != streams.Folders.Count) throw Invalid("The 7z member streams and compression folders disagree.");
        var members = new SevenZipRawMember[count]; var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        int nextFolder = 0, nextEmpty = 0;
        for (int index = 0; index < count; index++)
        {
            ct.ThrowIfCancellationRequested(); budget.AddMember();
            bool file = !empty[index] || emptyFiles is not null && emptyFiles[nextEmpty];
            if (empty[index]) nextEmpty++;
            var type = file ? ArchiveMemberType.File : ArchiveMemberType.Directory;
            string name = names![index];
            try { ArchiveWritePolicy.ValidateName(name, type); }
            catch (ArchiveWriteException ex) { throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The 7z member name is not supported for writing.", ex); }
            if (file && name.EndsWith('/')) throw Invalid("A 7z regular file has a directory name.");
            ValidateAttributes(attributes[index], type);
            int? folder = empty[index] ? null : nextFolder++;
            long size = folder is { } number ? streams.Folders[number].Length : 0;
            if (size > ArchiveWritePolicy.MaxPayloadBytes) throw Limit(); budget.AddLogicalBytes(size);
            var key = (name, type); int occurrence = occurrences.GetValueOrDefault(key); occurrences[key] = occurrence + 1;
            members[index] = new(name, type, occurrence, folder, size, folder is { } crc ? streams.MemberCrc32[crc] : null,
                attributes[index], created[index], accessed[index], modified[index]);
        }
        return new(members, mask, dummyBytes);
    }

    private static string[] Names(ref SevenZipStrictHeaderReader property, int count, ArchiveWriteBudget budget)
    {
        var names = new string[count];
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> rest = property.Bytes(property.Remaining); int end = 0;
            while (end + 1 < rest.Length && (rest[end] != 0 || rest[end + 1] != 0)) end += 2;
            if (end + 1 >= rest.Length) throw Invalid("A 7z name is unterminated or has an odd UTF-16 extent.");
            if (end / 2 > ArchiveWritePolicy.MaxNameChars) throw Limit();
            budget.AddNameChars(end / 2); budget.AddMetadata(0, checked(end * 2L + 256));
            try { names[index] = Utf16.GetString(rest[..end]); }
            catch (DecoderFallbackException ex) { throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The 7z name contains invalid UTF-16.", ex); }
            property = new SevenZipStrictHeaderReader(rest[(end + 2)..]);
        }
        return names;
    }
    private static void Times(ref SevenZipStrictHeaderReader reader, ulong?[] times)
    {
        var defined = reader.Bits(times.Length, optional: true);
        if (reader.Byte() != 0) throw Unsupported("External 7z timestamps cannot be preserved.");
        for (int index = 0; index < times.Length; index++) if (defined[index]) times[index] = reader.UInt64();
    }
    private static uint?[] Digests(ref SevenZipStrictHeaderReader reader, int count)
    {
        bool[] defined = reader.Bits(count, optional: true); var digests = new uint?[count];
        for (int index = 0; index < count; index++) if (defined[index]) digests[index] = reader.UInt32();
        return digests;
    }
    private static int Index(ref SevenZipStrictHeaderReader reader, int count)
    {
        ulong index = reader.Number(); if (index >= (ulong)count) throw Invalid("The 7z graph stream index is out of range.");
        return (int)index;
    }
    internal static ulong Method(SevenZipCoder coder)
    { ulong method = 0; foreach (byte b in coder.MethodId) method = method << 8 | b; return method; }

    private static void ValidateCoder(SevenZipCoder coder)
    {
        ulong method = Method(coder); int properties = coder.Properties.Length;
        if (coder.Outputs != 1 || coder.Inputs != (method == 0x0303011b ? 4 : 1))
            throw Unsupported("This 7z coder stream shape is not supported.");
        bool known = method switch
        {
            0 or 0x040202 or 0x040108 or 0x0303011b or 0x020302 or 0x020304 => properties == 0,
            0x030101 or 0x030401 or 0x04f71101 => properties == 5,
            0x21 or 3 => properties == 1,
            0x03030103 or 0x03030205 or 0x03030401 or 0x03030501 or 0x03030701 or 0x03030805 or 10 or 11 => properties is 0 or 4,
            0x06f10701 => properties is >= 1 and <= 34,
            _ => false,
        };
        if (!known) throw Unsupported("The 7z coder or its property width needs a separately qualified implementation.");
    }
    private static void ValidateAttributes(uint? attributes, ArchiveMemberType type)
    {
        if (attributes is not { } value) return;
        if (((value & 0x10) != 0) != (type == ArchiveMemberType.Directory)) throw Invalid("The 7z directory attribute and member kind disagree.");
        if ((value & 0x400) != 0) throw Unsupported("7z reparse-point entries require a link preservation implementation.");
        if ((value & 0x8000) != 0)
        {
            uint kind = (value >> 16) & 0xf000;
            if (kind != 0 && kind != (type == ArchiveMemberType.Directory ? 0x4000u : 0x8000u))
                throw Unsupported("7z links or special native modes cannot be preserved by this profile.");
        }
    }
    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidArchive, message);
    private static ArchiveWriteException Unsupported(string message) => new(ArchiveWriteReason.UnsupportedFeature, message);
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The 7z catalog exceeds the strict write profile's resource limits.");
}
