using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>
/// ELF inspector (Linux and BSD executables, shared libraries, objects, core files): class and byte order, type,
/// machine, interpreter, needed libraries, run paths, sections, and hardening (PIE, non-executable stack, RELRO, BIND_NOW).
/// Static and bounded; nothing is loaded.
/// </summary>
public static class ElfInspector
{
    private const int MaxSections = 512;
    private const int MaxProgramHeaders = 256;
    private const int MaxDynamic = 4096;
    private const int MaxNeeded = 200;

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
        ushort U16(ReadOnlySpan<byte> b, int at) => at + 2 > b.Length ? (ushort)0 : little ? BinaryPrimitives.ReadUInt16LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt16BigEndian(b[at..]);
        uint U32(ReadOnlySpan<byte> b, int at) => at + 4 > b.Length ? 0 : little ? BinaryPrimitives.ReadUInt32LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt32BigEndian(b[at..]);
        ulong U64(ReadOnlySpan<byte> b, int at) => at + 8 > b.Length ? 0 : little ? BinaryPrimitives.ReadUInt64LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt64BigEndian(b[at..]);
        ulong Word(ReadOnlySpan<byte> b, int at) => is64 ? U64(b, at) : U32(b, at);

        ushort type = U16(id, 16), machine = U16(id, 18);
        ulong entry = Word(id, 24);
        ulong phoff = Word(id, is64 ? 32 : 28), shoff = Word(id, is64 ? 40 : 32);
        ushort phentsize = U16(id, is64 ? 54 : 42), phnum = U16(id, is64 ? 56 : 44);
        ushort shentsize = U16(id, is64 ? 58 : 46), shnum = U16(id, is64 ? 60 : 48), shstrndx = U16(id, is64 ? 62 : 50);
        string typeName = type switch { 1 => "Relocatable object", 2 => "Executable", 3 => "Shared object", 4 => "Core dump", _ => $"type {type}" };
        var header = new List<(string, string)>
        {
            ("Class", is64 ? "64-bit" : "32-bit"),
            ("Byte order", little ? "little-endian" : "big-endian"),
            ("OS ABI", id[7] switch { 0 => "System V", 3 => "Linux", 6 => "Solaris", 9 => "FreeBSD", 12 => "OpenBSD", 2 => "NetBSD", _ => $"{id[7]}" }),
            ("Type", typeName),
            ("Machine", Machine(machine)),
            ("Entry point", $"0x{entry:X}"),
        };

        // Program headers: interpreter, stack, RELRO, dynamic section.
        string? interpreter = null;
        bool stackSeen = false, stackExecutable = false, relro = false;
        (ulong Offset, ulong Size)? dynamic = null;
        var segments = new List<(ulong Vaddr, ulong Offset, ulong Filesz)>();
        if (phnum > 0 && phentsize >= (is64 ? 56 : 32))
        {
            if (phnum > MaxProgramHeaders) warnings.Add($"The header declares {phnum} program headers; only the first {MaxProgramHeaders} are read.");
            int count = Math.Min(phnum, (ushort)MaxProgramHeaders);
            var ph = r.Read((long)Math.Min(phoff, long.MaxValue), count * phentsize);
            for (int i = 0; i + phentsize <= ph.Length; i += phentsize)
            {
                var p = ph.AsSpan(i, phentsize);
                uint ptype = U32(p, 0);
                uint flags = is64 ? U32(p, 4) : U32(p, 24);
                ulong offset = is64 ? U64(p, 8) : U32(p, 4);
                ulong vaddr = is64 ? U64(p, 16) : U32(p, 8);
                ulong filesz = is64 ? U64(p, 32) : U32(p, 16);
                switch (ptype)
                {
                    case 1: segments.Add((vaddr, offset, filesz)); break; // PT_LOAD
                    case 2: dynamic = (offset, filesz); break; // PT_DYNAMIC
                    case 3: interpreter = r.AsciiZ((long)Math.Min(offset, long.MaxValue), (int)Math.Min(filesz, 512)); break; // PT_INTERP
                    case 0x6474E551: stackSeen = true; stackExecutable = (flags & 1) != 0; break; // PT_GNU_STACK
                    case 0x6474E552: relro = true; break; // PT_GNU_RELRO
                }
            }
            if (ph.Length < count * phentsize) warnings.Add("The program headers are cut off by the end of the file.");
        }
        if (interpreter is { Length: > 0 }) header.Add(("Interpreter", interpreter));
        var sections = new List<InspectionSection> { new("Header", header) };

