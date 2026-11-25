using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The .NET application host: the launcher template of a Microsoft.NETCore.App.Host pack, which publishing copies to an
/// app's executable and binds to the app by writing its DLL's name over a placeholder. On Windows the SDK also copies the
/// DLL's Win32 resources in and sets the GUI subsystem of a windowed app; signing then adds an Authenticode signature. On
/// macOS packaging replaces the template's code signature. Nothing here runs either file.
/// </summary>
static class AppHost
{
    // The placeholder is the SHA-256 text of "foobar". The apphost keeps its two halves as separate strings too, to tell an
    // unbound copy; they survive binding, so together they mark a file made from an apphost.
    static ReadOnlySpan<byte> Placeholder => "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2"u8;
    static ReadOnlySpan<byte> HighHalf => "c3ab8ff13720e8ad9047dd39466b3c89"u8;
    static ReadOnlySpan<byte> LowHalf => "74e592c2fa383d4a3960714caef0c4f2"u8;
    static ReadOnlySpan<byte> ElfMagic => [0x7F, (byte)'E', (byte)'L', (byte)'F'];

    public static bool CarriesBindingCheck(ReadOnlySpan<byte> data) => data.IndexOf(HighHalf) >= 0 && data.IndexOf(LowHalf) >= 0;

    /// <summary>The template bound to <paramref name="appDll"/> as the SDK binds it: the name's UTF-8 bytes over the
    /// placeholder, zero padded to its length.</summary>
    public static byte[] Bind(byte[] template, string appDll)
    {
        int at = template.AsSpan().IndexOf(Placeholder);
        if (at < 0 || template.AsSpan(at + 1).IndexOf(Placeholder) >= 0)
            throw new InvalidDataException("The apphost template has no single placeholder.");
        byte[] name = Encoding.UTF8.GetBytes(appDll);
        if (name.Length == 0 || name.Length > Placeholder.Length) throw new InvalidDataException($"Unsupported app name for an apphost: {appDll}");
        byte[] bound = (byte[])template.Clone();
        bound.AsSpan(at, Placeholder.Length).Clear();
        name.CopyTo(bound, at);
        return bound;
    }

