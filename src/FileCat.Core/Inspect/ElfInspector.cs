using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>
/// ELF inspector (Linux and BSD executables, shared libraries, objects, core files), as deep as readelf: a summary first
/// (interpreter, needed libraries, the newest glibc they need, hardening: PIE, non-executable stack, RELRO, BIND_NOW,
/// stack protector, fortified functions, CET and BTI/PAC properties, build ID, the compiler's comment), then the ELF
/// header, program and section headers, the dynamic section, notes, and the imported and exported symbols with their
/// versions. Static and bounded; nothing is loaded.
/// </summary>
public static class ElfInspector
{
    private const int MaxSections = 512;
    private const int MaxProgramHeaders = 256;
    private const int MaxDynamic = 4096;
    private const int MaxNeeded = 200;
    private const int MaxSymbols = 200_000;
    private const int MaxListed = 5_000;
    private const int MaxNote = 64 * 1024;

    private readonly record struct Segment(uint Type, uint Flags, ulong Offset, ulong Vaddr, ulong Filesz, ulong Memsz, ulong Align);
    private readonly record struct SectionHeader(int Index, string Name, uint Type, ulong Flags, ulong Addr, ulong Offset, ulong Size, uint Link, uint Info, ulong Align, ulong EntSize);

    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct)
    {
        var r = new ContentReader(source);
        var id = r.Read(0, 64);
        if (id.Length < 16 || id[0] != 0x7F || id[1] != 'E' || id[2] != 'L' || id[3] != 'F') return null;
        var warnings = new List<string>();
        bool is64 = id[4] == 2, little = id[5] != 2;
        if (id[4] is not (1 or 2)) return new InspectionReport("ELF (unknown class)", [], ["The ELF class byte is invalid."]);
        int headerSize = is64 ? 64 : 52;
        if (id.Length < headerSize) return new InspectionReport("ELF (truncated)", [], ["The ELF header is cut off."]);
        ushort U16(ReadOnlySpan<byte> b, int at) => at < 0 || at + 2 > b.Length ? (ushort)0 : little ? BinaryPrimitives.ReadUInt16LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt16BigEndian(b[at..]);
        uint U32(ReadOnlySpan<byte> b, int at) => at < 0 || at + 4 > b.Length ? 0 : little ? BinaryPrimitives.ReadUInt32LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt32BigEndian(b[at..]);
        ulong U64(ReadOnlySpan<byte> b, int at) => at < 0 || at + 8 > b.Length ? 0 : little ? BinaryPrimitives.ReadUInt64LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt64BigEndian(b[at..]);
        ulong Word(ReadOnlySpan<byte> b, int at) => is64 ? U64(b, at) : U32(b, at);
        long At(ulong offset) => (long)Math.Min(offset, long.MaxValue);

        ushort type = U16(id, 16), machine = U16(id, 18);
        ulong entry = Word(id, 24);
        ulong phoff = Word(id, is64 ? 32 : 28), shoff = Word(id, is64 ? 40 : 32);
        uint flags = U32(id, is64 ? 48 : 36);
        ushort ehsize = U16(id, is64 ? 52 : 40), phentsize = U16(id, is64 ? 54 : 42), phnum = U16(id, is64 ? 56 : 44);
        ushort shentsize = U16(id, is64 ? 58 : 46), shnum = U16(id, is64 ? 60 : 48), shstrndx = U16(id, is64 ? 62 : 50);
        string typeName = type switch { 1 => "Relocatable object", 2 => "Executable", 3 => "Shared object", 4 => "Core dump", _ => $"type {type}" };
        string osAbi = id[7] switch
        {
            0 => "System V", 1 => "HP-UX", 2 => "NetBSD", 3 => "Linux (GNU)", 6 => "Solaris", 7 => "AIX", 8 => "IRIX", 9 => "FreeBSD",
            12 => "OpenBSD", 13 => "OpenVMS", 97 => "ARM", 255 => "standalone", _ => $"{id[7]}",
        };

        // Program headers: interpreter, stack, RELRO, the dynamic section, notes.
        var segments = new List<Segment>();
        // Entry sizes past a few hundred bytes are damage: nothing is read for them.
        if (phnum > 0 && phentsize >= (is64 ? 56 : 32) && phentsize <= 1024)
        {
            if (phnum > MaxProgramHeaders) warnings.Add($"The header declares {phnum} program headers; only the first {MaxProgramHeaders} are read.");
            int count = Math.Min(phnum, (ushort)MaxProgramHeaders);
            var ph = r.Read(At(phoff), count * phentsize);
            for (int i = 0; i + phentsize <= ph.Length; i += phentsize)
            {
                var p = ph.AsSpan(i, phentsize);
                segments.Add(is64
                    ? new Segment(U32(p, 0), U32(p, 4), U64(p, 8), U64(p, 16), U64(p, 32), U64(p, 40), U64(p, 48))
                    : new Segment(U32(p, 0), U32(p, 24), U32(p, 4), U32(p, 8), U32(p, 16), U32(p, 20), U32(p, 28)));
            }
            if (ph.Length < count * phentsize) warnings.Add("The program headers are cut off by the end of the file.");
        }
        string? interpreter = segments.FirstOrDefault(s => s.Type == 3) is { Type: 3 } interp ? r.AsciiZ(At(interp.Offset), (int)Math.Min(interp.Filesz, 512)) : null;
        var stack = segments.FirstOrDefault(s => s.Type == 0x6474E551);
        bool stackSeen = stack.Type != 0, stackExecutable = (stack.Flags & 1) != 0, relro = segments.Any(s => s.Type == 0x6474E552);
        var loads = segments.Where(s => s.Type == 1).ToList();
        long FileOffset(ulong address)
        {
            foreach (var s in loads)
                if (address >= s.Vaddr && address - s.Vaddr < s.Filesz) return At(s.Offset + (address - s.Vaddr));
            return -1;
        }

        // Section headers, and their names.
        var sectionHeaders = new List<SectionHeader>();
        if (shnum > 0 && shentsize >= (is64 ? 64 : 40) && shentsize <= 1024 && shoff > 0)
        {
            int count = Math.Min(shnum, (ushort)MaxSections);
            var sh = r.Read(At(shoff), count * shentsize);
            long names = -1;
            if (shstrndx < count && (shstrndx + 1) * shentsize <= sh.Length) names = At(Word(sh, shstrndx * shentsize + (is64 ? 24 : 16)));
            for (int i = 0; i + shentsize <= sh.Length; i += shentsize)
            {
                var s = sh.AsSpan(i, shentsize);
                uint nameOffset = U32(s, 0);
                string name = names >= 0 ? r.AsciiZ(names + nameOffset, 128) : $"#{i / shentsize}";
                sectionHeaders.Add(is64
                    ? new SectionHeader(i / shentsize, name, U32(s, 4), U64(s, 8), U64(s, 16), U64(s, 24), U64(s, 32), U32(s, 40), U32(s, 44), U64(s, 48), U64(s, 56))
                    : new SectionHeader(i / shentsize, name, U32(s, 4), U32(s, 8), U32(s, 12), U32(s, 16), U32(s, 20), U32(s, 24), U32(s, 28), U32(s, 32), U32(s, 36)));
            }
            if (shnum > MaxSections) warnings.Add($"The header declares {shnum} sections; only the first {MaxSections} are read.");
            if (sh.Length < count * shentsize) warnings.Add("The section headers are cut off by the end of the file.");
        }
        byte[] SectionData(SectionHeader s, int max) => s.Type == 8 ? [] : r.Read(At(s.Offset), (int)Math.Min(s.Size, (ulong)max));

        // Dynamic section: needed libraries, soname, run paths, flags.
        ct.ThrowIfCancellationRequested();
        var dynamicEntries = new List<(ulong Tag, ulong Value)>();
        var dynamicSegment = segments.FirstOrDefault(s => s.Type == 2);
        if (dynamicSegment.Type == 2 && dynamicSegment.Filesz > 0)
        {
            int entrySize = is64 ? 16 : 8;
            int count = (int)Math.Min(dynamicSegment.Filesz / (ulong)entrySize, MaxDynamic);
            var d = r.Read(At(dynamicSegment.Offset), count * entrySize);
            for (int i = 0; i + entrySize <= d.Length; i += entrySize)
            {
                ulong tag = Word(d, i), value = Word(d, i + entrySize / 2);
                if (tag == 0) break;
                dynamicEntries.Add((tag, value));
            }
        }
        ulong? Dyn(ulong tag) => dynamicEntries.FirstOrDefault(e => e.Tag == tag) is { Tag: > 0 } e ? e.Value : null;
        long strings = Dyn(5) is { } strtab ? FileOffset(strtab) : -1;
        ulong? strsz = Dyn(10);
        string Str(ulong offset) => strings < 0 || strsz is { } size && offset >= size ? "(unreadable)" : r.AsciiZ(strings + (long)offset);
        var needed = dynamicEntries.Where(e => e.Tag == 1).Take(MaxNeeded).Select(e => Str(e.Value)).ToList();
        ulong dtFlags = Dyn(30) ?? 0, dtFlags1 = Dyn(0x6FFFFFFB) ?? 0;
        bool bindNow = Dyn(24) is not null || (dtFlags & 0x8) != 0 || (dtFlags1 & 0x1) != 0;
        bool textRelocations = Dyn(22) is not null || (dtFlags & 0x4) != 0;
        if (textRelocations) warnings.Add("The file has text relocations: its code is written to at load time, which hardening forbids.");

        // Symbols: the dynamic symbol table with its versions.
        ct.ThrowIfCancellationRequested();
        var (imports, exports, symbolCount, versionsNeeded) = DynamicSymbols(r, sectionHeaders, is64, U16, U32, U64, ct);
        int localSymbols = sectionHeaders.FirstOrDefault(s => s.Type == 2) is { Type: 2 } symtab && symtab.EntSize > 0 ? (int)Math.Min(symtab.Size / symtab.EntSize, int.MaxValue) : 0;

        // Notes: build ID, ABI tag, properties (CET, BTI/PAC, x86-64 level), package metadata, Go build ID.
        var notes = new List<(string, string)>();
        var properties = new List<string>();
        var noteSources = segments.Where(s => s.Type == 4).Select(s => (Offset: s.Offset, Size: s.Filesz)).ToList();
        if (noteSources.Count == 0) noteSources = [.. sectionHeaders.Where(s => s.Type == 7).Select(s => (s.Offset, s.Size))];
        foreach (var (offset, size) in noteSources.Take(16))
            Notes(r.Read(At(offset), (int)Math.Min(size, MaxNote)), is64, machine, U32, notes, properties);
        string? Note(string name) => notes.FirstOrDefault(n => n.Item1 == name).Item2;

        // The summary.
        var header = new List<(string, string)>
        {
            ("Class", is64 ? "64-bit" : "32-bit"),
            ("Byte order", little ? "little-endian" : "big-endian"),
            ("OS ABI", osAbi),
            ("Type", typeName),
            ("Machine", Machine(machine)),
            ("Entry point", $"0x{entry:X}"),
        };
        if (interpreter is { Length: > 0 }) header.Add(("Interpreter", interpreter));
        if (Note("Build ID") is { } buildId) header.Add(("Build ID", buildId));
        var comment = sectionHeaders.FirstOrDefault(s => s.Name == ".comment");
        if (comment.Name is not null)
        {
            var tools = Encoding.UTF8.GetString(SectionData(comment, 4096)).Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().Take(8).ToList();
            if (tools.Count > 0) header.Add(("Built with", Printable(string.Join("; ", tools))));
        }
        var debugLink = sectionHeaders.FirstOrDefault(s => s.Name == ".gnu_debuglink");
        if (debugLink.Name is not null) header.Add(("Debug symbols in", r.AsciiZ(At(debugLink.Offset), 256)));
        if (sectionHeaders.Count > 0 && type is 2 or 3)
            header.Add(("Symbols", localSymbols > 0 ? $"{localSymbols:N0} (not stripped)" : "stripped"));
        var sections = new List<InspectionSection> { new("Header", header) };

        if (needed.Count > 0 || Dyn(14) is not null || versionsNeeded.Count > 0)
        {
            var libs = new List<(string, string)>();
            if (Dyn(14) is { } soname) libs.Add(("Library name (soname)", Str(soname)));
            foreach (var n in needed) libs.Add(("Needs", n));
            if (Dyn(15) is { } rpath) libs.Add(("RPATH", Str(rpath)));
            if (Dyn(29) is { } runpath) libs.Add(("RUNPATH", Str(runpath)));
            // The newest version of each library's symbols the file needs: the oldest system it runs on.
            foreach (var (library, newest) in NewestVersions(versionsNeeded)) libs.Add(($"Newest {library} version needed", newest));
            sections.Add(new InspectionSection($"Dynamic linking ({needed.Count} libraries)", libs));
        }

        bool pie = type == 3 && (interpreter is not null || (dtFlags1 & 0x08000000) != 0);
        var security = new List<(string, string)>
        {
            ("Position independent (PIE)", type == 2 ? "no (fixed addresses)" : pie ? "yes" : type == 3 ? "shared object" : "not applicable"),
            ("Non-executable stack", stackSeen ? stackExecutable ? "no: the stack is executable" : "yes" : "not declared (loader default)"),
            ("RELRO", relro ? bindNow ? "full (with BIND_NOW)" : "partial" : "no"),
        };
        if (imports.Count > 0 || exports.Count > 0)
        {
            bool protector = imports.Any(i => i[0] is "__stack_chk_fail" or "__stack_chk_guard" or "__stack_chk_fail_local");
            int fortified = imports.Count(i => i[0].StartsWith("__", StringComparison.Ordinal) && i[0].EndsWith("_chk", StringComparison.Ordinal) && i[0] != "__stack_chk_fail");
            security.Add(("Stack protector", protector ? "yes (__stack_chk_fail is used)" : "no sign of it"));
            security.Add(("Fortified functions (FORTIFY_SOURCE)", fortified > 0 ? $"{fortified} checked functions used" : "none used"));
        }
        foreach (var property in properties) security.Add(property switch
        {
            "IBT" => ("Indirect branch tracking (CET IBT)", "yes"),
            "SHSTK" => ("Shadow stack (CET SHSTK)", "yes"),
            "BTI" => ("Branch target identification (BTI)", "yes"),
            "PAC" => ("Pointer authentication (PAC)", "yes"),
            _ => ("Property", property),
        });
        if (textRelocations) security.Add(("Text relocations", "yes: code is patched at load time"));
        sections.Add(new InspectionSection("Security features", security));

        // The raw structures, as readelf shows them.
        sections.Add(new InspectionSection("ELF header", [
            ("Magic", Convert.ToHexString(id, 0, 16)),
            ("Class", is64 ? "ELF64" : "ELF32"),
            ("Data", little ? "two's complement, little-endian" : "two's complement, big-endian"),
            ("Version", $"{id[6]}{(id[6] == 1 ? " (current)" : "")}"),
            ("OS ABI", $"{osAbi}, ABI version {id[8]}"),
            ("Type", $"{type} ({typeName})"),
            ("Machine", $"{machine} ({Machine(machine)})"),
            ("Entry point", $"0x{entry:X}"),
            ("Program headers", $"{phnum} at offset 0x{phoff:X}, {phentsize} bytes each"),
            ("Section headers", $"{shnum} at offset 0x{shoff:X}, {shentsize} bytes each"),
            ("Section names in", $"section {shstrndx}"),
            ("Flags", MachineFlags(machine, flags)),
            ("Header size", $"{ehsize} bytes"),
        ]));
        if (segments.Count > 0)
            sections.Add(new InspectionSection($"Program headers ({segments.Count})", []) with
            {
                Table = new InspectionTable(["Type", "Offset", "Virtual address", "File size", "Memory size", "Flags", "Align"],
                    [.. segments.Select(s => new[] { SegmentType(s.Type), $"0x{s.Offset:X}", $"0x{s.Vaddr:X}", $"{s.Filesz:N0}", $"{s.Memsz:N0}",
                        ((s.Flags & 4) != 0 ? "R" : "-") + ((s.Flags & 2) != 0 ? "W" : "-") + ((s.Flags & 1) != 0 ? "X" : "-"), $"0x{s.Align:X}" })]),
            });
        if (sectionHeaders.Count > 0)
            sections.Add(new InspectionSection($"Sections ({sectionHeaders.Count})", []) with
            {
                Table = new InspectionTable(["#", "Name", "Type", "Address", "Offset", "Size", "Flags", "Link", "Align"],
                    [.. sectionHeaders.Where(s => s.Index > 0).Select(s => new[] { $"{s.Index}", s.Name.Length == 0 ? "(no name)" : s.Name, SectionType(s.Type),
                        $"0x{s.Addr:X}", $"0x{s.Offset:X}", $"{s.Size:N0}", SectionFlags(s.Flags), $"{s.Link}", $"{s.Align}" })]),
            });
        if (dynamicEntries.Count > 0)
            sections.Add(new InspectionSection($"Dynamic section ({dynamicEntries.Count} entries)", []) with
            {
                Table = new InspectionTable(["Tag", "Value"], [.. dynamicEntries.Select(e => new[] { DynamicTag(e.Tag), DynamicValue(e.Tag, e.Value, Str) })]),
            });
        if (notes.Count > 0) sections.Add(new InspectionSection("Notes", notes));
        if (imports.Count > 0)
            sections.Add(new InspectionSection($"Imported symbols ({imports.Count:N0})", []) with
            {
                Table = new InspectionTable(["Symbol", "Version", "Type", "Binding"], [.. imports.Take(MaxListed)]) { More = imports.Count > MaxListed ? $"{imports.Count - MaxListed:N0} more are not listed" : null },
            });
        if (exports.Count > 0)
            sections.Add(new InspectionSection($"Exported symbols ({exports.Count:N0})", []) with
            {
                Table = new InspectionTable(["Symbol", "Version", "Type", "Address", "Size"], [.. exports.Take(MaxListed)]) { More = exports.Count > MaxListed ? $"{exports.Count - MaxListed:N0} more are not listed" : null },
            });
        if (symbolCount >= MaxSymbols) warnings.Add($"The dynamic symbol table is larger than {MaxSymbols:N0} entries; the rest are not read.");
        // A file that is not stripped keeps its full symbol table: every function and variable with its address.
        if (localSymbols > 0 && FullSymbols(r, sectionHeaders, is64, U16, U32, U64, ct) is { Count: > 0 } symbols)
            sections.Add(new InspectionSection($"Symbol table ({localSymbols:N0})", []) with
            {
                Table = new InspectionTable(["Symbol", "Type", "Binding", "Section", "Address", "Size"], [.. symbols.Take(MaxListed)])
                {
                    More = symbols.Count > MaxListed ? $"{symbols.Count - MaxListed:N0} more are not listed" : null,
                },
            });
        return new InspectionReport($"ELF {(is64 ? "64" : "32")}-bit {typeName.ToLowerInvariant()} · {Machine(machine)}", sections, warnings);
    }

    private delegate ushort Reader16(ReadOnlySpan<byte> b, int at);
    private delegate uint Reader32(ReadOnlySpan<byte> b, int at);
    private delegate ulong Reader64(ReadOnlySpan<byte> b, int at);

    /// <summary>
    /// The dynamic symbol table (.dynsym): undefined global symbols are imports, defined visible ones exports; each with
    /// its version from .gnu.version against .gnu.version_r (needed) and .gnu.version_d (defined).
    /// </summary>
    private static (List<string[]> Imports, List<string[]> Exports, int Count, List<(string Library, string Version)> VersionsNeeded) DynamicSymbols(
        ContentReader r, List<SectionHeader> sections, bool is64, Reader16 u16, Reader32 u32, Reader64 u64, CancellationToken ct)
    {
        var imports = new List<string[]>();
        var exports = new List<string[]>();
        var needed = new List<(string, string)>();
        var dynsym = sections.FirstOrDefault(s => s.Type == 11);
        if (dynsym.Type != 11 || dynsym.EntSize < (is64 ? 24u : 16u) || dynsym.EntSize > 256 || dynsym.Link >= sections.Count) return (imports, exports, 0, needed);
        var strtab = sections[(int)dynsym.Link];
        long strings = (long)Math.Min(strtab.Offset, long.MaxValue);
        int count = (int)Math.Min(dynsym.Size / dynsym.EntSize, MaxSymbols);
        int entrySize = (int)Math.Min(dynsym.EntSize, 64);
        var table = r.Read((long)Math.Min(dynsym.Offset, long.MaxValue), count * entrySize);
        string Name(uint offset) => offset < strtab.Size ? r.AsciiZ(strings + offset, 256) : "(unreadable)";

        // Versions: index → name (and, for needed ones, the library).
        var versions = new Dictionary<int, string>();
        var versym = sections.FirstOrDefault(s => s.Type == 0x6FFFFFFF);
        byte[] versymData = versym.Type == 0x6FFFFFFF ? r.Read((long)Math.Min(versym.Offset, long.MaxValue), count * 2) : [];
        foreach (var verneed in sections.Where(s => s.Type == 0x6FFFFFFE))
        {
            var d = r.Read((long)Math.Min(verneed.Offset, long.MaxValue), (int)Math.Min(verneed.Size, 64 * 1024));
            var names = verneed.Link < sections.Count ? sections[(int)verneed.Link] : strtab;
            string VersionName(uint offset) => offset < names.Size ? r.AsciiZ((long)Math.Min(names.Offset, long.MaxValue) + offset, 128) : "(unreadable)";
            for (int at = 0, n = 0; at + 16 <= d.Length && n < 256; n++)
            {
                ushort auxCount = u16(d, at + 2);
                string library = VersionName(u32(d, at + 4));
                int aux = at + (int)u32(d, at + 8);
                for (int k = 0; k < auxCount && aux + 16 <= d.Length && aux >= 0; k++)
                {
                    int index = u16(d, aux + 6) & 0x7FFF;
                    string version = VersionName(u32(d, aux + 8));
                    versions[index] = version;
                    needed.Add((library, version));
                    uint nextAux = u32(d, aux + 12);
                    if (nextAux == 0) break;
                    aux += (int)nextAux;
                }
                uint next = u32(d, at + 12);
                if (next == 0) break;
                at += (int)next;
            }
        }
        foreach (var verdef in sections.Where(s => s.Type == 0x6FFFFFFD))
        {
            var d = r.Read((long)Math.Min(verdef.Offset, long.MaxValue), (int)Math.Min(verdef.Size, 64 * 1024));
            var names = verdef.Link < sections.Count ? sections[(int)verdef.Link] : strtab;
            for (int at = 0, n = 0; at + 20 <= d.Length && n < 1024; n++)
            {
                int index = u16(d, at + 4) & 0x7FFF;
                int aux = at + (int)u32(d, at + 12);
                if (aux >= 0 && aux + 8 <= d.Length && u32(d, aux) is var nameOffset && nameOffset < names.Size)
                    versions.TryAdd(index, r.AsciiZ((long)Math.Min(names.Offset, long.MaxValue) + nameOffset, 128));
                uint next = u32(d, at + 16);
                if (next == 0) break;
                at += (int)next;
            }
        }

        for (int i = 1; (i + 1) * entrySize <= table.Length; i++)
        {
            if (i % 4096 == 0) ct.ThrowIfCancellationRequested();
            var s = table.AsSpan(i * entrySize, entrySize);
            uint nameOffset = u32(s, 0);
            byte info = is64 ? s[4] : s[12], other = is64 ? s[5] : s[13];
            ushort section = is64 ? u16(s, 6) : u16(s, 14);
            ulong value = is64 ? u64(s, 8) : u32(s, 4), size = is64 ? u64(s, 16) : u32(s, 8);
            int binding = info >> 4, kind = info & 0xF, visibility = other & 3;
            if (binding is not (1 or 2 or 10) || nameOffset == 0) continue;
            string name = Name(nameOffset);
            int versionIndex = i * 2 + 2 <= versymData.Length ? u16(versymData, i * 2) & 0x7FFF : 0;
            string version = versionIndex > 1 && versions.TryGetValue(versionIndex, out var v) ? v : "";
            string symbolType = kind switch { 1 => "object", 2 => "function", 6 => "thread-local", 10 => "indirect function", 5 => "common", _ => "" };
            if (section == 0) imports.Add([name, version, symbolType, binding == 2 ? "weak" : "global"]);
            else if (visibility is 0 or 3) exports.Add([name, version, symbolType, $"0x{value:X}", $"{size:N0}"]);
        }
        return (imports, exports, count, needed);
    }

    /// <summary>The static symbol table (.symtab) of a file that is not stripped: named functions, variables, and thread-locals.</summary>
    private static List<string[]> FullSymbols(ContentReader r, List<SectionHeader> sections, bool is64, Reader16 u16, Reader32 u32, Reader64 u64, CancellationToken ct)
    {
        var rows = new List<string[]>();
        var symtab = sections.FirstOrDefault(s => s.Type == 2);
        if (symtab.Type != 2 || symtab.EntSize < (is64 ? 24u : 16u) || symtab.EntSize > 256 || symtab.Link >= sections.Count) return rows;
        var strtab = sections[(int)symtab.Link];
        int count = (int)Math.Min(symtab.Size / symtab.EntSize, MaxSymbols);
        int entrySize = (int)symtab.EntSize;
        var table = r.Read((long)Math.Min(symtab.Offset, long.MaxValue), count * entrySize);
        for (int i = 1; (i + 1) * entrySize <= table.Length; i++)
        {
            if (i % 4096 == 0) ct.ThrowIfCancellationRequested();
            var s = table.AsSpan(i * entrySize, entrySize);
            uint nameOffset = u32(s, 0);
            byte info = is64 ? s[4] : s[12];
            ushort section = is64 ? u16(s, 6) : u16(s, 14);
            ulong value = is64 ? u64(s, 8) : u32(s, 4), size = is64 ? u64(s, 16) : u32(s, 8);
            int kind = info & 0xF, binding = info >> 4;
            // Functions, variables, and thread-locals with names; sections and file names are left out.
            if (kind is not (1 or 2 or 6 or 10) || nameOffset == 0 || nameOffset >= strtab.Size) continue;
            string name = r.AsciiZ((long)Math.Min(strtab.Offset, long.MaxValue) + nameOffset, 256);
            string sectionName = section switch
            {
                0 => "undefined",
                0xFFF1 => "absolute",
                0xFFF2 => "common",
                _ when section < sections.Count => sections[section].Name,
                _ => $"#{section}",
            };
            rows.Add([name, kind switch { 1 => "object", 2 => "function", 6 => "thread-local", _ => "indirect function" },
                binding switch { 0 => "local", 1 => "global", 2 => "weak", 10 => "unique", _ => $"{binding}" }, sectionName, $"0x{value:X}", $"{size:N0}"]);
        }
        return rows;
    }

    /// <summary>For each library, the newest version of its symbols needed ("GLIBC_2.34"): versions compare part by part.</summary>
    private static IEnumerable<(string Library, string Newest)> NewestVersions(List<(string Library, string Version)> needed)
    {
        static int[] Parts(string version) =>
            [.. version.SkipWhile(c => !char.IsAsciiDigit(c)).Aggregate(new StringBuilder(), (sb, c) => sb.Append(char.IsAsciiDigit(c) || c == '.' ? c : ' ')).ToString()
                .Split('.', ' ').Where(p => p.Length > 0 && p.Length < 9).Select(p => int.Parse(p, CultureInfo.InvariantCulture))];
        foreach (var group in needed.Where(n => n.Version.Contains('_')).GroupBy(n => n.Version[..n.Version.IndexOf('_')]))
        {
            var newest = group.Select(n => n.Version).Distinct().OrderByDescending(Parts, Comparer<int[]>.Create((a, b) =>
            {
                for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
                {
                    int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                    if (x != y) return x.CompareTo(y);
                }
                return 0;
            })).First();
            yield return (group.Key.ToLowerInvariant() switch { "glibc" => "glibc", "glibcxx" => "libstdc++", "cxxabi" => "C++ ABI", "gcc" => "libgcc", var other => other }, newest);
        }
    }

    /// <summary>Note entries: owner, type, and a description that depends on both.</summary>
    private static void Notes(byte[] d, bool is64, ushort machine, Reader32 u32, List<(string, string)> notes, List<string> properties)
    {
        for (int at = 0, n = 0; at + 12 <= d.Length && n < 64; n++)
        {
            uint nameSize = u32(d, at), descSize = u32(d, at + 4), noteType = u32(d, at + 8);
            if (nameSize > 256 || descSize > MaxNote) break;
            int nameAt = at + 12, descAt = nameAt + (int)((nameSize + 3) & ~3u);
            if (descAt + descSize > d.Length) break;
            string owner = nameSize == 0 ? "" : Encoding.ASCII.GetString(d, nameAt, (int)nameSize - 1).TrimEnd('\0');
            var desc = d.AsSpan(descAt, (int)descSize);
            switch (owner, noteType)
            {
                case ("GNU", 1) when desc.Length >= 16:
                    string os = u32(desc, 0) switch { 0 => "Linux", 1 => "Hurd", 2 => "Solaris", 3 => "FreeBSD", var o => $"OS {o}" };
                    notes.Add(("ABI tag", $"{os} {u32(desc, 4)}.{u32(desc, 8)}.{u32(desc, 12)} or later"));
                    break;
                case ("GNU", 3):
                    notes.Add(("Build ID", Convert.ToHexStringLower(desc)));
                    break;
                case ("GNU", 4):
                    notes.Add(("Gold linker version", Printable(Encoding.ASCII.GetString(desc).TrimEnd('\0'))));
                    break;
                case ("GNU", 5):
                    // Properties: type, size, data, each aligned to 8 bytes in 64-bit files.
                    int align = is64 ? 8 : 4;
                    for (int p = 0; p + 8 <= desc.Length;)
                    {
                        uint propertyType = u32(desc, p), size = u32(desc, p + 4);
                        if (p + 8 + size > desc.Length) break;
                        uint bits = size >= 4 ? u32(desc, p + 8) : 0;
                        if (propertyType == 0xC0000002 && machine is 3 or 62)
                        {
                            if ((bits & 1) != 0) properties.Add("IBT");
                            if ((bits & 2) != 0) properties.Add("SHSTK");
                            notes.Add(("x86 features", bits == 0 ? "none" : string.Join(", ", new[] { (bits & 1) != 0 ? "IBT" : null, (bits & 2) != 0 ? "SHSTK" : null }.OfType<string>())));
                        }
                        else if (propertyType == 0xC0008002 && machine is 3 or 62)
                            notes.Add(("x86-64 level needed", bits switch { >= 8 => "v4", >= 4 => "v3", >= 2 => "v2", 1 => "baseline", _ => "none stated" }));
                        else if (propertyType == 0xC0000000 && machine == 183)
                        {
                            if ((bits & 1) != 0) properties.Add("BTI");
                            if ((bits & 2) != 0) properties.Add("PAC");
                            notes.Add(("AArch64 features", bits == 0 ? "none" : string.Join(", ", new[] { (bits & 1) != 0 ? "BTI" : null, (bits & 2) != 0 ? "PAC" : null }.OfType<string>())));
                        }
                        p += 8 + (int)((size + align - 1) & ~(uint)(align - 1));
                    }
                    break;
                case ("Go", 4):
                    notes.Add(("Go build ID", Printable(Encoding.ASCII.GetString(desc).TrimEnd('\0'))));
                    break;
                case ("FDO", 0xCAFE1A7E):
                    // systemd's package metadata: which distribution package the file came from.
                    notes.Add(("Package", Printable(Encoding.UTF8.GetString(desc).TrimEnd('\0'))));
                    break;
                case ("Android", 1) when desc.Length >= 4:
                    notes.Add(("Android API level", $"{u32(desc, 0)}"));
                    break;
                case ("stapsdt", 3):
                    notes.Add(("SystemTap probe", "present"));
                    break;
            }
            at = descAt + (int)((descSize + 3) & ~3u);
        }
    }

    private static string Printable(string s) => new(s.Select(c => char.IsControl(c) ? '?' : c).ToArray());

    private static string SegmentType(uint type) => type switch
    {
        0 => "NULL", 1 => "LOAD", 2 => "DYNAMIC", 3 => "INTERP", 4 => "NOTE", 5 => "SHLIB", 6 => "PHDR", 7 => "TLS",
        0x6474E550 => "GNU_EH_FRAME", 0x6474E551 => "GNU_STACK", 0x6474E552 => "GNU_RELRO", 0x6474E553 => "GNU_PROPERTY", 0x6474E554 => "GNU_SFRAME",
        0x65A3DBE6 => "OPENBSD_RANDOMIZE", 0x65A3DBE7 => "OPENBSD_WXNEEDED", 0x65A41BE6 => "OPENBSD_BOOTDATA",
        >= 0x70000000 and <= 0x7FFFFFFF => $"processor 0x{type:X}",
        >= 0x60000000 => $"OS 0x{type:X}",
        _ => $"0x{type:X}",
    };

    private static string SectionType(uint type) => type switch
    {
        0 => "NULL", 1 => "PROGBITS", 2 => "SYMTAB", 3 => "STRTAB", 4 => "RELA", 5 => "HASH", 6 => "DYNAMIC", 7 => "NOTE", 8 => "NOBITS",
        9 => "REL", 10 => "SHLIB", 11 => "DYNSYM", 14 => "INIT_ARRAY", 15 => "FINI_ARRAY", 16 => "PREINIT_ARRAY", 17 => "GROUP",
        18 => "SYMTAB_SHNDX", 19 => "RELR", 0x6FFFFFF5 => "GNU_ATTRIBUTES", 0x6FFFFFF6 => "GNU_HASH", 0x6FFFFFF7 => "GNU_LIBLIST",
        0x6FFFFFFD => "VERDEF", 0x6FFFFFFE => "VERNEED", 0x6FFFFFFF => "VERSYM", 0x6FFF4C00 => "LLVM_ODRTAB", 0x6FFF4C03 => "LLVM_ADDRSIG",
        0x70000001 => "PROC_UNWIND", 0x70000003 => "PROC_ATTRIBUTES",
        >= 0x70000000 and <= 0x7FFFFFFF => $"processor 0x{type:X}",
        >= 0x60000000 => $"OS 0x{type:X}",
        _ => $"0x{type:X}",
    };

    /// <summary>readelf's letters: W write, A alloc, X execute, M merge, S strings, I info, L link order, O OS, G group, T TLS, C compressed, E exclude.</summary>
    private static string SectionFlags(ulong f)
    {
        var sb = new StringBuilder();
        foreach (var (bit, letter) in new[] { (1UL, 'W'), (2UL, 'A'), (4UL, 'X'), (0x10UL, 'M'), (0x20UL, 'S'), (0x40UL, 'I'), (0x80UL, 'L'), (0x100UL, 'O'), (0x200UL, 'G'), (0x400UL, 'T'), (0x800UL, 'C'), (0x80000000UL, 'E') })
            if ((f & bit) != 0) sb.Append(letter);
        return sb.ToString();
    }

    private static string DynamicTag(ulong tag) => tag switch
    {
        1 => "NEEDED", 2 => "PLTRELSZ", 3 => "PLTGOT", 4 => "HASH", 5 => "STRTAB", 6 => "SYMTAB", 7 => "RELA", 8 => "RELASZ", 9 => "RELAENT",
        10 => "STRSZ", 11 => "SYMENT", 12 => "INIT", 13 => "FINI", 14 => "SONAME", 15 => "RPATH", 16 => "SYMBOLIC", 17 => "REL", 18 => "RELSZ",
        19 => "RELENT", 20 => "PLTREL", 21 => "DEBUG", 22 => "TEXTREL", 23 => "JMPREL", 24 => "BIND_NOW", 25 => "INIT_ARRAY", 26 => "FINI_ARRAY",
        27 => "INIT_ARRAYSZ", 28 => "FINI_ARRAYSZ", 29 => "RUNPATH", 30 => "FLAGS", 32 => "PREINIT_ARRAY", 33 => "PREINIT_ARRAYSZ",
        34 => "SYMTAB_SHNDX", 35 => "RELRSZ", 36 => "RELR", 37 => "RELRENT",
        0x6FFFFEF5 => "GNU_HASH", 0x6FFFFFF0 => "VERSYM", 0x6FFFFFF9 => "RELACOUNT", 0x6FFFFFFA => "RELCOUNT", 0x6FFFFFFB => "FLAGS_1",
        0x6FFFFFFC => "VERDEF", 0x6FFFFFFD => "VERDEFNUM", 0x6FFFFFFE => "VERNEED", 0x6FFFFFFF => "VERNEEDNUM", 0x6FFFFDF5 => "GNU_PRELINKED",
        0x6FFFFEF6 => "TLSDESC_PLT", 0x6FFFFEF7 => "TLSDESC_GOT",
        _ => $"0x{tag:X}",
    };

    private static string DynamicValue(ulong tag, ulong value, Func<ulong, string> text) => tag switch
    {
        1 or 14 or 15 or 29 => text(value),
        2 or 8 or 9 or 10 or 11 or 18 or 19 or 27 or 28 or 33 or 35 or 37 => $"{value:N0} bytes",
        20 => value == 7 ? "RELA" : value == 17 ? "REL" : $"{value}",
        30 => Flags(value, [(1, "ORIGIN"), (2, "SYMBOLIC"), (4, "TEXTREL"), (8, "BIND_NOW"), (0x10, "STATIC_TLS")]),
        0x6FFFFFFB => Flags(value, [(1, "NOW"), (2, "GLOBAL"), (4, "GROUP"), (8, "NODELETE"), (0x10, "LOADFLTR"), (0x20, "INITFIRST"), (0x40, "NOOPEN"),
            (0x80, "ORIGIN"), (0x100, "DIRECT"), (0x400, "INTERPOSE"), (0x800, "NODEFLIB"), (0x1000, "NODUMP"), (0x2000, "CONFALT"), (0x4000, "ENDFILTEE"),
            (0x8000, "DISPRELDNE"), (0x10000, "DISPRELPND"), (0x20000, "NODIRECT"), (0x40000, "IGNMULDEF"), (0x80000, "NOKSYMS"), (0x100000, "NOHDR"),
            (0x200000, "EDITED"), (0x400000, "NORELOC"), (0x800000, "SYMINTPOSE"), (0x1000000, "GLOBAUDIT"), (0x2000000, "SINGLETON"), (0x8000000, "PIE")]),
        0x6FFFFFF9 or 0x6FFFFFFA or 0x6FFFFFFD or 0x6FFFFFFF => $"{value:N0}",
        _ => $"0x{value:X}",
    };

    private static string Flags(ulong value, (ulong Bit, string Name)[] names)
    {
        var set = names.Where(n => (value & n.Bit) != 0).Select(n => n.Name).ToList();
        return $"0x{value:X}" + (set.Count > 0 ? $" ({string.Join(" ", set)})" : "");
    }

    /// <summary>The header's machine flags, with what they mean where it is well known (ARM EABI, RISC-V float ABI).</summary>
    private static string MachineFlags(ushort machine, uint flags)
    {
        string hex = $"0x{flags:X8}";
        if (machine == 40)
        {
            uint eabi = flags >> 24;
            return hex + (eabi > 0 ? $" (EABI version {eabi}{((flags & 0x400) != 0 ? ", hard float" : (flags & 0x200) != 0 ? ", soft float" : "")})" : "");
        }
        if (machine == 243)
        {
            string abi = ((flags >> 1) & 3) switch { 0 => "soft float", 1 => "single float", 2 => "double float", _ => "quad float" };
            return hex + $" ({abi}{((flags & 1) != 0 ? ", compressed instructions" : "")}{((flags & 8) != 0 ? ", RV32E" : "")})";
        }
        return hex;
    }

    private static string Machine(ushort m) => m switch
    {
        3 => "x86",
        62 => "x86-64",
        40 => "ARM",
        183 => "AArch64",
        243 => "RISC-V",
        8 => "MIPS",
        20 => "PowerPC",
        21 => "PowerPC 64",
        22 => "IBM S/390",
        2 => "SPARC",
        43 => "SPARC v9",
        50 => "IA-64",
        258 => "LoongArch",
        247 => "BPF",
        0 => "none",
        _ => $"0x{m:X}",
    };
}