        // Dynamic section: needed libraries, soname, run paths, flags.
        ct.ThrowIfCancellationRequested();
        var needed = new List<ulong>();
        ulong? soname = null, rpath = null, runpath = null, strtab = null, strsz = null;
        bool bindNow = false;
        if (dynamic is { } dyn && dyn.Size > 0)
        {
            int entrySize = is64 ? 16 : 8;
            int count = (int)Math.Min(dyn.Size / (ulong)entrySize, MaxDynamic);
            var d = r.Read((long)Math.Min(dyn.Offset, long.MaxValue), count * entrySize);
            for (int i = 0; i + entrySize <= d.Length; i += entrySize)
            {
                ulong tag = Word(d, i), val = Word(d, i + entrySize / 2);
                if (tag == 0) break;
                switch (tag)
                {
                    case 1: if (needed.Count < MaxNeeded) needed.Add(val); break;
                    case 5: strtab = val; break;
                    case 10: strsz = val; break;
                    case 14: soname = val; break;
                    case 15: rpath = val; break;
                    case 29: runpath = val; break;
                    case 24: bindNow = true; break; // DT_BIND_NOW
                    case 30: if ((val & 0x8) != 0) bindNow = true; break; // DT_FLAGS: DF_BIND_NOW
                    case 0x6FFFFFFB: if ((val & 0x1) != 0) bindNow = true; break; // DT_FLAGS_1: DF_1_NOW
                }
            }
        }
        long strings = strtab is { } addr ? FileOffset(segments, addr) : -1;
        string Str(ulong offset) => strings < 0 || strsz is { } size && offset >= size ? "(unreadable)" : r.AsciiZ(strings + (long)offset);
        if (needed.Count > 0 || soname is not null)
        {
            var libs = new List<(string, string)>();
            if (soname is { } s) libs.Add(("Library name (soname)", Str(s)));
            foreach (var n in needed) libs.Add(("Needs", Str(n)));
            if (rpath is { } rp) libs.Add(("RPATH", Str(rp)));
            if (runpath is { } rn) libs.Add(("RUNPATH", Str(rn)));
            sections.Add(new InspectionSection($"Dynamic linking ({needed.Count} libraries)", libs));
        }
        bool pie = type == 3 && interpreter is not null;
        sections.Add(new InspectionSection("Security features", [
            ("Position independent (PIE)", type == 2 ? "no (fixed addresses)" : pie ? "yes" : type == 3 ? "shared object" : "not applicable"),
            ("Non-executable stack", stackSeen ? stackExecutable ? "no: the stack is executable" : "yes" : "not declared (loader default)"),
            ("RELRO", relro ? bindNow ? "full (with BIND_NOW)" : "partial" : "no"),
        ]));