    /// <summary>Whether <paramref name="shipped"/> is the bound template, changed only in the ways publishing, signing
    /// and packaging change it; <paramref name="relation"/> then says how, and otherwise <paramref name="mismatch"/> says
    /// what differs.</summary>
    public static bool TryRelate(byte[] bound, byte[] shipped, string appDll, byte[] appDllBytes, out string relation, out string mismatch)
    {
        relation = mismatch = "";
        try
        {
            if (bound.AsSpan().StartsWith(ElfMagic))
            {
                if (bound.AsSpan().SequenceEqual(shipped)) relation = "byte for byte the template with the name written in";
                else mismatch = "its bytes differ from the bound template";
            }
            else if (bound.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bound) == 0xFEEDFACF) RelateMachO(bound, shipped, ref relation, ref mismatch);
            else if (bound.Length >= 2 && bound[0] == (byte)'M' && bound[1] == (byte)'Z') RelatePe(bound, shipped, appDll, appDllBytes, ref relation, ref mismatch);
            else mismatch = "the template is not an ELF, 64-bit Mach-O or PE file";
        }
        catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            relation = "";
            mismatch = error is InvalidDataException ? error.Message : "it is truncated or malformed";
        }
        return mismatch.Length == 0 && relation.Length != 0;
    }

    // A thin 64-bit Mach-O: everything before the code signature is the bound template's, apart from the three fields that
    // size the signature (LC_CODE_SIGNATURE's datasize and __LINKEDIT's vmsize and filesize), and nothing follows it.
    static void RelateMachO(byte[] bound, byte[] shipped, ref string relation, ref string mismatch)
    {
        MachO template = ReadMachO(bound), file = ReadMachO(shipped);
        if (template.CpuType != file.CpuType) { mismatch = "its CPU type differs from the template's"; return; }
        if (template.SignatureOffset != file.SignatureOffset) { mismatch = "its code signature starts elsewhere than the template's"; return; }
        if ((long)file.SignatureOffset + file.SignatureSize != shipped.LongLength) { mismatch = "bytes follow its code signature"; return; }
        if (!Normalized(bound, template).AsSpan().SequenceEqual(Normalized(shipped, file)))
        {
            mismatch = "its bytes before the code signature differ from the bound template";
            return;
        }
        if (file.AdHoc is not { } adHoc) { mismatch = "its code signature is no SuperBlob with a CodeDirectory"; return; }
        relation = "byte for byte the template with the name written in, up to its code signature: "
            + (adHoc ? "an ad-hoc signature" : "another code signature") + " replaces the template's, and the three load-command fields that size it change";
    }

    sealed record MachO(uint CpuType, int SignatureOffset, int SignatureSize, int[] SizeFields, bool? AdHoc);

    static MachO ReadMachO(byte[] data)
    {
        Need(data, 0, 32);
        if (U32(data, 0) != 0xFEEDFACF) throw new InvalidDataException("it is not a thin 64-bit Mach-O file");
        uint count = U32(data, 16), commandBytes = U32(data, 20);
        Need(data, 32, commandBytes);
        long end = 32L + commandBytes;
        int offset = 32;
        int? signatureOffset = null, signatureSize = null, sizeField = null, vmsizeField = null, filesizeField = null;
        for (uint index = 0; index < count; index++)
        {
            Need(data, offset, 8);
            uint command = U32(data, offset), size = U32(data, offset + 4);
            if (size < 8 || offset + (long)size > end) throw new InvalidDataException("it has a malformed load command");
            if (command == 0x19 && size >= 72 && data.AsSpan(offset + 8, 16).TrimEnd((byte)0).SequenceEqual("__LINKEDIT"u8))
            {
                vmsizeField = offset + 32;
                filesizeField = offset + 48;
            }
            else if (command == 0x1D && size >= 16)
            {
                if (signatureOffset is not null) throw new InvalidDataException("it has two code signatures");
                signatureOffset = checked((int)U32(data, offset + 8));
                signatureSize = checked((int)U32(data, offset + 12));
                sizeField = offset + 12;
            }
            offset += (int)size;
        }
        if (signatureOffset is not { } at || signatureSize is not { } length || sizeField is null || vmsizeField is null || filesizeField is null)
            throw new InvalidDataException("it has no code signature or no __LINKEDIT segment");
        Need(data, at, (uint)length);
        return new(U32(data, 4), at, length, [sizeField.Value, vmsizeField.Value, filesizeField.Value], IsAdHoc(data, at));
    }

    static byte[] Normalized(byte[] data, MachO file)
    {
        byte[] prefix = data.AsSpan(0, file.SignatureOffset).ToArray();
        prefix.AsSpan(file.SizeFields[0], 4).Clear();
        prefix.AsSpan(file.SizeFields[1], 8).Clear();
        prefix.AsSpan(file.SizeFields[2], 8).Clear();
        return prefix;
    }

    // The signature is a SuperBlob (big-endian): magic, length, count, then (slot, offset) pairs. Slot 0 is the
    // CodeDirectory, whose flags (at 12) carry CS_ADHOC (0x2) for an ad-hoc signature. Null: no such signature.
    static bool? IsAdHoc(byte[] data, int offset)
    {
        Need(data, offset, 12);
        if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)) != 0xFADE0CC0) return null;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 8));
        for (uint index = 0; index < count && index < 64; index++)
        {
            int entry = offset + 12 + (int)index * 8;
            Need(data, entry, 8);
            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry)) != 0) continue;
            long blob = offset + (long)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry + 4));
            if (blob > int.MaxValue) return null;
            Need(data, (int)blob, 16);
            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)blob)) != 0xFADE0C02) return null;
            return (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)blob + 12)) & 0x2) != 0;
        }
        return null;
    }

    // A PE file. Its headers are the template's apart from the timestamp and section count, the initialized-data and image
    // sizes, the checksum, the subsystem (the Windows GUI one, if changed) and the resource and certificate directories.
    // Every section of the template is unchanged. At most one section follows them, .rsrc, which holds exactly the app
    // DLL's Win32 resources; an Authenticode signature may follow the sections, and nothing else does.
    static void RelatePe(byte[] bound, byte[] shipped, string appDll, byte[] appDllBytes, ref string relation, ref string mismatch)
    {
        Pe template = ReadPe(bound), file = ReadPe(shipped);
        // The COFF header's section count and timestamp may change (the resource update rewrites the timestamp).
        if (template.PeOffset != file.PeOffset || template.Is64 != file.Is64 || template.OptionalSize != file.OptionalSize
            || !bound.AsSpan(0, template.PeOffset + 6).SequenceEqual(shipped.AsSpan(0, file.PeOffset + 6))
            || !bound.AsSpan(template.PeOffset + 12, 12).SequenceEqual(shipped.AsSpan(file.PeOffset + 12, 12)))
        {
            mismatch = "its DOS or COFF header differs from the template's";
            return;
        }
        if (template.Directory(2) != (0u, 0u) || template.Directory(4) != (0u, 0u))
        {
            mismatch = "the template already has resources or a signature";
            return;
        }
        if (file.Sections.Length != template.Sections.Length && file.Sections.Length != template.Sections.Length + 1)
        {
            mismatch = "its sections are not the template's";
            return;
        }
        // The optional header fields that the resource update, the GUI flag and signing set, with their names.
        int directories = template.Is64 ? 112 : 96;
        (int Offset, int Width, string Name)[] settable =
        [
            (8, 4, "the initialized-data size"), (56, 4, "the image size"), (64, 4, "the checksum"), (68, 2, "the subsystem (Windows GUI)"),
            (directories + 2 * 8, 8, "the resource directory"), (directories + 4 * 8, 8, "the certificate directory"),
        ];
        byte[] templateOptional = bound.AsSpan(template.OptionalOffset, template.OptionalSize).ToArray();
        byte[] fileOptional = shipped.AsSpan(file.OptionalOffset, file.OptionalSize).ToArray();
        var changed = new List<string>();
        if (U32(bound, template.PeOffset + 8) != U32(shipped, file.PeOffset + 8)) changed.Add("the timestamp");
        if (file.Sections.Length != template.Sections.Length) changed.Add("the section count");
        foreach ((int offset, int width, string name) in settable)
        {
            if (!templateOptional.AsSpan(offset, width).SequenceEqual(fileOptional.AsSpan(offset, width))) changed.Add(name);
            templateOptional.AsSpan(offset, width).Clear();
            fileOptional.AsSpan(offset, width).Clear();
        }
        if (!templateOptional.AsSpan().SequenceEqual(fileOptional))
        {
            mismatch = "its optional header differs from the template's beyond sizes, checksum, subsystem, resources and signature";
            return;
        }
        if (file.Subsystem != template.Subsystem && file.Subsystem != 2)
        {
            mismatch = "its subsystem is neither the template's nor the Windows GUI";
            return;
        }
        int tableBytes = template.Sections.Length * 40;
        if (!bound.AsSpan(template.SectionTable, tableBytes).SequenceEqual(shipped.AsSpan(file.SectionTable, tableBytes)))
        {
            mismatch = "its headers of the template's sections differ";
            return;
        }
        foreach (PeSection section in template.Sections)
        {
            Need(bound, section.RawPointer, section.RawSize);
            Need(shipped, section.RawPointer, section.RawSize);
            if (!bound.AsSpan((int)section.RawPointer, (int)section.RawSize).SequenceEqual(shipped.AsSpan((int)section.RawPointer, (int)section.RawSize)))
            {
                mismatch = $"its {section.Name} section differs from the bound template's";
                return;
            }
        }
        int headerEnd = file.SectionTable + file.Sections.Length * 40;
        Need(bound, 0, template.SizeOfHeaders);
        Need(shipped, 0, file.SizeOfHeaders);
        if (headerEnd > file.SizeOfHeaders || template.SizeOfHeaders != file.SizeOfHeaders
            || !bound.AsSpan(headerEnd, (int)file.SizeOfHeaders - headerEnd).SequenceEqual(shipped.AsSpan(headerEnd, (int)file.SizeOfHeaders - headerEnd)))
        {
            mismatch = "its header padding differs from the template's";
            return;
        }
        var parts = new List<string> { $"every section of the template ({string.Join(", ", template.Sections.Select(section => section.Name))}) unchanged apart from the name" };
        long rawEnd = template.Sections.Max(section => (long)section.RawPointer + section.RawSize);
        (uint resourceRva, uint resourceSize) = file.Directory(2);
        if (file.Sections.Length == template.Sections.Length + 1)
        {
            PeSection added = file.Sections[^1];
            long virtualEnd = template.Sections.Max(section => (long)section.VirtualAddress + Math.Max(section.VirtualSize, section.RawSize));
            if (added.Name != ".rsrc" || added.RawPointer != rawEnd || added.VirtualAddress < virtualEnd
                || resourceRva < added.VirtualAddress || (long)resourceRva + resourceSize > (long)added.VirtualAddress + Math.Max(added.VirtualSize, added.RawSize))
            {
                mismatch = "its added section is not a .rsrc section after the template's, holding its resources";
                return;
            }
            SortedDictionary<string, string> resources = ResourceLeaves(shipped, file);
            SortedDictionary<string, string> appResources;
            try { appResources = ResourceLeaves(appDllBytes, ReadPe(appDllBytes)); }
            catch (InvalidDataException) { appResources = []; }
            if (resources.Count == 0 || resources.Count != appResources.Count
                || resources.Any(pair => !appResources.TryGetValue(pair.Key, out string? value) || value != pair.Value))
            {
                mismatch = $"its Win32 resources are not exactly {appDll}'s";
                return;
            }
            rawEnd = (long)added.RawPointer + added.RawSize;
            parts.Add($"a .rsrc section with exactly {appDll}'s {resources.Count} Win32 resources");
        }
        else if (file.Sections.Length != template.Sections.Length || resourceRva != 0 || resourceSize != 0)
        {
            mismatch = "its sections are not the template's";
            return;
        }
        (uint certificateOffset, uint certificateSize) = file.Directory(4);
        if (certificateOffset == 0 && certificateSize == 0)
        {
            if (shipped.LongLength != rawEnd) { mismatch = "bytes follow its last section"; return; }
        }
        else
        {
            Need(shipped, certificateOffset, certificateSize);
            if (certificateOffset != rawEnd || (long)certificateOffset + certificateSize != shipped.LongLength || certificateSize < 8
                || U32(shipped, (int)certificateOffset) > certificateSize || U16(shipped, (int)certificateOffset + 4) != 0x0200 || U16(shipped, (int)certificateOffset + 6) != 0x0002)
            {
                mismatch = "what follows its sections is not one Authenticode signature";
                return;
            }
            parts.Add($"an Authenticode signature ({certificateSize} bytes) after the sections");
        }
        parts.Insert(1, changed.Count switch
        {
            0 => "its headers unchanged",
            1 => "its headers unchanged apart from " + changed[0],
            _ => "its headers unchanged apart from " + string.Join(", ", changed.SkipLast(1)) + " and " + changed[^1],
        });
        relation = string.Join("; ", parts);
    }

    sealed record PeSection(string Name, uint VirtualSize, uint VirtualAddress, uint RawSize, uint RawPointer);

    sealed record Pe(int PeOffset, bool Is64, int OptionalOffset, int OptionalSize, int SectionTable, PeSection[] Sections,
        (uint Rva, uint Size)[] Directories, ushort Subsystem, uint SizeOfHeaders)
    {
        public (uint Rva, uint Size) Directory(int index) => index < Directories.Length ? Directories[index] : (0u, 0u);
    }

    static Pe ReadPe(byte[] data)
    {
        Need(data, 0, 64);
        if (data[0] != (byte)'M' || data[1] != (byte)'Z') throw new InvalidDataException("it is not a PE file");
        int pe = checked((int)U32(data, 0x3C));
        Need(data, pe, 24);
        if (U32(data, pe) != 0x00004550) throw new InvalidDataException("it has no PE signature");
        ushort sectionCount = U16(data, pe + 6), optionalSize = U16(data, pe + 20);
        int optional = pe + 24;
        Need(data, optional, optionalSize);
        bool is64 = optionalSize >= 2 && U16(data, optional) == 0x20B;
        if (!is64 && (optionalSize < 2 || U16(data, optional) != 0x10B)) throw new InvalidDataException("it has an unknown optional header");
        int directoryBase = is64 ? 112 : 96;
        if (optionalSize < directoryBase) throw new InvalidDataException("its optional header is truncated");
        uint directoryCount = U32(data, optional + directoryBase - 4);
        if (directoryCount > 16 || directoryBase + directoryCount * 8 > optionalSize) throw new InvalidDataException("its data directories are malformed");
        var directories = new (uint, uint)[directoryCount];
        for (int index = 0; index < directoryCount; index++)
            directories[index] = (U32(data, optional + directoryBase + index * 8), U32(data, optional + directoryBase + index * 8 + 4));
        int table = optional + optionalSize;
        Need(data, table, (uint)sectionCount * 40);
        var sections = new PeSection[sectionCount];
        for (int index = 0; index < sectionCount; index++)
        {
            int header = table + index * 40;
            sections[index] = new(Encoding.ASCII.GetString(data.AsSpan(header, 8).TrimEnd((byte)0)),
                U32(data, header + 8), U32(data, header + 12), U32(data, header + 16), U32(data, header + 20));
        }
        return new(pe, is64, optional, optionalSize, table, sections, directories, U16(data, optional + 68), U32(data, optional + 60));
    }

    // Each resource by its type/name/language path, with its code page and the SHA-256 of its data.
    static SortedDictionary<string, string> ResourceLeaves(byte[] data, Pe pe)
    {
        var leaves = new SortedDictionary<string, string>(StringComparer.Ordinal);
        (uint rva, _) = pe.Directory(2);
        if (rva == 0) return leaves;
        int root = Offset(pe, rva);
        int visited = 0;
        void Walk(int directory, string path, int depth)
        {
            if (depth > 2) throw new InvalidDataException("its resource tree is too deep");
            Need(data, directory, 16);
            int entries = U16(data, directory + 12) + U16(data, directory + 14);
            for (int index = 0; index < entries; index++)
            {
                if (++visited > 4096) throw new InvalidDataException("its resource tree is too large");
                int entry = directory + 16 + index * 8;
                Need(data, entry, 8);
                uint name = U32(data, entry), target = U32(data, entry + 4);
                string key = path + "/" + ((name & 0x80000000) == 0 ? "#" + name : "'" + ResourceName(data, root + (int)(name & 0x7FFFFFFF)) + "'");
                if ((target & 0x80000000) != 0) Walk(root + (int)(target & 0x7FFFFFFF), key, depth + 1);
                else
                {
                    int leaf = root + (int)target;
                    Need(data, leaf, 16);
                    uint size = U32(data, leaf + 4);
                    int at = Offset(pe, U32(data, leaf));
                    Need(data, at, size);
                    leaves[key] = U32(data, leaf + 8) + ":" + Convert.ToHexString(SHA256.HashData(data.AsSpan(at, (int)size))).ToLowerInvariant();
                }
            }
        }
        Walk(root, "", 0);
        return leaves;
    }

    static string ResourceName(byte[] data, int offset)
    {
        Need(data, offset, 2);
        int characters = U16(data, offset);
        Need(data, offset + 2, (uint)characters * 2);
        return Encoding.Unicode.GetString(data, offset + 2, characters * 2);
    }

    static int Offset(Pe pe, uint rva)
    {
        foreach (PeSection section in pe.Sections)
            if (rva >= section.VirtualAddress && rva < (long)section.VirtualAddress + Math.Max(section.VirtualSize, section.RawSize))
                return checked((int)(section.RawPointer + (rva - section.VirtualAddress)));
        throw new InvalidDataException("a resource lies outside its sections");
    }

    static void Need(byte[] data, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset + length > data.LongLength) throw new InvalidDataException("it is truncated or malformed");
    }

    static uint U32(byte[] data, int offset)
    {
        Need(data, offset, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    }

    static ushort U16(byte[] data, int offset)
    {
        Need(data, offset, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    }
}
