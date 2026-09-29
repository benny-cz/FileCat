using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>
/// Mach-O inspector (macOS and iOS executables and libraries, including universal "fat" files), as deep as otool: a
/// summary (type, PIE, libraries with their versions, build platform and tools, UUID, hardening), the code signature
/// (identifier, team, flags such as the hardened runtime, entitlements, and the certificates; read, not verified), the
/// Mach header, every load command, segments and sections, and the imported and exported symbols. Java class files share
/// the 0xCAFEBABE magic and are told apart. Static and bounded.
/// </summary>
public static class MachOInspector
{
    private const int MaxArchitectures = 16;
    private const int MaxLoadCommands = 512;
    private const int MaxSymbols = 200_000;
    private const int MaxListed = 5_000;

    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct)
    {
        var r = new ContentReader(source);
        var head = r.Read(0, 32);
        if (head.Length < 8) return null;
        uint magicBe = BinaryPrimitives.ReadUInt32BigEndian(head);
        if (magicBe is 0xCAFEBABE or 0xCAFEBABF)
        {
            uint n = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4));
            // Java class files share the magic; their next field is a class-file version, far above any count of architectures.
            if (magicBe == 0xCAFEBABE && n >= 20) return JavaClass(head);
            return Fat(r, magicBe == 0xCAFEBABF, (int)n, ct);
        }
        return Thin(r, 0, ct, out var report) ? report : null;
    }

    private static InspectionReport JavaClass(byte[] head)
    {
        ushort minor = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(4)), major = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(6));
        string java = major switch { >= 53 => $"Java {major - 44}", 52 => "Java 8", 51 => "Java 7", 50 => "Java 6", 49 => "Java 5", >= 45 => "Java 1.x", _ => "unknown" };
        return new InspectionReport($"Java class file · {java}", [new InspectionSection("Class file", [("Version", $"{major}.{minor} ({java})")])], []);
    }

    private static InspectionReport Fat(ContentReader r, bool is64, int count, CancellationToken ct)
    {
        var warnings = new List<string>();
        if (count > MaxArchitectures) warnings.Add($"The header declares {count} architectures; only the first {MaxArchitectures} are read.");
        int entry = is64 ? 32 : 20;
        var table = r.Read(8, Math.Min(count, MaxArchitectures) * entry);
        var archs = new List<(string, string)>();
        var rows = new List<string[]>();
        var sections = new List<InspectionSection>();
        for (int i = 0; i + entry <= table.Length; i += entry)
        {
            ct.ThrowIfCancellationRequested();
            int cpu = BinaryPrimitives.ReadInt32BigEndian(table.AsSpan(i)), subtype = BinaryPrimitives.ReadInt32BigEndian(table.AsSpan(i + 4));
            long offset = is64 ? (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(table.AsSpan(i + 8)), long.MaxValue) : BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(i + 8));
            long size = is64 ? (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(table.AsSpan(i + 16)), long.MaxValue) : BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(i + 12));
            uint align = BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(i + (is64 ? 24 : 16)));
            archs.Add((Cpu(cpu), $"{size:N0} bytes at offset {offset:N0}"));
            rows.Add([Cpu(cpu), Subtype(cpu, subtype), $"0x{offset:X}", $"{size:N0}", align < 32 ? $"2^{align}" : $"{align}"]);
            // Every architecture in full, each section named for its architecture.
            if (Thin(r, offset, ct, out var slice) && slice is not null)
            {
                sections.AddRange(slice.Sections.Select(s => s with { Title = $"{Cpu(cpu)}: {s.Title}" }));
                warnings.AddRange(slice.Warnings.Select(w => $"{Cpu(cpu)}: {w}"));
            }
        }
        sections.Insert(0, new InspectionSection($"Architectures ({archs.Count})", archs) { Table = new InspectionTable(["CPU", "Subtype", "Offset", "Size", "Align"], rows) });
        return new InspectionReport($"Mach-O universal binary · {string.Join(", ", archs.Select(a => a.Item1))}", sections, warnings);
    }

    private readonly record struct Library(string Kind, string Path, uint Current, uint Compatibility);

    private static bool Thin(ContentReader r, long at, CancellationToken ct, out InspectionReport? report)
    {
        report = null;
        var h = r.Read(at, 32);
        if (h.Length < 28) return false;
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(h);
        bool little, is64;
        switch (magic)
        {
            case 0xFEEDFACE: little = true; is64 = false; break;
            case 0xFEEDFACF: little = true; is64 = true; break;
            case 0xCEFAEDFE: little = false; is64 = false; break;
            case 0xCFFAEDFE: little = false; is64 = true; break;
            default: return false;
        }
        uint U32(ReadOnlySpan<byte> b, int o) => o < 0 || o + 4 > b.Length ? 0 : little ? BinaryPrimitives.ReadUInt32LittleEndian(b[o..]) : BinaryPrimitives.ReadUInt32BigEndian(b[o..]);
        ulong U64(ReadOnlySpan<byte> b, int o) => o < 0 || o + 8 > b.Length ? 0 : little ? BinaryPrimitives.ReadUInt64LittleEndian(b[o..]) : BinaryPrimitives.ReadUInt64BigEndian(b[o..]);
        ushort U16(ReadOnlySpan<byte> b, int o) => o < 0 || o + 2 > b.Length ? (ushort)0 : little ? BinaryPrimitives.ReadUInt16LittleEndian(b[o..]) : BinaryPrimitives.ReadUInt16BigEndian(b[o..]);
        int cpu = (int)U32(h, 4), subtype = (int)U32(h, 8);
        uint fileType = U32(h, 12), ncmds = U32(h, 16), sizeofcmds = U32(h, 20), flags = U32(h, 24);
        var warnings = new List<string>();
        string type = fileType switch
        {
            1 => "Object", 2 => "Executable", 3 => "Fixed VM library", 4 => "Core dump", 5 => "Preloaded executable", 6 => "Dynamic library",
            7 => "Dynamic linker", 8 => "Bundle", 9 => "Stub library", 10 => "Debug symbols (dSYM)", 11 => "Kernel extension", 12 => "File set", _ => $"type {fileType}",
        };
        var header = new List<(string, string)>
        {
            ("CPU", Cpu(cpu) + (Subtype(cpu, subtype) is var sub and not ("all" or "") ? $" ({sub})" : "")),
            ("Type", type),
            ("Position independent (PIE)", (flags & 0x200000) != 0 ? "yes" : fileType == 2 ? "no" : "not applicable"),
        };
        if ((flags & 0x1000000) != 0) header.Add(("Heap", "not executable (MH_NO_HEAP_EXECUTION)"));
        if (ncmds > MaxLoadCommands) warnings.Add($"The header declares {ncmds} load commands; only the first {MaxLoadCommands} are read.");
        var cmds = r.Read(at + (is64 ? 32 : 28), (int)Math.Min(sizeofcmds, 4 * 1024 * 1024));
        var libraries = new List<Library>();
        var runPaths = new List<string>();
        var build = new List<(string, string)>();
        var commands = new List<string[]>();
        var segments = new List<string[]>();
        var sectionRows = new List<string[]>();
        bool encrypted = false, restricted = false;
        (uint Offset, uint Size)? signature = null;
        (uint SymOff, uint NSyms, uint StrOff, uint StrSize)? symtab = null;
        string Str(ReadOnlySpan<byte> command, int offsetField)
        {
            uint o = U32(command, offsetField);
            return o < command.Length ? Cstr(command[(int)o..]) : "(unreadable)";
        }
        string Name16(ReadOnlySpan<byte> b, int o) => o + 16 <= b.Length ? Cstr(b.Slice(o, 16)) : "";
        int pos = 0;
        for (int i = 0; i < Math.Min(ncmds, MaxLoadCommands) && pos + 8 <= cmds.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            uint cmd = U32(cmds, pos), size = U32(cmds, pos + 4);
            if (size < 8 || pos + size > cmds.Length)
            {
                warnings.Add("A load command is damaged; the rest are not read.");
                break;
            }
            var c = cmds.AsSpan(pos, (int)size);
            string detail = "";
            switch (cmd & 0x7FFFFFFF)
            {
                case 0x1 or 0x19: // LC_SEGMENT, LC_SEGMENT_64
                {
                    bool wide = (cmd & 0x7FFFFFFF) == 0x19;
                    string name = Name16(c, 8);
                    ulong vmaddr = wide ? U64(c, 24) : U32(c, 24), vmsize = wide ? U64(c, 32) : U32(c, 28);
                    ulong fileoff = wide ? U64(c, 40) : U32(c, 32), filesize = wide ? U64(c, 48) : U32(c, 36);
                    uint maxprot = U32(c, wide ? 56 : 40), initprot = U32(c, wide ? 60 : 44), nsects = U32(c, wide ? 64 : 48), segFlags = U32(c, wide ? 68 : 52);
                    segments.Add([name, $"0x{vmaddr:X}", $"{vmsize:N0}", $"0x{fileoff:X}", $"{filesize:N0}", $"{Protection(initprot)}/{Protection(maxprot)}", $"{nsects}", SegmentFlags(segFlags)]);
                    if (name == "__RESTRICT") restricted = true;
                    int sectionSize = wide ? 80 : 68, first = wide ? 72 : 56;
                    for (int s = 0; s < Math.Min(nsects, 256) && first + (s + 1) * sectionSize <= c.Length; s++)
                    {
                        var sec = c.Slice(first + s * sectionSize, sectionSize);
                        ulong addr = wide ? U64(sec, 32) : U32(sec, 32), secSize = wide ? U64(sec, 40) : U32(sec, 36);
                        uint offset = U32(sec, wide ? 48 : 40), align = U32(sec, wide ? 52 : 44), secFlags = U32(sec, wide ? 64 : 56);
                        sectionRows.Add([Name16(sec, 16), Name16(sec, 0), $"0x{addr:X}", $"{secSize:N0}", $"0x{offset:X}", align < 32 ? $"2^{align}" : $"{align}", SectionType(secFlags & 0xFF), SectionAttributes(secFlags)]);
                    }
                    detail = $"{name}, {nsects} sections";
                    break;
                }
                case 0x2 when c.Length >= 24: // LC_SYMTAB
                    symtab = (U32(c, 8), U32(c, 12), U32(c, 16), U32(c, 20));
                    detail = $"{U32(c, 12):N0} symbols";
                    break;
                case 0xB when c.Length >= 80: // LC_DYSYMTAB
                    detail = $"{U32(c, 12):N0} local, {U32(c, 20):N0} defined external, {U32(c, 28):N0} undefined";
                    break;
                case 0xC or 0x18 or 0x1F or 0x20 or 0x23 when c.Length >= 24: // LC_LOAD_DYLIB, LC_LOAD_WEAK_DYLIB, LC_REEXPORT_DYLIB, LC_LAZY_LOAD_DYLIB, LC_LOAD_UPWARD_DYLIB
                {
                    string kind = (cmd & 0x7FFFFFFF) switch { 0x18 => "weak", 0x1F => "re-export", 0x20 => "lazy", 0x23 => "upward", _ => "load" };
                    libraries.Add(new Library(kind, Str(c, 8), U32(c, 16), U32(c, 20)));
                    detail = Str(c, 8);
                    break;
                }
                case 0xD when c.Length >= 24: // LC_ID_DYLIB
                    header.Add(("Library name", Str(c, 8)));
                    header.Add(("Library version", $"{Version3(U32(c, 16))} (compatible with {Version3(U32(c, 20))})"));
                    detail = Str(c, 8);
                    break;
                case 0xE when c.Length >= 12: // LC_LOAD_DYLINKER
                    header.Add(("Dynamic linker", Str(c, 8)));
                    detail = Str(c, 8);
                    break;
                case 0x1C when c.Length >= 12: // LC_RPATH
                    runPaths.Add(Str(c, 8));
                    detail = Str(c, 8);
                    break;
                case 0x1B when c.Length >= 24: // LC_UUID
                    header.Add(("UUID", new Guid(c.Slice(8, 16), bigEndian: true).ToString().ToUpperInvariant()));
                    break;
                case 0x1D when c.Length >= 16: // LC_CODE_SIGNATURE
                    signature = (U32(c, 8), U32(c, 12));
                    detail = $"{U32(c, 12):N0} bytes at 0x{U32(c, 8):X}";
                    break;
                case 0x21 or 0x2C when c.Length >= 20: // LC_ENCRYPTION_INFO(_64): cryptid
                    encrypted |= U32(c, 16) != 0;
                    detail = U32(c, 16) != 0 ? "encrypted" : "not encrypted";
                    break;
                case 0x28 when c.Length >= 24: // LC_MAIN
                    header.Add(("Entry point offset", $"0x{U64(c, 8):X}"));
                    if (U64(c, 16) != 0) header.Add(("Stack size", $"{U64(c, 16):N0} bytes"));
                    detail = $"entry 0x{U64(c, 8):X}";
                    break;
                case 0x32 when c.Length >= 24: // LC_BUILD_VERSION
                {
                    build.Add(("Platform", Platform(U32(c, 8))));
                    build.Add(("Minimum OS", Version3(U32(c, 12))));
                    build.Add(("SDK", Version3(U32(c, 16))));
                    uint tools = U32(c, 20);
                    for (int t = 0; t < Math.Min(tools, 16) && 24 + t * 8 + 8 <= c.Length; t++)
                        build.Add(("Built with", $"{Tool(U32(c, 24 + t * 8))} {Version3(U32(c, 28 + t * 8))}"));
                    detail = $"{Platform(U32(c, 8))} {Version3(U32(c, 12))}";
                    break;
                }
                case 0x24 or 0x25 or 0x2F or 0x30 when c.Length >= 16: // LC_VERSION_MIN_*
                    build.Add(("Platform", (cmd & 0x7FFFFFFF) switch { 0x24 => "macOS", 0x25 => "iOS", 0x2F => "tvOS", _ => "watchOS" }));
                    build.Add(("Minimum OS", Version3(U32(c, 8))));
                    build.Add(("SDK", Version3(U32(c, 12))));
                    detail = Version3(U32(c, 8));
                    break;
                case 0x2A when c.Length >= 16: // LC_SOURCE_VERSION: A.B.C.D.E in 24.10.10.10.10 bits
                {
                    ulong v = U64(c, 8);
                    string version = $"{v >> 40}.{(v >> 30) & 0x3FF}.{(v >> 20) & 0x3FF}.{(v >> 10) & 0x3FF}.{v & 0x3FF}";
                    if (v != 0) build.Add(("Source version", version));
                    detail = version;
                    break;
                }
                case 0x2D when c.Length >= 12: // LC_LINKER_OPTION
                {
                    var options = Encoding.UTF8.GetString(c[12..]).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    detail = Printable(string.Join(" ", options.Take(16)));
                    break;
                }
                case 0x22 or 0x26 or 0x29 or 0x1E or 0x33 or 0x34 or 0x2B or 0x2E when c.Length >= 16: // linkedit data
                    detail = $"{U32(c, 12):N0} bytes at 0x{U32(c, 8):X}";
                    break;
            }
            commands.Add([$"{i}", CommandName(cmd), $"{size:N0}", detail]);
            pos += (int)size;
        }

        // Symbols: undefined external ones are imports (with the library that provides them), defined external ones exports.
        var imports = new List<string[]>();
        var exports = new List<string[]>();
        if (symtab is { } st && st.NSyms > 0)
        {
            int entry = is64 ? 16 : 12;
            int count = (int)Math.Min(st.NSyms, MaxSymbols);
            var table = r.Read(at + st.SymOff, count * entry);
            var names = r.Read(at + st.StrOff, (int)Math.Min(st.StrSize, 16u << 20));
            for (int i = 0; (i + 1) * entry <= table.Length; i++)
            {
                if (i % 4096 == 0) ct.ThrowIfCancellationRequested();
                var s = table.AsSpan(i * entry, entry);
                uint strx = U32(s, 0);
                byte nType = s[4];
                if ((nType & 0xE0) != 0 || (nType & 0x01) == 0 || strx >= names.Length) continue; // debug entries, local symbols
                string name = Cstr(names.AsSpan((int)strx));
                int kind = nType & 0x0E;
                if (kind == 0)
                {
                    int ordinal = (U16(s, 6) >> 8) & 0xFF;
                    string from = ordinal switch
                    {
                        0 => "this image",
                        0xFE => "looked up at run time",
                        0xFF => "the main executable",
                        _ when ordinal <= libraries.Count => libraries[ordinal - 1].Path,
                        _ => $"library #{ordinal}",
                    };
                    imports.Add([name, from]);
                }
                else if (kind == 0x0E && (nType & 0x10) == 0) exports.Add([name, $"0x{(is64 ? U64(s, 8) : U32(s, 8)):X}"]);
            }
            if (st.NSyms > MaxSymbols) warnings.Add($"The symbol table has {st.NSyms:N0} entries; only the first {MaxSymbols:N0} are read.");
        }

        // The code signature: a big-endian SuperBlob of the code directory, requirements, entitlements, and CMS signature.
        var signatureFields = new List<(string, string)>
        {
            ("Status", signature is not null ? "Present, not verified: FileCat does not check signatures or trust here" : "No code signature"),
            ("Encrypted", encrypted ? "yes (App Store encryption)" : "no"),
        };
        var signatureChildren = new List<InspectionSection>();
        bool hardened = false;
        if (signature is { } sig && sig.Size >= 12) CodeSignature(r.Read(at + sig.Offset, (int)Math.Min(sig.Size, 4u << 20)), signatureFields, signatureChildren, ref hardened);

        var sections = new List<InspectionSection> { new("Header", header) };
        if (build.Count > 0) sections.Add(new InspectionSection("Build", build));
        if (libraries.Count > 0 || runPaths.Count > 0)
            sections.Add(new InspectionSection($"Libraries ({libraries.Count(l => l.Kind != "re-export")})", [.. runPaths.Select(p => ("Run path", p))])
            {
                // The path last: framework paths are long, and the columns before it stay aligned.
                Table = new InspectionTable(["How", "Current version", "Compatibility", "Library"],
                    [.. libraries.Select(l => new[] { l.Kind, Version3(l.Current), Version3(l.Compatibility), l.Path })]),
            });
        var security = new List<(string, string)>
        {
            ("Position independent (PIE)", (flags & 0x200000) != 0 ? "yes" : fileType == 2 ? "no" : "not applicable"),
            ("Non-executable heap", (flags & 0x1000000) != 0 ? "yes" : "not requested"),
            ("Executable stack", (flags & 0x20000) != 0 ? "allowed (MH_ALLOW_STACK_EXECUTION)" : "no"),
            ("Hardened runtime", signature is null ? "no (not signed)" : hardened ? "yes" : "no"),
        };
        if (imports.Count > 0) security.Add(("Stack protector", imports.Any(i => i[0] is "___stack_chk_fail" or "___stack_chk_guard") ? "yes (___stack_chk_fail is used)" : "no sign of it"));
        if (Subtype(cpu, subtype) == "arm64e") security.Add(("Pointer authentication", "yes (arm64e)"));
        if (restricted) security.Add(("Restricted segment", "yes: dyld ignores DYLD_ environment variables"));
        sections.Add(new InspectionSection("Security features", security));
        sections.Add(new InspectionSection("Signature", signatureFields) { Children = signatureChildren });
        sections.Add(new InspectionSection("Mach header", [
            ("Magic", $"0x{magic:X8} ({(is64 ? "64" : "32")}-bit, {(little ? "little" : "big")}-endian)"),
            ("CPU type", $"0x{cpu:X8} ({Cpu(cpu)})"),
            ("CPU subtype", $"0x{subtype:X8} ({Subtype(cpu, subtype)})"),
            ("File type", $"{fileType} ({type})"),
            ("Load commands", $"{ncmds:N0} ({sizeofcmds:N0} bytes)"),
            ("Flags", HeaderFlags(flags)),
        ]));
        sections.Add(new InspectionSection($"Load commands ({commands.Count})", []) { Table = new InspectionTable(["#", "Command", "Size", "Detail"], commands) });
        if (segments.Count > 0)
            sections.Add(new InspectionSection($"Segments ({segments.Count})", []) { Table = new InspectionTable(["Name", "VM address", "VM size", "File offset", "File size", "Access (now/most)", "Sections", "Flags"], segments) });
        if (sectionRows.Count > 0)
            sections.Add(new InspectionSection($"Sections ({sectionRows.Count})", []) { Table = new InspectionTable(["Segment", "Section", "Address", "Size", "Offset", "Align", "Type", "Attributes"], sectionRows) });
        if (imports.Count > 0)
            sections.Add(new InspectionSection($"Imported symbols ({imports.Count:N0})", []) { Table = new InspectionTable(["Symbol", "From"], [.. imports.Take(MaxListed)]) { More = imports.Count > MaxListed ? $"{imports.Count - MaxListed:N0} more are not listed" : null } });
        if (exports.Count > 0)
            sections.Add(new InspectionSection($"Exported symbols ({exports.Count:N0})", []) { Table = new InspectionTable(["Symbol", "Address"], [.. exports.Take(MaxListed)]) { More = exports.Count > MaxListed ? $"{exports.Count - MaxListed:N0} more are not listed" : null } });
        report = new InspectionReport($"Mach-O {(is64 ? "64" : "32")}-bit · {Cpu(cpu)}", sections, warnings);
        return true;
    }

    /// <summary>The code directory's identifier, team, flags, and hashes; entitlements; the CMS signature's certificates.</summary>
    private static void CodeSignature(byte[] d, List<(string, string)> fields, List<InspectionSection> children, ref bool hardened)
    {
        static uint Be(ReadOnlySpan<byte> b, int o) => o >= 0 && o + 4 <= b.Length ? BinaryPrimitives.ReadUInt32BigEndian(b[o..]) : 0;
        if (Be(d, 0) != 0xFADE0CC0) return;
        uint count = Be(d, 8);
        bool certificate = false;
        for (int i = 0; i < Math.Min(count, 32); i++)
        {
            uint slot = Be(d, 12 + i * 8), offset = Be(d, 16 + i * 8);
            if (offset >= d.Length || d.Length - offset < 8) continue;
            var blob = d.AsSpan((int)offset);
            uint magic = Be(blob, 0), length = Math.Min(Be(blob, 4), (uint)blob.Length);
            if (magic == 0xFADE0C02 && slot == 0 && length >= 44)
            {
                // The code directory.
                uint version = Be(blob, 8), flags = Be(blob, 12), identOffset = Be(blob, 20), nCodeSlots = Be(blob, 28);
                byte hashType = blob[37], pageShift = blob[39];
                if (identOffset < length) fields.Add(("Identifier", Cstr(blob[(int)identOffset..(int)length])));
                if (version >= 0x20200 && length >= 52 && Be(blob, 48) is var team and > 0 && team < length) fields.Add(("Team ID", Cstr(blob[(int)team..(int)length])));
                hardened = (flags & 0x10000) != 0;
                fields.Add(("Code signing flags", SigningFlags(flags)));
                fields.Add(("Hashes", $"{nCodeSlots:N0} pages of {(pageShift is > 0 and < 32 ? 1u << pageShift : 0):N0} bytes, {hashType switch { 1 => "SHA-1", 2 => "SHA-256", 3 => "SHA-256 (truncated)", 4 => "SHA-384", _ => $"type {hashType}" }}"));
                fields.Add(("Code directory version", $"0x{version:X}"));
                if ((flags & 0x2) != 0) fields.Add(("Signed", "ad hoc: no certificate, so no identity"));
            }
            else if (magic == 0xFADE7171 && length > 8)
            {
                // Entitlements: an XML property list.
                var text = Encoding.UTF8.GetString(blob[8..(int)length]);
                children.Add(new InspectionSection("Entitlements", []) { Lines = [.. text.Split('\n').Take(500).Select(l => Printable(l.TrimEnd('\r')))] });
            }
            else if (magic == 0xFADE0B01 && length > 8)
            {
                certificate = true;
                if (SignedData.Read(blob[8..(int)length].ToArray()) is { } cms)
                {
                    if (cms.Signer is not null) fields.Add(("Signed by", cms.Signer));
                    if (cms.Certificates.Count > 0)
                        children.Add(new InspectionSection($"Certificates ({cms.Certificates.Count})", []) { Table = new InspectionTable(["Subject", "Issuer", "Valid from", "Valid to", "Thumbprint (SHA-1)"], cms.Certificates) });
                }
            }
        }
        if (!certificate && fields.All(f => f.Item1 != "Signed")) fields.Add(("Signed", "without a certificate"));
    }

    private static string SigningFlags(uint f)
    {
        var names = new List<string>();
        foreach (var (bit, name) in new[] { (0x2u, "ad hoc"), (0x4u, "get-task-allow"), (0x8u, "installer"), (0x10u, "forced library validation"),
            (0x100u, "hard"), (0x200u, "kill"), (0x400u, "check expiration"), (0x800u, "restrict"), (0x1000u, "enforcement"),
            (0x2000u, "library validation"), (0x10000u, "hardened runtime"), (0x20000u, "linker-signed") })
            if ((f & bit) != 0) names.Add(name);
        return $"0x{f:X8}" + (names.Count > 0 ? $" ({string.Join(", ", names)})" : "");
    }

    private static string HeaderFlags(uint f)
    {
        var names = new List<string>();
        foreach (var (bit, name) in new[] { (0x1u, "NOUNDEFS"), (0x2u, "INCRLINK"), (0x4u, "DYLDLINK"), (0x8u, "BINDATLOAD"), (0x10u, "PREBOUND"),
            (0x20u, "SPLIT_SEGS"), (0x80u, "TWOLEVEL"), (0x100u, "FORCE_FLAT"), (0x200u, "NOMULTIDEFS"), (0x2000u, "SUBSECTIONS_VIA_SYMBOLS"),
            (0x8000u, "WEAK_DEFINES"), (0x10000u, "BINDS_TO_WEAK"), (0x20000u, "ALLOW_STACK_EXECUTION"), (0x40000u, "ROOT_SAFE"), (0x80000u, "SETUID_SAFE"),
            (0x100000u, "NO_REEXPORTED_DYLIBS"), (0x200000u, "PIE"), (0x400000u, "DEAD_STRIPPABLE_DYLIB"), (0x800000u, "HAS_TLV_DESCRIPTORS"),
            (0x1000000u, "NO_HEAP_EXECUTION"), (0x2000000u, "APP_EXTENSION_SAFE"), (0x8000000u, "SIM_SUPPORT"), (0x80000000u, "DYLIB_IN_CACHE") })
            if ((f & bit) != 0) names.Add(name);
        return $"0x{f:X8}" + (names.Count > 0 ? $" ({string.Join(" ", names)})" : "");
    }

    private static string CommandName(uint cmd) => (cmd & 0x7FFFFFFF) switch
    {
        0x1 => "LC_SEGMENT", 0x2 => "LC_SYMTAB", 0x3 => "LC_SYMSEG", 0x4 => "LC_THREAD", 0x5 => "LC_UNIXTHREAD", 0xB => "LC_DYSYMTAB",
        0xC => "LC_LOAD_DYLIB", 0xD => "LC_ID_DYLIB", 0xE => "LC_LOAD_DYLINKER", 0xF => "LC_ID_DYLINKER", 0x10 => "LC_PREBOUND_DYLIB",
        0x11 => "LC_ROUTINES", 0x12 => "LC_SUB_FRAMEWORK", 0x13 => "LC_SUB_UMBRELLA", 0x14 => "LC_SUB_CLIENT", 0x15 => "LC_SUB_LIBRARY",
        0x16 => "LC_TWOLEVEL_HINTS", 0x17 => "LC_PREBIND_CKSUM", 0x18 => "LC_LOAD_WEAK_DYLIB", 0x19 => "LC_SEGMENT_64", 0x1A => "LC_ROUTINES_64",
        0x1B => "LC_UUID", 0x1C => "LC_RPATH", 0x1D => "LC_CODE_SIGNATURE", 0x1E => "LC_SEGMENT_SPLIT_INFO", 0x1F => "LC_REEXPORT_DYLIB",
        0x20 => "LC_LAZY_LOAD_DYLIB", 0x21 => "LC_ENCRYPTION_INFO", 0x22 => (cmd & 0x80000000) != 0 ? "LC_DYLD_INFO_ONLY" : "LC_DYLD_INFO",
        0x23 => "LC_LOAD_UPWARD_DYLIB", 0x24 => "LC_VERSION_MIN_MACOSX", 0x25 => "LC_VERSION_MIN_IPHONEOS", 0x26 => "LC_FUNCTION_STARTS",
        0x27 => "LC_DYLD_ENVIRONMENT", 0x28 => "LC_MAIN", 0x29 => "LC_DATA_IN_CODE", 0x2A => "LC_SOURCE_VERSION", 0x2B => "LC_DYLIB_CODE_SIGN_DRS",
        0x2C => "LC_ENCRYPTION_INFO_64", 0x2D => "LC_LINKER_OPTION", 0x2E => "LC_LINKER_OPTIMIZATION_HINT", 0x2F => "LC_VERSION_MIN_TVOS",
        0x30 => "LC_VERSION_MIN_WATCHOS", 0x31 => "LC_NOTE", 0x32 => "LC_BUILD_VERSION", 0x33 => "LC_DYLD_EXPORTS_TRIE", 0x34 => "LC_DYLD_CHAINED_FIXUPS",
        0x35 => "LC_FILESET_ENTRY", 0x36 => "LC_ATOM_INFO",
        _ => $"0x{cmd:X}",
    };

    private static string Protection(uint p) => ((p & 1) != 0 ? "r" : "-") + ((p & 2) != 0 ? "w" : "-") + ((p & 4) != 0 ? "x" : "-");

    private static string SegmentFlags(uint f)
    {
        var names = new List<string>();
        if ((f & 0x1) != 0) names.Add("high VM");
        if ((f & 0x2) != 0) names.Add("fixed VM library");
        if ((f & 0x4) != 0) names.Add("no relocations");
        if ((f & 0x8) != 0) names.Add("protected");
        if ((f & 0x10) != 0) names.Add("read-only after fixups");
        return string.Join(", ", names);
    }

    private static string SectionType(uint type) => type switch
    {
        0 => "regular", 1 => "zero fill", 2 => "C strings", 3 => "4-byte literals", 4 => "8-byte literals", 5 => "literal pointers",
        6 => "non-lazy symbol pointers", 7 => "lazy symbol pointers", 8 => "symbol stubs", 9 => "initializers", 10 => "terminators",
        11 => "coalesced", 12 => "zero fill (large)", 13 => "interposing", 14 => "16-byte literals", 15 => "DTrace", 16 => "lazy dylib pointers",
        17 => "thread-local data", 18 => "thread-local zero fill", 19 => "thread-local variables", 20 => "thread-local pointers",
        21 => "thread-local initializers", 22 => "initializer offsets", _ => $"type {type}",
    };

    private static string SectionAttributes(uint f)
    {
        var names = new List<string>();
        if ((f & 0x80000000) != 0) names.Add("instructions only");
        if ((f & 0x00000400) != 0) names.Add("some instructions");
        if ((f & 0x02000000) != 0) names.Add("debug");
        if ((f & 0x04000000) != 0) names.Add("self-modifying");
        if ((f & 0x10000000) != 0) names.Add("no dead strip");
        return string.Join(", ", names);
    }

    private static string Cstr(ReadOnlySpan<byte> b)
    {
        int end = b.IndexOf((byte)0);
        var s = Encoding.UTF8.GetString(end < 0 ? b[..Math.Min(b.Length, 512)] : b[..Math.Min(end, 512)]);
        return Printable(s);
    }

    private static string Printable(string s) => new(s.Select(ch => char.IsControl(ch) ? '?' : ch).ToArray());

    /// <summary>xxxx.yy.zz, as Mach-O packs versions (a trailing .0 left out).</summary>
    private static string Version3(uint v) => $"{v >> 16}.{(v >> 8) & 0xFF}" + ((v & 0xFF) != 0 ? $".{v & 0xFF}" : "");

    private static string Platform(uint p) => p switch
    {
        1 => "macOS", 2 => "iOS", 3 => "tvOS", 4 => "watchOS", 5 => "bridgeOS", 6 => "Mac Catalyst", 7 => "iOS Simulator",
        8 => "tvOS Simulator", 9 => "watchOS Simulator", 10 => "DriverKit", 11 => "visionOS", 12 => "visionOS Simulator",
        _ => $"platform {p}",
    };

    private static string Tool(uint tool) => tool switch { 1 => "clang", 2 => "swift", 3 => "ld", 4 => "lld", 1024 => "Metal", 1025 => "AIR linker", _ => $"tool {tool}" };

    private static string Cpu(int cpu) => (cpu & 0xFFFFFF) switch
    {
        7 => (cpu & 0x01000000) != 0 ? "x86-64" : "x86",
        12 => (cpu & 0x01000000) != 0 ? "ARM64" : (cpu & 0x02000000) != 0 ? "ARM64_32" : "ARM",
        18 => (cpu & 0x01000000) != 0 ? "PowerPC 64" : "PowerPC",
        _ => $"cpu {cpu}",
    };

    private static string Subtype(int cpu, int subtype) => ((cpu & 0xFFFFFF), subtype & 0xFFFFFF) switch
    {
        (12, 2) when (cpu & 0x01000000) != 0 => "arm64e",
        (12, 1) when (cpu & 0x01000000) != 0 => "arm64 v8",
        (12, 0) => "all",
        (12, 9) => "armv7",
        (12, 11) => "armv7s",
        (12, 12) => "armv7k",
        (7, 3) => "all",
        (7, 8) => "Haswell",
        (_, 0) => "all",
        (_, var s) => $"{s}",
    };
}