        // Section names.
        if (shnum > 0 && shentsize >= (is64 ? 64 : 40) && shoff > 0)
        {
            int count = Math.Min(shnum, (ushort)MaxSections);
            var sh = r.Read((long)Math.Min(shoff, long.MaxValue), count * shentsize);
            long names = -1;
            if (shstrndx < count && (shstrndx + 1) * shentsize <= sh.Length)
                names = (long)Math.Min(Word(sh, shstrndx * shentsize + (is64 ? 24 : 16)), long.MaxValue);
            var list = new List<(string, string)>();
            for (int i = 0; i + shentsize <= sh.Length && list.Count < 64; i += shentsize)
            {
                uint nameOffset = U32(sh, i);
                ulong size = Word(sh, i + (is64 ? 32 : 20));
                ulong flags = Word(sh, i + 8);
                if (i == 0) continue;
                string name = names >= 0 ? r.AsciiZ(names + nameOffset, 64) : $"#{i / shentsize}";
                list.Add((name.Length == 0 ? "(no name)" : name, $"{((flags & 1) != 0 ? "W" : "-")}{((flags & 4) != 0 ? "X" : "-")}  {size:N0} bytes"));
            }
            if (shnum > MaxSections) warnings.Add($"The header declares {shnum} sections; only the first {MaxSections} are read.");
            if (list.Count > 0) sections.Add(new InspectionSection($"Sections ({Math.Min((int)shnum, MaxSections)})", list));
            if (list.All(s => s.Item1 != ".symtab") && type is 2 or 3) sections[0] = sections[0] with { Fields = [.. sections[0].Fields, ("Symbols", "stripped")] };
        }
        return new InspectionReport($"ELF {(is64 ? "64" : "32")}-bit {typeName.ToLowerInvariant()} · {Machine(machine)}", sections, warnings);
    }

    /// <summary>A virtual address to a file offset through the loadable segments, or -1.</summary>
    private static long FileOffset(List<(ulong Vaddr, ulong Offset, ulong Filesz)> segments, ulong address)
    {
        foreach (var (vaddr, offset, filesz) in segments)
            if (address >= vaddr && address - vaddr < filesz) return (long)Math.Min(offset + (address - vaddr), long.MaxValue);
        return -1;
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

/// <summary>
/// Mach-O inspector (macOS and iOS executables and libraries, including universal "fat" files), and Java class files,
/// which share the 0xCAFEBABE magic. Static and bounded.
/// </summary>
public static class MachOInspector
{
    private const int MaxArchitectures = 16;
    private const int MaxLoadCommands = 512;

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
        var sections = new List<InspectionSection>();
        for (int i = 0; i + entry <= table.Length; i += entry)
        {
            ct.ThrowIfCancellationRequested();
            int cpu = BinaryPrimitives.ReadInt32BigEndian(table.AsSpan(i));
            long offset = is64 ? (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(table.AsSpan(i + 8)), long.MaxValue) : BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(i + 8));
            long size = is64 ? (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(table.AsSpan(i + 16)), long.MaxValue) : BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(i + 12));
            archs.Add((Cpu(cpu), $"{size:N0} bytes at offset {offset:N0}"));
            if (sections.Count == 0 && Thin(r, offset, ct, out var first) && first is not null)
                sections.AddRange(first.Sections.Select(s => s with { Title = $"{Cpu(cpu)}: {s.Title}" }));
        }
        sections.Insert(0, new InspectionSection($"Architectures ({archs.Count})", archs));
        return new InspectionReport($"Mach-O universal binary · {string.Join(", ", archs.Select(a => a.Item1))}", sections, warnings);
    }

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
        uint U32(ReadOnlySpan<byte> b, int o) => o + 4 > b.Length ? 0 : little ? BinaryPrimitives.ReadUInt32LittleEndian(b[o..]) : BinaryPrimitives.ReadUInt32BigEndian(b[o..]);
        ulong U64(ReadOnlySpan<byte> b, int o) => o + 8 > b.Length ? 0 : little ? BinaryPrimitives.ReadUInt64LittleEndian(b[o..]) : BinaryPrimitives.ReadUInt64BigEndian(b[o..]);
        int cpu = (int)U32(h, 4);
        uint fileType = U32(h, 12), ncmds = U32(h, 16), sizeofcmds = U32(h, 20), flags = U32(h, 24);
        var warnings = new List<string>();
        var header = new List<(string, string)>
        {
            ("CPU", Cpu(cpu)),
            ("Type", fileType switch { 1 => "Object", 2 => "Executable", 6 => "Dynamic library", 8 => "Bundle", 7 => "Dynamic linker", 4 => "Core dump", 9 => "Stub library", 11 => "Kernel extension", 12 => "File set", _ => $"type {fileType}" }),
            ("Position independent (PIE)", (flags & 0x200000) != 0 ? "yes" : "no"),
        };
        if ((flags & 0x1000000) != 0) header.Add(("Heap", "not executable (MH_NO_HEAP_EXECUTION)"));
        if (ncmds > MaxLoadCommands) warnings.Add($"The header declares {ncmds} load commands; only the first {MaxLoadCommands} are read.");
        var cmds = r.Read(at + (is64 ? 32 : 28), (int)Math.Min(sizeofcmds, 1024 * 1024));
        var libs = new List<(string, string)>();
        var build = new List<(string, string)>();
        bool signature = false, encrypted = false;
        string Str(ReadOnlySpan<byte> command, int offsetField)
        {
            uint o = U32(command, offsetField);
            return o < command.Length ? Cstr(command[(int)o..]) : "(unreadable)";
        }
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
            switch (cmd & 0x7FFFFFFF)
            {
                case 0xC: libs.Add(("Loads", Str(c, 8))); break; // LC_LOAD_DYLIB
                case 0x18: libs.Add(("Loads (weak)", Str(c, 8))); break; // LC_LOAD_WEAK_DYLIB
                case 0x1F: libs.Add(("Re-exports", Str(c, 8))); break; // LC_REEXPORT_DYLIB
                case 0xD: header.Add(("Library name", Str(c, 8))); break; // LC_ID_DYLIB
                case 0x1C: libs.Add(("Run path", Str(c, 8))); break; // LC_RPATH
                case 0x1B when c.Length >= 24: header.Add(("UUID", Convert.ToHexString(c.Slice(8, 16)))); break; // LC_UUID
                case 0x1D: signature = true; break; // LC_CODE_SIGNATURE
                case 0x21 or 0x2C when c.Length >= 20: encrypted |= U32(c, 16) != 0; break; // LC_ENCRYPTION_INFO(_64): cryptid
                case 0x28 when c.Length >= 16: header.Add(("Entry point offset", $"0x{U64(c, 8):X}")); break; // LC_MAIN
                case 0x32 when c.Length >= 24: // LC_BUILD_VERSION
                    build.Add(("Platform", Platform(U32(c, 8))));
                    build.Add(("Minimum OS", Version(U32(c, 12))));
                    build.Add(("SDK", Version(U32(c, 16))));
                    break;
                case 0x24 or 0x25 or 0x2F or 0x30 when c.Length >= 16: // LC_VERSION_MIN_*
                    build.Add(("Minimum OS", Version(U32(c, 8))));
                    build.Add(("SDK", Version(U32(c, 12))));
                    break;
            }
            pos += (int)size;
        }
        var sections = new List<InspectionSection> { new("Header", header) };
        if (build.Count > 0) sections.Add(new InspectionSection("Build", build));
        if (libs.Count > 0) sections.Add(new InspectionSection($"Libraries ({libs.Count(l => l.Item1.StartsWith("Loads", StringComparison.Ordinal))})", libs));
        sections.Add(new InspectionSection("Signature", [
            ("Status", signature ? "Present, not verified: FileCat does not check signatures or trust here" : "No code signature"),
            ("Encrypted", encrypted ? "yes (App Store encryption)" : "no"),
        ]));
        report = new InspectionReport($"Mach-O {(is64 ? "64" : "32")}-bit · {Cpu(cpu)}", sections, warnings);
        return true;
    }

    private static string Cstr(ReadOnlySpan<byte> b)
    {
        int end = b.IndexOf((byte)0);
        var s = Encoding.UTF8.GetString(end < 0 ? b[..Math.Min(b.Length, 512)] : b[..Math.Min(end, 512)]);
        return new string(s.Select(ch => char.IsControl(ch) ? '?' : ch).ToArray());
    }

    private static string Version(uint v) => $"{v >> 16}.{(v >> 8) & 0xFF}" + ((v & 0xFF) != 0 ? $".{v & 0xFF}" : "");

    private static string Platform(uint p) => p switch
    {
        1 => "macOS", 2 => "iOS", 3 => "tvOS", 4 => "watchOS", 5 => "bridgeOS", 6 => "Mac Catalyst", 7 => "iOS Simulator",
        8 => "tvOS Simulator", 9 => "watchOS Simulator", 10 => "DriverKit", 11 => "visionOS", 12 => "visionOS Simulator",
        _ => $"platform {p}",
    };

    private static string Cpu(int cpu) => (cpu & 0xFFFFFF) switch
    {
        7 => (cpu & 0x01000000) != 0 ? "x86-64" : "x86",
        12 => (cpu & 0x01000000) != 0 ? "ARM64" : (cpu & 0x02000000) != 0 ? "ARM64_32" : "ARM",
        18 => (cpu & 0x01000000) != 0 ? "PowerPC 64" : "PowerPC",
        _ => $"cpu {cpu}",
    };
}
